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

public class OrdersControllerTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    // Stripe simulat, comun tuturor „cererilor” dintr-un test
    private readonly FakePaymentGateway _payments = new();

    // Clienții 1 și 2, organizatorul 3.
    // Evenimentul 10 (publicat): 100 „Standard” 50 lei, 10 locuri | 101 „VIP” 200 lei, 2 locuri, 1 vândut.
    // Evenimentul 20 nepublicat, 30 a trecut deja, 40 alt eveniment publicat.
    public OrdersControllerTests()
    {
        using var db = _database.CreateContext();
        var future = DateTime.UtcNow.AddMonths(1);
        db.Users.AddRange(
            new User { Id = 1, Email = "ana@example.com", FullName = "Ana", Role = UserRole.Client },
            new User { Id = 2, Email = "ion@example.com", FullName = "Ion", Role = UserRole.Client },
            new User { Id = 3, Email = "org@example.com", FullName = "Org", Role = UserRole.Organizer });
        db.Events.AddRange(
            new Event { Id = 10, Title = "Concert", Venue = "BT Arena", City = "Cluj-Napoca", StartsAt = future, IsPublished = true, OrganizerId = 3 },
            new Event { Id = 20, Title = "Ciornă", Venue = "BT Arena", City = "Cluj-Napoca", StartsAt = future, IsPublished = false, OrganizerId = 3 },
            new Event { Id = 30, Title = "Trecut", Venue = "BT Arena", City = "Cluj-Napoca", StartsAt = DateTime.UtcNow.AddDays(-1), IsPublished = true, OrganizerId = 3 },
            new Event { Id = 40, Title = "Altul", Venue = "Form Space", City = "Cluj-Napoca", StartsAt = future, IsPublished = true, OrganizerId = 3 });
        db.TicketTypes.AddRange(
            new TicketType { Id = 100, EventId = 10, Name = "Standard", Price = 50, Capacity = 10 },
            new TicketType { Id = 101, EventId = 10, Name = "VIP", Price = 200, Capacity = 2, Sold = 1 },
            new TicketType { Id = 200, EventId = 20, Name = "Standard", Price = 10, Capacity = 10 },
            new TicketType { Id = 300, EventId = 30, Name = "Standard", Price = 10, Capacity = 10 },
            new TicketType { Id = 400, EventId = 40, Name = "Standard", Price = 10, Capacity = 10 });
        db.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    // Fiecare „cerere” primește propriul DbContext, ca în aplicația reală
    private (AppDbContext Db, OrdersController Controller) As(int userId)
    {
        var db = _database.CreateContext();
        var controller = new OrdersController(
            db,
            new SeatReservationService(db),
            _payments,
            TestHelpers.CreateExpiryService(db, _payments),
            Options.Create(new StripeOptions { ClientBaseUrl = "http://localhost:4200" }));
        TestHelpers.LoginAs(controller, userId, "Client");
        return (db, controller);
    }

    private int Sold(int ticketTypeId)
    {
        using var db = _database.CreateContext();
        return db.TicketTypes.Single(t => t.Id == ticketTypeId).Sold;
    }

    private static CreateOrderRequest Request(int eventId, params (int TicketTypeId, int Quantity)[] items) =>
        new(eventId, items.Select(i => new OrderItemRequest(i.TicketTypeId, i.Quantity)).ToList());

    private static OrderDto Created(ActionResult<OrderDto> result) =>
        Assert.IsType<OrderDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);

    [Fact]
    public async Task Create_RezervaLocurileSiCalculeazaPretulPeServer()
    {
        var (db, controller) = As(1);
        using var _ = db;
        var before = DateTime.UtcNow;

        var order = Created(await controller.Create(Request(10, (100, 3), (101, 1)), default));

        Assert.Equal("Pending", order.Status);
        Assert.Equal(350m, order.TotalAmount); // 3 × 50 + 1 × 200, din prețurile din baza de date
        Assert.Equal(3, Sold(100));
        Assert.Equal(2, Sold(101));
        Assert.InRange(order.ExpiresAt!.Value, before.AddMinutes(14), before.AddMinutes(16));
    }

    [Fact]
    public async Task Create_AceeasiCategorieDeDouaOri_DevineOSinguraLinie()
    {
        var (db, controller) = As(1);
        using var _ = db;

        var order = Created(await controller.Create(Request(10, (100, 2), (100, 1)), default));

        var line = Assert.Single(order.Items);
        Assert.Equal(3, line.Quantity);
    }

    [Fact]
    public async Task Create_UltimulLoc_IlPrimesteDoarPrimulCumparator()
    {
        var (dbAna, ana) = As(1);
        var (dbIon, ion) = As(2);
        using var _ = dbAna;
        using var __ = dbIon;

        var first = await ana.Create(Request(10, (101, 1)), default);
        var second = await ion.Create(Request(10, (101, 1)), default);

        Assert.IsType<CreatedAtActionResult>(first.Result);
        Assert.IsType<ConflictObjectResult>(second.Result);
        Assert.Equal(2, Sold(101)); // niciodată peste capacitate
    }

    [Fact]
    public async Task Create_DacaOLinieNuMaiAreLocuri_NuSeRezervaNimic()
    {
        var (db, controller) = As(1);
        using var _ = db;

        // Standard ar merge, dar VIP mai are doar 1 loc: tranzacția anulează tot
        var result = await controller.Create(Request(10, (100, 2), (101, 2)), default);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(0, Sold(100));
        Assert.Equal(1, Sold(101));
        using var check = _database.CreateContext();
        Assert.Empty(check.Orders);
    }

    [Fact]
    public async Task Create_PesteZeceBilete_Returneaza400()
    {
        var (db, controller) = As(1);
        using var _ = db;

        var result = await controller.Create(Request(10, (100, 10), (101, 1)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(0, Sold(100));
    }

    [Fact]
    public async Task Create_BiletDeLaAltEveniment_Returneaza400()
    {
        var (db, controller) = As(1);
        using var _ = db;

        var result = await controller.Create(Request(10, (400, 1)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(0, Sold(400));
    }

    [Fact]
    public async Task Create_EvenimentNepublicat_Returneaza404()
    {
        var (db, controller) = As(1);
        using var _ = db;

        var result = await controller.Create(Request(20, (200, 1)), default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_EvenimentTrecut_Returneaza400()
    {
        var (db, controller) = As(1);
        using var _ = db;

        var result = await controller.Create(Request(30, (300, 1)), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_PesteLimitaDeRezervariNeplatite_Returneaza409()
    {
        var (db, controller) = As(1);
        using var _ = db;
        for (var i = 0; i < OrdersController.MaxPendingOrdersPerUser; i++)
            Created(await controller.Create(Request(10, (100, 1)), default));

        var result = await controller.Create(Request(10, (100, 1)), default);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(OrdersController.MaxPendingOrdersPerUser, Sold(100));
    }

    [Fact]
    public async Task Cancel_ElibereazaLocurile()
    {
        var (db, controller) = As(1);
        using var _ = db;
        var order = Created(await controller.Create(Request(10, (100, 4)), default));

        var result = await controller.Cancel(order.Id, default);

        var dto = Assert.IsType<OrderDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Cancelled", dto.Status);
        Assert.Equal(0, Sold(100));
    }

    [Fact]
    public async Task Cancel_DeDouaOri_ADouaOaraReturneaza409SiNuElibereazaDinNou()
    {
        var (db, controller) = As(1);
        using var _ = db;
        var order = Created(await controller.Create(Request(10, (101, 1)), default));
        await controller.Cancel(order.Id, default);

        var result = await controller.Cancel(order.Id, default);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(1, Sold(101)); // înapoi la cât era la început, nu mai puțin
    }

    [Fact]
    public async Task Cancel_ComandaAltcuiva_Returneaza404()
    {
        var (dbAna, ana) = As(1);
        var (dbIon, ion) = As(2);
        using var _ = dbAna;
        using var __ = dbIon;
        var order = Created(await ana.Create(Request(10, (100, 2)), default));

        var result = await ion.Cancel(order.Id, default);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(2, Sold(100));
    }

    [Fact]
    public async Task Get_RezervareExpiratDarPlatitaLaStripe_ArataComandaPlatitaCuBilete()
    {
        // Clientul a plătit, webhook-ul nu a ajuns, iar rezervarea a expirat între timp
        var (db, controller) = As(1);
        using var _ = db;
        var order = Created(await controller.Create(Request(10, (100, 2)), default));
        Assert.IsType<OkObjectResult>((await controller.Checkout(order.Id, default)).Result);
        _payments.SetPaid("cs_test_1", 10000);
        using (var clock = _database.CreateContext())
            await clock.Orders.Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));

        var (db2, controller2) = As(1);
        using var __ = db2;
        var dto = Assert.IsType<OrderDto>(Assert.IsType<OkObjectResult>((await controller2.Get(order.Id, default)).Result).Value);

        Assert.Equal("Paid", dto.Status);
        Assert.Equal(2, dto.Tickets.Count);
        Assert.Equal(2, Sold(100));   // locurile NU s-au eliberat

        // Și lista „comenzile mele” arată la fel
        var (db3, controller3) = As(1);
        using var ___ = db3;
        var mine = Assert.IsType<List<OrderDto>>(Assert.IsType<OkObjectResult>((await controller3.GetMine(default)).Result).Value);
        Assert.Equal("Paid", mine.Single().Status);
    }

    [Fact]
    public async Task Get_RezervareExpirata_SeAnuleazaSiElibereazaLocurile()
    {
        var (db, controller) = As(1);
        using var _ = db;
        var order = Created(await controller.Create(Request(10, (100, 3)), default));
        using (var clock = _database.CreateContext())
        {
            // Simulăm trecerea celor 15 minute
            await clock.Orders.Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        }

        var (db2, controller2) = As(1);
        using var __ = db2;
        var result = await controller2.Get(order.Id, default);

        var dto = Assert.IsType<OrderDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Cancelled", dto.Status);
        Assert.Equal(0, Sold(100));
    }

    [Fact]
    public async Task GetMine_ArataDoarComenzileUtilizatoruluiLogat()
    {
        var (dbAna, ana) = As(1);
        var (dbIon, ion) = As(2);
        using var _ = dbAna;
        using var __ = dbIon;
        var anaOrder = Created(await ana.Create(Request(10, (100, 1)), default));
        Created(await ion.Create(Request(10, (100, 1)), default));

        var result = await ana.GetMine(default);

        var orders = Assert.IsType<List<OrderDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(anaOrder.Id, Assert.Single(orders).Id);
    }

    // ---- Plata (Stripe Checkout) ----

    private static string CheckoutUrl(ActionResult<CheckoutResponse> result) =>
        Assert.IsType<CheckoutResponse>(Assert.IsType<OkObjectResult>(result.Result).Value).Url;

    private Order LoadOrder(int id)
    {
        using var db = _database.CreateContext();
        return db.Orders.Single(o => o.Id == id);
    }

    [Fact]
    public async Task Checkout_TrimiteLaStripeSumeleInBaniSiEmailulClientului()
    {
        var (db, controller) = As(1);
        using var _ = db;
        var order = Created(await controller.Create(Request(10, (100, 2), (101, 1)), default));

        var url = CheckoutUrl(await controller.Checkout(order.Id, default));

        var request = Assert.Single(_payments.Requests);
        Assert.Equal("ana@example.com", request.CustomerEmail);
        Assert.Equal("ron", request.Currency);
        // 50 lei = 5000 bani, 200 lei = 20000 bani
        Assert.Contains(request.Lines, l => l.Name == "Concert – Standard" && l.UnitAmountMinor == 5000 && l.Quantity == 2);
        Assert.Contains(request.Lines, l => l.Name == "Concert – VIP" && l.UnitAmountMinor == 20000 && l.Quantity == 1);
        Assert.EndsWith($"/comenzi/{order.Id}?plata=succes", request.SuccessUrl);
        Assert.StartsWith("https://checkout.stripe.test/", url);
    }

    [Fact]
    public async Task Checkout_SalveazaSesiuneaSiPrelungesteRezervareaCatTinePlata()
    {
        var (db, controller) = As(1);
        using var _ = db;
        var order = Created(await controller.Create(Request(10, (100, 1)), default));

        await controller.Checkout(order.Id, default);

        var saved = LoadOrder(order.Id);
        Assert.Equal("cs_test_1", saved.StripeSessionId);
        // 15 minute rezervare → prelungită la ~31 de minute, cât e valabilă sesiunea Stripe
        Assert.True(saved.ExpiresAt > DateTime.UtcNow.AddMinutes(30));
        Assert.Equal(OrderStatus.Pending, saved.Status); // plătită devine abia prin webhook (#12)
    }

    [Fact]
    public async Task Checkout_DeDouaOri_RefolosesteSesiuneaDeschisa()
    {
        var (db, controller) = As(1);
        using var _ = db;
        var order = Created(await controller.Create(Request(10, (100, 1)), default));

        var first = CheckoutUrl(await controller.Checkout(order.Id, default));
        var second = CheckoutUrl(await controller.Checkout(order.Id, default));

        Assert.Equal(first, second);
        Assert.Single(_payments.Requests);
    }

    [Fact]
    public async Task Checkout_SesiuneaVecheAExpirat_CreeazaUnaNoua()
    {
        var (db, controller) = As(1);
        using var _ = db;
        var order = Created(await controller.Create(Request(10, (100, 1)), default));
        await controller.Checkout(order.Id, default);
        _payments.SetStatus("cs_test_1", CheckoutSessionStatus.Expired);

        await controller.Checkout(order.Id, default);

        Assert.Equal(2, _payments.Requests.Count);
        Assert.Equal("cs_test_2", LoadOrder(order.Id).StripeSessionId);
    }

    [Fact]
    public async Task Checkout_ComandaAnulata_Returneaza409SiNuContacteazaStripe()
    {
        var (db, controller) = As(1);
        using var _ = db;
        var order = Created(await controller.Create(Request(10, (100, 1)), default));
        await controller.Cancel(order.Id, default);

        var result = await controller.Checkout(order.Id, default);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Empty(_payments.Requests);
    }

    [Fact]
    public async Task Checkout_ComandaAltcuiva_Returneaza404()
    {
        var (dbAna, ana) = As(1);
        var (dbIon, ion) = As(2);
        using var _ = dbAna;
        using var __ = dbIon;
        var order = Created(await ana.Create(Request(10, (100, 1)), default));

        var result = await ion.Checkout(order.Id, default);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Empty(_payments.Requests);
    }

    [Fact]
    public async Task Checkout_ComandaGratuita_NuTrecePrinStripe()
    {
        using (var setup = _database.CreateContext())
        {
            setup.TicketTypes.Add(new TicketType { Id = 102, EventId = 10, Name = "Invitație", Price = 0, Capacity = 5 });
            setup.SaveChanges();
        }
        var (db, controller) = As(1);
        using var _ = db;
        var order = Created(await controller.Create(Request(10, (102, 1)), default));

        var result = await controller.Checkout(order.Id, default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(_payments.Requests);
    }

    [Fact]
    public async Task Checkout_FaraCheieStripe_Returneaza503()
    {
        var (db, controller) = As(1);
        using var _ = db;
        var order = Created(await controller.Create(Request(10, (100, 1)), default));
        _payments.FailWith = new PaymentsNotConfiguredException();

        var result = await controller.Checkout(order.Id, default);

        Assert.Equal(503, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Theory]
    [InlineData(90, 9000)]
    [InlineData(90.5, 9050)]
    [InlineData(0.01, 1)]
    [InlineData(19.999, 2000)]
    public void ToMinorUnits_TransformaLeiInBani(decimal lei, long bani)
    {
        Assert.Equal(bani, Money.ToMinorUnits(lei));
    }
}
