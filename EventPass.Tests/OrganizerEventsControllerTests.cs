using EventPass.Server.Controllers;
using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace EventPass.Tests;

public class OrganizerEventsControllerTests
{
    private static readonly DateTime Future = DateTime.UtcNow.AddMonths(2);

    // Doi organizatori: 1 (cel logat în teste) și 2 (altcineva).
    // Evenimentul 10 e al organizatorului 1, evenimentul 20 al organizatorului 2.
    private static (AppDbContext Db, OrganizerEventsController Controller) Setup()
    {
        var db = TestHelpers.CreateDb();
        db.Users.AddRange(
            new User { Id = 1, Email = "org1@example.com", Role = UserRole.Organizer },
            new User { Id = 2, Email = "org2@example.com", Role = UserRole.Organizer },
            new User { Id = 3, Email = "client@example.com", Role = UserRole.Client });
        db.Events.AddRange(
            new Event { Id = 10, Title = "Al meu", Location = "Cluj", StartsAt = Future, OrganizerId = 1 },
            new Event { Id = 20, Title = "Al altuia", Location = "Iași", StartsAt = Future, OrganizerId = 2 });
        db.SaveChanges();

        var controller = new OrganizerEventsController(db);
        TestHelpers.LoginAs(controller, userId: 1, role: "Organizer");
        return (db, controller);
    }

    private static SaveEventRequest Request(DateTime startsAt) =>
        new("Concert", "Descriere", "Sala Polivalentă", startsAt);

    [Fact]
    public async Task Create_EvenimentNouPornesteCaCiorna()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Create(Request(Future));

        var dto = Assert.IsType<EventDetailDto>(Assert.IsType<CreatedAtActionResult>(result.Result).Value);
        Assert.False(dto.IsPublished);
        Assert.Equal(1, db.Events.Find(dto.Id)!.OrganizerId);
    }

    [Fact]
    public async Task Create_DataInTrecut_Returneaza400()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Create(Request(DateTime.UtcNow.AddDays(-1)));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(2, db.Events.Count());
    }

    [Fact]
    public async Task GetMine_ReturneazaDoarEvenimenteleOrganizatoruluiLogat()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.GetMine();

        var events = Assert.IsType<List<EventSummaryDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(10, Assert.Single(events).Id);
    }

    [Fact]
    public async Task Get_EvenimentulAltuiOrganizator_Returneaza404()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Get(20);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Update_EvenimentulAltuiOrganizator_Returneaza404SiNuIlModifica()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Update(20, Request(Future) with { Title = "Furat" });

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal("Al altuia", db.Events.Find(20)!.Title);
    }

    [Fact]
    public async Task Delete_EvenimentulAltuiOrganizator_Returneaza404SiNuIlSterge()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Delete(20);

        Assert.IsType<NotFoundResult>(result);
        Assert.NotNull(db.Events.Find(20));
    }

    [Fact]
    public async Task Publish_FaraTipuriDeBilete_Returneaza400()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Publish(10);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(db.Events.Find(10)!.IsPublished);
    }

    [Fact]
    public async Task Publish_CuTipuriDeBilete_PublicaEvenimentul()
    {
        var (db, controller) = Setup();
        using var _ = db;
        db.TicketTypes.Add(new TicketType { Name = "Standard", Price = 50, Capacity = 100, EventId = 10 });
        db.SaveChanges();

        var result = await controller.Publish(10);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(db.Events.Find(10)!.IsPublished);
    }

    [Fact]
    public async Task Delete_FaraComenzi_StergeEvenimentul()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Delete(10);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(db.Events.Find(10));
    }

    [Fact]
    public async Task Delete_CuComenzi_Returneaza409()
    {
        var (db, controller) = Setup();
        using var _ = db;
        db.TicketTypes.Add(new TicketType { Id = 100, Name = "Standard", Price = 50, Capacity = 100, EventId = 10 });
        db.Orders.Add(new Order { Id = 500, UserId = 3 });
        db.OrderItems.Add(new OrderItem { OrderId = 500, TicketTypeId = 100, Quantity = 2, UnitPrice = 50 });
        db.SaveChanges();

        var result = await controller.Delete(10);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(db.Events.Find(10));
    }
}
