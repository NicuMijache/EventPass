namespace EventPass.Server.Models;

public enum UserRole
{
    Client,
    Organizer,
    Staff,
    Admin
}

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    // Doar hash-ul parolei, niciodată parola în clar
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Client;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Evenimentele create de organizator
    public List<Event> Events { get; set; } = new();
    // Comenzile făcute de client
    public List<Order> Orders { get; set; } = new();
}
