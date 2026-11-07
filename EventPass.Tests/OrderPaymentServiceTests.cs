using EventPass.Server.Data;
using EventPass.Server.Models;
using EventPass.Server.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static EventPass.Tests.PaymentTestData;

namespace EventPass.Tests;

public class OrderPaymentServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public OrderPaymentServiceTests() => Seed(_database);

    public void Dispose() => _database.Dispose();

    // Fiecare apel primește propriul DbContext, ca fiecare mesaj webhook în aplicația reală
    private Task<PaymentConfirmation> Confirm(
        int orderId = OrderId, string sessionId = SessionId, long amount = AmountMinor, string currency = "ron")
    {
        var db = _database.CreateContext();
        var service = new OrderPaymentService(db, NullLogger<OrderPaymentService>.Instance);
        return service.ConfirmPaymentAsync(orderId, sessionId, amount, currency);
    }

    private Order LoadOrder()
    {
        using var db = _database.CreateContext();
        return db.Orders.Single(o => o.Id == OrderId);
    }

    private List<Ticket> LoadTickets()
    {
        using var db = _database.CreateContext();
        return db.Tickets.AsNoTracking().ToList();
    }

    [Fact]
    public async Task Confirm_ComandaPending_TreceInPaidSiEmiteUnBiletPentruFiecareLoc()
    {
        var result = await Confirm();

        Assert.Equal(PaymentConfirmation.Paid, result);
        var order = LoadOrder();
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.NotNull(order.PaidAt);

        var tickets = LoadTickets();
        Assert.Equal(3, tickets.Count);                          // 2 + 1 locuri
        Assert.Equal(2, tickets.Count(t => t.TicketTypeId == 100));
        Assert.Equal(1, tickets.Count(t => t.TicketTypeId == 101));
        Assert.All(tickets, t => Assert.False(t.IsUsed));
    }

    [Fact]
    public async Task Confirm_FiecareBiletAreUnTokenUnic()
    {
        await Confirm();

        var tokens = LoadTickets().Select(t => t.Token).ToList();

        Assert.Equal(3, tokens.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, tokens);
    }

    [Fact]
    public async Task Confirm_AceeasiPlataDeDouaOri_EmiteBileteleOSingurDataSiRaspundeAlreadyPaid()
    {
        var first = await Confirm();
        var second = await Confirm();   // Stripe a retrimis același mesaj
        var third = await Confirm();

        Assert.Equal(PaymentConfirmation.Paid, first);
        Assert.Equal(PaymentConfirmation.AlreadyPaid, second);
        Assert.Equal(PaymentConfirmation.AlreadyPaid, third);
        Assert.Equal(3, LoadTickets().Count);                    // nu 6, nu 9
    }

    [Fact]
    public async Task Confirm_NuModificaStocul_LocurileEranDejaRezervate()
    {
        await Confirm();

        using var db = _database.CreateContext();
        Assert.Equal(2, db.TicketTypes.Single(t => t.Id == 100).Sold);
        Assert.Equal(1, db.TicketTypes.Single(t => t.Id == 101).Sold);
    }

    [Fact]
    public async Task Confirm_RezervareaDepasiseTermenulDarNuFuseseAnulata_TotusiSePlateste()
    {
        using (var db = _database.CreateContext())
        {
            // Termenul a trecut, dar nimeni n-a deschis comanda, deci locurile sunt încă ținute
            await db.Orders.Where(o => o.Id == OrderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, DateTime.UtcNow.AddMinutes(-5)));
        }

        var result = await Confirm();

        Assert.Equal(PaymentConfirmation.Paid, result);
        Assert.Equal(3, LoadTickets().Count);
    }

    [Theory]
    [InlineData(100L, "cs_test_1", "ron")]       // sumă prea mică
    [InlineData(30001L, "cs_test_1", "ron")]     // sumă diferită cu un ban
    [InlineData(30000L, "cs_test_ALTA", "ron")]  // altă sesiune decât cea creată de noi
    [InlineData(30000L, "cs_test_1", "usd")]     // altă monedă
    public async Task Confirm_PlataCareNuSePotrivește_NuEmiteBileteSiComandaRamanePending(
        long amount, string sessionId, string currency)
    {
        var result = await Confirm(amount: amount, sessionId: sessionId, currency: currency);

        Assert.Equal(PaymentConfirmation.PaymentMismatch, result);
        Assert.Equal(OrderStatus.Pending, LoadOrder().Status);
        Assert.Empty(LoadTickets());
    }

    [Fact]
    public async Task Confirm_MonedaScrisaCuMajuscule_EsteAcceptata()
    {
        var result = await Confirm(currency: "RON");

        Assert.Equal(PaymentConfirmation.Paid, result);
    }

    [Fact]
    public async Task Confirm_ComandaAnulata_NuEmiteBilete()
    {
        using (var db = _database.CreateContext())
        {
            await db.Orders.Where(o => o.Id == OrderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Cancelled));
        }

        var result = await Confirm();

        Assert.Equal(PaymentConfirmation.OrderNotPending, result);
        Assert.Equal(OrderStatus.Cancelled, LoadOrder().Status);
        Assert.Empty(LoadTickets());
    }

    [Fact]
    public async Task Confirm_ComandaInexistenta_ReturneazaOrderNotFound()
    {
        var result = await Confirm(orderId: 999);

        Assert.Equal(PaymentConfirmation.OrderNotFound, result);
        Assert.Empty(LoadTickets());
    }
}
