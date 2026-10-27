using System.Security.Claims;
using EventPass.Server.Data;
using EventPass.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
