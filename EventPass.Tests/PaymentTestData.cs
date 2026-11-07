using EventPass.Server.Models;

namespace EventPass.Tests;

// Date comune pentru testele plății: o comandă neplătită (Pending) cu 3 locuri rezervate
public static class PaymentTestData
{
    public const int OrderId = 1;
    public const string SessionId = "cs_test_1";
    // 2 × Standard (50 lei) + 1 × VIP (200 lei) = 300 lei = 30000 bani
    public const long AmountMinor = 30000;

    public static void Seed(SqliteTestDatabase database)
    {
        using var db = database.CreateContext();
        db.Users.AddRange(
            new User { Id = 1, Email = "ana@example.com", FullName = "Ana", Role = UserRole.Client },
            new User { Id = 3, Email = "org@example.com", FullName = "Org", Role = UserRole.Organizer });
        db.Events.Add(new Event
        {
            Id = 10, Title = "Concert", Venue = "BT Arena", City = "Cluj-Napoca",
            StartsAt = DateTime.UtcNow.AddMonths(1), IsPublished = true, OrganizerId = 3,
        });
        // Sold = locurile deja REZERVATE la crearea comenzii (plata nu mai modifică stocul)
        db.TicketTypes.AddRange(
            new TicketType { Id = 100, EventId = 10, Name = "Standard", Price = 50, Capacity = 10, Sold = 2 },
            new TicketType { Id = 101, EventId = 10, Name = "VIP", Price = 200, Capacity = 5, Sold = 1 });
        db.Orders.Add(new Order
        {
            Id = OrderId,
            UserId = 1,
            Status = OrderStatus.Pending,
            TotalAmount = 300,
            StripeSessionId = SessionId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            Items =
            {
                new OrderItem { TicketTypeId = 100, Quantity = 2, UnitPrice = 50 },
                new OrderItem { TicketTypeId = 101, Quantity = 1, UnitPrice = 200 },
            },
        });
        db.SaveChanges();
    }
}
