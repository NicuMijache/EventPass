namespace EventPass.Server.Services;

public interface IImageStorage
{
    // Salvează imaginea și întoarce calea publică (ex. "/uploads/events/abc.jpg")
    Task<string> SaveEventImageAsync(Stream content, string extension, CancellationToken ct = default);
    void Delete(string? publicPath);
}

// Salvează posterele pe disc, în wwwroot/uploads/events, de unde sunt servite ca fișiere statice
public class LocalImageStorage : IImageStorage
{
    private const string EventsFolder = "uploads/events";
    private readonly string _webRoot;

    public LocalImageStorage(IWebHostEnvironment env)
    {
        // WebRootPath e null dacă folderul wwwroot nu există încă
        _webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
    }

    public async Task<string> SaveEventImageAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var folder = Path.Combine(_webRoot, EventsFolder);
        Directory.CreateDirectory(folder);

        // Nume aleatoriu: nu folosim niciodată numele trimis de utilizator
        var fileName = $"{Guid.NewGuid():N}{extension}";
        await using var file = File.Create(Path.Combine(folder, fileName));
        await content.CopyToAsync(file, ct);

        return $"/{EventsFolder}/{fileName}";
    }

    public void Delete(string? publicPath)
    {
        if (string.IsNullOrEmpty(publicPath))
            return;

        // Ștergem doar fișiere din folderul nostru de upload-uri
        var fullPath = Path.GetFullPath(Path.Combine(_webRoot, publicPath.TrimStart('/')));
        var allowedFolder = Path.GetFullPath(Path.Combine(_webRoot, EventsFolder)) + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(allowedFolder, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            File.Delete(fullPath);
    }
}

public static class ImageValidator
{
    public const long MaxBytes = 5 * 1024 * 1024;

    // Recunoaște tipul după primii octeți ai fișierului („semnătura”), nu după extensie.
    // Întoarce extensia corectă sau null dacă nu e JPG, PNG sau WEBP.
    public static string? DetectExtension(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return ".jpg";
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return ".png";
        if (header.Length >= 12 &&
            header[..4].SequenceEqual("RIFF"u8) &&
            header[8..12].SequenceEqual("WEBP"u8))
            return ".webp";
        return null;
    }
}
