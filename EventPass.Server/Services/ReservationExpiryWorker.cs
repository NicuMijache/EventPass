namespace EventPass.Server.Services;

// Proces de fundal care eliberează locurile rezervărilor neplătite și expirate (#18).
// Fără el, o rezervare abandonată (clientul închide pagina) ar ține locurile pentru totdeauna:
// anularea „la deschidere” are loc doar dacă cineva mai deschide comanda.
public class ReservationExpiryWorker : BackgroundService
{
    // Cât de des verificăm. Un loc poate rămâne blocat cel mult atât după expirare.
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ReservationExpiryWorker> _logger;

    public ReservationExpiryWorker(IServiceScopeFactory scopes, ILogger<ReservationExpiryWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Lăsăm aplicația să pornească complet înainte de prima verificare
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            // PeriodicTimer: „bate” la fiecare 30 s; dacă o rundă durează mai mult, nu se suprapun două runde
            using var timer = new PeriodicTimer(CheckInterval);
            do
            {
                try
                {
                    // Fiecare rundă are propriul DbContext, ca o cerere HTTP
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ReservationExpiryService>().ExpireDueAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Ex. baza de date indisponibilă: nu oprim procesul, reîncercăm la runda următoare
                    _logger.LogError(e, "Verificarea rezervărilor expirate a eșuat.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Aplicația se oprește
        }
    }
}
