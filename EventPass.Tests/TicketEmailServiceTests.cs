using EventPass.Server.Data;
using EventPass.Server.Models;
using EventPass.Server.Services.Email;
using EventPass.Server.Services.Payments;
using EventPass.Server.Services.Qr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static EventPass.Tests.PaymentTestData;

namespace EventPass.Tests;

public class TicketEmailServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly FakeEmailSender _sender = new();

    // Comanda 1 (a Anei, 3 bilete) e plătită: așteaptă să primească emailul
    public TicketEmailServiceTests()
    {
        Seed(_database);
        PayOrder(OrderId, SessionId, AmountMinor);
    }

    public void Dispose() => _database.Dispose();

    private void PayOrder(int orderId, string sessionId, long amountMinor)
    {
        using var db = _database.CreateContext();
        new OrderPaymentService(db, NullLogger<OrderPaymentService>.Instance)
            .ConfirmPaymentAsync(orderId, sessionId, amountMinor, "ron").GetAwaiter().GetResult();
    }

    private Task<int> SendPending()
    {
        // Fiecare rulare a procesului primește propriul DbContext, ca în aplicația reală
        var db = _database.CreateContext();
        var service = new TicketEmailService(
            db,
            new TicketEmailBuilder(new QrCodeGenerator(), Options.Create(new ClientOptions())),
            _sender,
            NullLogger<TicketEmailService>.Instance);
        return service.SendPendingAsync();
    }

    private Order LoadOrder(int id = OrderId)
    {
        using var db = _database.CreateContext();
        return db.Orders.AsNoTracking().Single(o => o.Id == id);
    }

    private void UpdateOrder(int id, Action<Order> change)
    {
        using var db = _database.CreateContext();
        var order = db.Orders.Single(o => o.Id == id);
        change(order);
        db.SaveChanges();
    }

    // A doua comandă plătită, a lui Ion (utilizatorul 2), cu un bilet Standard
    private void AddPaidOrderForIon()
    {
        using (var db = _database.CreateContext())
        {
            db.Users.Add(new User { Id = 2, Email = "ion@example.com", FullName = "Ion", Role = UserRole.Client });
            db.Orders.Add(new Order
            {
                Id = 2, UserId = 2, Status = OrderStatus.Pending, TotalAmount = 50, StripeSessionId = "cs_test_2",
                Items = { new OrderItem { TicketTypeId = 100, Quantity = 1, UnitPrice = 50 } },
            });
            db.SaveChanges();
        }
        PayOrder(2, "cs_test_2", 5000);
    }

    [Fact]
    public async Task SendPending_ComandaPlatita_TrimiteUnEmailCuToateBiletele()
    {
        var sent = await SendPending();

        Assert.Equal(1, sent);
        var message = Assert.Single(_sender.Sent);
        Assert.Equal("ana@example.com", message.ToAddress);
        Assert.Equal(3, message.InlineImages.Count);   // 2 Standard + 1 VIP
    }

    [Fact]
    public async Task SendPending_DupaTrimitere_MarcheazaComandaSiNuMaiTrimiteOdata()
    {
        await SendPending();
        var again = await SendPending();

        Assert.Equal(0, again);
        Assert.Single(_sender.Sent);
        var order = LoadOrder();
        Assert.NotNull(order.TicketsEmailedAt);
        Assert.Equal(1, order.TicketsEmailAttempts);
    }

    [Fact]
    public async Task SendPending_ComandaNeplatita_NuPrimesteEmail()
    {
        UpdateOrder(OrderId, o => o.Status = OrderStatus.Pending);

        var sent = await SendPending();

        Assert.Equal(0, sent);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task SendPending_ComandaRambursata_NuPrimesteEmail()
    {
        UpdateOrder(OrderId, o => o.Status = OrderStatus.Refunded);

        Assert.Equal(0, await SendPending());
    }

    [Fact]
    public async Task SendPending_CandTrimitereaEsueaza_NuMarcheazaTrimisSiNoteazaIncercarea()
    {
        _sender.FailWhen = _ => new EmailSendException("SMTP căzut", new IOException());

        var sent = await SendPending();

        Assert.Equal(0, sent);
        var order = LoadOrder();
        Assert.Null(order.TicketsEmailedAt);
        Assert.Equal(1, order.TicketsEmailAttempts);
        Assert.NotNull(order.TicketsEmailLastAttemptAt);
    }

    [Fact]
    public async Task SendPending_DupaEsec_NuReincearcaImediat_CiDupaPauza()
    {
        var calls = 0;
        _sender.FailWhen = _ => { calls++; return calls == 1 ? new EmailSendException("SMTP căzut", new IOException()) : null; };
        await SendPending();                 // eșuează (încercarea 1)

        var tooSoon = await SendPending();   // imediat după: pauza (2 minute) nu a trecut
        Assert.Equal(0, tooSoon);
        Assert.Equal(1, calls);

        // Simulăm trecerea pauzei: ultima încercare a fost acum 10 minute
        UpdateOrder(OrderId, o => o.TicketsEmailLastAttemptAt = DateTime.UtcNow.AddMinutes(-10));
        var later = await SendPending();

        Assert.Equal(1, later);
        Assert.NotNull(LoadOrder().TicketsEmailedAt);
    }

    [Fact]
    public async Task SendPending_DupaMaximulDeIncercari_RenuntaSiNuMaiTrimite()
    {
        UpdateOrder(OrderId, o =>
        {
            o.TicketsEmailAttempts = TicketEmailService.MaxAttempts;
            o.TicketsEmailLastAttemptAt = DateTime.UtcNow.AddDays(-1);
        });

        var sent = await SendPending();

        Assert.Equal(0, sent);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task SendPending_ServerSmtpNeconfigurat_NuConsumaIncercari()
    {
        _sender.FailWhen = _ => new EmailNotConfiguredException();

        await SendPending();

        var order = LoadOrder();
        Assert.Equal(0, order.TicketsEmailAttempts);   // nu e vina comenzii
        Assert.Null(order.TicketsEmailedAt);

        // Când se configurează serverul, emailul pleacă la următoarea verificare
        _sender.FailWhen = null;
        Assert.Equal(1, await SendPending());
    }

    [Fact]
    public async Task SendPending_OComandaCareEsueaza_NuBlocheazaPeCelelalte()
    {
        AddPaidOrderForIon();
        _sender.FailWhen = m => m.ToAddress == "ana@example.com" ? new EmailSendException("refuzat", new IOException()) : null;

        var sent = await SendPending();

        Assert.Equal(1, sent);
        Assert.Equal("ion@example.com", Assert.Single(_sender.Sent).ToAddress);
        Assert.Null(LoadOrder(1).TicketsEmailedAt);
        Assert.NotNull(LoadOrder(2).TicketsEmailedAt);
    }

    [Fact]
    public async Task SendPending_RetrimitereDupaResetareaStarii_TrimiteDinNou()
    {
        // Așa va funcționa „retrimiterea biletului” (#21): se resetează starea, iar procesul îl trimite din nou
        await SendPending();

        UpdateOrder(OrderId, o => { o.TicketsEmailedAt = null; o.TicketsEmailAttempts = 0; o.TicketsEmailLastAttemptAt = null; });
        await SendPending();

        Assert.Equal(2, _sender.Sent.Count);
    }

    // ---- Semnalul către procesul de fundal ----

    [Fact]
    public async Task Signal_NotifyTrezesteAsteptarea()
    {
        var signal = new TicketEmailSignal();

        signal.Notify();

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    [Fact]
    public async Task Signal_FaraNotify_AsteptareaExpira()
    {
        var signal = new TicketEmailSignal();

        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Fact]
    public async Task Signal_MaiMulteNotifyLaRand_SeContopescInUnulSingur()
    {
        var signal = new TicketEmailSignal();

        signal.Notify();
        signal.Notify();
        signal.Notify();

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(5, 32)]
    public void BackoffAfter_CresteDeDouaOriLaFiecareEsec(int attempts, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), TicketEmailService.BackoffAfter(attempts));
    }
}
