using EventPass.Server.Data;
using EventPass.Server.Models;
using EventPass.Server.Services;
using EventPass.Server.Services.Qr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Controllers;

// Biletele unei comenzi. Fiecare utilizator vede doar biletele comenzilor lui.
[ApiController]
[Route("api/orders/{orderId:int}/tickets")]
[Authorize]
public class OrderTicketsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IQrCodeGenerator _qr;

    public OrderTicketsController(AppDbContext db, IQrCodeGenerator qr)
    {
        _db = db;
        _qr = qr;
    }

    // Textul din QR e tokenul cu MAJUSCULE (ex. 3F2A9C1B-7D4E-...). Cifre, litere mari și „-” intră în modul
    // „alfanumeric” al QR-ului, mai compact decât cel pe octeți: codul iese mai mic (37 în loc de 41 de module),
    // cu pătrățele mai mari, deci mai ușor de scanat. La scanare, serverul parsează tokenul cu Guid.TryParse,
    // care acceptă și majuscule, și litere mici.
    public static string TokenToQrText(Guid token) => token.ToString("D").ToUpperInvariant();

    // Imaginea QR a unui bilet. Nu o păstrăm nicăieri: o calculăm din token de fiecare dată.
    [HttpGet("{ticketId:int}/qr")]
    [Produces("image/png")]
    public async Task<IActionResult> GetQr(int orderId, int ticketId, [FromQuery] int size = QrCodeGenerator.DefaultPixelsPerModule, CancellationToken ct = default)
    {
        var userId = User.GetUserId();

        // Comanda trebuie să fie a utilizatorului logat, iar biletul să facă parte din ea.
        // Pentru orice altceva răspundem 404, ca să nu aflăm dacă un bilet străin există.
        var order = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId && o.UserId == userId)
            .Select(o => new { o.Status })
            .SingleOrDefaultAsync(ct);
        if (order is null)
            return NotFound();

        var token = await _db.Tickets
            .Where(t => t.Id == ticketId && t.OrderId == orderId)
            .Select(t => (Guid?)t.Token)
            .SingleOrDefaultAsync(ct);
        if (token is null)
            return NotFound();

        // Biletele există doar după plată, dar verificăm și starea: o comandă rambursată (#19)
        // nu trebuie să mai arate un cod valid
        if (order.Status != OrderStatus.Paid)
            return Conflict(new { message = "Codul QR este disponibil doar pentru comenzile plătite." });

        var png = _qr.GeneratePng(TokenToQrText(token.Value), Math.Clamp(size, 1, QrCodeGenerator.MaxPixelsPerModule));

        // Conține un secret (tokenul): nu vrem să rămână în memoria cache a browserului sau a unui proxy
        Response.Headers.CacheControl = "private, no-store";
        return File(png, "image/png");
    }
}
