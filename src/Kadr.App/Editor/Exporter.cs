using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kadr.App.Editor;

/// <summary>Рендер содержимого оверлея (фон + объекты) в физических пикселях монитора и обрезка по области.</summary>
public static class Exporter
{
    public static Bitmap Render(FrameworkElement content, Rect selectionDip, double scale)
    {
        int fullW = Math.Max(1, (int)Math.Round(content.ActualWidth * scale));
        int fullH = Math.Max(1, (int)Math.Round(content.ActualHeight * scale));
        var rtb = new RenderTargetBitmap(fullW, fullH, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(content);

        var crop = new Int32Rect(
            (int)Math.Round(selectionDip.X * scale), (int)Math.Round(selectionDip.Y * scale),
            (int)Math.Round(selectionDip.Width * scale), (int)Math.Round(selectionDip.Height * scale));
        crop.X = Math.Clamp(crop.X, 0, fullW - 1);
        crop.Y = Math.Clamp(crop.Y, 0, fullH - 1);
        crop.Width = Math.Clamp(crop.Width, 1, fullW - crop.X);
        crop.Height = Math.Clamp(crop.Height, 1, fullH - crop.Y);

        BitmapSource source = new CroppedBitmap(rtb, crop);
        return ToBitmap(source);
    }

    public static Bitmap ToBitmap(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        using var tmp = new Bitmap(ms);
        return new Bitmap(tmp); // копия, не привязанная к потоку
    }

    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var frame = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    /// <summary>Заготовка для инструмента «размытие»: уменьшить в factor раз и растянуть обратно.</summary>
    public static BitmapSource MakeBlurred(BitmapSource source, double factor = 8)
    {
        var down = new TransformedBitmap(source, new ScaleTransform(1 / factor, 1 / factor));
        var up = new TransformedBitmap(down, new ScaleTransform(factor, factor));
        var cached = new System.Windows.Media.Imaging.CachedBitmap(up, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        cached.Freeze();
        return cached;
    }
}
