using System.Reflection;
using EventPass.Server.Controllers;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Tests;

// Panoul organizatorului (#20). Evenimentul 10 e al organizatorului 3:
//   tipul 100 „Standard” 50 lei, 10 locuri | tipul 101 „VIP” 200 lei, 5 locuri
//   comanda 1 PLĂTITĂ (Ana):     2 × Standard + 1 × VIP, un bilet Standard scanat la intrare
//   comanda 2 REZERVATĂ (Ion):   1 × Standard (neplătită încă)
//   comanda 3 RAMBURSATĂ (Ion):  1 × VIP, 200 lei, bilet anulat
//   comanda 4 ANULATĂ (Ion):     1 × Standard, fără bilete (nu contează nicăieri)
// Evenimentul 20 e al organizatorului 4.
public class OrganizerDashboardTests : IDisposable
{
    private static readonly Guid UsedToken = Guid.Parse("aa000001-0000-4000-8000-000000000001");
    private static readonly Guid StandardToken = Guid.Parse("bb000002-0000-4000-8000-000000000002");
    private static readonly Guid VipToken = Guid.Parse("cc000003-0000-4000-8000-000000000003");
    private static readonly Guid RefundedToken = Guid.Parse("dd000004-0000-4000-8000-000000000004");

    private readonly SqliteTestDatabase _database = new();

    public OrganizerDashboardTests()
    {
        using var db = _database.CreateContext();
        var now = DateTime.UtcNow;
        db.Users.AddRange(
            new User { Id = 1, Email = "ana@example.com", FullName = "Ana Pop", Role = UserRole.Client },
            new User { Id = 2, Email = "ion@example.com", FullName = "Ion Ionescu", Role = UserRole.Client },
            new User { Id = 3, Email = "org@example.com", FullName = "Org", Role = UserRole.Organizer },
            new User { Id = 4, Email = "org2@example.com", FullName = "Org 2", Role = UserRole.Organizer });
        db.Events.AddRange(
            new Event { Id = 10, Title = "Concert", Venue = "BT Arena", City = "Cluj-Napoca", StartsAt = now.AddDays(10), IsPublished = true, OrganizerId = 3 },
            new Event { Id = 20, Title = "Teatru", Venue = "Sala", City = "Iași", StartsAt = now.AddDays(5), IsPublished = false, OrganizerId = 4 });
        db.TicketTypes.AddRange(
            // Sold = locuri ocupate: Standard 2 (plătite) + 1 (rezervat); VIP 1 (plătit); cel rambursat s-a eliberat
            new TicketType { Id = 100, EventId = 10, Name = "Standard", Price = 50, Capacity = 10, Sold = 3 },
            new TicketType { Id = 101, EventId = 10, Name = "VIP", Price = 200, Capacity = 5, Sold = 1 },
            new TicketType { Id = 200, EventId = 20, Name = "Balcon", Price = 30, Capacity = 50 });
        db.Orders.AddRange(
            new Order
            {
                Id = 1, UserId = 1, Status = OrderStatus.Paid, TotalAmount = 300, PaidAt = now,
                Items = { new OrderItem { TicketTypeId = 100, Quantity = 2, UnitPrice = 50 }, new OrderItem { TicketTypeId = 101, Quantity = 1, UnitPrice = 200 } },
                Tickets =
                {
                    new Ticket { TicketTypeId = 100, Token = UsedToken, IsUsed = true, UsedAt = now },
                    new Ticket { TicketTypeId = 100, Token = StandardToken },
                    new Ticket { TicketTypeId = 101, Token = VipToken },
                },
            },
            new Order
            {
                Id = 2, UserId = 2, Status = OrderStatus.Pending, TotalAmount = 50, ExpiresAt = now.AddMinutes(10),
                Items = { new OrderItem { TicketTypeId = 100, Quantity = 1, UnitPrice = 50 } },
            },
            new Order
            {
                Id = 3, UserId = 2, Status = OrderStatus.Refunded, TotalAmount = 200, PaidAt = now, RefundedAt = now,
                Items = { new OrderItem { TicketTypeId = 101, Quantity = 1, UnitPrice = 200 } },
                Tickets = { new Ticket { TicketTypeId = 101, Token = RefundedToken, VoidedAt = now } },
            },
            new Order
            {
                Id = 4, UserId = 2, Status = OrderStatus.Cancelled, TotalAmount = 50,
                Items = { new OrderItem { TicketTypeId = 100, Quantity = 1, UnitPrice = 50 } },
            });
        db.SaveChanges();
    }

    public void Dispose() => _database.Dispose();

    private OrganizerDashboardController As(int organizerId)
    {
        var controller = new OrganizerDashboardController(_database.CreateContext());
        TestHelpers.LoginAs(controller, organizerId, "Organizer");
        return controller;
    }

    private static T Ok<T>(ActionResult<T> result) => Assert.IsType<T>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private async Task<AttendeesPageDto> Attendees(string? q = null, string? filter = null, int page = 1, int pageSize = 50) =>
        Ok(await As(3).GetAttendees(10, q, filter, page, pageSize));

    // ---------- Vânzări ----------

    [Fact]
    public async Task Vanzari_TotaluriCorecte_NumaiDinComenzilePlatite()
    {
        var sales = Ok(await As(3).GetSales(10, default));

        var t = sales.Totals;
        Assert.Equal(15, t.Capacity);
        Assert.Equal(3, t.TicketsSold);      // 2 Standard + 1 VIP (comanda 1); rambursatul și anulata nu se numără
        Assert.Equal(1, t.Reserved);         // comanda 2, neplătită
        Assert.Equal(11, t.Available);       // (10 − 3) + (5 − 1)
        Assert.Equal(300m, t.Revenue);       // doar comanda plătită
        Assert.Equal(1, t.Validated);
        Assert.Equal(1, t.RefundedOrders);
        Assert.Equal(200m, t.RefundedAmount);
    }

    [Fact]
    public async Task Vanzari_PeFiecareCategorie_InOrdineaPretului()
    {
        var types = Ok(await As(3).GetSales(10, default)).TicketTypes;

        Assert.Equal(new[] { "Standard", "VIP" }, types.Select(x => x.Name));
        var standard = types[0];
        Assert.Equal((2, 1, 7, 100m, 1), (standard.TicketsSold, standard.Reserved, standard.Available, standard.Revenue, standard.Validated));
        var vip = types[1];
        Assert.Equal((1, 0, 4, 200m, 0), (vip.TicketsSold, vip.Reserved, vip.Available, vip.Revenue, vip.Validated));
    }

    [Fact]
    public async Task Vanzari_EvenimentulAltuiOrganizator_Returneaza404()
    {
        Assert.IsType<NotFoundResult>((await As(4).GetSales(10, default)).Result);
        Assert.IsType<NotFoundResult>((await As(3).GetSales(999, default)).Result);
    }

    [Fact]
    public async Task Panou_ArataDoarEvenimenteleMele_CuCifreleLor()
    {
        var mine = Ok(await As(3).GetDashboard(default));
        var other = Ok(await As(4).GetDashboard(default));

        var concert = Assert.Single(mine);
        Assert.Equal("Concert", concert.Title);
        Assert.Equal((3, 300m, 1), (concert.Totals.TicketsSold, concert.Totals.Revenue, concert.Totals.Validated));

        var teatru = Assert.Single(other);
        Assert.Equal("Teatru", teatru.Title);
        Assert.Equal((50, 0, 0m, 50), (teatru.Totals.Capacity, teatru.Totals.TicketsSold, teatru.Totals.Revenue, teatru.Totals.Available));
    }

    [Fact]
    public async Task Panou_IntaiEvenimenteleCareUrmeaza_ApoiCeleTrecute()
    {
        using (var db = _database.CreateContext())
        {
            var now = DateTime.UtcNow;
            db.Events.AddRange(
                new Event { Id = 11, Title = "Peste o lună", Venue = "V", City = "C", StartsAt = now.AddDays(30), OrganizerId = 3 },
                new Event { Id = 12, Title = "Ieri", Venue = "V", City = "C", StartsAt = now.AddDays(-1), OrganizerId = 3 },
                new Event { Id = 13, Title = "Acum un an", Venue = "V", City = "C", StartsAt = now.AddYears(-1), OrganizerId = 3 },
                new Event { Id = 14, Title = "Mâine", Venue = "V", City = "C", StartsAt = now.AddDays(1), OrganizerId = 3 });
            db.SaveChanges();
        }

        var titles = Ok(await As(3).GetDashboard(default)).Select(e => e.Title);

        // Concertul e peste 10 zile
        Assert.Equal(new[] { "Mâine", "Concert", "Peste o lună", "Ieri", "Acum un an" }, titles);
    }

    [Fact]
    public async Task Panou_OrganizatorFaraEvenimente_ListaGoala()
    {
        var controller = new OrganizerDashboardController(_database.CreateContext());
        TestHelpers.LoginAs(controller, 77, "Organizer");

        Assert.Empty(Ok(await controller.GetDashboard(default)));
    }

    [Fact]
    public void Endpointurile_SuntDoarPentruOrganizatori()
    {
        var attribute = typeof(OrganizerDashboardController).GetCustomAttribute<AuthorizeAttribute>()!;

        Assert.Equal("Organizer", attribute.Roles);
    }

    // ---------- Participanți ----------

    [Fact]
    public async Task Participanti_TotiCeiCuBilete_InclusivCelRambursat_ComenzileNoiPrimele()
    {
        var page = await Attendees();

        Assert.Equal(4, page.Total);
        Assert.Equal(new[] { 3, 1, 1, 1 }, page.Items.Select(a => a.OrderId));   // comanda 3 e mai nouă
        var refunded = page.Items[0];
        Assert.Equal(("Ion Ionescu", "ion@example.com", "Refunded", true), (refunded.HolderName, refunded.HolderEmail, refunded.OrderStatus, refunded.IsVoided));
        Assert.Equal("DD000004", refunded.ShortCode);
        // Comanda rezervată (neplătită) și cea anulată nu au bilete
        Assert.DoesNotContain(page.Items, a => a.OrderId is 2 or 4);
    }

    [Theory]
    [InlineData("validated", 1)]
    [InlineData("notValidated", 2)]
    [InlineData("refunded", 1)]
    [InlineData("all", 4)]
    [InlineData("ceva-necunoscut", 4)]
    public async Task Participanti_Filtre(string filter, int expected)
    {
        var page = await Attendees(filter: filter);

        Assert.Equal(expected, page.Total);
        Assert.Equal(expected, page.Items.Count);
    }

    [Fact]
    public async Task Participanti_FiltrulValidate_ArataOraScanarii()
    {
        var a = Assert.Single((await Attendees(filter: "validated")).Items);

        Assert.True(a.IsUsed);
        Assert.NotNull(a.UsedAt);
        Assert.Equal("AA000001", a.ShortCode);
    }

    [Theory]
    [InlineData("ana", 3)]              // nume, fără să conteze literele mari
    [InlineData("ANA POP", 3)]
    [InlineData("ion@example", 1)]      // email
    [InlineData("bb0000", 1)]           // începutul codului scurt
    [InlineData("BB000002", 1)]
    [InlineData("  cc000003  ", 1)]     // spațiile se ignoră
    [InlineData("nimeni", 0)]
    public async Task Participanti_Cautare(string q, int expected)
    {
        Assert.Equal(expected, (await Attendees(q: q)).Total);
    }

    [Fact]
    public async Task Participanti_CautareSiFiltruImpreuna()
    {
        Assert.Equal(1, (await Attendees(q: "ana", filter: "validated")).Total);
        Assert.Equal(0, (await Attendees(q: "ion", filter: "validated")).Total);
    }

    [Fact]
    public async Task Participanti_PePagini()
    {
        var first = await Attendees(page: 1, pageSize: 3);
        var second = await Attendees(page: 2, pageSize: 3);

        Assert.Equal((4, 3), (first.Total, first.Items.Count));
        Assert.Equal((4, 1), (second.Total, second.Items.Count));
        Assert.Empty(first.Items.Select(a => a.TicketId).Intersect(second.Items.Select(a => a.TicketId)));
    }

    [Fact]
    public async Task Participanti_MarimeaPaginiiELimitata()
    {
        var huge = await Attendees(pageSize: 100000);
        var zero = await Attendees(page: 0, pageSize: 0);

        Assert.Equal(OrganizerDashboardController.MaxPageSize, huge.PageSize);
        Assert.Equal((1, 1), (zero.Page, zero.PageSize));
    }

    [Fact]
    public async Task Participanti_PotRambursaDoarComenzilePlatiteFaraBileteFolosite()
    {
        // Comanda 1 are un bilet scanat: nu se mai poate rambursa
        Assert.All((await Attendees()).Items, a => Assert.False(a.CanRefund));

        using (var db = _database.CreateContext())
            db.Tickets.ExecuteUpdate(s => s.SetProperty(t => t.IsUsed, false).SetProperty(t => t.UsedAt, (DateTime?)null));

        var items = (await Attendees()).Items;
        Assert.All(items.Where(a => a.OrderId == 1), a => Assert.True(a.CanRefund));
        Assert.All(items.Where(a => a.OrderId == 3), a => Assert.False(a.CanRefund));   // deja rambursată
    }

    [Fact]
    public async Task Participanti_EvenimentulAltuiOrganizator_Returneaza404()
    {
        Assert.IsType<NotFoundResult>((await As(4).GetAttendees(10, null, null)).Result);
    }
}
