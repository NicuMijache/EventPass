using System.Text.RegularExpressions;
using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Services;

// Rezultatul unei scanări la intrare
public enum TicketValidationResult
{
    Valid,          // bilet bun: tocmai l-am marcat ca folosit, persoana intră
    AlreadyUsed,    // biletul a mai fost scanat (a doua oară = nu intră)
    NotFound,       // nu există niciun bilet cu acest cod
    NotPaid,        // biletul există, dar comanda nu mai e plătită (ex. rambursată, #19)
    OtherOrganizer, // un organizator a scanat biletul unui eveniment care nu e al lui
    Ambiguous,      // codul scurt se potrivește la mai multe bilete: trebuie scanat QR-ul
    InvalidCode     // textul nu arată deloc a cod de bilet
}

// Validarea biletelor la intrare (#17). Toată logica e aici, nu în controller, ca s-o putem testa direct.
public partial class TicketValidationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<TicketValidationService> _logger;

    public TicketValidationService(AppDbContext db, ILogger<TicketValidationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // organizerId: dacă cel care scanează e Organizer, acceptăm doar biletele evenimentelor lui.
    // Staff și Admin (organizerId = null) pot valida orice bilet.
    public async Task<TicketValidationDto> ValidateAsync(string? code, int scannedByUserId, int? organizerId, CancellationToken ct = default)
    {
        var parsed = Parse(code);
        if (parsed is null)
            return Result(TicketValidationResult.InvalidCode, "Codul nu este un cod de bilet EventPass.");

        // 1. Căutăm biletul. Cu tokenul întreg e cel mult unul (tokenul e unic).
        //    Cu codul scurt (8 caractere = începutul tokenului) pot fi, rar, mai multe: luăm 2 ca să știm dacă e așa.
        var query = _db.Tickets.AsNoTracking();
        query = parsed.Value.Token is Guid token
            ? query.Where(t => t.Token == token)
            // Tokenul e salvat ca uuid în PostgreSQL; îl transformăm în text cu majuscule și comparăm începutul.
            // Nu folosește un index (citește toate biletele), dar e suficient pentru câteva mii de bilete.
            : query.Where(t => t.Token.ToString().ToUpper().StartsWith(parsed.Value.ShortCode!));

        var candidates = await query
            .OrderBy(t => t.Id)
            .Take(2)
            .Select(t => new TicketInfo(
                t.Id,
                t.Token,
                t.Order.Status,
                t.TicketType.Event.OrganizerId,
                t.TicketType.Event.Title,
                t.TicketType.Event.StartsAt,
                t.TicketType.Name,
                t.Order.User.FullName))
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return Log(Result(TicketValidationResult.NotFound, "Nu există niciun bilet cu acest cod."), code, scannedByUserId);
        if (candidates.Count > 1)
            return Log(Result(TicketValidationResult.Ambiguous, "Mai multe bilete încep cu acest cod. Scanează codul QR."), code, scannedByUserId);

        var ticket = candidates[0];

        // 2. Un organizator nu are ce căuta la intrarea evenimentului altcuiva
        if (organizerId is not null && ticket.OrganizerId != organizerId)
            return Log(Result(TicketValidationResult.OtherOrganizer, "Biletul este pentru un eveniment al altui organizator."), code, scannedByUserId);

        if (ticket.OrderStatus != OrderStatus.Paid)
            return Log(Result(TicketValidationResult.NotPaid, NotValidMessage(ticket.OrderStatus), ticket), code, scannedByUserId);

        // 3. Marcăm biletul ca folosit cu UN SINGUR UPDATE, care pune condiția „încă nefolosit” chiar în WHERE.
        //    Dacă doi angajați scanează același bilet în aceeași secundă, baza de date execută cele două UPDATE-uri
        //    unul după altul: primul găsește IsUsed = false și îl schimbă (1 rând), al doilea nu mai găsește nimic (0 rânduri).
        //    Așa, un bilet intră o singură dată, fără să citim întâi și să scriem după (unde s-ar strecura al doilea).
        // PostgreSQL păstrează ora cu 6 zecimale (microsecunde), .NET cu 7. Tăiem a 7-a zecimală, ca ora trimisă acum
        // să fie identică cu cea citită din baza de date la următoarele scanări.
        var now = DateTime.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        // VoidedAt == null: biletul n-a fost anulat de o rambursare (#19). Condiția e pe rândul biletului însuși,
        // deci dacă rambursarea îl anulează exact acum, baza de date o vede și aici.
        var updated = await _db.Tickets
            .Where(t => t.Id == ticket.Id && !t.IsUsed && t.VoidedAt == null && t.Order.Status == OrderStatus.Paid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsUsed, true)
                .SetProperty(t => t.UsedAt, now), ct);

        if (updated == 1)
            return Log(Result(TicketValidationResult.Valid, "Bilet valid. Poate intra.", ticket, now), code, scannedByUserId);

        // 0 rânduri: fie biletul era deja folosit, fie comanda tocmai a fost rambursată. Citim starea actuală.
        var current = await _db.Tickets
            .AsNoTracking()
            .Where(t => t.Id == ticket.Id)
            .Select(t => new { t.IsUsed, t.UsedAt, t.Order.Status })
            .SingleAsync(ct);

        return current.IsUsed
            ? Log(Result(TicketValidationResult.AlreadyUsed, "Bilet deja utilizat.", ticket, current.UsedAt), code, scannedByUserId)
            : Log(Result(TicketValidationResult.NotPaid, NotValidMessage(current.Status), ticket), code, scannedByUserId);
    }

    private static string NotValidMessage(OrderStatus status) => status is OrderStatus.Refunded or OrderStatus.RefundPending
        ? "Bilet anulat: comanda a fost rambursată."
        : "Biletul nu mai este valabil (comanda nu este plătită).";

    // Acceptă aceleași forme ca scannerul din browser: tokenul întreg (cu sau fără cratime, litere mari sau mici)
    // sau codul scurt de 8 caractere de sub QR. Spațiile se ignoră (oamenii mai tastează cu spații).
    public static (Guid? Token, string? ShortCode)? Parse(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64)
            return null;

        var text = Whitespace().Replace(code, "");
        if (ShortCodePattern().IsMatch(text))
            return (null, text.ToUpperInvariant());
        // Formatele "D" (cu cratime) și "N" (fără), exact cele 2 pe care le scriem noi; nu acceptăm {…} sau (…)
        if (Guid.TryParseExact(text, "D", out var token) || Guid.TryParseExact(text, "N", out token))
            return (token, null);
        return null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("^[0-9A-Fa-f]{8}$")]
    private static partial Regex ShortCodePattern();

    private record TicketInfo(
        int Id, Guid Token, OrderStatus OrderStatus, int OrganizerId,
        string EventTitle, DateTime EventStartsAt, string TicketTypeName, string HolderName);

    // Detaliile biletului le arătăm doar după ce știm că biletul e al unui eveniment la care cel care scanează are acces
    private static TicketValidationDto Result(TicketValidationResult result, string message, TicketInfo? ticket = null, DateTime? usedAt = null) =>
        new(result.ToString(), message,
            ticket is null ? null : TicketCode.Short(ticket.Token),
            ticket?.EventTitle, ticket?.EventStartsAt, ticket?.TicketTypeName, ticket?.HolderName,
            usedAt);

    // Jurnal: cine a scanat, ce rezultat. Nu scriem tokenul întreg (e secretul biletului), doar începutul lui.
    private TicketValidationDto Log(TicketValidationDto dto, string? code, int userId)
    {
        var typed = Whitespace().Replace(code ?? "", "");
        var shortCode = dto.ShortCode ?? typed[..Math.Min(8, typed.Length)].ToUpperInvariant();
        _logger.LogInformation("Scanare bilet {ShortCode} de utilizatorul {UserId}: {Result}", shortCode, userId, dto.Result);
        return dto;
    }
}
