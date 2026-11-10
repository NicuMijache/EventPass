using EventPass.Server.Data;
using EventPass.Server.Models;
using EventPass.Server.Services;
using EventPass.Server.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static EventPass.Tests.PaymentTestData;

namespace EventPass.Tests;

// Expirarea rezervărilor (#18). Comanda 1 din PaymentTestData: Pending, sesiunea cs_test_1,
// ține 2 × Standard (tipul 100) și 1 × VIP (tipul 101).
public class ReservationExpiryServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly FakePaymentGateway _stripe = new();
    private readonly FakeTicketEmailSignal _emails = new();

    public ReservationExpiryServiceTests() => Seed(_database);

    public void Dispose() => _database.Dispose();

    private void Expire(int orderId = OrderId, string? sessionId = SessionId)
    {
        using var db = _database.CreateContext();
        db.Orders.Where(o => o.Id == orderId).ExecuteUpdate(s => s
            .SetProperty(o => o.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))
            .SetProperty(o => o.StripeSessionId, sessionId));
    }

    private void StripeSession(CheckoutSessionStatus status) =>
        _stripe.Sessions[SessionId] = new CheckoutSession(SessionId, "https://checkout.stripe.test/x", status, DateTime.UtcNow.AddMinutes(-1));

    private async Task<ExpiryOutcome> ExpireOrder(int orderId = OrderId)
    {
        await using var db = _database.CreateContext();
        var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == orderId);
        return await TestHelpers.CreateExpiryService(db, _stripe, _emails).ExpireAsync(order);
    }

    private async Task<int> RunWorkerRound()
    {
        await using var db = _database.CreateContext();
        return await TestHelpers.CreateExpiryService(db, _stripe, _emails).ExpireDueAsync();
    }

    private (OrderStatus Status, int Standard, int Vip, int Tickets) State(int orderId = OrderId)
    {
        using var db = _database.CreateContext();
        return (
            db.Orders.Single(o => o.Id == orderId).Status,
            db.TicketTypes.Single(t => t.Id == 100).Sold,
            db.TicketTypes.Single(t => t.Id == 101).Sold,
            db.Tickets.Count(t => t.OrderId == orderId));
    }

    // ---------- O singură comandă ----------

    [Fact]
    public async Task RezervareaIncaValabila_NuSeAtinge_SiNuSeIntreabaStripe()
    {
        var outcome = await ExpireOrder();

        Assert.Equal(ExpiryOutcome.NotExpired, outcome);
        Assert.Equal((OrderStatus.Pending, 2, 1, 0), State());
        Assert.Equal(0, _stripe.GetCalls);
    }

    [Fact]
    public async Task ExpirataFaraPlataPornita_SeAnuleaza_SiLocurileSeElibereaza_FaraStripe()
    {
        Expire(sessionId: null);   // clientul n-a apăsat niciodată „Plătește”

        var outcome = await ExpireOrder();

        Assert.Equal(ExpiryOutcome.Cancelled, outcome);
        Assert.Equal((OrderStatus.Cancelled, 0, 0, 0), State());
        Assert.Equal(0, _stripe.GetCalls);
    }

    [Fact]
    public async Task ExpirataCuSesiuneaExpirataLaStripe_SeAnuleaza()
    {
        Expire();
        StripeSession(CheckoutSessionStatus.Expired);

        var outcome = await ExpireOrder();

        Assert.Equal(ExpiryOutcome.Cancelled, outcome);
        Assert.Equal((OrderStatus.Cancelled, 0, 0, 0), State());
        Assert.Equal(1, _stripe.GetCalls);
    }

    [Fact]
    public async Task PlatitaLaStripeDarFaraWebhook_NuSeAnuleaza_CiSeEmitBiletele()
    {
        // Cursa periculoasă: clientul plătește în ultima secundă, iar webhook-ul întârzie
        Expire();
        _stripe.SetPaid(SessionId, AmountMinor);

        var outcome = await ExpireOrder();

        Assert.Equal(ExpiryOutcome.PaymentRecovered, outcome);
        // Plătită, cu 3 bilete, iar locurile RĂMÂN ocupate (nu s-au eliberat pentru altcineva)
        Assert.Equal((OrderStatus.Paid, 2, 1, 3), State());
        Assert.Equal(1, _emails.Notifications);   // pleacă și emailul cu biletele
    }

    [Fact]
    public async Task PlataRecuperata_WebhookulCareVineDupa_NuMaiEmiteBileteInPlus()
    {
        Expire();
        _stripe.SetPaid(SessionId, AmountMinor);
        await ExpireOrder();

        await using var db = _database.CreateContext();
        var late = await new OrderPaymentService(db, NullLogger<OrderPaymentService>.Instance)
            .ConfirmPaymentAsync(OrderId, SessionId, AmountMinor, "ron");

        Assert.Equal(PaymentConfirmation.AlreadyPaid, late);
        Assert.Equal(3, State().Tickets);
    }

    [Fact]
    public async Task PlatitaCuAltaSuma_NuSeConfirma_DarNiciNuSeAnuleaza()
    {
        // Ceva e în neregulă (sumă diferită): nu emitem bilete, dar nici nu luăm locurile cuiva care a plătit
        Expire();
        _stripe.SetPaid(SessionId, AmountMinor - 100);

        var outcome = await ExpireOrder();

        Assert.Equal(ExpiryOutcome.Postponed, outcome);
        Assert.Equal((OrderStatus.Pending, 2, 1, 0), State());
    }

    [Theory]
    [InlineData(CheckoutSessionStatus.Open)]      // ceasurile diferă: la Stripe încă se poate plăti
    [InlineData(CheckoutSessionStatus.Complete)]  // „complete” fără „paid”: plată în curs (ex. transfer)
    public async Task SesiuneIncaNeterminata_SeAmana(CheckoutSessionStatus status)
    {
        Expire();
        StripeSession(status);

        var outcome = await ExpireOrder();

        Assert.Equal(ExpiryOutcome.Postponed, outcome);
        Assert.Equal((OrderStatus.Pending, 2, 1, 0), State());
    }

    [Fact]
    public async Task StripeIndisponibil_SeAmana_ApoiSeAnuleazaCandRaspunde()
    {
        Expire();
        _stripe.GetFailsWith = new PaymentGatewayException("timeout", new TimeoutException());

        Assert.Equal(ExpiryOutcome.Postponed, await ExpireOrder());
        Assert.Equal(OrderStatus.Pending, State().Status);

        // Stripe își revine: sesiunea a expirat, deci se poate anula
        _stripe.GetFailsWith = null;
        StripeSession(CheckoutSessionStatus.Expired);
        Assert.Equal(ExpiryOutcome.Cancelled, await ExpireOrder());
        Assert.Equal((OrderStatus.Cancelled, 0, 0, 0), State());
    }

    [Fact]
    public async Task FaraCheieStripe_SeAmana()
    {
        Expire();
        _stripe.GetFailsWith = new PaymentsNotConfiguredException();

        Assert.Equal(ExpiryOutcome.Postponed, await ExpireOrder());
        Assert.Equal(OrderStatus.Pending, State().Status);
    }

    [Fact]
    public async Task SesiuneNegasitaLaStripe_NuSeAnuleazaSingura()
    {
        // Ex. s-a schimbat cheia Stripe cu a altui cont: nu putem ști dacă s-a plătit
        Expire();

        Assert.Equal(ExpiryOutcome.Postponed, await ExpireOrder());
        Assert.Equal(OrderStatus.Pending, State().Status);
    }

    [Fact]
    public async Task ComandaDejaAnulata_DeAltcineva_NuElibereazaLocurileADouaOara()
    {
        Expire(sessionId: null);
        await using var db = _database.CreateContext();
        var stale = await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == OrderId);   // citită înainte de anulare
        await ExpireOrder();                                                                     // altcineva o anulează

        var outcome = await TestHelpers.CreateExpiryService(db, _stripe).ExpireAsync(stale);

        Assert.Equal(ExpiryOutcome.AlreadyHandled, outcome);
        Assert.Equal((OrderStatus.Cancelled, 0, 0, 0), State());   // nu -2 (constrângerea Sold >= 0 ar fi oprit-o oricum)
    }

    // ---------- Runda procesului de fundal ----------

    [Fact]
    public async Task Runda_AnuleazaDoarComenzilePendingExpirate()
    {
        using (var db = _database.CreateContext())
        {
            var past = DateTime.UtcNow.AddMinutes(-5);
            db.Orders.AddRange(
                // 2: expirată, fără plată → se anulează
                new Order { Id = 2, UserId = 1, Status = OrderStatus.Pending, TotalAmount = 50, ExpiresAt = past,
                    Items = { new OrderItem { TicketTypeId = 100, Quantity = 1, UnitPrice = 50 } } },
                // 3: plătită (ExpiresAt în trecut nu mai contează) → neatinsă
                new Order { Id = 3, UserId = 1, Status = OrderStatus.Paid, TotalAmount = 50, ExpiresAt = past,
                    Items = { new OrderItem { TicketTypeId = 100, Quantity = 1, UnitPrice = 50 } } },
                // 4: anulată deja → neatinsă
                new Order { Id = 4, UserId = 1, Status = OrderStatus.Cancelled, TotalAmount = 50, ExpiresAt = past,
                    Items = { new OrderItem { TicketTypeId = 100, Quantity = 1, UnitPrice = 50 } } });
            // Stocul: 2 (comanda 1) + 1 (comanda 2) + 1 (comanda 3, plătită)
            db.TicketTypes.Where(t => t.Id == 100).ExecuteUpdate(s => s.SetProperty(t => t.Sold, 4));
            db.SaveChanges();
        }

        var cancelled = await RunWorkerRound();

        Assert.Equal(1, cancelled);
        Assert.Equal(OrderStatus.Pending, State(1).Status);     // comanda 1 încă e valabilă
        Assert.Equal(OrderStatus.Cancelled, State(2).Status);
        Assert.Equal(OrderStatus.Paid, State(3).Status);
        Assert.Equal(3, State().Standard);                     // s-a eliberat doar locul comenzii 2

        Assert.Equal(0, await RunWorkerRound());                // a doua rundă nu mai are ce face
        Assert.Equal(3, State().Standard);
    }

    [Fact]
    public async Task Runda_OComandaCuProbleme_NuLeOprestePeCelelalte()
    {
        Expire();   // comanda 1: Stripe dă eroare → amânată
        _stripe.GetFailsWith = new PaymentGatewayException("timeout", new TimeoutException());
        using (var db = _database.CreateContext())
        {
            db.Orders.Add(new Order { Id = 2, UserId = 1, Status = OrderStatus.Pending, TotalAmount = 50, ExpiresAt = DateTime.UtcNow.AddMinutes(-2),
                Items = { new OrderItem { TicketTypeId = 100, Quantity = 1, UnitPrice = 50 } } });
            db.TicketTypes.Where(t => t.Id == 100).ExecuteUpdate(s => s.SetProperty(t => t.Sold, 3));
            db.SaveChanges();
        }

        var cancelled = await RunWorkerRound();

        Assert.Equal(1, cancelled);
        Assert.Equal(OrderStatus.Pending, State(1).Status);
        Assert.Equal(OrderStatus.Cancelled, State(2).Status);
        Assert.Equal(2, State().Standard);
    }
}
