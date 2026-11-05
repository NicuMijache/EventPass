using EventPass.Server.Data;
using EventPass.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Services;

// Rezervarea și eliberarea locurilor (stocul = TicketType.Sold).
// Toate modificările de stoc trec pe aici, ca regulile să fie într-un singur loc.
public class SeatReservationService
{
    // Cât timp ține o comandă Pending locurile înainte să expire
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _db;

    public SeatReservationService(AppDbContext db)
    {
        _db = db;
    }

    // Încearcă să rezerve `quantity` locuri. Verificarea și scrierea sunt O SINGURĂ comandă SQL:
    //   UPDATE "TicketTypes" SET "Sold" = "Sold" + @q WHERE "Id" = @id AND "Sold" + @q <= "Capacity"
    // Baza de date o execută atomic, deci doi cumpărători simultani nu pot lua același ultim loc.
    // Întoarce false dacă nu mai sunt destule locuri.
    public async Task<bool> TryReserveAsync(int ticketTypeId, int quantity, CancellationToken ct = default)
    {
        var updated = await _db.TicketTypes
            .Where(t => t.Id == ticketTypeId && t.Sold + quantity <= t.Capacity)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Sold, t => t.Sold + quantity), ct);
        return updated == 1;
    }

    // Pune locurile înapoi în stoc (la anulare sau expirare)
    public async Task ReleaseAsync(IEnumerable<OrderItem> items, CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            var quantity = item.Quantity;
            await _db.TicketTypes
                .Where(t => t.Id == item.TicketTypeId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Sold, t => t.Sold - quantity), ct);
        }
    }

    // Anulează comanda și eliberează locurile, într-o tranzacție (totul sau nimic).
    // Condiția „încă e Pending” e verificată tot în UPDATE, ca aceeași comandă
    // să nu poată fi anulată de două ori (de ex. de client și de expirare în același timp).
    public async Task<bool> CancelAsync(Order order, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;
        var changed = await _db.Orders
            .Where(o => o.Id == order.Id && o.Status == OrderStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Cancelled)
                .SetProperty(o => o.CancelledAt, now), ct);
        if (changed == 0)
            return false;

        await ReleaseAsync(order.Items, ct);
        await transaction.CommitAsync(ct);

        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = now;
        return true;
    }
}
