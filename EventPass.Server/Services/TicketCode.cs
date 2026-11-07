namespace EventPass.Server.Services;

// Cum arată tokenul unui bilet în afara bazei de date: în QR și ca „cod scurt” pentru oameni.
// Un singur loc, ca pagina comenzii, emailul și scannerul să vorbească aceeași limbă.
public static class TicketCode
{
    // Textul din QR e tokenul cu MAJUSCULE (ex. 3F2A9C1B-7D4E-...). Cifre, litere mari și „-” intră în modul
    // „alfanumeric” al QR-ului, mai compact decât cel pe octeți: codul iese mai mic (37 în loc de 41 de module),
    // cu pătrățele mai mari, deci mai ușor de scanat. La scanare, serverul parsează tokenul cu Guid.TryParse,
    // care acceptă și majuscule, și litere mici.
    public static string QrText(Guid token) => token.ToString("D").ToUpperInvariant();

    // Primele 8 caractere, pentru referință (ex. FA46C34B). Tokenul întreg e doar în QR.
    public static string Short(Guid token) => token.ToString("N")[..8].ToUpperInvariant();
}
