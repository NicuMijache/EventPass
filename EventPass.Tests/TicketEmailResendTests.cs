using EventPass.Server.Controllers;
using EventPass.Server.Data;
using EventPass.Server.Models;
using EventPass.Server.Services.Email;
using EventPass.Server.Services.Payments;
using EventPass.Server.Services.Qr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static EventPass.Tests.PaymentTestData;

namespace EventPass.Tests;

// Retrimiterea biletelor pe email (#21). Comanda 1 (a Anei, 3 bilete) e plătită.
public class TicketEmailResendTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly FakeEmailSender _sender = new();
    private readonly FakeTicketEmailSignal _signal = new();

    public TicketEmailResendTests()
    {
        Seed(_database);
        using var db = _database.CreateContext();
        db.Users.Add(new User { Id = 2, Email = "ion@example.com", FullName = "Ion", Role = UserRole.Client });
        db.SaveChanges();
        new OrderPaymentService(db, NullLogger<OrderPaymentService>.Instance)
            .ConfirmPaymentAsync(OrderId, SessionId, AmountMinor, "ron").GetAwaiter().GetResult();
    }

    public void Dispose() => _database.Dispose();

    private TicketEmailService Service(AppDbContext db) => new(
        db,
        new TicketEmailBuilder(new QrCodeGenerator(), Options.Create(new ClientOptions())),
        _sender,
        NullLogger<TicketEmailService>.Instance);

    // O rundă a procesului de fundal
    private async Task<int> SendPending()
    {
        await using var db = _database.CreateContext();
        return await Service(db).SendPendingAsync();
    }

    private async Task<ResendResult> Resend()
    {
        await using var db = _database.CreateContext();
        return await Service(db).RequestResendAsync(OrderId);
    }

    // Mută „ceasul” comenzii: emailul a fost trimis / ultima încercare a fost acum `ago`
    private void Sent(TimeSpan ago)
    {
        using var db = _database.CreateContext();
        var at = DateTime.UtcNow - ago;
        db.Orders.Where(o => o.Id == OrderId).ExecuteUpdate(s => s
            .SetProperty(o => o.TicketsEmailedAt, at)
            .SetProperty(o => o.TicketsEmailAttempts, 1)
            .SetProperty(o => o.TicketsEmailLastAttemptAt, at));
    }

    private void Failing(int attempts, TimeSpan lastAttemptAgo)
    {
        using var db = _database.CreateContext();
        db.Orders.Where(o => o.Id == OrderId).ExecuteUpdate(s => s
            .SetProperty(o => o.TicketsEmailedAt, (DateTime?)null)
            .SetProperty(o => o.TicketsEmailAttempts, attempts)
            .SetProperty(o => o.TicketsEmailLastAttemptAt, DateTime.UtcNow - lastAttemptAgo));
    }

    private Order Load()
    {
        using var db = _database.CreateContext();
        return db.Orders.AsNoTracking().Single(o => o.Id == OrderId);
    }

    // ---------- Serviciul ----------

    [Fact]
    public async Task EmailTrimisDemult_SeRetrimite_CuAceleasiBilete()
    {
        Assert.Equal(1, await SendPending());
        var first = _sender.Sent.Single();
        Sent(TimeSpan.FromMinutes(10));

        var result = await Resend();

        Assert.Equal(ResendOutcome.Queued, result.Outcome);
        var order = Load();
        Assert.Null(order.TicketsEmailedAt);              // din nou „de trimis”
        Assert.Equal(0, order.TicketsEmailAttempts);

        Assert.Equal(1, await SendPending());              // procesul de fundal îl trimite
        var second = _sender.Sent[1];
        Assert.Equal(first.ToAddress, second.ToAddress);
        Assert.Equal(first.Subject, second.Subject);
        // Aceleași coduri QR (aceleași bilete), nu bilete noi
        Assert.Equal(first.InlineImages.Select(i => Convert.ToBase64String(i.Content)), second.InlineImages.Select(i => Convert.ToBase64String(i.Content)));
        Assert.NotNull(Load().TicketsEmailedAt);
    }

    [Fact]
    public async Task EmailTrimisAcum30DeSecunde_TrebuieSaAstepte()
    {
        Sent(TimeSpan.FromSeconds(30));

        var result = await Resend();

        Assert.Equal(ResendOutcome.TooSoon, result.Outcome);
        Assert.InRange(result.RetryAfter!.Value.TotalSeconds, 85, 91);   // ~90 s până la 2 minute
        Assert.NotNull(Load().TicketsEmailedAt);                           // nimic schimbat
    }

    [Fact]
    public async Task EmailulNuAPlecatInca_EDejaPeDrum_NuSeDubleaza()
    {
        // Comanda abia plătită: procesul de fundal nu a apucat să trimită
        var result = await Resend();

        Assert.Equal(ResendOutcome.AlreadyQueued, result.Outcome);
        Assert.Equal(1, await SendPending());
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task DupaCeProcesulARenuntat_RetrimitereaIlRepornește()
    {
        // 6 eșecuri: procesul de fundal nu mai încearcă singur
        Failing(TicketEmailService.MaxAttempts, TimeSpan.FromHours(1));
        Assert.Equal(0, await SendPending());

        Assert.Equal(ResendOutcome.Queued, (await Resend()).Outcome);

        Assert.Equal(1, await SendPending());
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task TrimitereaTocmaiAEsuat_NuOcolesteAsteptarea()
    {
        // Ultima încercare acum 20 s (SMTP căzut): retrimiterea nu are voie să forțeze încercări la nesfârșit
        Failing(2, TimeSpan.FromSeconds(20));

        var result = await Resend();

        Assert.Equal(ResendOutcome.TooSoon, result.Outcome);
        Assert.Equal(2, Load().TicketsEmailAttempts);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refunded)]
    [InlineData(OrderStatus.RefundPending)]
    public async Task ComandaNeplatita_NuSeRetrimite(OrderStatus status)
    {
        Sent(TimeSpan.FromHours(1));
        using (var db = _database.CreateContext())
            db.Orders.Where(o => o.Id == OrderId).ExecuteUpdate(s => s.SetProperty(o => o.Status, status));

        Assert.Equal(ResendOutcome.NotPaid, (await Resend()).Outcome);
        Assert.NotNull(Load().TicketsEmailedAt);
    }

    [Fact]
    public async Task DouaCereriLaRand_UnSingurEmail()
    {
        await SendPending();
        Sent(TimeSpan.FromMinutes(10));

        var first = await Resend();
        var second = await Resend();   // dublu-click

        Assert.Equal(ResendOutcome.Queued, first.Outcome);
        Assert.Equal(ResendOutcome.AlreadyQueued, second.Outcome);
        await SendPending();
        Assert.Equal(2, _sender.Sent.Count);   // primul email + O SINGURĂ retrimitere
        Assert.Equal(ResendOutcome.TooSoon, (await Resend()).Outcome);   // și acum trebuie iar să aștepte
    }

    // ---------- Controller ----------

    private async Task<IActionResult> Call(int userId)
    {
        await using var db = _database.CreateContext();
        var controller = new OrderEmailsController(db, Service(db), _signal);
        TestHelpers.LoginAs(controller, userId, "Client");
        return await controller.Resend(OrderId, default);
    }

    [Fact]
    public async Task Controller_Retrimitere_202_SiTrezesteProcesulDeEmail()
    {
        Sent(TimeSpan.FromMinutes(10));

        var result = await Call(1);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(1, _signal.Notifications);
    }

    [Fact]
    public async Task Controller_PreaDevreme_429_CuAntetulRetryAfter()
    {
        Sent(TimeSpan.FromSeconds(30));
        await using var db = _database.CreateContext();
        var controller = new OrderEmailsController(db, Service(db), _signal);
        TestHelpers.LoginAs(controller, 1, "Client");

        var result = await controller.Resend(OrderId, default);

        Assert.Equal(429, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.InRange(int.Parse(controller.Response.Headers.RetryAfter.ToString()), 85, 91);
        Assert.Equal(0, _signal.Notifications);
    }

    [Fact]
    public async Task Controller_ComandaAltcuiva_404()
    {
        Sent(TimeSpan.FromMinutes(10));

        Assert.IsType<NotFoundResult>(await Call(2));
        Assert.NotNull(Load().TicketsEmailedAt);   // comanda Anei neatinsă
    }

    [Fact]
    public async Task Controller_ComandaNeplatita_409()
    {
        using (var db = _database.CreateContext())
            db.Orders.Where(o => o.Id == OrderId).ExecuteUpdate(s => s.SetProperty(o => o.Status, OrderStatus.Refunded));

        Assert.IsType<ConflictObjectResult>(await Call(1));
    }
}

public class SecondsWordingTests
{
    [Theory]
    [InlineData(1, "o secundă")]
    [InlineData(5, "5 secunde")]
    [InlineData(19, "19 secunde")]
    [InlineData(20, "20 de secunde")]
    [InlineData(118, "118 de secunde")]
    public void Secundele_SeScriuCorectInRomana(int n, string expected) =>
        Assert.Equal(expected, EventPass.Server.Controllers.OrderEmailsController.Seconds(n));
}
