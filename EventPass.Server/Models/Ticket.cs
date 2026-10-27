namespace EventPass.Server.Models;

public class Ticket
{
    public int Id { get; set; }
    // Token unic, pus în codul QR și verificat la intrare
    public Guid Token { get; set; } = Guid.NewGuid();
    public bool IsUsed { get; set; }
    public DateTime? UsedAt { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int TicketTypeId { get; set; }
    public TicketType TicketType { get; set; } = null!;
}
