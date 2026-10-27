using EventPass.Server.Controllers;
using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace EventPass.Tests;

public class OrganizerTicketTypesControllerTests
{
    // Evenimentul 10 e al organizatorului 1 (logat) și are tipurile:
    //   100 "Standard" (200 locuri, 30 vândute) și 101 "VIP" (50 locuri, nimic vândut).
    // Evenimentul 20 e al organizatorului 2.
    private static (AppDbContext Db, OrganizerTicketTypesController Controller) Setup(bool published = false)
    {
        var db = TestHelpers.CreateDb();
        var future = DateTime.UtcNow.AddMonths(2);
        db.Users.AddRange(
            new User { Id = 1, Email = "org1@example.com", Role = UserRole.Organizer },
            new User { Id = 2, Email = "org2@example.com", Role = UserRole.Organizer });
        db.Events.AddRange(
            new Event { Id = 10, Title = "Al meu", Venue = "BT Arena", City = "Cluj-Napoca", StartsAt = future, OrganizerId = 1, IsPublished = published },
            new Event { Id = 20, Title = "Al altuia", Venue = "Sala Polivalentă", City = "Iași", StartsAt = future, OrganizerId = 2 });
        db.TicketTypes.AddRange(
            new TicketType { Id = 100, Name = "Standard", Price = 80, Capacity = 200, Sold = 30, EventId = 10 },
            new TicketType { Id = 101, Name = "VIP", Price = 200, Capacity = 50, EventId = 10 });
        db.SaveChanges();

        var controller = new OrganizerTicketTypesController(db);
        TestHelpers.LoginAs(controller, userId: 1, role: "Organizer");
        return (db, controller);
    }

    [Fact]
    public async Task Create_AdaugaTipNouCuToateLocurileDisponibile()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Create(10, new SaveTicketTypeRequest("Student", 40, 100));

        var dto = Assert.IsType<TicketTypeDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.Equal(100, dto.Available);
        Assert.Equal(3, db.TicketTypes.Count(t => t.EventId == 10));
    }

    [Fact]
    public async Task Create_NumeExistentCuAlteMajuscule_Returneaza409()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Create(10, new SaveTicketTypeRequest("vip", 10, 5));

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_LaEvenimentulAltuiOrganizator_Returneaza404()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Create(20, new SaveTicketTypeRequest("Intrus", 1, 1));

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Empty(db.TicketTypes.Where(t => t.EventId == 20));
    }

    [Fact]
    public async Task Update_CapacitateSubBileteleVandute_Returneaza400()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Update(10, 100, new SaveTicketTypeRequest("Standard", 80, 29));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(200, db.TicketTypes.Find(100)!.Capacity);
    }

    [Fact]
    public async Task Update_CapacitateEgalaCuBileteleVandute_EstePermisa()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Update(10, 100, new SaveTicketTypeRequest("Standard", 80, 30));

        var dto = Assert.IsType<TicketTypeDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(0, dto.Available);
    }

    [Fact]
    public async Task Update_TipDeLaAltEveniment_Returneaza404()
    {
        var (db, controller) = Setup();
        using var _ = db;

        // Tipul 100 există, dar nu aparține evenimentului 20
        var result = await controller.Update(20, 100, new SaveTicketTypeRequest("X", 1, 300));

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Delete_TipFaraComenzi_IlSterge()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Delete(10, 101);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(db.TicketTypes.Find(101));
    }

    [Fact]
    public async Task Delete_TipCuComenzi_Returneaza409()
    {
        var (db, controller) = Setup();
        using var _ = db;
        db.Orders.Add(new Order { Id = 500, UserId = 2 });
        db.OrderItems.Add(new OrderItem { OrderId = 500, TicketTypeId = 101, Quantity = 1, UnitPrice = 200 });
        db.SaveChanges();

        var result = await controller.Delete(10, 101);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(db.TicketTypes.Find(101));
    }

    [Fact]
    public async Task Delete_UltimulTipLaEvenimentPublicat_Returneaza400()
    {
        var (db, controller) = Setup(published: true);
        using var _ = db;
        await controller.Delete(10, 101);

        var result = await controller.Delete(10, 100);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(db.TicketTypes.Find(100));
    }
}
