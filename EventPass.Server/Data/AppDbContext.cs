using EventPass.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<TicketType> TicketTypes => Set<TicketType>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(256);
            e.Property(u => u.FullName).HasMaxLength(200);
            // Enum-urile se salvează ca text ("Organizer"), nu ca număr
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Event>(e =>
        {
            e.Property(ev => ev.Title).HasMaxLength(200);
            e.Property(ev => ev.Location).HasMaxLength(300);
            // Un organizator cu evenimente nu poate fi șters
            e.HasOne(ev => ev.Organizer)
                .WithMany(u => u.Events)
                .HasForeignKey(ev => ev.OrganizerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TicketType>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(100);
            e.Property(t => t.Price).HasPrecision(10, 2);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_TicketType_Price", "\"Price\" >= 0");
                t.HasCheckConstraint("CK_TicketType_Sold", "\"Sold\" >= 0 AND \"Sold\" <= \"Capacity\"");
            });
            // Ștergerea evenimentului șterge și tipurile lui de bilet
            e.HasOne(t => t.Event)
                .WithMany(ev => ev.TicketTypes)
                .HasForeignKey(t => t.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.Property(o => o.TotalAmount).HasPrecision(10, 2);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(o => o.StripeSessionId).IsUnique();
            e.HasOne(o => o.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.Property(i => i.UnitPrice).HasPrecision(10, 2);
            e.ToTable(t => t.HasCheckConstraint("CK_OrderItem_Quantity", "\"Quantity\" > 0"));
            e.HasOne(i => i.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            // Un tip de bilet care apare în comenzi nu poate fi șters
            e.HasOne(i => i.TicketType)
                .WithMany()
                .HasForeignKey(i => i.TicketTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Ticket>(e =>
        {
            // Token-ul din QR trebuie să fie unic și căutat rapid la scanare
            e.HasIndex(t => t.Token).IsUnique();
            e.HasOne(t => t.Order)
                .WithMany(o => o.Tickets)
                .HasForeignKey(t => t.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.TicketType)
                .WithMany(tt => tt.Tickets)
                .HasForeignKey(t => t.TicketTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
