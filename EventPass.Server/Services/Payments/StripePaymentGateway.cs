using Stripe;
using Stripe.Checkout;

namespace EventPass.Server.Services.Payments;

public class StripeOptions
{
    // Cheia secretă de test (sk_test_...), doar în User Secrets
    public string SecretKey { get; set; } = string.Empty;
    // Secretul de semnare a webhook-urilor (whsec_...), tot doar în User Secrets.
    // Cu el verificăm că un mesaj „plata s-a făcut” chiar vine de la Stripe.
    public string WebhookSecret { get; set; } = string.Empty;
    // Adresa frontend-ului, unde Stripe trimite clientul înapoi după plată
    public string ClientBaseUrl { get; set; } = "http://localhost:4200";
}

// Implementarea reală: vorbește cu Stripe prin biblioteca oficială Stripe.net
public class StripePaymentGateway : IPaymentGateway
{
    private readonly StripeOptions _options;

    public StripePaymentGateway(Microsoft.Extensions.Options.IOptions<StripeOptions> options)
    {
        _options = options.Value;
    }

    public async Task<CheckoutSession> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        var options = new SessionCreateOptions
        {
            Mode = "payment",
            CustomerEmail = request.CustomerEmail,
            // Legătura dintre plata Stripe și comanda noastră (o folosim la webhook, #12)
            ClientReferenceId = request.OrderId.ToString(),
            Metadata = new Dictionary<string, string> { ["orderId"] = request.OrderId.ToString() },
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            ExpiresAt = request.ExpiresAt,
            LineItems = request.Lines.Select(line => new SessionLineItemOptions
            {
                Quantity = line.Quantity,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = request.Currency,
                    UnitAmount = line.UnitAmountMinor,
                    ProductData = new SessionLineItemPriceDataProductDataOptions { Name = line.Name },
                },
            }).ToList(),
        };

        var session = await CallStripe(service => service.CreateAsync(options, cancellationToken: ct));
        return ToCheckoutSession(session);
    }

    public async Task<CheckoutSession?> GetCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var session = await CallStripe(service => service.GetAsync(sessionId, cancellationToken: ct));
            return ToCheckoutSession(session);
        }
        catch (PaymentGatewayException e) when (e.InnerException is StripeException { HttpStatusCode: System.Net.HttpStatusCode.NotFound })
        {
            return null;
        }
    }

    public async Task<CheckoutSession?> ExpireCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var session = await CallStripe(service => service.ExpireAsync(sessionId, cancellationToken: ct));
            return ToCheckoutSession(session);
        }
        catch (PaymentGatewayException e) when (e.InnerException is StripeException { HttpStatusCode: System.Net.HttpStatusCode.BadRequest })
        {
            // Stripe refuză să închidă o sesiune care nu mai e deschisă (plătită sau deja expirată):
            // citim starea ei, ca să știm care din două
            return await GetCheckoutSessionAsync(sessionId, ct);
        }
        catch (PaymentGatewayException e) when (e.InnerException is StripeException { HttpStatusCode: System.Net.HttpStatusCode.NotFound })
        {
            return null;
        }
    }

    public async Task<RefundResult> RefundAsync(string sessionId, string idempotencyKey, CancellationToken ct = default)
    {
        // Rambursarea se face pe „PaymentIntent” (plata propriu-zisă), pe care o aflăm din sesiune
        var session = await CallStripe(service => service.GetAsync(sessionId, cancellationToken: ct));
        if (string.IsNullOrEmpty(session.PaymentIntentId) || session.PaymentStatus != "paid")
            throw new PaymentGatewayException($"Sesiunea {sessionId} nu are o plată încasată.", new InvalidOperationException());

        var client = new StripeClient(_options.SecretKey);
        try
        {
            var refund = await new RefundService(client).CreateAsync(
                new RefundCreateOptions { PaymentIntent = session.PaymentIntentId, Reason = RefundReasons.RequestedByCustomer },
                new RequestOptions { IdempotencyKey = idempotencyKey },
                ct);
            return new RefundResult(refund.Id, refund.Status);
        }
        catch (StripeException e) when (e.StripeError?.Code == "charge_already_refunded")
        {
            // Rambursată deja (ex. din panoul Stripe): luăm rambursarea existentă în loc să eșuăm
            var existing = await new RefundService(client).ListAsync(
                new RefundListOptions { PaymentIntent = session.PaymentIntentId, Limit = 1 }, cancellationToken: ct);
            var first = existing.Data.FirstOrDefault()
                ?? throw new PaymentGatewayException("Stripe spune că plata e rambursată, dar nu găsesc rambursarea.", e);
            return new RefundResult(first.Id, first.Status);
        }
        catch (StripeException e)
        {
            throw new PaymentGatewayException($"Stripe a refuzat rambursarea: {e.Message}", e);
        }
    }

    private async Task<Session> CallStripe(Func<SessionService, Task<Session>> call)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
            throw new PaymentsNotConfiguredException();

        var service = new SessionService(new StripeClient(_options.SecretKey));
        try
        {
            return await call(service);
        }
        catch (StripeException e)
        {
            throw new PaymentGatewayException($"Stripe a răspuns cu o eroare: {e.Message}", e);
        }
    }

    private static CheckoutSession ToCheckoutSession(Session session) => new(
        session.Id,
        session.Url,
        session.Status switch
        {
            "open" => CheckoutSessionStatus.Open,
            "complete" => CheckoutSessionStatus.Complete,
            _ => CheckoutSessionStatus.Expired,
        },
        session.ExpiresAt,
        // „complete” poate însemna și „plata e în curs” (ex. transfer bancar); plătit = doar „paid”
        IsPaid: session.PaymentStatus == "paid",
        AmountTotalMinor: session.AmountTotal ?? 0,
        Currency: session.Currency ?? string.Empty);
}
