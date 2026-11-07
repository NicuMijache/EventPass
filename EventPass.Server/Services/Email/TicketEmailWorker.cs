namespace EventPass.Server.Services.Email;

// Proces de fundal care trimite emailurile cu bilete, în afara cererilor HTTP.
// De ce nu direct în webhook: trimiterea poate dura sau pica, iar Stripe așteaptă un răspuns rapid;
// un email nereușit nu trebuie să strice confirmarea plății.
// Pornește la fiecare repornire a aplicației și verifică baza de date, deci nu se pierde nimic între reporniri.
public class TicketEmailWorker : BackgroundService
{
    // Chiar fără semnal, verificăm din când în când (pentru încercările reprogramate după un eșec)
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopes;
    private readonly TicketEmailSignal _signal;
    private readonly ILogger<TicketEmailWorker> _logger;

    public TicketEmailWorker(IServiceScopeFactory scopes, TicketEmailSignal signal, ILogger<TicketEmailWorker> logger)
    {
        _scopes = scopes;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Lăsăm aplicația să pornească complet înainte de prima verificare
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Fiecare rundă are propriul „scope” (propriul DbContext), ca într-o cerere HTTP
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<TicketEmailService>().SendPendingAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Un eșec (ex. baza de date indisponibilă) nu oprește procesul; reîncearcă la următoarea rundă
                    _logger.LogError(e, "Verificarea emailurilor cu bilete a eșuat.");
                }

                await _signal.WaitAsync(CheckInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Aplicația se oprește
        }
    }
}
