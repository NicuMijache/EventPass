using EventPass.Server.Controllers;
using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using EventPass.Server.Services;
using EventPass.Server.Services.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventPass.Tests;

// Teste de CURSĂ pentru stoc (#18): multe „cereri” în paralel, fiecare cu propria conexiune la aceeași bază de date
// (un fișier SQLite temporar). Așa cererile chiar se întrec, ca pe server, nu rulează una după alta.
public sealed class StockRaceTests : IDisposable
{
    private const int Seats = 5;
    private const int Buyers = 30;

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"eventpass-cursa-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly FakePaymentGateway _stripe = new();

    // Un eveniment cu 5 locuri și 30 de clienți
    public StockRaceTests()
    {
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_file};Pooling=False;Default Timeout=60")
            .Options;
        using var db = Db();
        db.Database.EnsureCreated();
        db.Users.Add(new User { Id = 1000, Email = "org@example.com", FullName = "Org", Role = UserRole.Organizer });
        db.Users.AddRange(Enumerable.Range(1, Buyers).Select(i =>
            new User { Id = i, Email = $"client{i}@example.com", FullName = $"Client {i}", Role = UserRole.Client }));
        db.Events.Add(new Event { Id = 1, Title = "Concert", Venue = "Sala", City = "Cluj", StartsAt = DateTime.UtcNow.AddDays(10), IsPublished = true, OrganizerId = 1000 });
        db.TicketTypes.Add(new TicketType { Id = 1, EventId = 1, Name = "Standard", Price = 50, Capacity = Seats });
        db.SaveChanges();
    }

    public void Dispose() => File.Delete(_file);

    private AppDbContext Db() => new(_options);

    private int Sold()
    {
        using var db = Db();
        return db.TicketTypes.Single().Sold;
    }

    // Stocul trebuie să fie EXACT suma locurilor din comenzile care încă le țin (Pending sau Paid)
    private int HeldBySavedOrders()
    {
        using var db = Db();
        return db.OrderItems.Where(i => i.Order.Status == OrderStatus.Pending || i.Order.Status == OrderStatus.Paid).Sum(i => i.Quantity);
    }

    // O cerere „POST /api/orders” a clientului `userId`, cu propriul DbContext
    private async Task<ActionResult<OrderDto>> Buy(int userId, int quantity = 1)
    {
        await using var db = Db();
        var controller = new OrdersController(db, new SeatReservationService(db), _stripe,
            TestHelpers.CreateExpiryService(db, _stripe), Options.Create(new StripeOptions()));
        TestHelpers.LoginAs(controller, userId, "Client");
        return await controller.Create(new CreateOrderRequest(1, [new OrderItemRequest(1, quantity)]), default);
    }

    [Fact]
    public async Task TreizeciDeClientiSimultan_PentruCinciLocuri_DoarCinciPrimescBilete()
    {
        var results = await Task.WhenAll(Enumerable.Range(1, Buyers).Select(id => Task.Run(() => Buy(id))));

        var created = results.Count(r => r.Result is CreatedAtActionResult);
        var refused = results.Count(r => r.Result is ConflictObjectResult);
        Assert.Equal(Seats, created);
        Assert.Equal(Buyers - Seats, refused);
        Assert.Equal(Seats, Sold());                 // nu 6, nu 30: nicio supra-vânzare
        Assert.Equal(Seats, HeldBySavedOrders());    // și fiecare loc ocupat aparține unei comenzi salvate
    }

    [Fact]
    public async Task CumparaturiDeCateDouaLocuri_NuDepasescNiciodataCapacitatea()
    {
        // 5 locuri, câte 2 pe comandă: maxim 2 comenzi (4 locuri); al 5-lea loc rămâne liber, nu „se rupe” în 2
        var results = await Task.WhenAll(Enumerable.Range(1, 12).Select(id => Task.Run(() => Buy(id, quantity: 2))));

        Assert.Equal(2, results.Count(r => r.Result is CreatedAtActionResult));
        Assert.Equal(4, Sold());
        Assert.Equal(4, HeldBySavedOrders());
    }

    [Fact]
    public async Task ExpirareaSiCumparareaInAcelasiTimp_StoculRamaneCorect()
    {
        // Toate cele 5 locuri sunt ținute de rezervări EXPIRATE. Procesul de fundal le eliberează exact
        // în timp ce 20 de clienți noi încearcă să cumpere.
        await Task.WhenAll(Enumerable.Range(1, Seats).Select(id => Buy(id)));
        await using (var db = Db())
            await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal(Seats, Sold());

        var worker = Task.Run(async () =>
        {
            await using var db = Db();
            return await TestHelpers.CreateExpiryService(db, _stripe).ExpireDueAsync();
        });
        var buyers = Enumerable.Range(Seats + 1, 20).Select(id => Task.Run(() => Buy(id))).ToList();
        await Task.WhenAll(buyers.Append<Task>(worker));

        var created = buyers.Count(b => b.Result.Result is CreatedAtActionResult);
        Assert.Equal(Seats, await worker);              // toate cele 5 rezervări expirate au fost anulate
        Assert.InRange(created, 0, Seats);              // cine a prins un loc eliberat, l-a primit
        Assert.Equal(created, Sold());                  // stocul = exact locurile cumpărate acum
        Assert.Equal(Sold(), HeldBySavedOrders());
    }

    [Fact]
    public async Task ProcesulDeFundalSiClientul_AnuleazaSimultanAceeasiComanda_LocurileSeElibereazaODataSingura()
    {
        var order = Assert.IsType<OrderDto>(Assert.IsType<CreatedAtActionResult>((await Buy(1, quantity: 3)).Result).Value);
        await using (var db = Db())
            await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));

        // 10 „procese” încearcă să expire aceeași comandă în același timp
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            await using var db = Db();
            var loaded = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
            return await TestHelpers.CreateExpiryService(db, _stripe).ExpireAsync(loaded);
        })));

        Assert.Equal(1, outcomes.Count(o => o == ExpiryOutcome.Cancelled));
        // Ceilalți: fie au citit-o încă Pending și au pierdut cursa (AlreadyHandled),
        // fie au citit-o deja anulată, deci nu mai era nimic de expirat (NotExpired)
        Assert.All(outcomes, o => Assert.Contains(o, new[] { ExpiryOutcome.Cancelled, ExpiryOutcome.AlreadyHandled, ExpiryOutcome.NotExpired }));
        Assert.Equal(0, Sold());   // 3 locuri eliberate o singură dată (nu -27)
    }

    [Fact]
    public async Task PlataSiExpirareaInAcelasiTimp_ComandaEsteSauPlatitaSauAnulata_NiciodataAmandoua()
    {
        // Webhook-ul de plată și procesul de fundal se întrec. Stripe spune „expirată” procesului de fundal
        // (cazul cel mai rău pentru noi), dar webhook-ul aduce totuși o plată.
        var order = Assert.IsType<OrderDto>(Assert.IsType<CreatedAtActionResult>((await Buy(1, quantity: 2)).Result).Value);
        await using (var db = Db())
            await db.Orders.ExecuteUpdateAsync(s => s
                .SetProperty(o => o.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))
                .SetProperty(o => o.StripeSessionId, "cs_test_cursa"));
        _stripe.Sessions["cs_test_cursa"] = new CheckoutSession("cs_test_cursa", "u", CheckoutSessionStatus.Expired, DateTime.UtcNow);

        var expire = Task.Run(async () =>
        {
            await using var db = Db();
            var loaded = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);
            return await TestHelpers.CreateExpiryService(db, _stripe).ExpireAsync(loaded);
        });
        var pay = Task.Run(async () =>
        {
            await using var db = Db();
            return await new OrderPaymentService(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderPaymentService>.Instance)
                .ConfirmPaymentAsync(order.Id, "cs_test_cursa", 10000, "ron");
        });
        await Task.WhenAll(expire, pay);

        await using var check = Db();
        var saved = await check.Orders.SingleAsync(o => o.Id == order.Id);
        var tickets = await check.Tickets.CountAsync();
        if (saved.Status == OrderStatus.Paid)
        {
            Assert.Equal(PaymentConfirmation.Paid, pay.Result);
            Assert.Equal(2, tickets);
            Assert.Equal(2, Sold());   // locurile rămân ale celui care a plătit
        }
        else
        {
            Assert.Equal(OrderStatus.Cancelled, saved.Status);
            Assert.Equal(PaymentConfirmation.OrderNotPending, pay.Result);   // ajunge în jurnal pentru rambursare (#19)
            Assert.Equal(0, tickets);
            Assert.Equal(0, Sold());
        }
    }
}
