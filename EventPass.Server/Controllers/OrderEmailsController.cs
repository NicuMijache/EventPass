using EventPass.Server.Data;
using EventPass.Server.Services;
using EventPass.Server.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Controllers;

// Retrimiterea biletelor pe email (#21). Emailul pleacă la adresa contului (nu se poate alege alta:
// altfel cineva ar putea trimite biletele altcuiva oriunde).
[ApiController]
[Route("api/orders/{orderId:int}/resend-tickets")]
[Authorize]
public class OrderEmailsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TicketEmailService _emails;
    private readonly ITicketEmailSignal _signal;

    public OrderEmailsController(AppDbContext db, TicketEmailService emails, ITicketEmailSignal signal)
    {
        _db = db;
        _emails = emails;
        _signal = signal;
    }

    // 202 Accepted: cererea e primită, emailul pleacă în câteva secunde (îl trimite procesul de fundal)
    [HttpPost]
    public async Task<IActionResult> Resend(int orderId, CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (!await _db.Orders.AnyAsync(o => o.Id == orderId && o.UserId == userId, ct))
            return NotFound();

        var result = await _emails.RequestResendAsync(orderId, ct);
        switch (result.Outcome)
        {
            case ResendOutcome.Queued:
            case ResendOutcome.AlreadyQueued:
                _signal.Notify();   // trezim procesul de email acum, nu peste un minut
                return Accepted(new { message = "Trimitem biletele pe email în câteva secunde." });

            case ResendOutcome.TooSoon:
                var seconds = (int)Math.Ceiling(result.RetryAfter!.Value.TotalSeconds);
                // Retry-After: antet standard, spune cât să aștepte cel care a cerut
                Response.Headers.RetryAfter = seconds.ToString();
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    message = $"Emailul a fost trimis de curând. Poți cere din nou peste {Seconds(seconds)}.",
                    retryAfterSeconds = seconds,
                });

            default:
                return Conflict(new { message = "Doar biletele unei comenzi plătite se pot retrimite." });
        }
    }

    // În română: „o secundă”, „5 secunde”, „20 de secunde” (de la 20 în sus se pune „de”)
    public static string Seconds(int n) => n switch
    {
        1 => "o secundă",
        < 20 => $"{n} secunde",
        _ => $"{n} de secunde",
    };
}
