using EventPass.Server.Controllers;
using EventPass.Server.Data;
using EventPass.Server.Models;
using EventPass.Server.Services;
using EventPass.Server.Services.Payments;
using EventPass.Server.Services.Qr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static EventPass.Tests.PaymentTestData;

namespace EventPass.Tests;

public class OrderTicketsControllerTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    // Clientul 1 are comanda 1, plătită (3 bilete). Clientul 2 e altcineva.
    public OrderTicketsControllerTests()
    {
        Seed(_database);
        using (var db = _database.CreateContext())
        {
            db.Users.Add(new User { Id = 2, Email = "ion@example.com", FullName = "Ion", Role = UserRole.Client });
            db.SaveChanges();
        }
        PayOrder();
    }

    public void Dispose() => _database.Dispose();

    private void PayOrder()
    {
        using var db = _database.CreateContext();
        var service = new OrderPaymentService(db, NullLogger<OrderPaymentService>.Instance);
        service.ConfirmPaymentAsync(OrderId, SessionId, AmountMinor, "ron").GetAwaiter().GetResult();
    }

    private (AppDbContext Db, OrderTicketsController Controller) As(int userId)
    {
        var db = _database.CreateContext();
        var controller = new OrderTicketsController(db, new QrCodeGenerator());
        TestHelpers.LoginAs(controller, userId, "Client");
        controller.ControllerContext.HttpContext = new DefaultHttpContext { User = controller.ControllerContext.HttpContext.User };
        return (db, controller);
    }

    private Ticket FirstTicket()
    {
        using var db = _database.CreateContext();
        return db.Tickets.AsNoTracking().OrderBy(t => t.Id).First();
    }

    [Fact]
    public async Task GetQr_ProprietaruluComenziiPlatite_PrimesteUnPngCuTokenulBiletului()
    {
        var ticket = FirstTicket();
        var (db, controller) = As(1);
        using var _ = db;

        var result = await controller.GetQr(OrderId, ticket.Id);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/png", file.ContentType);
        // Cap-coadă: scanăm imaginea primită și ar trebui să dea exact tokenul din baza de date
        Assert.Equal(TicketCode.QrText(ticket.Token), QrCodeGeneratorTests.Decode(file.FileContents));
        Assert.Equal(ticket.Token.ToString("D").ToUpperInvariant(), QrCodeGeneratorTests.Decode(file.FileContents));
    }

    [Fact]
    public void QrText_SeParseazaInapoiCuGuidTryParse_IndiferentDeMajuscule()
    {
        var token = Guid.NewGuid();

        var text = TicketCode.QrText(token);

        Assert.Equal(text, text.ToUpperInvariant());
        Assert.True(Guid.TryParse(text, out var parsed));
        Assert.Equal(token, parsed);   // la scanare (#17) serverul regăsește exact același token
    }

    [Fact]
    public async Task GetQr_FiecareBiletAreUnQrDiferit()
    {
        List<Ticket> tickets;
        using (var setup = _database.CreateContext())
            tickets = setup.Tickets.AsNoTracking().OrderBy(t => t.Id).ToList();
        var (db, controller) = As(1);
        using var _ = db;

        var decoded = new List<string?>();
        foreach (var ticket in tickets)
        {
            var file = Assert.IsType<FileContentResult>(await controller.GetQr(OrderId, ticket.Id));
            decoded.Add(QrCodeGeneratorTests.Decode(file.FileContents));
        }

        Assert.Equal(3, decoded.Distinct().Count());
    }

    [Fact]
    public async Task GetQr_RaspunsulNuSePastreazaInCacheulBrowserului()
    {
        var ticket = FirstTicket();
        var (db, controller) = As(1);
        using var _ = db;

        await controller.GetQr(OrderId, ticket.Id);

        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task GetQr_ComandaAltcuiva_Returneaza404()
    {
        var ticket = FirstTicket();
        var (db, controller) = As(2);   // Ion încearcă biletul Anei
        using var _ = db;

        var result = await controller.GetQr(OrderId, ticket.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetQr_BiletInexistent_Returneaza404()
    {
        var (db, controller) = As(1);
        using var _ = db;

        var result = await controller.GetQr(OrderId, ticketId: 9999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetQr_BiletulUneiAlteComenzi_Returneaza404()
    {
        // Clientul 2 are propria comandă plătită, cu un bilet. Ana nu poate cere acel bilet prin comanda ei.
        int foreignTicketId;
        using (var setup = _database.CreateContext())
        {
            var order = new Order { Id = 2, UserId = 2, Status = OrderStatus.Paid, TotalAmount = 50, StripeSessionId = "cs_test_2" };
            setup.Orders.Add(order);
            setup.Tickets.Add(new Ticket { OrderId = 2, TicketTypeId = 100, Token = Guid.NewGuid() });
            setup.SaveChanges();
            foreignTicketId = setup.Tickets.Single(t => t.OrderId == 2).Id;
        }
        var (db, controller) = As(1);
        using var _ = db;

        var result = await controller.GetQr(OrderId, foreignTicketId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetQr_ComandaRambursata_Returneaza409SiNuMaiArataCodul()
    {
        var ticket = FirstTicket();
        using (var setup = _database.CreateContext())
        {
            await setup.Orders.Where(o => o.Id == OrderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Refunded));
        }
        var (db, controller) = As(1);
        using var _ = db;

        var result = await controller.GetQr(OrderId, ticket.Id);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task GetQr_MarimeaCeruta_EsteLimitata()
    {
        var ticket = FirstTicket();
        var (db, controller) = As(1);
        using var _ = db;

        // Cineva cere o imagine uriașă ca să încarce serverul: se limitează la maximul permis
        var huge = Assert.IsType<FileContentResult>(await controller.GetQr(OrderId, ticket.Id, size: 100000));
        var max = Assert.IsType<FileContentResult>(await controller.GetQr(OrderId, ticket.Id, size: QrCodeGenerator.MaxPixelsPerModule));

        Assert.Equal(max.FileContents, huge.FileContents);
    }
}
