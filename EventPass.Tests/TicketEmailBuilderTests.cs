using EventPass.Server.Models;
using EventPass.Server.Services;
using EventPass.Server.Services.Email;
using EventPass.Server.Services.Qr;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EventPass.Tests;

public class TicketEmailBuilderTests
{
    private static readonly Guid Token1 = Guid.Parse("3f2a9c1b-7d4e-4a2b-9c81-5e6f7a8b9c0d");
    private static readonly Guid Token2 = Guid.Parse("a0b1c2d3-e4f5-4678-9abc-def012345678");

    private static TicketEmailBuilder CreateBuilder(string baseUrl = "http://localhost:4200") =>
        new(new QrCodeGenerator(), Options.Create(new ClientOptions { BaseUrl = baseUrl }));

    // O comandă plătită de Ana: 2 × Standard (bilete 11 și 12) la evenimentul „Concert”
    private static Order PaidOrder(string eventTitle = "Concert", string fullName = "Ana Pop")
    {
        var ev = new Event
        {
            Id = 10, Title = eventTitle, Venue = "BT Arena", City = "Cluj-Napoca",
            StartsAt = new DateTime(2027, 2, 12, 19, 0, 0, DateTimeKind.Utc),
        };
        var type = new TicketType { Id = 100, Name = "Standard", Price = 50, Capacity = 10, Event = ev, EventId = 10 };
        return new Order
        {
            Id = 7,
            Status = OrderStatus.Paid,
            User = new User { Id = 1, Email = "ana@example.com", FullName = fullName },
            Items = { new OrderItem { TicketTypeId = 100, TicketType = type, Quantity = 2, UnitPrice = 50 } },
            Tickets =
            {
                new Ticket { Id = 11, Token = Token1, TicketTypeId = 100 },
                new Ticket { Id = 12, Token = Token2, TicketTypeId = 100 },
            },
        };
    }

    [Fact]
    public void Build_AdresaSiNumeleDestinatarului_SuntCeleAleClientului()
    {
        var message = CreateBuilder().Build(PaidOrder());

        Assert.Equal("ana@example.com", message.ToAddress);
        Assert.Equal("Ana Pop", message.ToName);
    }

    [Fact]
    public void Build_SubiectulContineTitlulEvenimentului()
    {
        var message = CreateBuilder().Build(PaidOrder("Nocturne Jazz Session"));

        Assert.Equal("Biletele tale: Nocturne Jazz Session", message.Subject);
    }

    [Fact]
    public void Build_CuteUnQrPentruFiecareBilet_CuIdUnic()
    {
        var message = CreateBuilder().Build(PaidOrder());

        Assert.Equal(2, message.InlineImages.Count);
        Assert.Equal(2, message.InlineImages.Select(i => i.ContentId).Distinct().Count());
        Assert.All(message.InlineImages, i => Assert.Equal("image/png", i.MediaType));
    }

    [Fact]
    public void Build_FiecareImagineSeCiesteInapoiCaTokenulBiletului()
    {
        var message = CreateBuilder().Build(PaidOrder());

        // Cap-coadă: scanăm imaginea din email și ar trebui să dea exact tokenul biletului
        Assert.Equal(TicketCode.QrText(Token1), QrCodeGeneratorTests.Decode(message.InlineImages[0].Content));
        Assert.Equal(TicketCode.QrText(Token2), QrCodeGeneratorTests.Decode(message.InlineImages[1].Content));
    }

    [Fact]
    public void Build_HtmlulReferaFiecareImagineProCid()
    {
        var message = CreateBuilder().Build(PaidOrder());

        Assert.All(message.InlineImages, image => Assert.Contains($"src=\"cid:{image.ContentId}\"", message.HtmlBody));
    }

    [Fact]
    public void Build_ArataCodulScurtSiDetaliileEvenimentului()
    {
        var message = CreateBuilder().Build(PaidOrder());

        Assert.Contains("3F2A9C1B", message.HtmlBody);
        Assert.Contains("A0B1C2D3", message.HtmlBody);
        Assert.Contains("BT Arena", message.HtmlBody);
        Assert.Contains("Cluj-Napoca", message.HtmlBody);
        Assert.Contains("2027", message.HtmlBody);
        Assert.Contains("Bilet 2 din 2", message.HtmlBody);
        Assert.Contains("comanda #7", message.HtmlBody);
    }

    [Fact]
    public void Build_LinkulCatreComandaFolosesteAdresaFrontendului_FaraDubluSlash()
    {
        var message = CreateBuilder("https://eventpass.example.com/").Build(PaidOrder());

        Assert.Contains("https://eventpass.example.com/comenzi/7", message.HtmlBody);
        Assert.DoesNotContain("com//comenzi", message.HtmlBody);
    }

    [Fact]
    public void Build_TitlulEvenimentuluiEsteEscapatInHtml_NuSeExecutaCaCod()
    {
        // Titlul îl scrie organizatorul: nu trebuie să poată strecura cod în emailul clientului
        var message = CreateBuilder().Build(PaidOrder(eventTitle: "<script>alert(1)</script> & \"Cluj\""));

        Assert.DoesNotContain("<script>", message.HtmlBody);
        Assert.Contains("&lt;script&gt;", message.HtmlBody);
        Assert.Contains("&amp;", message.HtmlBody);
    }

    [Fact]
    public void Build_NumeleClientuluiEsteEscapatInHtml()
    {
        var message = CreateBuilder().Build(PaidOrder(fullName: "<img src=x onerror=alert(1)>"));

        Assert.DoesNotContain("<img src=x", message.HtmlBody);
        Assert.Contains("&lt;img src=x", message.HtmlBody);
    }

    [Fact]
    public void Build_VarianteaTextContineBileteleSiLinkul_FaraHtml()
    {
        var message = CreateBuilder().Build(PaidOrder());

        Assert.Contains("Bilet 1 din 2: Standard (cod 3F2A9C1B)", message.TextBody);
        Assert.Contains("http://localhost:4200/comenzi/7", message.TextBody);
        Assert.DoesNotContain("<", message.TextBody);
    }

    [Fact]
    public void Build_OOrdineStabilaADeBilete_DupaId()
    {
        var order = PaidOrder();
        order.Tickets.Reverse();   // aduse din baza de date în altă ordine

        var message = CreateBuilder().Build(order);

        Assert.Equal(TicketCode.QrText(Token1), QrCodeGeneratorTests.Decode(message.InlineImages[0].Content));
    }

    // ---- Construirea mesajului MIME (formatul real al emailului) ----

    [Fact]
    public void MimeMessage_AreImaginileInlineCuContentIdSiCorpHtmlPlusText()
    {
        var message = CreateBuilder().Build(PaidOrder());

        var mime = MimeMessageFactory.Create(message, new MailboxAddress("EventPass", "bilete@eventpass.local"));

        Assert.Equal("bilete@eventpass.local", ((MailboxAddress)mime.From[0]).Address);
        Assert.Equal("ana@example.com", ((MailboxAddress)mime.To[0]).Address);
        // Standardul email folosește \r\n pentru rânduri noi; comparăm conținutul fără diferența asta
        static string Normalize(string? text) => (text ?? "").Replace("\r\n", "\n").TrimEnd();
        Assert.Equal(Normalize(message.HtmlBody), Normalize(mime.HtmlBody));
        Assert.Equal(Normalize(message.TextBody), Normalize(mime.TextBody));

        var images = mime.BodyParts.OfType<MimePart>().Where(p => p.ContentType.MimeType == "image/png").ToList();
        Assert.Equal(2, images.Count);
        Assert.All(images, p => Assert.False(string.IsNullOrEmpty(p.ContentId)));
        Assert.Equal(message.InlineImages.Select(i => i.ContentId).Order(), images.Select(p => p.ContentId).Order());
    }

    [Fact]
    public void MimeMessage_RandNouInSubiect_NuPoateAdaugaAnteturiFalse()
    {
        // Un organizator pune în titlu un rând nou urmat de un antet fals (încercare de „header injection”)
        var message = CreateBuilder().Build(PaidOrder("Concert\r\nBcc: victima@example.com"));

        var mime = MimeMessageFactory.Create(message, new MailboxAddress("EventPass", "bilete@eventpass.local"));

        Assert.Empty(mime.Bcc);
        Assert.DoesNotContain('\n', mime.Subject ?? "");
        Assert.DoesNotContain('\r', mime.Subject ?? "");
    }
}
