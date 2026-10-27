using EventPass.Server.Controllers;
using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using EventPass.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EventPass.Tests;

public class EventImageTests
{
    // Primii octeți („semnătura”) ai fiecărui format, urmați de date oarecare
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    private static readonly byte[] Webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();
    private static readonly byte[] Text = "nu sunt o imagine"u8.ToArray();

    private static IFormFile File(byte[] content, string name = "poster.jpg", long? claimedLength = null) =>
        new FormFile(new MemoryStream(content), 0, claimedLength ?? content.Length, "file", name);

    // Evenimentul 10 e al organizatorului 1 (logat), evenimentul 20 al organizatorului 2
    private static (AppDbContext Db, OrganizerEventsController Controller, FakeImageStorage Images) Setup()
    {
        var db = TestHelpers.CreateDb();
        db.Users.AddRange(
            new User { Id = 1, Email = "org1@example.com", Role = UserRole.Organizer },
            new User { Id = 2, Email = "org2@example.com", Role = UserRole.Organizer });
        db.Events.AddRange(
            new Event { Id = 10, Title = "Al meu", Venue = "BT Arena", City = "Cluj-Napoca", StartsAt = DateTime.UtcNow.AddMonths(1), OrganizerId = 1 },
            new Event { Id = 20, Title = "Al altuia", Venue = "Sala Polivalentă", City = "Iași", StartsAt = DateTime.UtcNow.AddMonths(1), OrganizerId = 2 });
        db.SaveChanges();

        var images = new FakeImageStorage();
        var controller = new OrganizerEventsController(db, images);
        TestHelpers.LoginAs(controller, userId: 1, role: "Organizer");
        return (db, controller, images);
    }

    [Theory]
    [InlineData("png", ".png")]
    [InlineData("jpg", ".jpg")]
    [InlineData("webp", ".webp")]
    public void DetectExtension_RecunoasteFormateleDupaContinut(string format, string expected)
    {
        var bytes = format switch { "png" => Png, "jpg" => Jpg, _ => Webp };

        Assert.Equal(expected, ImageValidator.DetectExtension(bytes));
    }

    [Fact]
    public void DetectExtension_TextulNuEsteImagine()
    {
        Assert.Null(ImageValidator.DetectExtension(Text));
    }

    [Fact]
    public async Task UploadImage_PngValid_SalveazaSiFoloseșteExtensiaReala()
    {
        var (db, controller, images) = Setup();
        using var _ = db;

        // Numele spune .jpg, dar conținutul e PNG: contează conținutul
        var result = await controller.UploadImage(10, File(Png, "poster.jpg"), CancellationToken.None);

        var dto = Assert.IsType<EventDetailDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.EndsWith(".png", dto.ImageUrl);
        Assert.Equal(dto.ImageUrl, db.Events.Find(10)!.ImagePath);
        Assert.Single(images.Saved);
    }

    [Fact]
    public async Task UploadImage_TextRedenumitJpg_Returneaza400SiNuSalveaza()
    {
        var (db, controller, images) = Setup();
        using var _ = db;

        var result = await controller.UploadImage(10, File(Text, "virus.jpg"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(images.Saved);
        Assert.Null(db.Events.Find(10)!.ImagePath);
    }

    [Fact]
    public async Task UploadImage_PesteCinciMB_Returneaza400()
    {
        var (db, controller, images) = Setup();
        using var _ = db;

        var result = await controller.UploadImage(10, File(Png, claimedLength: ImageValidator.MaxBytes + 1), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(images.Saved);
    }

    [Fact]
    public async Task UploadImage_InlocuiestePozaSiOStergePeCeaVeche()
    {
        var (db, controller, images) = Setup();
        using var _ = db;
        await controller.UploadImage(10, File(Png), CancellationToken.None);
        var oldPath = db.Events.Find(10)!.ImagePath;

        await controller.UploadImage(10, File(Jpg), CancellationToken.None);

        Assert.Equal(new[] { oldPath }, images.Deleted);
        Assert.EndsWith(".jpg", db.Events.Find(10)!.ImagePath);
    }

    [Fact]
    public async Task UploadImage_LaEvenimentulAltuiOrganizator_Returneaza404()
    {
        var (db, controller, images) = Setup();
        using var _ = db;

        var result = await controller.UploadImage(20, File(Png), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Empty(images.Saved);
    }

    [Fact]
    public async Task Delete_EvenimentCuPoza_StergeSiFisierul()
    {
        var (db, controller, images) = Setup();
        using var _ = db;
        await controller.UploadImage(10, File(Png), CancellationToken.None);
        var path = db.Events.Find(10)!.ImagePath;

        await controller.Delete(10);

        Assert.Contains(path, images.Deleted);
    }
}
