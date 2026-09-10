namespace Kadr.Capture;

/// <summary>Монитор: границы в физических пикселях и масштаб DPI.</summary>
public sealed record MonitorInfo(nint Handle, string DeviceName, Rectangle Bounds, Rectangle WorkArea, bool IsPrimary, uint Dpi)
{
    public double Scale => Dpi / 96.0;
    public override string ToString() => $"{DeviceName} ({Bounds.X},{Bounds.Y})[{Bounds.Width}x{Bounds.Height}]{{{Scale:P0}}}";
}

public static class Monitors
{
    public static IReadOnlyList<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(0, 0, (nint h, nint _, ref Native.RECT _, nint _) =>
        {
            var info = new Native.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (Native.GetMonitorInfo(h, ref info))
            {
                uint dpi = 96;
                if (Native.GetDpiForMonitor(h, 0, out var dx, out _) == 0) dpi = dx;
                list.Add(new MonitorInfo(h, info.szDevice, info.rcMonitor.ToRectangle(), info.rcWork.ToRectangle(),
                    (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0, dpi));
            }
            return true;
        }, 0);
        return list;
    }

    public static MonitorInfo? FromPoint(Point p)
    {
        var h = Native.MonitorFromPoint(new Native.POINT { X = p.X, Y = p.Y }, Native.MONITOR_DEFAULTTONEAREST);
        return All().FirstOrDefault(m => m.Handle == h);
    }

    public static MonitorInfo? FromWindow(nint hwnd)
    {
        var h = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTOPRIMARY);
        return All().FirstOrDefault(m => m.Handle == h);
    }

    public static MonitorInfo? Primary() => All().FirstOrDefault(m => m.IsPrimary);

    /// <summary>Объединённый прямоугольник всех мониторов.</summary>
    public static Rectangle VirtualScreen()
    {
        var all = All();
        if (all.Count == 0) return Rectangle.Empty;
        var r = all[0].Bounds;
        foreach (var m in all.Skip(1)) r = Rectangle.Union(r, m.Bounds);
        return r;
    }
}
