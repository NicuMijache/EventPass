using EventPass.Server.Data;
using EventPass.Server.Models;
using EventPass.Server.Services.Email;
using EventPass.Server.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Services;

public enum ExpiryOutcome
{
    NotExpired,      // rezervarea încă e valabilă (sau comanda nu mai e Pending)
    Cancelled,       // comanda a fost anulată acum și locurile s-au eliberat
    AlreadyHandled,  // între timp, altcineva a anulat-o sau a plătit-o (nu am mai eliberat nimic)
    PaymentRecovered,// Stripe spune că s-a plătit (webhook-ul n-a ajuns încă): am emis biletele, NU am anulat
    Postponed,       // nu știm sigur dacă s-a plătit (Stripe indisponibil, sesiune încă deschisă...): reîncercăm mai târziu
}

// Expirarea rezervărilor (#18): o comandă Pending ține locurile până la ExpiresAt; după aceea le eliberăm.
// Folosită în două locuri: de procesul de fundal (ReservationExpiryWorker), pentru toate comenzile,
// și când clientul își deschide comanda (ca să vadă imediat starea corectă).
public class ReservationExpiryService
{
    // Câte comenzi tratăm într-o rundă (restul, la runda următoare)
    public const int BatchSize = 100;

    private readonly AppDbContext _db;
    private readonly SeatReservationService _seats;
    private readonly IPaymentGateway _payments;
    private readonly OrderPaymentService _orderPayments;
    private readonly ITicketEmailSignal _emails;
    private readonly ILogger<ReservationExpiryService> _logger;

    public ReservationExpiryService(
        AppDbContext db,
        SeatReservationService seats,
        IPaymentGateway payments,
        OrderPaymentService orderPayments,
        ITicketEmailSignal emails,
        ILogger<ReservationExpiryService> logger)
    {
        _db = db;
        _seats = seats;
        _payments = payments;
        _orderPayments = orderPayments;
        _emails = emails;
        _logger = logger;
    }

    // Tratează toate comenzile expirate (o rundă a procesului de fundal). Întoarce câte s-au anulat.
    public async Task<int> ExpireDueAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var due = await _db.Orders
            .Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.Pending && o.ExpiresAt <= now)
            .OrderBy(o => o.ExpiresAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        var cancelled = 0;
        foreach (var order in due)
        {
            try
            {
                if (await ExpireAsync(order, ct) == ExpiryOutcome.Cancelled)
                    cancelled++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // O comandă cu probleme nu le blochează pe celelalte; o reîncercăm la runda următoare
                _logger.LogError(e, "Expirarea comenzii {OrderId} a eșuat.", order.Id);
            }
        }

        if (cancelled > 0)
            _logger.LogInformation("{Count} rezervări expirate au fost anulate; locurile s-au eliberat.", cancelled);
        return cancelled;
    }

    // Comanda trebuie încărcată cu Items (ca să știm câte locuri eliberăm)
    public async Task<ExpiryOutcome> ExpireAsync(Order order, CancellationToken ct = default)
    {
        if (!order.IsExpired(DateTime.UtcNow))
            return ExpiryOutcome.NotExpired;

        // Clientul a ajuns la plată: înainte să-i luăm locurile, întrebăm Stripe ce s-a întâmplat cu sesiunea.
        // Altfel apare o cursă periculoasă: clientul plătește în ultima secundă, webhook-ul întârzie câteva minute,
        // noi anulăm și vindem locul altcuiva, iar clientul rămâne cu banii luați și fără bilet.
        if (order.StripeSessionId is not null)
        {
            var decision = await CheckPaymentAsync(order, ct);
            if (decision is not null)
                return decision.Value;
        }

        // Anularea însăși e protejată: UPDATE ... WHERE Status = Pending reușește o singură dată,
        // deci chiar dacă procesul de fundal și clientul anulează simultan, locurile se eliberează o singură dată
        return await _seats.CancelAsync(order, ct) ? ExpiryOutcome.Cancelled : ExpiryOutcome.AlreadyHandled;
    }

    // null = sesiunea a expirat la Stripe, deci nu se mai poate plăti: comanda se poate anula în siguranță
    private async Task<ExpiryOutcome?> CheckPaymentAsync(Order order, CancellationToken ct)
    {
        CheckoutSession? session;
        try
        {
            session = await _payments.GetCheckoutSessionAsync(order.StripeSessionId!, ct);
        }
        catch (Exception e) when (e is PaymentGatewayException or PaymentsNotConfiguredException)
        {
            // Nu știm dacă s-a plătit. Mai bine ținem locurile câteva minute în plus decât să anulăm o comandă plătită.
            _logger.LogWarning("Comanda {OrderId}: nu am putut verifica plata la Stripe ({Reason}); reîncerc mai târziu.", order.Id, e.Message);
            return ExpiryOutcome.Postponed;
        }

        switch (session)
        {
            case null:
                // Sesiunea nu există în contul Stripe configurat (ex. s-a schimbat cheia cu a altui cont).
                // Nu putem ști dacă s-a plătit: nu anulăm singuri; trebuie verificat de un om.
                _logger.LogError("Comanda {OrderId}: sesiunea {SessionId} nu există la Stripe. Verifică manual.", order.Id, order.StripeSessionId);
                return ExpiryOutcome.Postponed;

            case { Status: CheckoutSessionStatus.Complete, IsPaid: true }:
                // S-a plătit, dar webhook-ul nu a ajuns (încă). Confirmăm plata noi, cu aceleași verificări
                // (sumă, monedă, sesiune) ca la webhook. Dacă webhook-ul vine după, găsește comanda deja Paid.
                var result = await _orderPayments.ConfirmPaymentAsync(order.Id, session.Id, session.AmountTotalMinor, session.Currency, ct);
                if (result is PaymentConfirmation.Paid or PaymentConfirmation.AlreadyPaid)
                {
                    _emails.Notify();
                    _logger.LogWarning("Comanda {OrderId} era plătită, dar webhook-ul nu ajunsese: am emis biletele ({Result}).", order.Id, result);
                    return ExpiryOutcome.PaymentRecovered;
                }
                _logger.LogError("Comanda {OrderId}: Stripe spune plătit, dar confirmarea a dat {Result}. Verifică manual.", order.Id, result);
                return ExpiryOutcome.Postponed;

            case { Status: CheckoutSessionStatus.Complete }:
                // Plată în curs (ex. transfer bancar): așteptăm rezultatul
                return ExpiryOutcome.Postponed;

            case { Status: CheckoutSessionStatus.Open }:
                // Ceasul nostru și al Stripe diferă puțin: sesiunea se mai poate plăti câteva secunde. Așteptăm.
                return ExpiryOutcome.Postponed;

            default:
                return null;   // Expired
        }
    }
}
