namespace EventPass.Server.Models;

// Tip de bilet pentru un eveniment, de ex. "Standard" sau "VIP"
public class TicketType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    // Numărul total de locuri pentru acest tip
    public int Capacity { get; set; }
    // Câte bilete s-au vândut deja (Capacity - Sold = locuri rămase)
    public int Sold { get; set; }

    public int EventId { get; set; }
    public Event Event { get; set; } = null!;

    public List<Ticket> Tickets { get; set; } = new();
}
