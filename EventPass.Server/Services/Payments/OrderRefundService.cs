using EventPass.Server.Data;
using EventPass.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Services.Payments;

public enum RefundOutcome
{
    Refunded,     // banii au plecat înapoi (Stripe a creat rambursarea), locurile s-au eliberat
    InProgress,   // biletele sunt anulate, dar Stripe n-a răspuns: procesul de fundal reîncearcă
    NotPaid,      // comanda nu e plătită (neplătită, anulată)
    TicketsUsed,  // cel puțin un bilet a fost folosit la intrare: nu mai rambursăm
}

// Rambursarea unei comenzi plătite (#19), în doi pași:
//   1. Start (doar baza de date, o tranzacție): Paid → RefundPending și TOATE biletele se anulează (VoidedAt).
//      Din acest moment niciun bilet nu mai deschide ușa.
//   2. Complete: cerem rambursarea la Stripe (cu cheie de idempotență), apoi RefundPending → Refunded și eliberăm locurile.
// De ce doi pași: baza de date și Stripe nu pot fi într-o singură tranzacție. Dacă Stripe nu răspunde la pasul 2,
// comanda rămâne RefundPending (biletele deja anulate), iar procesul de fundal reîncearcă; cheia de idempotență
// garantează că reîncercările nu dau banii de două ori.
public class OrderRefundService
{
    // După atâtea eșecuri nu mai reîncercăm singuri: trebuie să se uite un om (jurnal „Critical”)
    public const int MaxAttempts = 10;

    private readonly AppDbContext _db;
    private readonly SeatReservationService _seats;
    private readonly IPaymentGateway _payments;
    private readonly ILogger<OrderRefundService> _logger;

    public OrderRefundService(AppDbContext db, SeatReservationService seats, IPaymentGateway payments, ILogger<OrderRefundService> logger)
    {
        _db = db;
        _seats = seats;
        _payments = payments;
        _logger = logger;
    }

    // Cine are voie să ceară rambursarea (și până când) verifică apelantul; aici sunt regulile comune
    public async Task<RefundOutcome> RefundAsync(int orderId, CancellationToken ct = default)
    {
        var started = await StartAsync(orderId, ct);
        return started is RefundOutcome.NotPaid or RefundOutcome.TicketsUsed or RefundOutcome.Refunded
            ? started
            : await CompleteAsync(orderId, ct);
    }

    // Pasul 1. Întoarce InProgress dacă rambursarea a început acum (sau începuse deja).
    public async Task<RefundOutcome> StartAsync(int orderId, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;

        // „Ușa” Paid → RefundPending: doar o cerere trece (dublu-click, client + organizator în același timp)
        var changed = await _db.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.Paid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.RefundPending)
                .SetProperty(o => o.RefundRequestedAt, now)
                .SetProperty(o => o.RefundAttempts, 0), ct);
        if (changed == 0)
        {
            await transaction.RollbackAsync(ct);
            return await _db.Orders.Where(o => o.Id == orderId).Select(o => (OrderStatus?)o.Status).SingleOrDefaultAsync(ct) switch
            {
                OrderStatus.RefundPending => RefundOutcome.InProgress,   // începută deja: o continuăm
                OrderStatus.Refunded => RefundOutcome.Refunded,
                _ => RefundOutcome.NotPaid,
            };
        }

        // Anulăm biletele NEFOLOSITE. UPDATE-ul blochează rândurile biletelor: dacă exact acum cineva scanează
        // un bilet, una dintre operații așteaptă după cealaltă. Ori scanarea câștigă (biletul e folosit și nu-l
        // mai putem anula), ori rambursarea (biletul are VoidedAt, iar scanarea îl respinge). Niciodată amândouă.
        var total = await _db.Tickets.CountAsync(t => t.OrderId == orderId, ct);
        var voided = await _db.Tickets
            .Where(t => t.OrderId == orderId && !t.IsUsed && t.VoidedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.VoidedAt, now), ct);

        if (voided != total)
        {
            // Cel puțin un bilet a fost folosit: anulăm totul (tranzacția), comanda rămâne Paid, biletele valabile
            await transaction.RollbackAsync(ct);
            return RefundOutcome.TicketsUsed;
        }

        await transaction.CommitAsync(ct);
        _logger.LogInformation("Rambursarea comenzii {OrderId} a început: {Count} bilete anulate.", orderId, voided);
        return RefundOutcome.InProgress;
    }

    // Pasul 2. Se poate apela de oricâte ori (cerere, reîncercare, proces de fundal): banii pleacă o singură dată.
    public async Task<RefundOutcome> CompleteAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.AsNoTracking().Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.Id == orderId && o.Status == OrderStatus.RefundPending, ct);
        if (order is null)
        {
            var status = await _db.Orders.Where(o => o.Id == orderId).Select(o => (OrderStatus?)o.Status).SingleOrDefaultAsync(ct);
            return status == OrderStatus.Refunded ? RefundOutcome.Refunded : RefundOutcome.NotPaid;
        }

        string? refundId = null;
        // O comandă gratuită n-a trecut prin Stripe: nu e nimic de dat înapoi
        if (order.StripeSessionId is not null)
        {
            try
            {
                var refund = await _payments.RefundAsync(order.StripeSessionId, RefundKey(orderId), ct);
                refundId = refund.RefundId;
            }
            catch (Exception e) when (e is PaymentGatewayException or PaymentsNotConfiguredException)
            {
                var attempts = order.RefundAttempts + 1;
                await _db.Orders.Where(o => o.Id == orderId)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.RefundAttempts, attempts), ct);
                if (attempts >= MaxAttempts)
                    _logger.LogCritical("Rambursarea comenzii {OrderId} a eșuat de {Attempts} ori ({Reason}). Rezolvă manual din panoul Stripe.", orderId, attempts, e.Message);
                else
                    _logger.LogWarning("Rambursarea comenzii {OrderId} a eșuat (încercarea {Attempts}): {Reason}. Reîncerc.", orderId, attempts, e.Message);
                return RefundOutcome.InProgress;
            }
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var changed = await _db.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.RefundPending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Refunded)
                .SetProperty(o => o.RefundedAt, now)
                .SetProperty(o => o.StripeRefundId, refundId), ct);
        if (changed == 0)
        {
            // Altă cerere a terminat-o exact acum (aceeași rambursare la Stripe, datorită cheii)
            await transaction.RollbackAsync(ct);
            return RefundOutcome.Refunded;
        }

        // Locurile se pot vinde din nou
        await _seats.ReleaseAsync(order.Items, ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation("Comanda {OrderId} a fost rambursată ({RefundId}).", orderId, refundId ?? "fără plată");
        return RefundOutcome.Refunded;
    }

    // Pentru procesul de fundal: rambursările rămase neterminate (Stripe a picat, aplicația s-a oprit la jumătate).
    // Le lăsăm un minut „în pace”, ca să nu ne întrecem cu cererea care tocmai le-a început.
    public async Task<int> CompletePendingAsync(CancellationToken ct = default)
    {
        var before = DateTime.UtcNow.AddMinutes(-1);
        var ids = await _db.Orders
            .Where(o => o.Status == OrderStatus.RefundPending && o.RefundAttempts < MaxAttempts && o.RefundRequestedAt <= before)
            .OrderBy(o => o.RefundRequestedAt)
            .Select(o => o.Id)
            .Take(50)
            .ToListAsync(ct);

        var done = 0;
        foreach (var id in ids)
        {
            try
            {
                if (await CompleteAsync(id, ct) == RefundOutcome.Refunded)
                    done++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogError(e, "Terminarea rambursării comenzii {OrderId} a eșuat.", id);
            }
        }
        return done;
    }

    // O plată ajunsă pentru o comandă deja ANULATĂ (ex. clientul a anulat cât era pe pagina Stripe):
    // nu avem bilete de dat (locurile s-au eliberat), deci dăm banii înapoi. Excepțiile urcă la webhook,
    // care răspunde cu eroare, iar Stripe retrimite mesajul mai târziu (cheia de idempotență previne dublarea).
    public async Task RefundOrphanPaymentAsync(int orderId, string sessionId, CancellationToken ct = default)
    {
        var order = await _db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is not { Status: OrderStatus.Cancelled } || order.StripeSessionId != sessionId)
            return;

        var refund = await _payments.RefundAsync(sessionId, RefundKey(orderId), ct);
        await _db.Orders
            .Where(o => o.Id == orderId && o.StripeRefundId == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.StripeRefundId, refund.RefundId)
                .SetProperty(o => o.RefundedAt, DateTime.UtcNow), ct);
        _logger.LogWarning("Comanda {OrderId} era anulată, dar a fost plătită: am rambursat automat ({RefundId}).", orderId, refund.RefundId);
    }

    // Aceeași cheie pentru o comandă, oricâte încercări: Stripe face o singură rambursare
    private static string RefundKey(int orderId) => $"eventpass-refund-order-{orderId}";
}
