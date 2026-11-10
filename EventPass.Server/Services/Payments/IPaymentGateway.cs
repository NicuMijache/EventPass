namespace EventPass.Server.Services.Payments;

// O linie de pe pagina de plată: „Nocturne Jazz – Standard”, 9000 bani, × 2
public record CheckoutLine(string Name, long UnitAmountMinor, int Quantity);

public record CheckoutRequest(
    int OrderId,
    string CustomerEmail,
    string Currency,
    List<CheckoutLine> Lines,
    string SuccessUrl,
    string CancelUrl,
    DateTime ExpiresAt);

public enum CheckoutSessionStatus
{
    Open,      // clientul poate încă plăti
    Complete,  // plata s-a făcut
    Expired    // sesiunea nu mai poate fi folosită
}

// IsPaid, AmountTotalMinor și Currency: ce a încasat Stripe (folosite când verificăm noi plata, fără webhook)
public record CheckoutSession(
    string Id,
    string Url,
    CheckoutSessionStatus Status,
    DateTime ExpiresAt,
    bool IsPaid = false,
    long AmountTotalMinor = 0,
    string Currency = "");

// Tot ce are nevoie aplicația de la procesatorul de plăți.
// În aplicație: StripePaymentGateway. În teste: o variantă falsă, fără internet.
public interface IPaymentGateway
{
    Task<CheckoutSession> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default);

    // null dacă sesiunea nu există
    Task<CheckoutSession?> GetCheckoutSessionAsync(string sessionId, CancellationToken ct = default);
}

// Nu s-a configurat cheia Stripe (Stripe:SecretKey lipsește din User Secrets)
public class PaymentsNotConfiguredException() : Exception("Stripe:SecretKey nu este setat.");

// Stripe a răspuns cu o eroare sau nu a putut fi contactat
public class PaymentGatewayException(string message, Exception inner) : Exception(message, inner);

public static class Money
{
    // Stripe lucrează cu numere întregi în cea mai mică unitate a monedei: 90,50 lei = 9050 bani.
    // Așa se evită erorile de rotunjire ale numerelor cu virgulă.
    public static long ToMinorUnits(decimal amount) => (long)decimal.Round(amount * 100m, MidpointRounding.AwayFromZero);

    // Cea mai mică plată pe care o acceptă Stripe în RON
    public const decimal MinimumChargeRon = 2.00m;
}
