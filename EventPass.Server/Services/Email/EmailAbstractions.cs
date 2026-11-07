namespace EventPass.Server.Services.Email;

// O imagine pusă „în interiorul” emailului (se vede direct în mesaj, fără să fie descărcată separat).
// În HTML o referim cu src="cid:{ContentId}".
public record InlineImage(string ContentId, string FileName, byte[] Content, string MediaType = "image/png");

public record EmailMessage(
    string ToAddress,
    string ToName,
    string Subject,
    string HtmlBody,
    // Varianta text simplu, pentru clienții de email care nu afișează HTML
    string TextBody,
    IReadOnlyList<InlineImage> InlineImages);

// Tot ce are nevoie aplicația de la „serviciul de email”. În aplicație: SmtpEmailSender (MailKit).
// În teste: o variantă falsă, fără rețea.
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

// Serverul SMTP nu e configurat (Smtp:Host lipsește). Nu e o eroare a emailului în sine: nu consumăm încercări.
public class EmailNotConfiguredException() : Exception("Smtp:Host nu este setat.");

// Serverul SMTP a refuzat mesajul sau nu a putut fi contactat
public class EmailSendException(string message, Exception inner) : Exception(message, inner);

// Setările serverului SMTP (secțiunea „Smtp” din appsettings.json; Username/Password doar în User Secrets)
public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 2525;
    // None (dezvoltare, fără criptare), StartTls, SslOnConnect sau Auto
    public string Security { get; set; } = "None";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "bilete@eventpass.local";
    public string FromName { get; set; } = "EventPass";
}

// Adresa frontend-ului, folosită în linkurile din email
public class ClientOptions
{
    public string BaseUrl { get; set; } = "http://localhost:4200";
}
