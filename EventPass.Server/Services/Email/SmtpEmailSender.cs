using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EventPass.Server.Services.Email;

// Construiește mesajul MIME (formatul real al unui email) din mesajul nostru simplu
public static class MimeMessageFactory
{
    public static MimeMessage Create(EmailMessage message, MailboxAddress from)
    {
        var mime = new MimeMessage();
        mime.From.Add(from);
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = SanitizeHeader(message.Subject);

        var body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
        foreach (var image in message.InlineImages)
        {
            // „LinkedResources” = imagini legate de HTML prin cid:, deci apar în corpul mesajului
            var part = body.LinkedResources.Add(image.FileName, image.Content, ContentType.Parse(image.MediaType));
            part.ContentId = image.ContentId;
        }
        mime.Body = body.ToMessageBody();
        return mime;
    }

    // Titlul unui eveniment e scris de organizator și ajunge în subiectul emailului. Un rând nou ar putea
    // adăuga anteturi false (ex. un destinatar în plus), așa că orice caracter de control devine spațiu.
    public static string SanitizeHeader(string value) =>
        string.Concat(value.Select(c => char.IsControl(c) ? ' ' : c)).Trim();
}

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpEmailSender(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
            throw new EmailNotConfiguredException();

        var mime = MimeMessageFactory.Create(message, new MailboxAddress(_options.FromName, _options.FromAddress));

        try
        {
            using var client = new SmtpClient { Timeout = 15_000 };
            await client.ConnectAsync(_options.Host, _options.Port, ParseSecurity(_options.Security), ct);
            if (!string.IsNullOrEmpty(_options.Username))
                await client.AuthenticateAsync(_options.Username, _options.Password, ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(quit: true, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Orice problemă cu serverul de email (căzut, refuz, parolă greșită) devine o eroare de „trimitere”
            throw new EmailSendException($"Emailul nu a putut fi trimis: {e.Message}", e);
        }
    }

    private static SecureSocketOptions ParseSecurity(string value) =>
        Enum.TryParse<SecureSocketOptions>(value, ignoreCase: true, out var parsed) ? parsed : SecureSocketOptions.Auto;
}
