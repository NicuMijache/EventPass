using System.Security.Claims;

namespace EventPass.Server.Services;

public static class ClaimsPrincipalExtensions
{
    // Id-ul utilizatorului logat, citit din token (claim-ul NameIdentifier)
    public static int GetUserId(this ClaimsPrincipal principal) =>
        int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
