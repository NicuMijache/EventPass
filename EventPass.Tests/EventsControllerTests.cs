using EventPass.Server.Controllers;
using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace EventPass.Tests;

public class EventsControllerTests
{
    private static readonly DateTime Future = DateTime.UtcNow.AddMonths(2);

    // 1 Jazz (Cluj, publicat), 2 Rock (Iași, publicat, epuizat), 3 Teatru (Cluj, publicat, alt loc),
    // 4 Ciornă (Cluj, nepublicat), 5 Trecut (Cluj, publicat dar a avut loc deja)
    private static (AppDbContext Db, EventsController Controller) Setup()
    {
        var db = TestHelpers.CreateDb();
        db.Users.Add(new User { Id = 1, Email = "org@example.com", Role = UserRole.Organizer });
        db.Events.AddRange(
            new Event { Id = 1, Title = "Jazz Night", Venue = "Form Space", City = "Cluj-Napoca", StartsAt = Future.AddDays(1), IsPublished = true, OrganizerId = 1 },
            new Event { Id = 2, Title = "Rock Live", Venue = "Sala Polivalentă", City = "Iași", StartsAt = Future.AddDays(2), IsPublished = true, OrganizerId = 1 },
            new Event { Id = 3, Title = "Hamlet", Venue = "Teatrul Național", City = "Cluj-Napoca", StartsAt = Future, IsPublished = true, OrganizerId = 1 },
            new Event { Id = 4, Title = "Ciornă", Venue = "Form Space", City = "Cluj-Napoca", StartsAt = Future, IsPublished = false, OrganizerId = 1 },
            new Event { Id = 5, Title = "Trecut", Venue = "Form Space", City = "Cluj-Napoca", StartsAt = DateTime.UtcNow.AddDays(-3), IsPublished = true, OrganizerId = 1 });
        db.TicketTypes.AddRange(
            new TicketType { Id = 10, EventId = 1, Name = "Standard", Price = 90, Capacity = 100, Sold = 10 },
            new TicketType { Id = 11, EventId = 1, Name = "VIP", Price = 220, Capacity = 20 },
            new TicketType { Id = 20, EventId = 2, Name = "Intrare", Price = 60, Capacity = 50, Sold = 50 },
            new TicketType { Id = 30, EventId = 3, Name = "Stal", Price = 0, Capacity = 300 });
        db.SaveChanges();
        return (db, new EventsController(db));
    }

    private static List<PublicEventCardDto> Cards(ActionResult<List<PublicEventCardDto>> result) =>
        Assert.IsType<List<PublicEventCardDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task GetAll_ArataDoarEvenimentePublicateSiViitoare_OrdonateDupaData()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var events = Cards(await controller.GetAll(null, null, null));

        Assert.Equal(new[] { 3, 1, 2 }, events.Select(e => e.Id));
    }

    [Fact]
    public async Task GetAll_CalculeazaPretulMinimSiEpuizarea()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var events = Cards(await controller.GetAll(null, null, null));

        var jazz = events.Single(e => e.Id == 1);
        Assert.Equal(90, jazz.MinPrice);
        Assert.False(jazz.SoldOut);
        Assert.True(events.Single(e => e.Id == 2).SoldOut);
        Assert.Equal(0, events.Single(e => e.Id == 3).MinPrice);
    }

    [Fact]
    public async Task GetAll_FiltruDupaUnOras()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var events = Cards(await controller.GetAll(new[] { "Iași" }, null, null));

        Assert.Equal(2, Assert.Single(events).Id);
    }

    [Fact]
    public async Task GetAll_FiltruDupaMaiMulteOrase()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var events = Cards(await controller.GetAll(new[] { "Iași", "Cluj-Napoca" }, null, null));

        Assert.Equal(3, events.Count);
    }

    [Fact]
    public async Task GetAll_FiltruDupaLocatie()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var events = Cards(await controller.GetAll(null, new[] { "Teatrul Național" }, null));

        Assert.Equal(3, Assert.Single(events).Id);
    }

    [Theory]
    [InlineData("JAZZ", 1)]       // titlu, fără să conteze majusculele
    [InlineData("polivalent", 2)] // locație
    [InlineData("iași", 2)]       // oraș
    public async Task GetAll_CautareInTitluLocatieSiOras(string search, int expectedId)
    {
        var (db, controller) = Setup();
        using var _ = db;

        var events = Cards(await controller.GetAll(null, null, search));

        Assert.Equal(expectedId, Assert.Single(events).Id);
    }

    [Fact]
    public async Task GetAll_CautareFaraRezultate_ListaGoala()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var events = Cards(await controller.GetAll(null, null, "nu-exista"));

        Assert.Empty(events);
    }

    [Fact]
    public async Task GetFilters_NumaraDoarEvenimenteleActive()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.GetFilters();

        var filters = Assert.IsType<EventFiltersDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        // Cluj are 4 evenimente în total, dar doar 2 active (fără ciornă și fără cel trecut)
        Assert.Equal(2, filters.Cities.Single(c => c.Name == "Cluj-Napoca").Count);
        Assert.Equal(1, filters.Cities.Single(c => c.Name == "Iași").Count);
        Assert.Equal(1, filters.Venues.Single(v => v.Name == "Form Space").Count);
    }

    [Fact]
    public async Task Get_EvenimentPublicat_ReturneazaBileteleCuLocurileRamase()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Get(1);

        var dto = Assert.IsType<PublicEventDetailDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new[] { "Standard", "VIP" }, dto.TicketTypes.Select(t => t.Name));
        Assert.Equal(90, dto.TicketTypes[0].Available);
    }

    [Fact]
    public async Task Get_Ciorna_Returneaza404()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.Get(4);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
