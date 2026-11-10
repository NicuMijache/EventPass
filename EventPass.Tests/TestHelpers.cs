using System.Security.Claims;
using EventPass.Server.Data;
using EventPass.Server.Services;
using EventPass.Server.Services.Email;
using EventPass.Server.Services.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EventPass.Tests;

// Înlocuiește stocarea reală a imaginilor: nu scrie nimic pe disc,
// doar ține minte ce s-a salvat și ce s-a șters
public class FakeImageStorage : IImageStorage
{
    public List<string> Saved { get; } = new();
    public List<string> Deleted { get; } = new();

    public Task<string> SaveEventImageAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var path = $"/uploads/events/test-{Saved.Count + 1}{extension}";
        Saved.Add(path);
        return Task.FromResult(path);
    }

    public void Delete(string? publicPath)
    {
        if (!string.IsNullOrEmpty(publicPath))
            Deleted.Add(publicPath);
    }
}

// Serviciul de email simulat: ține minte ce i s-a cerut să trimită; testul poate cere să „pice”
public class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = new();
    // Dacă întoarce o excepție pentru un mesaj, trimiterea acelui mesaj eșuează
    public Func<EmailMessage, Exception?>? FailWhen { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var failure = FailWhen?.Invoke(message);
        if (failure is not null)
            throw failure;
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

// Semnalul către procesul de email, simulat: doar numără câte anunțuri au venit
public class FakeTicketEmailSignal : ITicketEmailSignal
{
    public int Notifications { get; private set; }
    public void Notify() => Notifications++;
}

// Stripe simulat: nu iese pe internet, doar ține minte ce i s-a cerut
// și întoarce sesiuni inventate. Testul poate schimba starea unei sesiuni.
public class FakePaymentGateway : IPaymentGateway
{
    public List<CheckoutRequest> Requests { get; } = new();
    public Dictionary<string, CheckoutSession> Sessions { get; } = new();
    public Exception? FailWith { get; set; }

    public Task<CheckoutSession> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        if (FailWith is not null)
            throw FailWith;

        Requests.Add(request);
        var id = $"cs_test_{Requests.Count}";
        var session = new CheckoutSession(id, $"https://checkout.stripe.test/{id}", CheckoutSessionStatus.Open, request.ExpiresAt);
        Sessions[id] = session;
        return Task.FromResult(session);
    }

    // Dacă e setată, citirea unei sesiuni eșuează (Stripe indisponibil)
    public Exception? GetFailsWith { get; set; }
    public int GetCalls { get; private set; }

    public Task<CheckoutSession?> GetCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
    {
        GetCalls++;
        if (GetFailsWith is not null)
            throw GetFailsWith;
        return Task.FromResult(Sessions.GetValueOrDefault(sessionId));
    }

    public void SetStatus(string sessionId, CheckoutSessionStatus status) =>
        Sessions[sessionId] = Sessions[sessionId] with { Status = status };

    // Clientul a plătit pe pagina Stripe (sesiune „complete”, „paid”)
    public void SetPaid(string sessionId, long amountMinor, string currency = "ron") =>
        Sessions[sessionId] = Sessions.TryGetValue(sessionId, out var s)
            ? s with { Status = CheckoutSessionStatus.Complete, IsPaid = true, AmountTotalMinor = amountMinor, Currency = currency }
            : new CheckoutSession(sessionId, $"https://checkout.stripe.test/{sessionId}", CheckoutSessionStatus.Complete,
                DateTime.UtcNow, true, amountMinor, currency);
}

public static class TestHelpers
{
    public static readonly JwtOptions Jwt = new()
    {
        Issuer = "EventPass.Tests",
        Audience = "EventPass.Tests.Client",
        Key = "cheie-de-test-doar-pentru-teste-unitare-0123456789",
        ExpiresMinutes = 60
    };

    public static TokenService CreateTokenService() => new(Options.Create(Jwt));

    // Serviciul de expirare a rezervărilor, cu dependențele lui (toate pe același DbContext, ca într-o cerere)
    public static ReservationExpiryService CreateExpiryService(AppDbContext db, IPaymentGateway payments, ITicketEmailSignal? emails = null) =>
        new(db,
            new SeatReservationService(db),
            payments,
            new OrderPaymentService(db, NullLogger<OrderPaymentService>.Instance),
            emails ?? new FakeTicketEmailSignal(),
            NullLogger<ReservationExpiryService>.Instance);

    // Fiecare test primește propria bază de date în memorie (nume unic),
    // ca testele să nu se influențeze între ele
    public static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // Simulează un utilizator logat (ca și cum ar fi trimis un token valid)
    public static void LoginAs(ControllerBase controller, int userId, string role) =>
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, role)
                }, "Test"))
            }
        };
}
