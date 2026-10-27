namespace EventPass.Server.Models;

public class Event
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    // Locul exact (ex. "BT Arena") și orașul (ex. "Cluj-Napoca"), separate ca să putem filtra după oraș
    public string Venue { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    // Calea relativă a posterului (ex. "/uploads/events/abc.jpg"); null dacă nu are poză
    public string? ImagePath { get; set; }
    // Toate datele se salvează în UTC
    public DateTime StartsAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // Ciornă (false) = vizibil doar organizatorului; publicat (true) = vizibil clienților
    public bool IsPublished { get; set; }

    public int OrganizerId { get; set; }
    public User Organizer { get; set; } = null!;

    public List<TicketType> TicketTypes { get; set; } = new();
}
