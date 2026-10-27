namespace EventPass.Server.Models;

// O linie din comandă, de ex. "2 x VIP". Există înainte de plată;
// biletele (Ticket) se emit din aceste linii abia după confirmarea Stripe.
public class OrderItem
{
    public int Id { get; set; }
    public int Quantity { get; set; }
    // Prețul de la momentul comenzii, chiar dacă organizatorul îl schimbă ulterior
    public decimal UnitPrice { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int TicketTypeId { get; set; }
    public TicketType TicketType { get; set; } = null!;
}
