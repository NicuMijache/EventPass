using EventPass.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Data;

// Creează primul cont de Admin la pornire, din setările SeedAdmin (User Secrets).
// Fără el n-ar exista nimeni care să poată da roluri de Organizer/Staff.
public static class AdminSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(AdminSeeder));

        var email = config["SeedAdmin:Email"]?.Trim().ToLowerInvariant();
        var password = config["SeedAdmin:Password"];
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            logger.LogInformation("SeedAdmin nu este setat; nu se creează contul de Admin.");
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Users.AnyAsync(u => u.Email == email))
            return;

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var admin = new User
        {
            Email = email,
            FullName = "Administrator",
            Role = UserRole.Admin
        };
        admin.PasswordHash = hasher.HashPassword(admin, password);

        db.Users.Add(admin);
        await db.SaveChangesAsync();
        logger.LogInformation("Contul de Admin {Email} a fost creat.", email);
    }
}
