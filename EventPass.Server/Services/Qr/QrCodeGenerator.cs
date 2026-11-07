using QRCoder;

namespace EventPass.Server.Services.Qr;

// Transformă un text în imaginea unui cod QR. Îl folosim pentru tokenul fiecărui bilet,
// iar la issue-ul #14 (email) îl refolosim ca să punem QR-ul în mesaj.
public interface IQrCodeGenerator
{
    // Întoarce octeții unui fișier PNG. `pixelsPerModule` = câți pixeli are un „pătrățel” al codului.
    byte[] GeneratePng(string content, int pixelsPerModule = 10);
}

public class QrCodeGenerator : IQrCodeGenerator
{
    // Mărimea la care un QR se scanează comod de pe un ecran de telefon, dar nu devine uriaș
    public const int DefaultPixelsPerModule = 10;
    public const int MaxPixelsPerModule = 40;

    public byte[] GeneratePng(string content, int pixelsPerModule = DefaultPixelsPerModule)
    {
        if (string.IsNullOrEmpty(content))
            throw new ArgumentException("Textul din codul QR nu poate fi gol.", nameof(content));
        if (pixelsPerModule is < 1 or > MaxPixelsPerModule)
            throw new ArgumentOutOfRangeException(nameof(pixelsPerModule), $"Valoarea trebuie să fie între 1 și {MaxPixelsPerModule}.");

        // Nivelul Q repară până la ~25% din cod: biletul poate fi pe un ecran cu reflexii
        // sau pe o hârtie îndoită și tot se citește, fără ca imaginea să devină prea aglomerată
        using var data = QRCodeGenerator.GenerateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
