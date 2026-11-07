using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using EventPass.Server.Services;
using EventPass.Server.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

    // Stripe cere ca o sesiune de plată să fie valabilă cel puțin 30 de minute
    public static readonly TimeSpan CheckoutSessionDuration = TimeSpan.FromMinutes(31);

    private readonly AppDbContext _db;
    private readonly SeatReservationService _seats;
    private readonly IPaymentGateway _payments;
    private readonly StripeOptions _stripe;

    public OrdersController(
        AppDbContext db,
        SeatReservationService seats,
        IPaymentGateway payments,
        IOptions<StripeOptions> stripe)
    {
        _db = db;
        _seats = seats;
        _payments = payments;
        _stripe = stripe.Value;
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

    // Pornește plata: creează (sau refolosește) o sesiune Stripe Checkout și întoarce adresa ei.
    // ATENȚIE: întoarcerea clientului pe site NU înseamnă că a plătit. Comanda devine Paid
    // doar când Stripe confirmă direct serverului, prin webhook (issue-ul #12).
    [HttpPost("{id:int}/checkout")]
    public async Task<ActionResult<CheckoutResponse>> Checkout(int id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var order = await LoadOrderAsync(id, userId, ct);
        if (order is null)
            return NotFound();

        await ExpireIfNeededAsync(order, ct);
        if (order.Status == OrderStatus.Paid)
            return Conflict(new { message = "Comanda este deja plătită." });
        if (order.Status != OrderStatus.Pending)
            return Conflict(new { message = "Rezervarea a expirat sau a fost anulată. Rezervă din nou biletele." });
        if (order.TotalAmount == 0)
            return BadRequest(new { message = "Comenzile gratuite nu trec prin plată; confirmarea lor vine în curând." });
        if (order.TotalAmount < Money.MinimumChargeRon)
            return BadRequest(new { message = $"Plata minimă acceptată este {Money.MinimumChargeRon} lei." });

        try
        {
            // Dacă există deja o sesiune deschisă (a apăsat „Plătește” de două ori), o refolosim
            if (order.StripeSessionId is not null)
            {
                var existing = await _payments.GetCheckoutSessionAsync(order.StripeSessionId, ct);
                if (existing is { Status: CheckoutSessionStatus.Open })
                    return Ok(new CheckoutResponse(existing.Url));
                if (existing is { Status: CheckoutSessionStatus.Complete })
                    return Conflict(new { message = "Plata a fost deja făcută și se confirmă." });
            }

            var email = await _db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleAsync(ct);
            var orderUrl = $"{_stripe.ClientBaseUrl.TrimEnd('/')}/comenzi/{order.Id}";
            var session = await _payments.CreateCheckoutSessionAsync(new CheckoutRequest(
                order.Id,
                email,
                "ron",
                order.Items
                    .Select(i => new CheckoutLine(
                        $"{i.TicketType.Event.Title} – {i.TicketType.Name}",
                        Money.ToMinorUnits(i.UnitPrice),
                        i.Quantity))
                    .ToList(),
                SuccessUrl: $"{orderUrl}?plata=succes",
                CancelUrl: $"{orderUrl}?plata=anulata",
                ExpiresAt: DateTime.UtcNow.Add(CheckoutSessionDuration)), ct);

            // Salvăm sesiunea și prelungim rezervarea cât e valabilă plata,
            // ca locurile să nu expire în timp ce clientul e pe pagina Stripe.
            // Condiția Status == Pending: dacă între timp s-a anulat, nu o „reînviem”.
            var updated = await _db.Orders
                .Where(o => o.Id == order.Id && o.Status == OrderStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.StripeSessionId, session.Id)
                    .SetProperty(o => o.ExpiresAt, session.ExpiresAt), ct);
            if (updated == 0)
                return Conflict(new { message = "Rezervarea a expirat sau a fost anulată. Rezervă din nou biletele." });

            // ExecuteUpdate scrie direct în baza de date; actualizăm și obiectul din memorie
            order.StripeSessionId = session.Id;
            order.ExpiresAt = session.ExpiresAt;

            return Ok(new CheckoutResponse(session.Url));
        }
        catch (PaymentsNotConfiguredException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Plățile nu sunt configurate pe server (lipsește cheia Stripe)." });
        }
        catch (PaymentGatewayException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "Nu am putut contacta procesatorul de plăți. Încearcă din nou." });
        }
    }

    // O rezervare expirată se anulează când cineva se uită la ea. Comenzile pe care
    // nu le mai deschide nimeni le va anula un proces automat (issue-ul #18).
    private async Task ExpireIfNeededAsync(Order order, CancellationToken ct)
    {
        if (order.IsExpired(DateTime.UtcNow))
            await _seats.CancelAsync(order, ct);
    }

    private IQueryable<Order> OrdersQuery() => _db.Orders
        .Include(o => o.Tickets)
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
                .ToList(),
            // Numele categoriei îl luăm din liniile comenzii, deja încărcate
            order.Tickets
                .OrderBy(t => t.Id)
                .Select(t => new TicketDto(
                    t.Id,
                    t.Token,
                    order.Items.First(i => i.TicketTypeId == t.TicketTypeId).TicketType.Name,
                    t.IsUsed))
                .ToList(),
            order.TicketsEmailedAt);
    }
}
