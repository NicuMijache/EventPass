using System.Text.Json;
using EventPass.Server.Services.Email;
using EventPass.Server.Services.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace EventPass.Server.Controllers;

// Adresa la care Stripe ne anunță direct (fără browserul clientului) că o plată s-a făcut.
// E publică, fără login: singura apărare e semnătura mesajului, verificată cu secretul webhook-ului.
[ApiController]
[Route("api/webhooks/stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly OrderPaymentService _payments;
    private readonly OrderRefundService _refunds;
    private readonly ITicketEmailSignal _emails;
    private readonly StripeOptions _options;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        OrderPaymentService payments,
        OrderRefundService refunds,
        ITicketEmailSignal emails,
        IOptions<StripeOptions> options,
        ILogger<StripeWebhookController> logger)
    {
        _payments = payments;
        _refunds = refunds;
        _emails = emails;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            _logger.LogError("Stripe:WebhookSecret nu este setat; nu pot verifica webhook-urile.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        // Semnătura se calculează pe TEXTUL EXACT primit, așa că îl citim brut,
        // nu ca obiect JSON deja procesat de framework (altfel semnătura nu s-ar mai potrivi)
        string payload;
        using (var reader = new StreamReader(Request.Body))
            payload = await reader.ReadToEndAsync(ct);

        Event stripeEvent;
        try
        {
            // Aruncă excepție dacă semnătura lipsește, e greșită sau mesajul e mai vechi de 5 minute
            stripeEvent = EventUtility.ConstructEvent(
                payload,
                Request.Headers["Stripe-Signature"].ToString(),
                _options.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (Exception e) when (e is StripeException or JsonException)
        {
            _logger.LogWarning("Webhook respins: semnătură invalidă sau mesaj neinteligibil ({Reason}).", e.Message);
            return BadRequest(new { message = "Semnătură invalidă." });
        }

        // Ne interesează doar plata reușită; orice alt eveniment îl confirmăm (200) și îl ignorăm,
        // ca Stripe să nu-l retrimită la nesfârșit
        if (stripeEvent.Type is not ("checkout.session.completed" or "checkout.session.async_payment_succeeded") ||
            stripeEvent.Data.Object is not Session session)
            return Ok();

        // „completed” poate însemna și „plata e în curs” (ex. transfer bancar); biletele se emit doar la „paid”
        if (session.PaymentStatus != "paid")
            return Ok();

        if (!int.TryParse(session.ClientReferenceId, out var orderId))
        {
            _logger.LogError("Sesiunea {SessionId} nu are un id de comandă valid ({Ref}).", session.Id, session.ClientReferenceId);
            return Ok();
        }

        // Dacă aici apare o eroare de bază de date, excepția duce la 500, iar Stripe RETRIMITE mesajul
        // (de aceea procesarea trebuie să fie idempotentă)
        var result = await _payments.ConfirmPaymentAsync(
            orderId, session.Id, session.AmountTotal ?? 0, session.Currency ?? string.Empty, ct);

        // Trezim procesul care trimite emailul cu biletele. Nu așteptăm trimiterea: webhook-ul răspunde imediat.
        // La AlreadyPaid anunțăm și el (mesaj repetat): dacă anunțul anterior s-ar fi pierdut, nu rămâne comanda netrimisă.
        if (result is PaymentConfirmation.Paid or PaymentConfirmation.AlreadyPaid)
            _emails.Notify();

        // Plată pentru o comandă deja anulată (locurile s-au eliberat): banii se dau înapoi automat (#19).
        // Dacă Stripe nu răspunde acum, excepția duce la 500 și Stripe retrimite mesajul mai târziu.
        if (result is PaymentConfirmation.OrderNotPending)
            await _refunds.RefundOrphanPaymentAsync(orderId, session.Id, ct);

        _logger.LogInformation("Webhook {EventId} pentru comanda {OrderId}: {Result}.", stripeEvent.Id, orderId, result);
        return Ok();
    }
}
