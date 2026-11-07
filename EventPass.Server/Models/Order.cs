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
    // Până când sunt ținute locurile pentru o comandă Pending; după, comanda se anulează
    public DateTime? ExpiresAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    // Trimiterea biletelor pe email. Starea stă în baza de date (nu în memorie), ca după o repornire
    // a aplicației să se știe ce a rămas de trimis. Pentru „retrimitere” (#21) se resetează TicketsEmailedAt la null.
    public DateTime? TicketsEmailedAt { get; set; }
    public int TicketsEmailAttempts { get; set; }
    public DateTime? TicketsEmailLastAttemptAt { get; set; }

    public bool IsExpired(DateTime now) => Status == OrderStatus.Pending && ExpiresAt <= now;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public List<OrderItem> Items { get; set; } = new();
    public List<Ticket> Tickets { get; set; } = new();
}
