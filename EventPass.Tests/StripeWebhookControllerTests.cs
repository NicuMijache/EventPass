using System.Security.Cryptography;
using System.Text;
using EventPass.Server.Controllers;
using EventPass.Server.Models;
using EventPass.Server.Services.Email;
using EventPass.Server.Services.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static EventPass.Tests.PaymentTestData;

namespace EventPass.Tests;

// Testează endpoint-ul webhook cu mesaje SEMNATE EXACT CA DE STRIPE (HMAC-SHA256),
// deci verificarea semnăturii rulează pe bune, nu e simulată.
[Trait("Regula", "EmitereWebhook")]
public class StripeWebhookControllerTests : IDisposable
{
    private const string Secret = "whsec_secret_de_test";

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTicketEmailSignal _emailSignal = new();
    // Stripe simulat, pentru rambursarea automată a plăților venite după anularea comenzii
    private readonly FakePaymentGateway _stripe = new();

    public StripeWebhookControllerTests() => Seed(_database);

    public void Dispose() => _database.Dispose();

    // Mesajul pe care Stripe îl trimite când o plată se încheie
    private static string EventJson(
        string type = "checkout.session.completed",
        string paymentStatus = "paid",
        long amount = AmountMinor,
        string sessionId = SessionId,
        string orderRef = "1") => $$"""
        {
          "id": "evt_test_1",
          "object": "event",
          "api_version": "2025-01-27.acacia",
          "created": 1700000000,
          "livemode": false,
          "type": "{{type}}",
          "data": {
            "object": {
              "id": "{{sessionId}}",
              "object": "checkout.session",
              "client_reference_id": "{{orderRef}}",
              "payment_status": "{{paymentStatus}}",
              "status": "complete",
              "amount_total": {{amount}},
              "currency": "ron"
            }
          }
        }
        """;

    // Semnătura Stripe: HMAC-SHA256 al textului „{moment}.{conținut}”, cu secretul webhook-ului
    private static string Sign(string payload, string secret, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        return $"t={timestamp},v1={hash}";
    }

    private StripeWebhookController CreateController(string payload, string? signature, string webhookSecret = Secret)
    {
        var db = _database.CreateContext();
        var controller = new StripeWebhookController(
            new OrderPaymentService(db, NullLogger<OrderPaymentService>.Instance),
            TestHelpers.CreateRefundService(db, _stripe),
            _emailSignal,
            Options.Create(new StripeOptions { WebhookSecret = webhookSecret }),
            NullLogger<StripeWebhookController>.Instance);

        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        if (signature is not null)
            http.Request.Headers["Stripe-Signature"] = signature;
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    private Order LoadOrder()
    {
        using var db = _database.CreateContext();
        return db.Orders.Single(o => o.Id == OrderId);
    }

    private int TicketCount()
    {
        using var db = _database.CreateContext();
        return db.Tickets.Count();
    }

    [Fact]
    public async Task Receive_PlataPentruOComandaAnulata_DaBaniiInapoiAutomat()
    {
        // Clientul a anulat comanda (locurile s-au eliberat), dar plata a ajuns totuși
        using (var db = _database.CreateContext())
            await db.Orders.Where(o => o.Id == OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Cancelled));
        _stripe.SetPaid(SessionId, AmountMinor);
        var payload = EventJson();

        var result = await CreateController(payload, Sign(payload, Secret)).Receive(default);
        var again = await CreateController(payload, Sign(payload, Secret)).Receive(default);   // Stripe retrimite

        Assert.IsType<OkResult>(result);
        Assert.IsType<OkResult>(again);
        var order = LoadOrder();
        Assert.Equal(OrderStatus.Cancelled, order.Status);   // fără bilete
        Assert.Equal(0, TicketCount());
        Assert.Equal("re_test_1", order.StripeRefundId);     // dar cu banii dați înapoi
        Assert.NotNull(order.RefundedAt);
        Assert.Single(_stripe.Refunds);                      // o singură rambursare, deși mesajul a venit de 2 ori
    }

    [Fact]
    public async Task Receive_PlataPentruComandaAnulata_StripeIndisponibil_Raspunde500CaSaFieRetrimis()
    {
        using (var db = _database.CreateContext())
            await db.Orders.Where(o => o.Id == OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Cancelled));
        _stripe.SetPaid(SessionId, AmountMinor);
        _stripe.RefundFailsWith = new PaymentGatewayException("timeout", new TimeoutException());
        var payload = EventJson();

        await Assert.ThrowsAsync<PaymentGatewayException>(() => CreateController(payload, Sign(payload, Secret)).Receive(default));

        Assert.Null(LoadOrder().StripeRefundId);
    }

    [Fact]
    public async Task Receive_MesajCuSemnaturaValida_ConfirmaPlataSiEmiteBilete()
    {
        var payload = EventJson();

        var result = await CreateController(payload, Sign(payload, Secret)).Receive(default);

        Assert.IsType<OkResult>(result);
        Assert.Equal(OrderStatus.Paid, LoadOrder().Status);
        Assert.Equal(3, TicketCount());
    }

    [Fact]
    public async Task Receive_PlataConfirmata_TrezesteProcesulCareTrimiteEmailul()
    {
        var payload = EventJson();

        await CreateController(payload, Sign(payload, Secret)).Receive(default);

        Assert.Equal(1, _emailSignal.Notifications);
    }

    [Fact]
    public async Task Receive_MesajRepetat_AnuntaDinNouCaSaNuRamanaNetrimis()
    {
        var payload = EventJson();

        await CreateController(payload, Sign(payload, Secret)).Receive(default);
        await CreateController(payload, Sign(payload, Secret)).Receive(default);

        Assert.Equal(2, _emailSignal.Notifications);
    }

    [Fact]
    public async Task Receive_SemnaturaFalsa_NuTrezesteProcesul()
    {
        var payload = EventJson();

        await CreateController(payload, Sign(payload, "whsec_alt_secret")).Receive(default);

        Assert.Equal(0, _emailSignal.Notifications);
    }

    [Fact]
    public async Task Receive_SumaGresita_NuTrezesteProcesul()
    {
        var payload = EventJson(amount: 100);

        await CreateController(payload, Sign(payload, Secret)).Receive(default);

        Assert.Equal(0, _emailSignal.Notifications);
    }

    [Fact]
    public async Task Receive_AcelasiMesajDeDouaOri_NuDuplicaBiletele()
    {
        var payload = EventJson();

        var first = await CreateController(payload, Sign(payload, Secret)).Receive(default);
        var second = await CreateController(payload, Sign(payload, Secret)).Receive(default);

        // Ambele primesc 200 (ca Stripe să se oprească din retrimis), dar biletele sunt tot 3
        Assert.IsType<OkResult>(first);
        Assert.IsType<OkResult>(second);
        Assert.Equal(3, TicketCount());
    }

    [Fact]
    public async Task Receive_SemnaturaCalculataCuAltSecret_Returneaza400SiNuFacePlata()
    {
        var payload = EventJson();

        var result = await CreateController(payload, Sign(payload, "whsec_secretul_atacatorului")).Receive(default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(OrderStatus.Pending, LoadOrder().Status);
        Assert.Equal(0, TicketCount());
    }

    [Fact]
    public async Task Receive_FaraHeaderDeSemnatura_Returneaza400()
    {
        var result = await CreateController(EventJson(), signature: null).Receive(default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, TicketCount());
    }

    [Fact]
    public async Task Receive_ContinutModificatDupaSemnare_Returneaza400()
    {
        // Atacatorul interceptează un mesaj real (suma 100 bani) și schimbă suma, păstrând semnătura
        var signedPayload = EventJson(amount: 100);
        var signature = Sign(signedPayload, Secret);
        var tamperedPayload = EventJson(amount: AmountMinor);

        var result = await CreateController(tamperedPayload, signature).Receive(default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, TicketCount());
    }

    [Fact]
    public async Task Receive_MesajVechiRetrimis_Returneaza400()
    {
        // Semnătură corectă, dar de acum 10 minute: protecție la retrimiterea unui mesaj interceptat
        var payload = EventJson();
        var oldSignature = Sign(payload, Secret, DateTimeOffset.UtcNow.AddMinutes(-10));

        var result = await CreateController(payload, oldSignature).Receive(default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, TicketCount());
    }

    [Fact]
    public async Task Receive_FaraSecretConfigurat_Returneaza503()
    {
        var payload = EventJson();

        var result = await CreateController(payload, Sign(payload, Secret), webhookSecret: "").Receive(default);

        Assert.Equal(503, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal(0, TicketCount());
    }

    [Fact]
    public async Task Receive_PlataNeincasataInca_NuEmiteBilete()
    {
        // „completed” cu payment_status „unpaid” (ex. transfer bancar în curs)
        var payload = EventJson(paymentStatus: "unpaid");

        var result = await CreateController(payload, Sign(payload, Secret)).Receive(default);

        Assert.IsType<OkResult>(result);
        Assert.Equal(OrderStatus.Pending, LoadOrder().Status);
        Assert.Equal(0, TicketCount());
    }

    [Fact]
    public async Task Receive_AltTipDeEveniment_EsteIgnoratCu200()
    {
        var payload = EventJson(type: "checkout.session.expired");

        var result = await CreateController(payload, Sign(payload, Secret)).Receive(default);

        Assert.IsType<OkResult>(result);
        Assert.Equal(OrderStatus.Pending, LoadOrder().Status);
    }

    [Fact]
    public async Task Receive_SumaGresita_ComandaRamanePending()
    {
        var payload = EventJson(amount: 100);

        var result = await CreateController(payload, Sign(payload, Secret)).Receive(default);

        // 200, ca Stripe să nu retrimită la nesfârșit un mesaj care nu se va potrivi niciodată
        Assert.IsType<OkResult>(result);
        Assert.Equal(OrderStatus.Pending, LoadOrder().Status);
        Assert.Equal(0, TicketCount());
    }

    [Fact]
    public async Task Receive_IdDeComandaInvalid_Returneaza200FaraEfect()
    {
        var payload = EventJson(orderRef: "nu-e-numar");

        var result = await CreateController(payload, Sign(payload, Secret)).Receive(default);

        Assert.IsType<OkResult>(result);
        Assert.Equal(0, TicketCount());
    }
}
