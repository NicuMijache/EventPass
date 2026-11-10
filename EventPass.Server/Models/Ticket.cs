namespace EventPass.Server.Models;

public class Ticket
{
    public int Id { get; set; }
    // Token unic, pus în codul QR și verificat la intrare
    public Guid Token { get; set; } = Guid.NewGuid();
    public bool IsUsed { get; set; }
    public DateTime? UsedAt { get; set; }
    // Biletul a fost anulat (comanda rambursată, #19): nu mai deschide ușa, chiar dacă cineva are încă QR-ul
    public DateTime? VoidedAt { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int TicketTypeId { get; set; }
    public TicketType TicketType { get; set; } = null!;
}
