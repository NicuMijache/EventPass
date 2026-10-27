namespace EventPass.Server.Models;

public class Event
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    // Toate datele se salvează în UTC
    public DateTime StartsAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // Ciornă (false) = vizibil doar organizatorului; publicat (true) = vizibil clienților
    public bool IsPublished { get; set; }

    public int OrganizerId { get; set; }
    public User Organizer { get; set; } = null!;

    public List<TicketType> TicketTypes { get; set; } = new();
}
