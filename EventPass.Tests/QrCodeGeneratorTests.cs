using EventPass.Server.Services.Qr;
using StbImageSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace EventPass.Tests;

public class QrCodeGeneratorTests
{
    private readonly QrCodeGenerator _generator = new();

    // Un cititor real de coduri QR (ZXing): decodează PNG-ul în pixeli și citește codul din imagine.
    // Folosește modul PURE_BARCODE, făcut pentru imagini curate generate pe calculator. Modul obișnuit
    // de detecție al ZXing pierde ~1% din imaginile pixel-perfect (am verificat pe 6000 de tokenuri,
    // și că un decodor independent, jsQR, le citește pe toate), deci un test care l-ar folosi ar pica aleatoriu.
    public static string? Decode(byte[] png, Action<byte[], int, int>? damage = null)
    {
        var image = ImageResult.FromMemory(png, ColorComponents.Grey);
        damage?.Invoke(image.Data, image.Width, image.Height);
        var source = new RGBLuminanceSource(image.Data, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.Gray8);
        var hints = new Dictionary<DecodeHintType, object> { [DecodeHintType.PURE_BARCODE] = true };
        return new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(source)), hints)?.Text;
    }

    [Fact]
    public void GeneratePng_ProduceUnFisierPngValid()
    {
        var png = _generator.GeneratePng(Guid.NewGuid().ToString());

        // Orice PNG începe cu aceiași 8 octeți („semnătura” formatului)
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8));
    }

    [Theory]
    [InlineData("3f2a9c1b-7d4e-4a2b-9c81-5e6f7a8b9c0d")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void GeneratePng_CodulSeCitesteInapoi_CuExactTokenul(string token)
    {
        var png = _generator.GeneratePng(token);

        Assert.Equal(token, Decode(png));
    }

    [Fact]
    public void GeneratePng_CitireInapoiPentruMaiMulteTokenuriAleatorii()
    {
        for (var i = 0; i < 25; i++)
        {
            var token = Guid.NewGuid().ToString("D");
            Assert.Equal(token, Decode(_generator.GeneratePng(token)));
        }
    }

    [Fact]
    public void GeneratePng_CodulPartialAcoperitSeCitesteTotusi_DatoritaCorectieiDeErori()
    {
        // 20 de tokenuri FIXE (nu aleatorii), ca rezultatul să fie același la fiecare rulare
        for (var i = 1; i <= 20; i++)
        {
            var token = new Guid(i * 7919, (short)(i * 31), (short)(i * 17), 1, 2, 3, 4, 5, 6, 7, (byte)i).ToString("D");
            var png = _generator.GeneratePng(token, pixelsPerModule: 10);

            // „Murdărim” cu alb un pătrat din mijlocul imaginii (ca o pată sau un reflex). Zona conține
            // doar date: nu atingem pătratele de reper din colțuri și nici liniile de sincronizare
            var text = Decode(png, (pixels, width, height) =>
            {
                for (var y = height * 40 / 100; y < height * 58 / 100; y++)
                    for (var x = width * 40 / 100; x < width * 58 / 100; x++)
                        pixels[y * width + x] = 255;
            });

            Assert.Equal(token, text);
        }
    }

    [Fact]
    public void GeneratePng_CodulAcoperitInMareParte_NuSeMaiCiteste()
    {
        // Contrastul testului de mai sus: corecția de erori are o limită. Dacă acoperim ~jumătate
        // din cod, citirea trebuie să eșueze; altfel testul cu pata mică n-ar dovedi nimic.
        var png = _generator.GeneratePng("3F2A9C1B-7D4E-4A2B-9C81-5E6F7A8B9C0D");

        var text = Decode(png, (pixels, width, height) =>
        {
            for (var y = height * 25 / 100; y < height * 75 / 100; y++)
                for (var x = width * 25 / 100; x < width * 75 / 100; x++)
                    pixels[y * width + x] = 255;
        });

        Assert.Null(text);
    }

    [Fact]
    public void GeneratePng_AcelasiTokenDaAcelasiPng_IarTokenuriDiferiteDauImaginiDiferite()
    {
        var a1 = _generator.GeneratePng("aaaaaaaa-0000-0000-0000-000000000000");
        var a2 = _generator.GeneratePng("aaaaaaaa-0000-0000-0000-000000000000");
        var b = _generator.GeneratePng("bbbbbbbb-0000-0000-0000-000000000000");

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
    }

    [Fact]
    public void GeneratePng_UnPixelsPerModuleMaiMareDaImagineMaiMare()
    {
        var small = ImageResult.FromMemory(_generator.GeneratePng("token", 5), ColorComponents.Grey);
        var large = ImageResult.FromMemory(_generator.GeneratePng("token", 20), ColorComponents.Grey);

        Assert.Equal(small.Width * 4, large.Width);
        Assert.Equal(small.Height * 4, large.Height);
    }

    [Fact]
    public void GeneratePng_ImagineaEstePatrata()
    {
        var image = ImageResult.FromMemory(_generator.GeneratePng(Guid.NewGuid().ToString()), ColorComponents.Grey);

        Assert.Equal(image.Width, image.Height);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void GeneratePng_FaraText_AruncaExceptie(string? content)
    {
        Assert.Throws<ArgumentException>(() => _generator.GeneratePng(content!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(QrCodeGenerator.MaxPixelsPerModule + 1)]
    public void GeneratePng_MarimeInvalida_AruncaExceptie(int pixelsPerModule)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.GeneratePng("token", pixelsPerModule));
    }
}
