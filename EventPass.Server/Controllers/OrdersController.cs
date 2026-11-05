using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using EventPass.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Controllers;

// Comenzile utilizatorului logat. Fiecare vede doar comenzile lui (404 pentru ale altora).
[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    public const int MaxTicketsPerOrder = 10;
    // Câte rezervări neplătite poate avea cineva odată (ca să nu blocheze tot stocul)
    public const int MaxPendingOrdersPerUser = 3;

    private readonly AppDbContext _db;
    private readonly SeatReservationService _seats;

    public OrdersController(AppDbContext db, SeatReservationService seats)
    {
        _db = db;
        _seats = seats;
    }

    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();

        // Aceeași categorie trimisă de două ori se adună într-o singură linie
        var wanted = request.Items
            .GroupBy(i => i.TicketTypeId)
            .Select(g => new { TicketTypeId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToList();

        if (wanted.Sum(i => i.Quantity) > MaxTicketsPerOrder)
            return BadRequest(new { message = $"Poți cumpăra cel mult {MaxTicketsPerOrder} bilete într-o comandă." });

        var ev = await _db.Events
            .AsNoTracking()
            .Include(e => e.TicketTypes)
            .SingleOrDefaultAsync(e => e.Id == request.EventId && e.IsPublished, ct);
        if (ev is null)
            return NotFound(new { message = "Evenimentul nu există sau nu este disponibil." });
        if (ev.StartsAt <= DateTime.UtcNow)
            return BadRequest(new { message = "Evenimentul a avut deja loc." });

        // Toate tipurile de bilete trebuie să fie de la ACEST eveniment
        var ticketTypes = ev.TicketTypes.ToDictionary(t => t.Id);
        if (wanted.Any(i => !ticketTypes.ContainsKey(i.TicketTypeId)))
            return BadRequest(new { message = "Unul dintre biletele alese nu aparține acestui eveniment." });

        var pendingCount = await _db.Orders.CountAsync(
            o => o.UserId == userId && o.Status == OrderStatus.Pending && o.ExpiresAt > DateTime.UtcNow, ct);
        if (pendingCount >= MaxPendingOrdersPerUser)
            return Conflict(new { message = "Ai deja prea multe rezervări neplătite. Plătește sau anulează una dintre ele." });

        // Tranzacție: dacă rezervarea eșuează la oricare linie, nu rămâne nimic rezervat
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        foreach (var item in wanted)
        {
            if (!await _seats.TryReserveAsync(item.TicketTypeId, item.Quantity, ct))
            {
                await transaction.RollbackAsync(ct);
                var available = await _db.TicketTypes
                    .Where(t => t.Id == item.TicketTypeId)
                    .Select(t => t.Capacity - t.Sold)
                    .SingleAsync(ct);
                var name = ticketTypes[item.TicketTypeId].Name;
                return Conflict(new
                {
                    message = available == 0
                        ? $"Biletele \"{name}\" s-au epuizat."
                        : $"Mai sunt doar {available} bilete \"{name}\"."
                });
            }
        }

        // Prețul vine din baza de date, nu de la client
        var now = DateTime.UtcNow;
        var order = new Order
        {
            UserId = userId,
            Status = OrderStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now.Add(SeatReservationService.HoldDuration),
            Items = wanted.Select(i => new OrderItem
            {
                TicketTypeId = i.TicketTypeId,
                Quantity = i.Quantity,
                UnitPrice = ticketTypes[i.TicketTypeId].Price
            }).ToList()
        };
        order.TotalAmount = order.Items.Sum(i => i.UnitPrice * i.Quantity);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var saved = await LoadOrderAsync(order.Id, userId, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, ToDto(saved!));
    }

    [HttpGet("mine")]
    public async Task<ActionResult<List<OrderDto>>> GetMine(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var orders = await OrdersQuery()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        foreach (var order in orders)
            await ExpireIfNeededAsync(order, ct);

        return Ok(orders.Select(ToDto).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDto>> Get(int id, CancellationToken ct)
    {
        var order = await LoadOrderAsync(id, User.GetUserId(), ct);
        if (order is null)
            return NotFound();

        await ExpireIfNeededAsync(order, ct);
        return Ok(ToDto(order));
    }

    // Clientul renunță la rezervare: comanda devine Cancelled și locurile se eliberează
    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<OrderDto>> Cancel(int id, CancellationToken ct)
    {
        var order = await LoadOrderAsync(id, User.GetUserId(), ct);
        if (order is null)
            return NotFound();

        if (!await _seats.CancelAsync(order, ct))
            return Conflict(new { message = "Doar o comandă neplătită poate fi anulată." });

        return Ok(ToDto(order));
    }

    // O rezervare expirată se anulează când cineva se uită la ea. Comenzile pe care
    // nu le mai deschide nimeni le va anula un proces automat (issue-ul #18).
    private async Task ExpireIfNeededAsync(Order order, CancellationToken ct)
    {
        if (order.IsExpired(DateTime.UtcNow))
            await _seats.CancelAsync(order, ct);
    }

    private IQueryable<Order> OrdersQuery() => _db.Orders
        .Include(o => o.Items)
            .ThenInclude(i => i.TicketType)
                .ThenInclude(t => t.Event);

    private Task<Order?> LoadOrderAsync(int id, int userId, CancellationToken ct) =>
        OrdersQuery().SingleOrDefaultAsync(o => o.Id == id && o.UserId == userId, ct);

    private static OrderDto ToDto(Order order)
    {
        var ev = order.Items[0].TicketType.Event;
        return new OrderDto(
            order.Id,
            order.Status.ToString(),
            order.TotalAmount,
            order.CreatedAt,
            order.ExpiresAt,
            order.PaidAt,
            new OrderEventDto(ev.Id, ev.Title, ev.Venue, ev.City, ev.StartsAt, ev.ImagePath),
            order.Items
                .OrderBy(i => i.UnitPrice)
                .Select(i => new OrderLineDto(i.TicketTypeId, i.TicketType.Name, i.Quantity, i.UnitPrice, i.UnitPrice * i.Quantity))
                .ToList());
    }
}
