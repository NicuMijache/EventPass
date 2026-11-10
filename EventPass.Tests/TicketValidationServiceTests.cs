using System.Reflection;
using EventPass.Server.Controllers;
using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using EventPass.Server.Services;
using EventPass.Server.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static EventPass.Tests.PaymentTestData;

namespace EventPass.Tests;

public class TicketValidationServiceTests : IDisposable
{
    private const int StaffId = 5;
    private const int OrganizerId = 3;        // organizatorul evenimentului 10 (din PaymentTestData)
    private const int OtherOrganizerId = 4;   // are evenimentul 20

    private readonly SqliteTestDatabase _database = new();

    // Comanda 1 (Ana, evenimentul 10) e plătită: 3 bilete. Comanda 2 (Ion, evenimentul 20, alt organizator): 1 bilet.
    public TicketValidationServiceTests()
    {
        Seed(_database);
        using (var db = _database.CreateContext())
        {
            db.Users.AddRange(
                new User { Id = 2, Email = "ion@example.com", FullName = "Ion Pop", Role = UserRole.Client },
                new User { Id = OtherOrganizerId, Email = "org2@example.com", FullName = "Org 2", Role = UserRole.Organizer },
                new User { Id = StaffId, Email = "staff@example.com", FullName = "Staff", Role = UserRole.Staff });
            db.Events.Add(new Event
            {
                Id = 20, Title = "Teatru", Venue = "Sala Mare", City = "Iași",
                StartsAt = DateTime.UtcNow.AddDays(7), IsPublished = true, OrganizerId = OtherOrganizerId,
            });
            db.TicketTypes.Add(new TicketType { Id = 200, EventId = 20, Name = "Balcon", Price = 30, Capacity = 50, Sold = 1 });
            db.Orders.Add(new Order { Id = 2, UserId = 2, Status = OrderStatus.Paid, TotalAmount = 30, PaidAt = DateTime.UtcNow });
            db.Tickets.Add(new Ticket { OrderId = 2, TicketTypeId = 200, Token = Guid.Parse("0a1b2c3d-1111-4222-8333-444455556666") });
            db.SaveChanges();
        }

        using (var db = _database.CreateContext())
        {
            new OrderPaymentService(db, NullLogger<OrderPaymentService>.Instance)
                .ConfirmPaymentAsync(OrderId, SessionId, AmountMinor, "ron").GetAwaiter().GetResult();
        }
    }

    public void Dispose() => _database.Dispose();

    private async Task<TicketValidationDto> Scan(string? code, int userId = StaffId, int? organizerId = null)
    {
        await using var db = _database.CreateContext();
        return await new TicketValidationService(db, NullLogger<TicketValidationService>.Instance)
            .ValidateAsync(code, userId, organizerId);
    }

    private Ticket AnaTicket()
    {
        using var db = _database.CreateContext();
        return db.Tickets.AsNoTracking().Where(t => t.OrderId == OrderId).OrderBy(t => t.Id).First();
    }

    private Ticket Reload(int id)
    {
        using var db = _database.CreateContext();
        return db.Tickets.AsNoTracking().Single(t => t.Id == id);
    }

    // ---------- Cazul principal ----------

    [Fact]
    public async Task PrimaScanare_BiletValid_IlMarcheazaFolositSiArataDetaliile()
    {
        var ticket = AnaTicket();
        var before = DateTime.UtcNow;

        var result = await Scan(TicketCode.QrText(ticket.Token));

        Assert.Equal("Valid", result.Result);
        Assert.Equal(TicketCode.Short(ticket.Token), result.ShortCode);
        Assert.Equal("Concert", result.EventTitle);
        Assert.Equal("Ana", result.HolderName);
        Assert.Contains(result.TicketTypeName, new[] { "Standard", "VIP" });

        var saved = Reload(ticket.Id);
        Assert.True(saved.IsUsed);
        Assert.NotNull(saved.UsedAt);
        Assert.InRange(saved.UsedAt!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(saved.UsedAt, result.UsedAt);
        // Fără a 7-a zecimală (PostgreSQL ține doar microsecunde): altfel ora de la al doilea scan ar diferi
        Assert.Equal(0, result.UsedAt!.Value.Ticks % 10);
    }

    [Fact]
    public async Task AlDoileaScan_ReturneazaDejaUtilizat_CuOraPrimeiScanari()
    {
        var code = TicketCode.QrText(AnaTicket().Token);

        var first = await Scan(code);
        var second = await Scan(code);
        var third = await Scan(code);

        Assert.Equal("Valid", first.Result);
        Assert.Equal("AlreadyUsed", second.Result);
        Assert.Equal("Bilet deja utilizat.", second.Message);
        Assert.Equal(first.UsedAt, second.UsedAt);   // ora rămâne cea de la intrare, nu se mută la fiecare scanare
        Assert.Equal("AlreadyUsed", third.Result);
        Assert.Equal(first.UsedAt, third.UsedAt);
    }

    [Fact]
    public async Task ScanareaUnuiBilet_NuAtingeCelelalteBileteAleComenzii()
    {
        var ticket = AnaTicket();

        await Scan(TicketCode.QrText(ticket.Token));

        using var db = _database.CreateContext();
        var others = db.Tickets.AsNoTracking().Where(t => t.OrderId == OrderId && t.Id != ticket.Id).ToList();
        Assert.Equal(2, others.Count);
        Assert.All(others, t => { Assert.False(t.IsUsed); Assert.Null(t.UsedAt); });
    }

    // ---------- Formele codului ----------

    [Theory]
    [InlineData("D")]   // cu cratime, majuscule (ce e în QR)
    [InlineData("d")]   // litere mici
    [InlineData("N")]   // fără cratime
    [InlineData(" spatii ")]
    public async Task TokenulIntreg_EsteAcceptatInFormeleObisnuite(string form)
    {
        var token = AnaTicket().Token;
        var code = form switch
        {
            "D" => token.ToString("D").ToUpperInvariant(),
            "d" => token.ToString("D"),
            "N" => token.ToString("N"),
            _ => $"  {token.ToString("D")[..18]} {token.ToString("D")[18..]}\n",
        };

        var result = await Scan(code);

        Assert.Equal("Valid", result.Result);
    }

    [Theory]
    [InlineData("upper")]
    [InlineData("lower")]
    [InlineData("spaced")]
    public async Task CodulScurt_GasesteBiletul(string form)
    {
        var ticket = AnaTicket();
        var shortCode = TicketCode.Short(ticket.Token);
        var code = form switch
        {
            "upper" => shortCode,
            "lower" => shortCode.ToLowerInvariant(),
            _ => $"{shortCode[..4]} {shortCode[4..]}",
        };

        var result = await Scan(code);

        Assert.Equal("Valid", result.Result);
        Assert.True(Reload(ticket.Id).IsUsed);
        Assert.Equal("AlreadyUsed", (await Scan(shortCode)).Result);
    }

    [Fact]
    public async Task CodulScurt_PotrivitLaDouaBilete_NuMarcheazaNimic()
    {
        // Două bilete ale căror tokenuri încep cu aceleași 8 caractere (se poate, rar: 1 la ~4 miliarde)
        using (var db = _database.CreateContext())
        {
            db.Tickets.AddRange(
                new Ticket { OrderId = 2, TicketTypeId = 200, Token = Guid.Parse("abcdef12-0000-4000-8000-000000000001") },
                new Ticket { OrderId = 2, TicketTypeId = 200, Token = Guid.Parse("abcdef12-0000-4000-8000-000000000002") });
            db.SaveChanges();
        }

        var result = await Scan("ABCDEF12");

        Assert.Equal("Ambiguous", result.Result);
        using var check = _database.CreateContext();
        Assert.False(check.Tickets.Any(t => t.IsUsed));
        // Cu QR-ul (tokenul întreg) fiecare se validează normal
        Assert.Equal("Valid", (await Scan("abcdef12-0000-4000-8000-000000000002")).Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.com")]
    [InlineData("FA46C34")]                                   // 7 caractere
    [InlineData("FA46C34BX")]                                 // 9 caractere
    [InlineData("GGGGGGGG")]                                  // nu sunt cifre hexazecimale
    [InlineData("{0a1b2c3d-1111-4222-8333-444455556666}")]    // altă formă de GUID, pe care noi n-o scriem nicăieri
    public async Task TextCareNuECodDeBilet_EsteRespinsFaraCautare(string? code)
    {
        var result = await Scan(code);

        Assert.Equal("InvalidCode", result.Result);
        Assert.Null(result.ShortCode);
    }

    [Fact]
    public async Task TextFoarteLung_EsteRespins()
    {
        var result = await Scan(new string('A', 5000));

        Assert.Equal("InvalidCode", result.Result);
    }

    [Fact]
    public async Task TokenInexistent_NuEsteGasit()
    {
        var result = await Scan(Guid.NewGuid().ToString());

        Assert.Equal("NotFound", result.Result);
        Assert.Null(result.EventTitle);   // nu dezvăluim nimic
    }

    [Fact]
    public async Task CodScurtInexistent_NuEsteGasit()
    {
        var result = await Scan("00000000");

        Assert.Equal("NotFound", result.Result);
    }

    // ---------- Starea comenzii ----------

    [Theory]
    [InlineData(OrderStatus.Refunded)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task BiletulUneiComenziNeplatite_NuEsteValid_SiNuSeMarcheaza(OrderStatus status)
    {
        var ticket = AnaTicket();
        using (var db = _database.CreateContext())
            await db.Orders.Where(o => o.Id == OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, status));

        var result = await Scan(TicketCode.QrText(ticket.Token));

        Assert.Equal("NotPaid", result.Result);
        Assert.False(Reload(ticket.Id).IsUsed);
    }

    [Fact]
    public async Task BiletFolositApoiRambursat_NuMaiEsteValid()
    {
        // Persoana a intrat, apoi comanda a fost rambursată: biletul nu mai e bun (comanda contează prima)
        var code = TicketCode.QrText(AnaTicket().Token);
        await Scan(code);
        using (var db = _database.CreateContext())
            await db.Orders.Where(o => o.Id == OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Refunded));

        var result = await Scan(code);

        Assert.Equal("NotPaid", result.Result);
    }

    // ---------- Cine scanează ----------

    [Fact]
    public async Task Organizatorul_IsiValideazaBileteleEvenimentuluiSau()
    {
        var result = await Scan(TicketCode.QrText(AnaTicket().Token), OrganizerId, organizerId: OrganizerId);

        Assert.Equal("Valid", result.Result);
    }

    [Fact]
    public async Task Organizatorul_NuPoateValidaBileteleAltuiOrganizator()
    {
        const string ionToken = "0a1b2c3d-1111-4222-8333-444455556666";

        var result = await Scan(ionToken, OrganizerId, organizerId: OrganizerId);

        Assert.Equal("OtherOrganizer", result.Result);
        Assert.Null(result.HolderName);   // nu vede nici numele clientului altui organizator
        using var db = _database.CreateContext();
        Assert.False(db.Tickets.Single(t => t.OrderId == 2).IsUsed);   // și biletul rămâne bun pentru intrarea corectă
        Assert.Equal("Valid", (await Scan(ionToken, OtherOrganizerId, organizerId: OtherOrganizerId)).Result);
    }

    [Fact]
    public async Task Staff_PoateValidaBileteLaOriceEveniment()
    {
        Assert.Equal("Valid", (await Scan(TicketCode.QrText(AnaTicket().Token))).Result);
        Assert.Equal("Valid", (await Scan("0A1B2C3D")).Result);
    }

    // ---------- Concurență: două scanări în același timp ----------

    [Fact]
    public async Task DouaScanariSimultane_DoarUnaEsteValida()
    {
        // Fiecare „angajat” are propria conexiune la aceeași bază de date (fișier temporar),
        // ca cererile să fie cu adevărat separate, ca pe server
        var file = Path.Combine(Path.GetTempPath(), $"eventpass-concurenta-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={file};Pooling=False;Default Timeout=30").Options;
            Guid token;
            await using (var setup = new AppDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                setup.Users.Add(new User { Id = 1, Email = "a@example.com", FullName = "A", Role = UserRole.Client });
                setup.Users.Add(new User { Id = 3, Email = "o@example.com", FullName = "O", Role = UserRole.Organizer });
                setup.Events.Add(new Event { Id = 10, Title = "X", Venue = "V", City = "C", StartsAt = DateTime.UtcNow, OrganizerId = 3 });
                setup.TicketTypes.Add(new TicketType { Id = 100, EventId = 10, Name = "S", Price = 1, Capacity = 1, Sold = 1 });
                setup.Orders.Add(new Order { Id = 1, UserId = 1, Status = OrderStatus.Paid, TotalAmount = 1 });
                var ticket = new Ticket { OrderId = 1, TicketTypeId = 100 };
                setup.Tickets.Add(ticket);
                await setup.SaveChangesAsync();
                token = ticket.Token;
            }

            var scans = Enumerable.Range(0, 10).Select(async _ =>
            {
                await using var db = new AppDbContext(options);
                return await new TicketValidationService(db, NullLogger<TicketValidationService>.Instance)
                    .ValidateAsync(token.ToString(), StaffId, null);
            });
            var results = await Task.WhenAll(scans);

            Assert.Equal(1, results.Count(r => r.Result == "Valid"));
            Assert.Equal(9, results.Count(r => r.Result == "AlreadyUsed"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---------- Controller ----------

    [Fact]
    public void Endpointul_EstePermisDoarPersonalului()
    {
        var roles = typeof(TicketsController).GetCustomAttribute<AuthorizeAttribute>()!.Roles!.Split(',');

        Assert.Equal(new[] { "Admin", "Organizer", "Staff" }, roles.Order());
        Assert.DoesNotContain("Client", roles);
    }

    [Fact]
    public void Endpointul_EstePost()
    {
        var method = typeof(TicketsController).GetMethod(nameof(TicketsController.Validate))!;

        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
    }

    [Theory]
    [InlineData("Organizer", OrganizerId, "OtherOrganizer")]   // ca organizator, biletul lui Ion nu e al lui
    [InlineData("Staff", StaffId, "Valid")]
    [InlineData("Admin", 6, "Valid")]
    public async Task Controllerul_AplicaRestrictiaDeOrganizatorDoarPentruRolulOrganizer(string role, int userId, string expected)
    {
        await using var db = _database.CreateContext();
        var controller = new TicketsController(new TicketValidationService(db, NullLogger<TicketValidationService>.Instance));
        TestHelpers.LoginAs(controller, userId, role);

        var response = await controller.Validate(new ValidateTicketRequest("0A1B2C3D-1111-4222-8333-444455556666"), default);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Equal(expected, Assert.IsType<TicketValidationDto>(ok.Value).Result);
    }
}
