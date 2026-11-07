using EventPass.Server.Data;
using EventPass.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Services.Email;

// „Anunță” procesul de fundal că a apărut o comandă de trimis. E doar un semnal de trezire:
// adevărul despre ce trebuie trimis stă în baza de date (Order.TicketsEmailedAt), nu aici.
public interface ITicketEmailSignal
{
    void Notify();
}

public sealed class TicketEmailSignal : ITicketEmailSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    // Mai multe notificări la rând se contopesc într-una singură (un singur „trezește-te” ajunge)
    public void Notify()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { }
    }

    // Așteaptă un semnal sau, dacă nu vine niciunul, trece timpul. Returnează true dacă a venit un semnal.
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

// Trimite emailurile cu bilete pentru comenzile plătite care nu au primit încă unul.
public class TicketEmailService
{
    // După atâtea încercări eșuate renunțăm (comanda rămâne în baza de date, vizibilă în log)
    public const int MaxAttempts = 6;
    private const int BatchSize = 100;

    private readonly AppDbContext _db;
    private readonly TicketEmailBuilder _builder;
    private readonly IEmailSender _sender;
    private readonly ILogger<TicketEmailService> _logger;

    public TicketEmailService(AppDbContext db, TicketEmailBuilder builder, IEmailSender sender, ILogger<TicketEmailService> logger)
    {
        _db = db;
        _builder = builder;
        _sender = sender;
        _logger = logger;
    }

    // Pauza dinaintea următoarei încercări: 2, 4, 8, 16 minute… (crește de două ori la fiecare eșec)
    public static TimeSpan BackoffAfter(int attempts) => TimeSpan.FromMinutes(Math.Pow(2, attempts));

    private static bool IsDue(Order order, DateTime now) =>
        order.TicketsEmailAttempts == 0 ||
        order.TicketsEmailLastAttemptAt is null ||
        order.TicketsEmailLastAttemptAt + BackoffAfter(order.TicketsEmailAttempts) <= now;

    // Întoarce câte emailuri s-au trimis acum
    public async Task<int> SendPendingAsync(CancellationToken ct = default)
    {
        var candidates = await _db.Orders
            .Include(o => o.User)
            .Include(o => o.Tickets)
            .Include(o => o.Items).ThenInclude(i => i.TicketType).ThenInclude(t => t.Event)
            .Where(o => o.Status == OrderStatus.Paid
                        && o.TicketsEmailedAt == null
                        && o.TicketsEmailAttempts < MaxAttempts
                        && o.Tickets.Any())
            .OrderBy(o => o.PaidAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var order in candidates)
        {
            var now = DateTime.UtcNow;
            if (!IsDue(order, now))
                continue;

            try
            {
                await _sender.SendAsync(_builder.Build(order), ct);
            }
            catch (EmailNotConfiguredException)
            {
                // Nu e vina comenzii: nu consumăm încercări și nu mai încercăm pe celelalte acum
                _logger.LogWarning("Emailurile cu bilete așteaptă: serverul SMTP nu este configurat (Smtp:Host).");
                break;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // O comandă care nu se poate trimite nu le blochează pe celelalte
                await _db.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.TicketsEmailAttempts, o => o.TicketsEmailAttempts + 1)
                    .SetProperty(o => o.TicketsEmailLastAttemptAt, now), ct);
                _logger.LogError(e,
                    "Emailul cu biletele comenzii {OrderId} nu s-a trimis (încercarea {Attempt} din {Max}).",
                    order.Id, order.TicketsEmailAttempts + 1, MaxAttempts);
                continue;
            }

            await _db.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.TicketsEmailedAt, now)
                .SetProperty(o => o.TicketsEmailAttempts, o => o.TicketsEmailAttempts + 1)
                .SetProperty(o => o.TicketsEmailLastAttemptAt, now), ct);
            _logger.LogInformation("Biletele comenzii {OrderId} au fost trimise pe {Email}.", order.Id, order.User.Email);
            sent++;
        }

        return sent;
    }
}
