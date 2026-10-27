namespace EventPass.Server.Services;

// Setările din secțiunea "Jwt" (appsettings.json + User Secrets pentru Key)
public class JwtOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    // Cheia secretă de semnare; minim 32 de caractere, doar în User Secrets
    public string Key { get; set; } = string.Empty;
    public int ExpiresMinutes { get; set; } = 60;
}
