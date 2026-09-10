using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Kadr.App.Editor;
using Bitmap = System.Drawing.Bitmap;

namespace Kadr.App.Services;

/// <summary>Печать снимка: диалог принтера, вписывание в страницу с сохранением пропорций.</summary>
public static class PrintService
{
    public static PrintDialog? ShowDialog(Window? owner)
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        bool? ok;
        try { ok = dialog.ShowDialog(); }
        catch { ok = false; }
        return ok == true ? dialog : null;
    }

    public static void Print(PrintDialog dialog, Bitmap bitmap, string jobName = "Kadr")
    {
        var source = Exporter.ToBitmapSource(bitmap);
        double pageW = dialog.PrintableAreaWidth, pageH = dialog.PrintableAreaHeight;
        if (pageW <= 0 || pageH <= 0) { pageW = 816; pageH = 1056; }

        bool landscape = source.PixelWidth > source.PixelHeight && pageW < pageH;
        if (landscape) dialog.PrintTicket.PageOrientation = System.Printing.PageOrientation.Landscape;
        if (landscape) (pageW, pageH) = (pageH, pageW);

        const double margin = 24;
        var availW = pageW - 2 * margin; var availH = pageH - 2 * margin;
        var k = Math.Min(availW / source.PixelWidth, availH / source.PixelHeight);
        var w = source.PixelWidth * k; var h = source.PixelHeight * k;

        var page = new FixedPage { Width = pageW, Height = pageH, Background = Brushes.White };
        var image = new Image { Source = source, Width = w, Height = h, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        FixedPage.SetLeft(image, (pageW - w) / 2);
        FixedPage.SetTop(image, (pageH - h) / 2);
        page.Children.Add(image);
        page.Measure(new Size(pageW, pageH));
        page.Arrange(new Rect(0, 0, pageW, pageH));
        page.UpdateLayout();

        dialog.PrintVisual(page, jobName);
    }
}
