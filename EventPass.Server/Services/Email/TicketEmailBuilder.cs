using System.Globalization;
using System.Net;
using System.Text;
using EventPass.Server.Models;
using EventPass.Server.Services.Qr;
using Microsoft.Extensions.Options;

namespace EventPass.Server.Services.Email;

// Compune emailul cu biletele unei comenzi plătite: detalii eveniment + câte un cod QR pe bilet.
// Comanda trebuie încărcată cu User, Tickets și Items → TicketType → Event.
public class TicketEmailBuilder
{
    private static readonly CultureInfo Romanian = CultureInfo.GetCultureInfo("ro-RO");

    private readonly IQrCodeGenerator _qr;
    private readonly ClientOptions _client;

    public TicketEmailBuilder(IQrCodeGenerator qr, IOptions<ClientOptions> client)
    {
        _qr = qr;
        _client = client.Value;
    }

    public EmailMessage Build(Order order)
    {
        var ev = order.Items[0].TicketType.Event;
        var tickets = order.Tickets.OrderBy(t => t.Id).ToList();
        var typeNames = order.Items.ToDictionary(i => i.TicketTypeId, i => i.TicketType.Name);
        var orderUrl = $"{_client.BaseUrl.TrimEnd('/')}/comenzi/{order.Id}";
        var when = FormatWhen(ev.StartsAt);

        var images = tickets
            .Select(t => new InlineImage(
                ContentId: $"ticket-{t.Id}@eventpass",
                FileName: $"bilet-{order.Id}-{t.Id}.png",
                // Aceeași imagine și același text ca pe pagina comenzii
                Content: _qr.GeneratePng(TicketCode.QrText(t.Token), pixelsPerModule: 6)))
            .ToList();

        var html = new StringBuilder();
        html.Append($$"""
            <!doctype html>
            <html lang="ro"><body style="margin:0;padding:0;background:#f6f6f4;font-family:Arial,Helvetica,sans-serif;color:#0b0b0b;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f6f6f4;padding:24px 12px;"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;background:#ffffff;border-radius:16px;"><tr><td style="padding:24px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
            <tr><td style="font-size:14px;font-weight:bold;letter-spacing:3px;padding-bottom:24px;">EVENTPASS</td></tr>
            <tr><td style="font-size:26px;font-weight:bold;line-height:1.2;padding-bottom:8px;">Biletele tale sunt gata</td></tr>
            <tr><td style="font-size:15px;line-height:1.6;color:#444;padding-bottom:24px;">Salut, {{H(order.User.FullName)}}! Plata pentru comanda #{{order.Id}} a fost confirmată.</td></tr>
            <tr><td style="background:#f6f6f4;border-radius:12px;padding:20px;">
              <div style="font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#8c8c8c;">{{H(when)}}</div>
              <div style="font-size:20px;font-weight:bold;padding:4px 0;">{{H(ev.Title)}}</div>
              <div style="font-size:14px;color:#444;">{{H(ev.Venue)}}, {{H(ev.City)}}</div>
            </td></tr>
            """);

        for (var i = 0; i < tickets.Count; i++)
        {
            var ticket = tickets[i];
            var image = images[i];
            html.Append($$"""
                <tr><td style="padding-top:20px;">
                  <!-- Pe verticală (QR deasupra, detalii dedesubt): încape pe orice lățime de ecran, inclusiv pe telefon -->
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:1px solid #cfcfcc;border-radius:12px;"><tr>
                    <td align="center" style="padding:16px;font-size:14px;">
                      <img src="cid:{{image.ContentId}}" width="{{PngWidth(image.Content)}}" height="{{PngWidth(image.Content)}}" alt="Cod QR pentru biletul {{i + 1}}" style="display:block;margin:0 auto 12px;max-width:100%;height:auto;">
                      <div style="font-size:18px;font-weight:bold;">{{H(typeNames[ticket.TicketTypeId])}}</div>
                      <div style="color:#8c8c8c;padding:2px 0 10px;">Bilet {{i + 1}} din {{tickets.Count}}</div>
                      <div style="font-size:11px;letter-spacing:1px;text-transform:uppercase;color:#8c8c8c;">Cod</div>
                      <div style="font-family:Consolas,Menlo,monospace;font-size:16px;letter-spacing:2px;">{{TicketCode.Short(ticket.Token)}}</div>
                    </td>
                  </tr></table>
                </td></tr>
                """);
        }

        html.Append($$"""
            <tr><td style="font-size:14px;line-height:1.6;color:#444;padding-top:24px;">Arată codul QR de pe telefon la intrare. Fiecare cod poate fi folosit o singură dată, deci nu îl trimite altora.</td></tr>
            <tr><td style="padding-top:20px;"><a href="{{H(orderUrl)}}" style="display:inline-block;background:#0b0b0b;color:#ffffff;text-decoration:none;padding:14px 28px;border-radius:999px;font-size:13px;letter-spacing:1px;text-transform:uppercase;">Vezi comanda</a></td></tr>
            </table>
            </td></tr></table>
            </td></tr></table>
            </body></html>
            """);

        var text = new StringBuilder();
        text.AppendLine($"Salut, {order.User.FullName}!");
        text.AppendLine($"Plata pentru comanda #{order.Id} a fost confirmată.");
        text.AppendLine();
        text.AppendLine($"{ev.Title}");
        text.AppendLine($"{when}");
        text.AppendLine($"{ev.Venue}, {ev.City}");
        text.AppendLine();
        for (var i = 0; i < tickets.Count; i++)
            text.AppendLine($"Bilet {i + 1} din {tickets.Count}: {typeNames[tickets[i].TicketTypeId]} (cod {TicketCode.Short(tickets[i].Token)})");
        text.AppendLine();
        text.AppendLine("Codurile QR sunt în varianta HTML a acestui email, iar toate biletele se văd și aici:");
        text.AppendLine(orderUrl);

        return new EmailMessage(
            order.User.Email,
            order.User.FullName,
            Subject: $"Biletele tale: {ev.Title}",
            html.ToString(),
            text.ToString(),
            images);
    }

    // Tot ce vine de la utilizatori (nume, titlul evenimentului etc.) se „scapă” înainte să intre în HTML,
    // ca un titlu de forma <script>… să apară ca text, nu să fie interpretat ca cod
    private static string H(string value) => WebUtility.HtmlEncode(value);

    // Data evenimentului e salvată în UTC; în email o arătăm pe ora României
    private static string FormatWhen(DateTime utc)
    {
        var local = ToRomaniaTime(utc, out var isUtcFallback);
        var text = local.ToString("dddd, d MMMM yyyy 'la' HH:mm", Romanian);
        return isUtcFallback ? $"{text} (UTC)" : text;
    }

    private static DateTime ToRomaniaTime(DateTime utc, out bool isUtcFallback)
    {
        foreach (var id in new[] { "Europe/Bucharest", "E. Europe Standard Time" })
        {
            try
            {
                isUtcFallback = false;
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(id));
            }
            catch (TimeZoneNotFoundException) { }
        }
        isUtcFallback = true;
        return utc;
    }

    // Lățimea unui PNG e în octeții 16–19 (antetul IHDR); o citim ca să afișăm imaginea la mărimea ei reală
    private static int PngWidth(byte[] png) => (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
}
