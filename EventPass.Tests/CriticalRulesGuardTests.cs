using System.Reflection;
using System.Text.RegularExpressions;
using EventPass.Server.Controllers;
using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using EventPass.Server.Services;
using EventPass.Server.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventPass.Tests;

// „Gărzi” pentru regulile critice (#22). Celelalte teste verifică ce face codul de azi;
// acestea pică dacă cineva, mai târziu, ocolește o regulă (ex. emite bilete din alt loc decât confirmarea plății).
//
// Regulile:
//   BiletUnic       – un bilet intră o singură dată (vezi și TicketValidationServiceTests)
//   Stoc            – nu se vând mai multe locuri decât capacitatea (vezi și StockRaceTests)
//   EmitereWebhook  – biletele apar doar după ce Stripe confirmă plata (vezi și StripeWebhookControllerTests)
// Se pot rula separat: dotnet test --filter Regula=Stoc
public class CriticalRulesGuardTests
{
    // ---------- Codul sursă: unde se face fiecare lucru periculos ----------

    // Folderul EventPass.Server, găsit urcând din folderul testelor (bin/Debug/net10.0)
    private static readonly string ServerDir = FindServerDir();

    private static string FindServerDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "EventPass.Server");
            if (File.Exists(Path.Combine(candidate, "EventPass.Server.csproj")))
                return candidate;
        }
        throw new InvalidOperationException("Nu găsesc folderul EventPass.Server.");
    }

    // Fișierele .cs ale serverului (fără cod generat: bin, obj, migrări)
    private static IEnumerable<(string File, string Code)> ServerSources() =>
        Directory.EnumerateFiles(ServerDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Select(f => (Path.GetRelativePath(ServerDir, f).Replace('\\', '/'), WithoutComments(File.ReadAllText(f))));

    // Căutăm doar în cod, nu și în comentarii (ex. „Capacity - Sold = locuri rămase” nu e o atribuire).
    // „//” care începe un comentariu, dar nu „https://” din texte (acolo „//” e lipit de „:”).
    private static string WithoutComments(string code) =>
        Regex.Replace(
            Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline),
            @"(^|\s)//.*$", "$1", RegexOptions.Multiline);

    // În ce fișiere apare un tipar de cod
    private static List<string> FilesMatching(string pattern) =>
        ServerSources().Where(s => Regex.IsMatch(s.Code, pattern)).Select(s => s.File).OrderBy(f => f).ToList();

    [Fact]
    public void Comentariile_NuSuntLuateDreptCod()
    {
        var code = WithoutComments("""
            var a = 1; // Sold = 5
            /* IsUsed = false */
            var url = "https://exemplu.ro"; var b = 2;
            """);

        Assert.DoesNotContain("Sold", code);
        Assert.DoesNotContain("IsUsed", code);
        Assert.Contains("https://exemplu.ro", code);
        Assert.Contains("var b = 2;", code);
    }

    [Fact]
    public void Sursele_AuFostGasite()
    {
        // Dacă testul nu găsește codul, celelalte gărzi ar trece „pe gol”
        Assert.True(ServerSources().Count() > 30);
        Assert.Contains("Services/Payments/OrderPaymentService.cs", ServerSources().Select(s => s.File));
    }

    [Fact]
    [Trait("Regula", "EmitereWebhook")]
    public void BiletelesSeCreeazaIntrUnSingurLoc_ConfirmareaPlatii()
    {
        Assert.Equal(new[] { "Services/Payments/OrderPaymentService.cs" }, FilesMatching(@"new\s+(?:[\w.]+\.)?Ticket\s*[\{\(]"));
        Assert.Equal(new[] { "Services/Payments/OrderPaymentService.cs" }, FilesMatching(@"\.Tickets\.Add(Range)?(Async)?\(|\.Set<(?:[\w.]+\.)?Ticket>\(\)\.Add"));
    }

    [Fact]
    [Trait("Regula", "EmitereWebhook")]
    public void ComandaDevinePlatitaIntrUnSingurLoc()
    {
        // Atât prin UPDATE (SetProperty), cât și prin atribuire directă (Status = Paid, dar nu comparația ==)
        Assert.Equal(new[] { "Services/Payments/OrderPaymentService.cs" },
            FilesMatching(@"SetProperty\(\s*\w+\s*=>\s*\w+\.Status\s*,\s*(?:[\w.]+\.)?OrderStatus\.Paid\s*\)"));
        Assert.Empty(FilesMatching(@"\bStatus\s*=(?!=)\s*(?:[\w.]+\.)?OrderStatus\.Paid"));
    }

    [Fact]
    [Trait("Regula", "EmitereWebhook")]
    public void ConfirmareaPlatii_OApeleazaDoarWebhookulSiVerificareaLaStripe()
    {
        // Webhook-ul (semnat de Stripe) și recuperarea plăților cu webhook pierdut (întreabă Stripe direct, #18).
        // Niciodată un endpoint pe care îl apelează clientul cu „am plătit”.
        var callers = FilesMatching(@"\.ConfirmPaymentAsync\(").Where(f => !f.EndsWith("OrderPaymentService.cs")).ToList();

        Assert.Equal(new[] { "Controllers/StripeWebhookController.cs", "Services/ReservationExpiryService.cs" }, callers);
    }

    [Fact]
    [Trait("Regula", "BiletUnic")]
    public void BiletulSeMarcheazaFolositIntrUnSingurLoc_SiNuSeDemarcheazaNiciodata()
    {
        Assert.Equal(new[] { "Services/TicketValidationService.cs" }, FilesMatching(@"SetProperty\(\s*\w+\s*=>\s*\w+\.IsUsed\s*,\s*true\s*\)"));
        Assert.Empty(FilesMatching(@"SetProperty\(\s*\w+\s*=>\s*\w+\.IsUsed\s*,\s*false\s*\)"));
        Assert.Empty(FilesMatching(@"\bIsUsed\s*=(?!=)"));
    }

    [Fact]
    [Trait("Regula", "Stoc")]
    public void StoculSeModificaDoarDinServiciulDeRezervari()
    {
        Assert.Equal(new[] { "Services/SeatReservationService.cs" }, FilesMatching(@"SetProperty\(\s*\w+\s*=>\s*\w+\.Sold\s*,"));
        Assert.Empty(FilesMatching(@"\bSold\s*(=(?!=)|\+=|-=|\+\+|--)"));
    }

    // ---------- API: cine poate apela ce ----------

    private static IEnumerable<(Type Controller, MethodInfo Action, string Verb)> Actions() =>
        typeof(OrdersController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(m => (t, m, Verb: m.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).FirstOrDefault())))
            .Where(a => a.Verb is not null)
            .Select(a => (a.t, a.m, a.Verb!));

    private static bool IsPublic(Type controller, MethodInfo action) =>
        action.GetCustomAttribute<AllowAnonymousAttribute>() is not null ||
        (controller.GetCustomAttribute<AuthorizeAttribute>() is null && action.GetCustomAttribute<AuthorizeAttribute>() is null);

    [Fact]
    [Trait("Regula", "EmitereWebhook")]
    public void OriceEndpointCareModificaDate_CereLogin_CuExceptiaCelorPubliceIntentionat()
    {
        var publicWrites = Actions()
            .Where(a => a.Verb != "GET" && IsPublic(a.Controller, a.Action))
            .Select(a => $"{a.Controller.Name}.{a.Action.Name}")
            .OrderBy(x => x)
            .ToList();

        // Contul nou și login-ul (firesc, fără cont) și webhook-ul Stripe (protejat de semnătură, nu de login)
        Assert.Equal(new[] { "AuthController.Login", "AuthController.Register", "StripeWebhookController.Receive" }, publicWrites);
    }

    [Fact]
    public void CitirileFaraLogin_SuntDoarCatalogulPublicDeEvenimente()
    {
        var publicReads = Actions()
            .Where(a => a.Verb == "GET" && IsPublic(a.Controller, a.Action))
            .Select(a => a.Controller.Name)
            .Distinct()
            .ToList();

        Assert.Equal(new[] { "EventsController" }, publicReads);
    }

    // ---------- Plasa de siguranță din baza de date ----------

    [Fact]
    [Trait("Regula", "Stoc")]
    public async Task BazaDeDate_RefuzaMaiMulteLocuriVanduteDecatCapacitatea()
    {
        // Chiar dacă un cod greșit ar sări peste verificare, regula CHECK din baza de date oprește supra-vânzarea
        using var database = new SqliteTestDatabase();
        PaymentTestData.Seed(database);
        await using var db = database.CreateContext();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.TicketTypes.Where(t => t.Id == 101).ExecuteUpdateAsync(s => s.SetProperty(t => t.Sold, t => t.Capacity + 1)));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.TicketTypes.Where(t => t.Id == 101).ExecuteUpdateAsync(s => s.SetProperty(t => t.Sold, -1)));
        Assert.Equal(1, db.TicketTypes.AsNoTracking().Single(t => t.Id == 101).Sold);   // neschimbat
    }

    [Fact]
    [Trait("Regula", "BiletUnic")]
    public async Task BazaDeDate_RefuzaDouaBileteCuAcelasiToken()
    {
        // Tokenul e ce scanăm la intrare: două bilete cu același token ar însemna un QR care deschide două uși
        using var database = new SqliteTestDatabase();
        PaymentTestData.Seed(database);
        await using var db = database.CreateContext();
        var token = Guid.NewGuid();
        db.Tickets.Add(new Ticket { OrderId = PaymentTestData.OrderId, TicketTypeId = 100, Token = token });
        await db.SaveChangesAsync();

        db.Tickets.Add(new Ticket { OrderId = PaymentTestData.OrderId, TicketTypeId = 100, Token = token });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---------- Clientul nu poate „declara” singur plata ----------

    [Fact]
    [Trait("Regula", "EmitereWebhook")]
    public async Task ClientulIntorsDePeStripe_NuPrimesteBileteFaraConfirmareaStripe()
    {
        // Clientul a plătit pe pagina Stripe și s-a întors pe site, dar webhook-ul n-a venit încă.
        // Nici deschiderea comenzii, nici „Plătește” din nou nu emit bilete: comanda rămâne Pending.
        using var database = new SqliteTestDatabase();
        PaymentTestData.Seed(database);
        var stripe = new FakePaymentGateway();
        stripe.SetPaid(PaymentTestData.SessionId, PaymentTestData.AmountMinor);
        await using var db = database.CreateContext();
        var controller = new OrdersController(db, new SeatReservationService(db), stripe,
            TestHelpers.CreateExpiryService(db, stripe), TestHelpers.CreateRefundService(db, stripe), Options.Create(new StripeOptions()));
        TestHelpers.LoginAs(controller, 1, "Client");

        var opened = Assert.IsType<OrderDto>(Assert.IsType<OkObjectResult>((await controller.Get(PaymentTestData.OrderId, default)).Result).Value);
        var payAgain = await controller.Checkout(PaymentTestData.OrderId, default);

        Assert.Equal("Pending", opened.Status);
        Assert.Empty(opened.Tickets);
        Assert.IsType<ConflictObjectResult>(payAgain.Result);   // „Plata a fost deja făcută și se confirmă.”
        await using var check = database.CreateContext();
        Assert.Equal(0, check.Tickets.Count());
    }
}
