using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Kadr.App.Services;

/// <summary>Локальное распознавание текста через Windows.Media.Ocr (языки из установленных в Windows).</summary>
public static class OcrService
{
    public static bool IsAvailable => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    public static async Task<string> RecognizeAsync(Bitmap bitmap, CancellationToken ct = default)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? (OcrEngine.AvailableRecognizerLanguages.Count > 0 ? OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]) : null)
                     ?? throw new InvalidOperationException("В Windows не установлены языки распознавания текста");

        // Ограничение движка по размеру изображения.
        var max = (int)OcrEngine.MaxImageDimension;
        Bitmap work = bitmap;
        bool scaled = false;
        if (bitmap.Width > max || bitmap.Height > max)
        {
            var k = Math.Min((double)max / bitmap.Width, (double)max / bitmap.Height);
            work = new Bitmap(bitmap, new Size((int)(bitmap.Width * k), (int)(bitmap.Height * k)));
            scaled = true;
        }

        try
        {
            using var ms = new MemoryStream();
            work.Save(ms, ImageFormat.Png);
            using var ras = new InMemoryRandomAccessStream();
            await ras.WriteAsync(ms.ToArray().AsBuffer());
            ras.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(ras);
            using var software = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            ct.ThrowIfCancellationRequested();
            var result = await engine.RecognizeAsync(software);
            return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text)).Trim();
        }
        finally
        {
            if (scaled) work.Dispose();
        }
    }
}
