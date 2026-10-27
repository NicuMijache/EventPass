namespace EventPass.Server.Models;

public enum OrderStatus
{
    Pending,
    Paid,
    Cancelled,
    Refunded
}

public class Order
{
    public int Id { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal TotalAmount { get; set; }
    // Id-ul sesiunii Stripe Checkout, folosit la webhook ca să găsim comanda
    public string? StripeSessionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public List<OrderItem> Items { get; set; } = new();
    public List<Ticket> Tickets { get; set; } = new();
}
