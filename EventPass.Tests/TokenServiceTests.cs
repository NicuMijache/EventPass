using System.Security.Claims;
using System.Text;
using EventPass.Server.Models;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EventPass.Tests;

public class TokenServiceTests
{
    private static readonly User TestUser = new()
    {
        Id = 7,
        Email = "ana@example.com",
        FullName = "Ana Pop",
        Role = UserRole.Organizer
    };

    private static TokenValidationParameters ValidationParameters(string key) => new()
    {
        ValidIssuer = TestHelpers.Jwt.Issuer,
        ValidAudience = TestHelpers.Jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
    };

    [Fact]
    public async Task CreateToken_ContineIdEmailSiRolulUtilizatorului()
    {
        var (token, _) = TestHelpers.CreateTokenService().CreateToken(TestUser);

        var result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(token, ValidationParameters(TestHelpers.Jwt.Key));

        Assert.True(result.IsValid);
        Assert.Equal("7", result.ClaimsIdentity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("ana@example.com", result.ClaimsIdentity.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal("Organizer", result.ClaimsIdentity.FindFirst(ClaimTypes.Role)?.Value);
    }

    [Fact]
    public void CreateToken_ExpiraDupaTimpulSetat()
    {
        var before = DateTime.UtcNow;

        var (_, expiresAt) = TestHelpers.CreateTokenService().CreateToken(TestUser);

        var expected = before.AddMinutes(TestHelpers.Jwt.ExpiresMinutes);
        Assert.InRange(expiresAt, expected.AddSeconds(-5), expected.AddSeconds(5));
    }

    [Fact]
    public async Task CreateToken_EsteRespinsDacaCheiaEsteAlta()
    {
        var (token, _) = TestHelpers.CreateTokenService().CreateToken(TestUser);

        // Un token semnat cu cheia serverului nu trebuie acceptat cu altă cheie
        var result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(token, ValidationParameters("o-cu-totul-alta-cheie-secreta-de-minim-32-caractere"));

        Assert.False(result.IsValid);
    }
}
