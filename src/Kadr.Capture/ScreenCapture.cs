using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Kadr.Capture;

/// <summary>Снимки экрана через GDI (BitBlt с CAPTUREBLT). Координаты в физических пикселях.</summary>
public static class ScreenCapture
{
    public static Bitmap CaptureRectangle(Rectangle rect, bool includeCursor)
    {
        if (rect.Width <= 0 || rect.Height <= 0) throw new ArgumentException("Пустая область захвата", nameof(rect));
        var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        // С композицией DWM (Windows 10/11) SourceCopy захватывает и слоистые окна; флаг CaptureBlt System.Drawing не принимает.
        g.CopyFromScreen(rect.X, rect.Y, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
        if (includeCursor) DrawCursor(g, rect);
        return bmp;
    }

    public static Bitmap CaptureMonitor(MonitorInfo monitor, bool includeCursor) => CaptureRectangle(monitor.Bounds, includeCursor);

    public static Bitmap CaptureVirtualScreen(bool includeCursor) => CaptureRectangle(Monitors.VirtualScreen(), includeCursor);

    private static void DrawCursor(Graphics g, Rectangle rect)
    {
        var ci = new Native.CURSORINFO { cbSize = Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref ci) || ci.flags != Native.CURSOR_SHOWING || ci.hCursor == 0) return;
        if (!Native.GetIconInfo(ci.hCursor, out var ii)) return;
        try
        {
            int x = ci.ptScreenPos.X - ii.xHotspot - rect.X;
            int y = ci.ptScreenPos.Y - ii.yHotspot - rect.Y;
            var hdc = g.GetHdc();
            try { Native.DrawIconEx(hdc, x, y, ci.hCursor, 0, 0, 0, 0, Native.DI_NORMAL); }
            finally { g.ReleaseHdc(hdc); }
        }
        finally
        {
            if (ii.hbmMask != 0) Native.DeleteObject(ii.hbmMask);
            if (ii.hbmColor != 0) Native.DeleteObject(ii.hbmColor);
        }
    }

    /// <summary>Быстрая проверка «кадр полностью чёрный» по сетке выборок (защищённый контент).</summary>
    public static bool IsBlack(Bitmap bmp, int samples = 24)
    {
        for (int i = 0; i < samples; i++)
            for (int j = 0; j < samples; j++)
            {
                var c = bmp.GetPixel(bmp.Width * i / samples, bmp.Height * j / samples);
                if (c.R > 8 || c.G > 8 || c.B > 8) return false;
            }
        return true;
    }
}

public static class ForegroundWindow
{
    public static nint Handle => Native.GetForegroundWindow();

    /// <summary>Границы окна без тени (DWM extended frame bounds), в физических пикселях.</summary>
    public static Rectangle? Bounds(nint hwnd)
    {
        if (hwnd == 0) return null;
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<Native.RECT>()) == 0)
            return r.ToRectangle();
        return Native.GetWindowRect(hwnd, out var wr) ? wr.ToRectangle() : null;
    }

    public static string Title(nint hwnd)
    {
        if (hwnd == 0) return "";
        var len = Native.GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        Native.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
