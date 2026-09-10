using System.Windows;

namespace Kadr.App.Editor;

/// <summary>Размещение панелей редактора рядом с областью: снаружи, без пересечений, иначе внутри.</summary>
public static class PanelPlacer
{
    private const double Gap = 6;

    public static (Point Toolbar, Point Action) Place(Rect area, Size toolbar, Size action, Size window)
    {
        var bounds = new Rect(0, 0, window.Width, window.Height);

        // Панель инструментов: под областью справа/слева, над областью справа/слева, затем внутри снизу.
        var toolbarCandidates = new[]
        {
            new Point(area.Right - toolbar.Width, area.Bottom + Gap),
            new Point(area.Left, area.Bottom + Gap),
            new Point(area.Right - toolbar.Width, area.Top - Gap - toolbar.Height),
            new Point(area.Left, area.Top - Gap - toolbar.Height),
            new Point(area.Right - toolbar.Width - Gap, area.Bottom - toolbar.Height - Gap),
            new Point(area.Left + Gap, area.Bottom - toolbar.Height - Gap),
        };
        var toolbarPos = ClampInside(Pick(toolbarCandidates, toolbar, bounds, null), toolbar, bounds);
        var toolbarRect = new Rect(toolbarPos, toolbar);

        // Панель действий: справа сверху/снизу, слева сверху/снизу, затем внутри справа сверху.
        var actionCandidates = new[]
        {
            new Point(area.Right + Gap, area.Top),
            new Point(area.Right + Gap, area.Bottom - action.Height),
            new Point(area.Left - Gap - action.Width, area.Top),
            new Point(area.Left - Gap - action.Width, area.Bottom - action.Height),
            new Point(area.Right - action.Width - Gap, area.Top + Gap),
            new Point(area.Left + Gap, area.Top + Gap),
        };
        var actionPos = ClampInside(Pick(actionCandidates, action, bounds, toolbarRect), action, bounds);
        return (toolbarPos, actionPos);
    }

    private static Point Pick(Point[] candidates, Size size, Rect bounds, Rect? avoid)
    {
        foreach (var p in candidates)
        {
            var r = new Rect(p, size);
            if (!bounds.Contains(r)) continue;
            if (avoid is { } a && r.IntersectsWith(a)) continue;
            return p;
        }
        return candidates[^1];
    }

    private static Point ClampInside(Point p, Size size, Rect bounds)
    {
        var x = Math.Max(bounds.Left, Math.Min(p.X, bounds.Right - size.Width));
        var y = Math.Max(bounds.Top, Math.Min(p.Y, bounds.Bottom - size.Height));
        return new Point(Math.Round(x), Math.Round(y));
    }
}
