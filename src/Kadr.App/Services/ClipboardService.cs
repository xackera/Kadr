using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Kadr.App.Services;

/// <summary>Буфер обмена: изображение (DIB), PNG и путь к файлу одновременно, с повторами при занятом буфере.</summary>
public static class ClipboardService
{
    public static void SetImage(Bitmap bitmap, string? filePath)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        var png = ms.ToArray();

        var source = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        source.Freeze();

        var data = new DataObject();
        data.SetImage(source);
        data.SetData("PNG", new MemoryStream(png), false);
        if (filePath is not null && File.Exists(filePath))
            data.SetFileDropList(new StringCollection { filePath });

        SetWithRetry(data);
    }

    public static void SetText(string text) => SetWithRetry(new DataObject(DataFormats.UnicodeText, text));

    public static void SetFile(string filePath)
    {
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { filePath });
        SetWithRetry(data);
    }

    private static void SetWithRetry(DataObject data)
    {
        Exception? last = null;
        for (int attempt = 0, delay = 5; attempt < 10; attempt++, delay = Math.Min(delay * 2, 100))
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return;
            }
            catch (COMException ex)
            {
                last = ex;
                Thread.Sleep(delay);
            }
        }
        throw new InvalidOperationException("Буфер обмена занят другим приложением", last);
    }
}
