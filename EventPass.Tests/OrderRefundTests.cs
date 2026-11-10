using EventPass.Server.Controllers;
using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using EventPass.Server.Services;
using EventPass.Server.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Reflection;
using static EventPass.Tests.PaymentTestData;

namespace EventPass.Tests;

// Rambursări (#19). Comanda 1 (Ana, evenimentul 10 al organizatorului 3) e PLĂTITĂ: 3 bilete,
// locurile ocupate: 2 × Standard (tipul 100), 1 × VIP (tipul 101). Evenimentul e peste o lună.
public class OrderRefundTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly FakePaymentGateway _stripe = new();

    public OrderRefundTests()
    {
        Seed(_database);
        using (var db = _database.CreateContext())
        {
            db.Users.AddRange(
                new User { Id = 2, Email = "ion@example.com", FullName = "Ion", Role = UserRole.Client },
                new User { Id = 4, Email = "org2@example.com", FullName = "Org 2", Role = UserRole.Organizer },
                new User { Id = 9, Email = "admin@example.com", FullName = "Admin", Role = UserRole.Admin });
            db.SaveChanges();
            new OrderPaymentService(db, NullLogger<OrderPaymentService>.Instance)
                .ConfirmPaymentAsync(OrderId, SessionId, AmountMinor, "ron").GetAwaiter().GetResult();
        }
        _stripe.SetPaid(SessionId, AmountMinor);
    }

    public void Dispose() => _database.Dispose();

    private async Task<RefundOutcome> Refund()
    {
        await using var db = _database.CreateContext();
        return await TestHelpers.CreateRefundService(db, _stripe).RefundAsync(OrderId);
    }

    private (Order Order, List<Ticket> Tickets, int Standard, int Vip) State()
    {
        using var db = _database.CreateContext();
        return (
            db.Orders.AsNoTracking().Single(o => o.Id == OrderId),
            db.Tickets.AsNoTracking().Where(t => t.OrderId == OrderId).OrderBy(t => t.Id).ToList(),
            db.TicketTypes.Single(t => t.Id == 100).Sold,
            db.TicketTypes.Single(t => t.Id == 101).Sold);
    }

    private void UseFirstTicket()
    {
        using var db = _database.CreateContext();
        var id = db.Tickets.Where(t => t.OrderId == OrderId).OrderBy(t => t.Id).Select(t => t.Id).First();
        db.Tickets.Where(t => t.Id == id).ExecuteUpdate(s => s.SetProperty(t => t.IsUsed, true).SetProperty(t => t.UsedAt, DateTime.UtcNow));
    }

    private async Task<TicketValidationDto> Scan(Guid token)
    {
        await using var db = _database.CreateContext();
        return await new TicketValidationService(db, NullLogger<TicketValidationService>.Instance).ValidateAsync(token.ToString(), 5, null);
    }

    // ---------- Serviciul ----------

    [Fact]
    public async Task Rambursare_DaBaniiInapoi_AnuleazaBileteleSiElibereazaLocurile()
    {
        var outcome = await Refund();

        Assert.Equal(RefundOutcome.Refunded, outcome);
        var (order, tickets, standard, vip) = State();
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal("re_test_1", order.StripeRefundId);
        Assert.NotNull(order.RefundedAt);
        Assert.Equal(3, tickets.Count);
        Assert.All(tickets, t => Assert.NotNull(t.VoidedAt));
        Assert.Equal((0, 0), (standard, vip));   // locurile se pot vinde din nou
        Assert.Equal(new[] { "eventpass-refund-order-1" }, _stripe.Refunds.Keys);
    }

    [Fact]
    public async Task BiletRambursat_LaIntrare_EsteRespins()
    {
        var token = State().Tickets[0].Token;
        await Refund();

        var scan = await Scan(token);

        Assert.Equal("NotPaid", scan.Result);
        Assert.Equal("Bilet anulat: comanda a fost rambursată.", scan.Message);
        Assert.False(State().Tickets[0].IsUsed);
    }

    [Fact]
    public async Task DeDouaOri_BaniiPleacaOSinguraData()
    {
        Assert.Equal(RefundOutcome.Refunded, await Refund());
        Assert.Equal(RefundOutcome.Refunded, await Refund());

        Assert.Single(_stripe.Refunds);
        Assert.Equal(1, _stripe.RefundCalls);       // a doua oară nici nu mai întreabă Stripe
        Assert.Equal((0, 0), (State().Standard, State().Vip));   // locurile nu se eliberează de două ori
    }

    [Fact]
    public async Task UnBiletFolosit_NuSeRamburseaza_SiNimicNuSeSchimba()
    {
        UseFirstTicket();

        var outcome = await Refund();

        Assert.Equal(RefundOutcome.TicketsUsed, outcome);
        var (order, tickets, standard, vip) = State();
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.All(tickets, t => Assert.Null(t.VoidedAt));   // nici biletele nefolosite nu s-au anulat (tranzacția)
        Assert.Equal((2, 1), (standard, vip));
        Assert.Equal(0, _stripe.RefundCalls);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task ComandaNeplatita_NuSeRamburseaza(OrderStatus status)
    {
        using (var db = _database.CreateContext())
            db.Orders.Where(o => o.Id == OrderId).ExecuteUpdate(s => s.SetProperty(o => o.Status, status));

        Assert.Equal(RefundOutcome.NotPaid, await Refund());
        Assert.Equal(0, _stripe.RefundCalls);
    }

    [Fact]
    public async Task StripeIndisponibil_BileteleRamanAnulate_ApoiProcesulDeFundalTermina()
    {
        var token = State().Tickets[0].Token;
        _stripe.RefundFailsWith = new PaymentGatewayException("timeout", new TimeoutException());

        var outcome = await Refund();

        Assert.Equal(RefundOutcome.InProgress, outcome);
        var (order, tickets, standard, vip) = State();
        Assert.Equal(OrderStatus.RefundPending, order.Status);
        Assert.Equal(1, order.RefundAttempts);
        Assert.All(tickets, t => Assert.NotNull(t.VoidedAt));    // biletele nu mai deschid ușa...
        Assert.Equal((2, 1), (standard, vip));                  // ...dar locurile nu se revând înainte de rambursare
        Assert.Equal("NotPaid", (await Scan(token)).Result);

        // Stripe își revine; procesul de fundal reia rambursarea (după minutul de „pauză”)
        _stripe.RefundFailsWith = null;
        using (var db = _database.CreateContext())
            db.Orders.Where(o => o.Id == OrderId).ExecuteUpdate(s => s.SetProperty(o => o.RefundRequestedAt, DateTime.UtcNow.AddMinutes(-2)));
        await using (var db = _database.CreateContext())
            Assert.Equal(1, await TestHelpers.CreateRefundService(db, _stripe).CompletePendingAsync());

        Assert.Equal(OrderStatus.Refunded, State().Order.Status);
        Assert.Equal((0, 0), (State().Standard, State().Vip));
        Assert.Single(_stripe.Refunds);
    }

    [Fact]
    public async Task ProcesulDeFundal_NuAtingeRambursariAbiaIncepute_SauCeleCuPreaMulteEsecuri()
    {
        _stripe.RefundFailsWith = new PaymentGatewayException("timeout", new TimeoutException());
        await Refund();
        _stripe.RefundFailsWith = null;

        await using (var db = _database.CreateContext())
            Assert.Equal(0, await TestHelpers.CreateRefundService(db, _stripe).CompletePendingAsync());   // abia începută

        using (var db = _database.CreateContext())
            db.Orders.Where(o => o.Id == OrderId).ExecuteUpdate(s => s
                .SetProperty(o => o.RefundRequestedAt, DateTime.UtcNow.AddMinutes(-5))
                .SetProperty(o => o.RefundAttempts, OrderRefundService.MaxAttempts));
        await using (var db = _database.CreateContext())
            Assert.Equal(0, await TestHelpers.CreateRefundService(db, _stripe).CompletePendingAsync());   // lăsată unui om

        Assert.Equal(OrderStatus.RefundPending, State().Order.Status);
    }

    [Fact]
    public async Task ComandaGratuita_SeRamburseazaFaraStripe()
    {
        using (var db = _database.CreateContext())
            db.Orders.Where(o => o.Id == OrderId).ExecuteUpdate(s => s.SetProperty(o => o.StripeSessionId, (string?)null));

        Assert.Equal(RefundOutcome.Refunded, await Refund());
        Assert.Null(State().Order.StripeRefundId);
        Assert.Equal(0, _stripe.RefundCalls);
    }

    // ---------- Curse ----------

    [Fact]
    public async Task ZeceCereriSimultane_OSinguraRambursare_LocurileEliberateOData()
    {
        var file = Path.Combine(Path.GetTempPath(), $"eventpass-rambursare-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={file};Pooling=False;Default Timeout=60").Options;
            await SeedPaidOrder(options, tickets: 3);

            var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                await using var db = new AppDbContext(options);
                return await TestHelpers.CreateRefundService(db, _stripe).RefundAsync(1);
            })));

            Assert.All(outcomes, o => Assert.Equal(RefundOutcome.Refunded, o));
            Assert.Single(_stripe.Refunds);
            await using var check = new AppDbContext(options);
            Assert.Equal(0, check.TicketTypes.Single().Sold);   // 3 locuri eliberate o dată (nu -27)
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ScanareSiRambursareInAcelasiTimp_NiciodataAmandoua()
    {
        // Persoana e la intrare și, în aceeași clipă, cere banii înapoi de pe telefon.
        // Rezultat permis: ori intră (și rambursarea e refuzată), ori primește banii (și biletul e respins).
        var file = Path.Combine(Path.GetTempPath(), $"eventpass-rambursare-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={file};Pooling=False;Default Timeout=60").Options;
            for (var round = 0; round < 15; round++)
            {
                var (orderId, token) = await SeedPaidOrder(options, tickets: 1, orderId: round + 1);

                var scan = Task.Run(async () =>
                {
                    await using var db = new AppDbContext(options);
                    return await new TicketValidationService(db, NullLogger<TicketValidationService>.Instance).ValidateAsync(token.ToString(), 5, null);
                });
                var refund = Task.Run(async () =>
                {
                    await using var db = new AppDbContext(options);
                    return await TestHelpers.CreateRefundService(db, _stripe).RefundAsync(orderId);
                });
                await Task.WhenAll(scan, refund);

                if (scan.Result.Result == "Valid")
                    Assert.Equal(RefundOutcome.TicketsUsed, refund.Result);
                else
                    Assert.Equal(RefundOutcome.Refunded, refund.Result);
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    // O comandă plătită, într-o bază separată (pentru cursele cu conexiuni reale)
    private async Task<(int OrderId, Guid Token)> SeedPaidOrder(DbContextOptions<AppDbContext> options, int tickets, int orderId = 1)
    {
        await using var db = new AppDbContext(options);
        if (await db.Database.EnsureCreatedAsync())
        {
            db.Users.AddRange(
                new User { Id = 1, Email = "a@example.com", FullName = "A", Role = UserRole.Client },
                new User { Id = 3, Email = "o@example.com", FullName = "O", Role = UserRole.Organizer });
            db.Events.Add(new Event { Id = 10, Title = "X", Venue = "V", City = "C", StartsAt = DateTime.UtcNow.AddDays(30), OrganizerId = 3 });
            db.TicketTypes.Add(new TicketType { Id = 100, EventId = 10, Name = "S", Price = 10, Capacity = 100 });
        }
        var session = $"cs_cursa_{orderId}";
        var order = new Order
        {
            Id = orderId, UserId = 1, Status = OrderStatus.Paid, TotalAmount = 10 * tickets, StripeSessionId = session,
            Items = { new OrderItem { TicketTypeId = 100, Quantity = tickets, UnitPrice = 10 } },
        };
        for (var i = 0; i < tickets; i++)
            order.Tickets.Add(new Ticket { TicketTypeId = 100 });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        // Locurile comenzii sunt ocupate (după ce tipul de bilet există în baza de date)
        await db.TicketTypes.ExecuteUpdateAsync(s => s.SetProperty(t => t.Sold, t => t.Sold + tickets));
        _stripe.SetPaid(session, 1000L * tickets);
        return (orderId, order.Tickets[0].Token);
    }

    // ---------- Endpoint-ul clientului ----------

    private (AppDbContext Db, OrdersController Controller) Client(int userId)
    {
        var db = _database.CreateContext();
        var controller = new OrdersController(db, new SeatReservationService(db), _stripe,
            TestHelpers.CreateExpiryService(db, _stripe), TestHelpers.CreateRefundService(db, _stripe),
            Options.Create(new StripeOptions()));
        TestHelpers.LoginAs(controller, userId, "Client");
        return (db, controller);
    }

    [Fact]
    public async Task Client_ComandaPlatita_PoateCereRambursarea_SiVedeBileteleAnulate()
    {
        var (db, controller) = Client(1);
        using var _ = db;
        var before = Assert.IsType<OrderDto>(Assert.IsType<OkObjectResult>((await controller.Get(OrderId, default)).Result).Value);
        Assert.True(before.CanRefund);

        var result = await controller.Refund(OrderId, default);

        var dto = Assert.IsType<OrderDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Refunded", dto.Status);
        Assert.False(dto.CanRefund);
        Assert.NotNull(dto.RefundedAt);
        Assert.All(dto.Tickets, t => Assert.True(t.IsVoided));
    }

    [Fact]
    public async Task Client_CuMaiPutinDe24DeOreInainte_NuMaiPoateRambursa()
    {
        using (var db = _database.CreateContext())
            db.Events.Where(e => e.Id == 10).ExecuteUpdate(s => s.SetProperty(e => e.StartsAt, DateTime.UtcNow.AddHours(10)));
        var (db2, controller) = Client(1);
        using var _ = db2;

        var dto = Assert.IsType<OrderDto>(Assert.IsType<OkObjectResult>((await controller.Get(OrderId, default)).Result).Value);
        var result = await controller.Refund(OrderId, default);

        Assert.False(dto.CanRefund);
        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(OrderStatus.Paid, State().Order.Status);
        Assert.Equal(0, _stripe.RefundCalls);
    }

    [Fact]
    public async Task Client_BiletFolosit_NuPoateRambursa()
    {
        UseFirstTicket();
        var (db, controller) = Client(1);
        using var _ = db;

        var dto = Assert.IsType<OrderDto>(Assert.IsType<OkObjectResult>((await controller.Get(OrderId, default)).Result).Value);
        var result = await controller.Refund(OrderId, default);

        Assert.False(dto.CanRefund);
        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Client_ComandaAltcuiva_Returneaza404()
    {
        var (db, controller) = Client(2);
        using var _ = db;

        Assert.IsType<NotFoundResult>((await controller.Refund(OrderId, default)).Result);
        Assert.Equal(OrderStatus.Paid, State().Order.Status);
    }

    [Fact]
    public async Task Client_TermenulRambursarii_E24DeOreInainteaEvenimentului()
    {
        var (db, controller) = Client(1);
        using var _ = db;
        var dto = Assert.IsType<OrderDto>(Assert.IsType<OkObjectResult>((await controller.Get(OrderId, default)).Result).Value);

        Assert.Equal(dto.Event.StartsAt.AddHours(-24), dto.RefundDeadline);
    }

    // ---------- Anularea unei rezervări neplătite (pagina de plată deschisă) ----------

    private void MakePending(CheckoutSessionStatus stripeStatus, bool paid = false)
    {
        using (var db = _database.CreateContext())
        {
            db.Tickets.ExecuteDelete();
            db.Orders.Where(o => o.Id == OrderId).ExecuteUpdate(s => s.SetProperty(o => o.Status, OrderStatus.Pending));
        }
        _stripe.Sessions[SessionId] = new CheckoutSession(SessionId, "u", stripeStatus, DateTime.UtcNow.AddMinutes(30), paid);
    }

    [Fact]
    public async Task Anulare_CuPaginaDePlataDeschisa_OInchideIntaiLaStripe()
    {
        MakePending(CheckoutSessionStatus.Open);
        var (db, controller) = Client(1);
        using var _ = db;

        var result = await controller.Cancel(OrderId, default);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(1, _stripe.ExpireCalls);
        Assert.Equal(CheckoutSessionStatus.Expired, _stripe.Sessions[SessionId].Status);   // nu se mai poate plăti
        Assert.Equal(OrderStatus.Cancelled, State().Order.Status);
    }

    [Fact]
    public async Task Anulare_DupaCeClientulAPlatitDeja_EsteRefuzata()
    {
        MakePending(CheckoutSessionStatus.Complete, paid: true);
        var (db, controller) = Client(1);
        using var _ = db;

        var result = await controller.Cancel(OrderId, default);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(OrderStatus.Pending, State().Order.Status);   // plata se va confirma; locurile rămân
        Assert.Equal((2, 1), (State().Standard, State().Vip));
    }

    [Fact]
    public async Task Anulare_StripeIndisponibil_NuAnuleazaPeGhicite()
    {
        MakePending(CheckoutSessionStatus.Open);
        _stripe.GetFailsWith = new PaymentGatewayException("timeout", new TimeoutException());
        var (db, controller) = Client(1);
        using var _ = db;

        var result = await controller.Cancel(OrderId, default);

        Assert.Equal(502, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Equal(OrderStatus.Pending, State().Order.Status);
    }

    // ---------- Endpoint-ul organizatorului / adminului ----------

    private async Task<IActionResult> Manage(int userId, string role)
    {
        await using var db = _database.CreateContext();
        var controller = new ManageOrdersController(db, TestHelpers.CreateRefundService(db, _stripe));
        TestHelpers.LoginAs(controller, userId, role);
        return await controller.Refund(OrderId, default);
    }

    [Fact]
    public void Manage_DoarOrganizatoriiSiAdminii()
    {
        var roles = typeof(ManageOrdersController).GetCustomAttribute<AuthorizeAttribute>()!.Roles!.Split(',');

        Assert.Equal(new[] { "Admin", "Organizer" }, roles.Order());
    }

    [Fact]
    public async Task Manage_OrganizatorulEvenimentului_PoateRambursa_ChiarSiCuOZiInainte()
    {
        using (var db = _database.CreateContext())
            db.Events.Where(e => e.Id == 10).ExecuteUpdate(s => s.SetProperty(e => e.StartsAt, DateTime.UtcNow.AddHours(2)));

        Assert.IsType<OkObjectResult>(await Manage(3, "Organizer"));
        Assert.Equal(OrderStatus.Refunded, State().Order.Status);
    }

    [Fact]
    public async Task Manage_AltOrganizator_Primeste404()
    {
        Assert.IsType<NotFoundResult>(await Manage(4, "Organizer"));
        Assert.Equal(OrderStatus.Paid, State().Order.Status);
    }

    [Fact]
    public async Task Manage_Adminul_PoateRambursaOrice()
    {
        Assert.IsType<OkObjectResult>(await Manage(9, "Admin"));
        Assert.Equal(OrderStatus.Refunded, State().Order.Status);
    }

    [Fact]
    public async Task Manage_StripeIndisponibil_Raspunde202()
    {
        _stripe.RefundFailsWith = new PaymentGatewayException("timeout", new TimeoutException());

        Assert.IsType<AcceptedResult>(await Manage(9, "Admin"));
        Assert.Equal(OrderStatus.RefundPending, State().Order.Status);
    }
}
