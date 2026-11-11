using System.Text.RegularExpressions;
using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using EventPass.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Controllers;

// Panoul organizatorului (#20): vânzări, încasări, participanți, bilete validate.
// Doar citire. Fiecare organizator vede doar evenimentele lui; pentru ale altora, 404.
[ApiController]
[Route("api/organizer")]
[Authorize(Roles = nameof(UserRole.Organizer))]
public partial class OrganizerDashboardController : ControllerBase
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    private readonly AppDbContext _db;

    public OrganizerDashboardController(AppDbContext db) => _db = db;

    // Toate evenimentele mele, cu cifrele principale.
    // Ordinea: întâi cele care urmează (cel mai apropiat primul), apoi cele trecute (cel mai recent primul).
    [HttpGet("dashboard")]
    public async Task<ActionResult<List<DashboardEventDto>>> GetDashboard(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var now = DateTime.UtcNow;
        var events = (await _db.Events.AsNoTracking()
                .Where(e => e.OrganizerId == userId)
                .Select(e => new { e.Id, e.Title, e.Venue, e.City, e.StartsAt, e.IsPublished })
                .ToListAsync(ct))
            .OrderBy(e => e.StartsAt < now)
            .ThenBy(e => e.StartsAt < now ? now - e.StartsAt : e.StartsAt - now)
            .ToList();

        var sales = await LoadSalesAsync(events.Select(e => e.Id).ToList(), ct);
        return Ok(events.Select(e => new DashboardEventDto(
            e.Id, e.Title, e.Venue, e.City, e.StartsAt, e.IsPublished, sales.TotalsFor(e.Id))).ToList());
    }

    // Un eveniment: totalurile și fiecare categorie de bilet
    [HttpGet("events/{id:int}/sales")]
    public async Task<ActionResult<EventSalesDto>> GetSales(int id, CancellationToken ct)
    {
        var ev = await _db.Events.AsNoTracking()
            .Where(e => e.Id == id && e.OrganizerId == User.GetUserId())
            .Select(e => new { e.Id, e.Title, e.Venue, e.City, e.StartsAt, e.IsPublished })
            .SingleOrDefaultAsync(ct);
        if (ev is null)
            return NotFound();

        var sales = await LoadSalesAsync([id], ct);
        return Ok(new EventSalesDto(ev.Id, ev.Title, ev.Venue, ev.City, ev.StartsAt, ev.IsPublished,
            sales.TotalsFor(id), sales.TypesFor(id)));
    }

    // Participanții: fiecare bilet emis, cu cumpărătorul. Căutare după nume, email sau codul scurt.
    // filter: all | validated (scanate) | notValidated (încă nescanate, valabile) | refunded (anulate prin rambursare)
    [HttpGet("events/{id:int}/attendees")]
    public async Task<ActionResult<AttendeesPageDto>> GetAttendees(
        int id, [FromQuery] string? q, [FromQuery] string? filter,
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize, CancellationToken ct = default)
    {
        var owns = await _db.Events.AnyAsync(e => e.Id == id && e.OrganizerId == User.GetUserId(), ct);
        if (!owns)
            return NotFound();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // Biletele există doar după plată; le arătăm și pe cele anulate prin rambursare (barate în pagină)
        var query = _db.Tickets.AsNoTracking()
            .Where(t => t.TicketType.EventId == id &&
                        (t.Order.Status == OrderStatus.Paid || t.Order.Status == OrderStatus.RefundPending || t.Order.Status == OrderStatus.Refunded));

        query = filter switch
        {
            "validated" => query.Where(t => t.IsUsed),
            "notValidated" => query.Where(t => !t.IsUsed && t.Order.Status == OrderStatus.Paid),
            "refunded" => query.Where(t => t.Order.Status != OrderStatus.Paid),
            _ => query,
        };

        var text = q?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            if (text.Length > 100)
                text = text[..100];
            var lower = text.ToLowerInvariant();
            // Codul scurt e începutul tokenului (ex. FA46C34B): îl căutăm doar dacă textul arată a cod
            var code = ShortCodePrefix().IsMatch(text) ? text.ToUpperInvariant() : null;
            query = query.Where(t =>
                t.Order.User.FullName.ToLower().Contains(lower) ||
                t.Order.User.Email.ToLower().Contains(lower) ||
                (code != null && t.Token.ToString().ToUpper().StartsWith(code)));
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(t => t.OrderId)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.Token,
                TypeName = t.TicketType.Name,
                t.Order.User.FullName,
                t.Order.User.Email,
                t.OrderId,
                t.Order.Status,
                t.Order.PaidAt,
                t.IsUsed,
                t.UsedAt,
                Voided = t.VoidedAt != null,
                AnyUsedInOrder = t.Order.Tickets.Any(o => o.IsUsed),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new AttendeeDto(
            r.Id, TicketCode.Short(r.Token), r.TypeName, r.FullName, r.Email, r.OrderId, r.Status.ToString(),
            r.PaidAt, r.IsUsed, r.UsedAt, r.Voided,
            CanRefund: r.Status == OrderStatus.Paid && !r.AnyUsedInOrder)).ToList();

        return Ok(new AttendeesPageDto(total, page, pageSize, items));
    }

    [GeneratedRegex("^[0-9A-Fa-f]{1,8}$")]
    private static partial Regex ShortCodePrefix();

    // ---------- Calculul vânzărilor ----------

    // Citim liniile comenzilor și numărul de bilete scanate, apoi adunăm în memorie.
    // (Sumele de bani le adunăm în C#, nu în SQL: SQLite, folosit în teste, nu știe să adune decimal.)
    private async Task<Sales> LoadSalesAsync(List<int> eventIds, CancellationToken ct)
    {
        var types = await _db.TicketTypes.AsNoTracking()
            .Where(t => eventIds.Contains(t.EventId))
            .OrderBy(t => t.Price).ThenBy(t => t.Id)
            .Select(t => new TypeRow(t.Id, t.EventId, t.Name, t.Price, t.Capacity, t.Sold))
            .ToListAsync(ct);

        var lines = await _db.OrderItems.AsNoTracking()
            .Where(i => eventIds.Contains(i.TicketType.EventId) &&
                        (i.Order.Status == OrderStatus.Paid || i.Order.Status == OrderStatus.Pending ||
                         i.Order.Status == OrderStatus.RefundPending || i.Order.Status == OrderStatus.Refunded))
            .Select(i => new LineRow(i.TicketTypeId, i.TicketType.EventId, i.OrderId, i.Order.Status, i.Quantity, i.UnitPrice, i.Order.TotalAmount))
            .ToListAsync(ct);

        // Biletele scanate, numărate direct în baza de date, pe categorii
        var validated = await _db.Tickets.AsNoTracking()
            .Where(t => eventIds.Contains(t.TicketType.EventId) && t.IsUsed)
            .GroupBy(t => t.TicketTypeId)
            .Select(g => new { TicketTypeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.TicketTypeId, g => g.Count, ct);

        return new Sales(types, lines, validated);
    }

    private record TypeRow(int Id, int EventId, string Name, decimal Price, int Capacity, int Sold);
    private record LineRow(int TicketTypeId, int EventId, int OrderId, OrderStatus Status, int Quantity, decimal UnitPrice, decimal OrderTotal);

    private sealed class Sales(List<TypeRow> types, List<LineRow> lines, Dictionary<int, int> validated)
    {
        public List<TicketTypeSalesDto> TypesFor(int eventId) => types
            .Where(t => t.EventId == eventId)
            .Select(t =>
            {
                var mine = lines.Where(l => l.TicketTypeId == t.Id).ToList();
                var paid = mine.Where(l => l.Status == OrderStatus.Paid).ToList();
                return new TicketTypeSalesDto(
                    t.Id, t.Name, t.Price, t.Capacity,
                    TicketsSold: paid.Sum(l => l.Quantity),
                    Reserved: mine.Where(l => l.Status == OrderStatus.Pending).Sum(l => l.Quantity),
                    // Sold din baza de date = locuri ocupate (plătite + rezervate + rambursări în curs)
                    Available: t.Capacity - t.Sold,
                    Revenue: paid.Sum(l => l.UnitPrice * l.Quantity),
                    Validated: validated.GetValueOrDefault(t.Id));
            })
            .ToList();

        public SalesTotalsDto TotalsFor(int eventId)
        {
            var byType = TypesFor(eventId);
            // O comandă poate avea mai multe linii; suma rambursată se ia o dată pe comandă
            var refunded = lines
                .Where(l => l.EventId == eventId && l.Status is OrderStatus.Refunded or OrderStatus.RefundPending)
                .GroupBy(l => l.OrderId)
                .Select(g => g.First().OrderTotal)
                .ToList();
            return new SalesTotalsDto(
                byType.Sum(t => t.Capacity),
                byType.Sum(t => t.TicketsSold),
                byType.Sum(t => t.Reserved),
                byType.Sum(t => t.Available),
                byType.Sum(t => t.Revenue),
                byType.Sum(t => t.Validated),
                refunded.Count,
                refunded.Sum());
        }
    }
}
