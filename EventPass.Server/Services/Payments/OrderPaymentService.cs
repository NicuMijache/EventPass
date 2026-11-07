using EventPass.Server.Data;
using EventPass.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Services.Payments;

public enum PaymentConfirmation
{
    Paid,            // comanda a trecut în Paid acum și i s-au emis biletele
    AlreadyPaid,     // mesaj repetat: comanda era deja plătită, nu s-a făcut nimic
    OrderNotFound,   // niciun fel de comandă cu acest id
    PaymentMismatch, // suma, moneda sau sesiunea nu se potrivesc cu comanda
    OrderNotPending, // comanda e anulată/expirată: banii sunt luați, dar nu putem emite bilete
}

// Confirmarea unei plăți: comanda trece din Pending în Paid și se emit biletele.
// Se apelează DOAR din webhook-ul Stripe (după verificarea semnăturii), niciodată din cererea clientului.
public class OrderPaymentService
{
    private readonly AppDbContext _db;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(AppDbContext db, ILogger<OrderPaymentService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Idempotentă: apelată de mai multe ori pentru aceeași plată, emite biletele o singură dată.
    public async Task<PaymentConfirmation> ConfirmPaymentAsync(
        int orderId, string sessionId, long amountMinor, string currency, CancellationToken ct = default)
    {
        var order = await _db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            _logger.LogWarning("Plată primită pentru comanda inexistentă {OrderId} (sesiunea {SessionId}).", orderId, sessionId);
            return PaymentConfirmation.OrderNotFound;
        }

        if (order.Status == OrderStatus.Paid)
            return PaymentConfirmation.AlreadyPaid;

        // Nu ne bazăm doar pe id-ul comenzii: suma și moneda încasate trebuie să fie cele așteptate,
        // iar sesiunea de plată trebuie să fie chiar cea creată de noi pentru această comandă
        if (order.StripeSessionId != sessionId ||
            !string.Equals(currency, "ron", StringComparison.OrdinalIgnoreCase) ||
            amountMinor != Money.ToMinorUnits(order.TotalAmount))
        {
            _logger.LogError(
                "Plata nu se potrivește cu comanda {OrderId}: sesiune {SessionId} (așteptată {ExpectedSession}), {Amount} {Currency} (așteptat {ExpectedAmount}).",
                orderId, sessionId, order.StripeSessionId, amountMinor, currency, Money.ToMinorUnits(order.TotalAmount));
            return PaymentConfirmation.PaymentMismatch;
        }

        if (order.Status != OrderStatus.Pending)
        {
            // Plătită, dar comanda fusese anulată (locurile s-au eliberat). Rămâne pentru rambursare (#19).
            _logger.LogError(
                "Comanda {OrderId} este {Status}, dar Stripe a încasat plata (sesiunea {SessionId}). Necesită rambursare.",
                orderId, order.Status, sessionId);
            return PaymentConfirmation.OrderNotPending;
        }

        // Tranzacție: schimbarea de stare și emiterea biletelor reușesc împreună sau deloc
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // „Ușa” Pending → Paid: condiția Status == Pending face ca doar PRIMUL mesaj să treacă.
        // Un al doilea mesaj identic (Stripe retrimite) găsește comanda deja Paid și nu modifică nimic.
        var now = DateTime.UtcNow;
        var changed = await _db.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Paid)
                .SetProperty(o => o.PaidAt, now), ct);

        if (changed == 0)
        {
            // Între verificare și actualizare, altcineva a schimbat starea comenzii
            await transaction.RollbackAsync(ct);
            var status = await _db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync(ct);
            return status == OrderStatus.Paid ? PaymentConfirmation.AlreadyPaid : PaymentConfirmation.OrderNotPending;
        }

        // Câte un bilet pentru fiecare loc cumpărat, fiecare cu token unic (acela ajunge în QR)
        var tickets = order.Items
            .SelectMany(item => Enumerable.Range(0, item.Quantity).Select(_ => new Ticket
            {
                OrderId = orderId,
                TicketTypeId = item.TicketTypeId,
                Token = Guid.NewGuid(),
            }))
            .ToList();
        _db.Tickets.AddRange(tickets);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation("Comanda {OrderId} plătită: {Count} bilete emise.", orderId, tickets.Count);
        return PaymentConfirmation.Paid;
    }
}
