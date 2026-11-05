using Stripe;
using Stripe.Checkout;

namespace EventPass.Server.Services.Payments;

public class StripeOptions
{
    // Cheia secretă de test (sk_test_...), doar în User Secrets
    public string SecretKey { get; set; } = string.Empty;
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
        session.ExpiresAt);
}
