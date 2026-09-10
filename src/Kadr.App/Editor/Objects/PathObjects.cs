using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Kadr.Common.Settings;

namespace Kadr.App.Editor.Objects;

/// <summary>Карандаш: свободная линия со сглаживанием после завершения. Ресайз масштабирует точки.</summary>
public class PencilObject : VisualObject
{
    private static readonly HandleKind[] EightHandles =
    {
        HandleKind.TopLeft, HandleKind.Top, HandleKind.TopRight, HandleKind.Right,
        HandleKind.BottomRight, HandleKind.Bottom, HandleKind.BottomLeft, HandleKind.Left,
    };

    protected readonly Path Path = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
    protected List<Point> Points = new();
    private Point _origin;
    private int? _axis; // 0 = горизонталь, 1 = вертикаль

    public override DrawingTool Tool => DrawingTool.Pensil;
    public override FrameworkElement View => Path;
    public override IReadOnlyList<HandleKind> Handles => EightHandles;

    protected virtual double EffectiveThickness => Thickness;

    public override Rect Bounds
    {
        get
        {
            if (Points.Count == 0) return Rect.Empty;
            double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
            foreach (var p in Points) { l = Math.Min(l, p.X); t = Math.Min(t, p.Y); r = Math.Max(r, p.X); b = Math.Max(b, p.Y); }
            var rect = new Rect(new Point(l, t), new Point(r, b));
            rect.Inflate(EffectiveThickness / 2, EffectiveThickness / 2);
            return rect;
        }
    }

    public override bool HitTest(Point p)
    {
        var tol = Math.Max(6, EffectiveThickness / 2 + 3);
        for (int i = 1; i < Points.Count; i++)
        {
            var a = Points[i - 1]; var b = Points[i];
            var v = b - a; var w = p - a;
            var len2 = v.LengthSquared;
            double t = len2 < 1e-6 ? 0 : Math.Clamp((w.X * v.X + w.Y * v.Y) / len2, 0, 1);
            if ((p - (a + v * t)).Length <= tol) return true;
        }
        return Points.Count == 1 && (p - Points[0]).Length <= tol;
    }

    public override void Offset(Vector delta) { for (int i = 0; i < Points.Count; i++) Points[i] += delta; Render(); }

    public override void Render()
    {
        Path.Stroke = new SolidColorBrush(Color);
        Path.StrokeThickness = EffectiveThickness;
        Path.Data = BuildGeometry();
        ApplyShadow(Path);
    }

    private Geometry BuildGeometry()
    {
        if (Points.Count == 0) return Geometry.Empty;
        if (Points.Count == 1) return new EllipseGeometry(Points[0], 0.1, 0.1);
        var figure = new PathFigure { StartPoint = Points[0], IsClosed = false, IsFilled = false };
        figure.Segments.Add(new PolyLineSegment(Points.Skip(1), true));
        return new PathGeometry(new[] { figure });
    }

    public override object CaptureState() => (Points.ToList(), Color, Thickness);
    public override void RestoreState(object state) { var (pts, c, t) = ((List<Point>, Color, double))state; Points = pts.ToList(); Color = c; Thickness = t; Render(); }

    public override void BeginDraw(Point p) { _origin = p; _axis = null; Points = new List<Point> { p }; Render(); }

    public override void ContinueDraw(Point p, bool constrain)
    {
        if (constrain)
        {
            if (_axis is null && (p - _origin).Length >= 2) _axis = Math.Abs(p.X - _origin.X) >= Math.Abs(p.Y - _origin.Y) ? 0 : 1;
            if (_axis == 0) p = new Point(p.X, _origin.Y); else if (_axis == 1) p = new Point(_origin.X, p.Y);
        }
        else _axis = null;
        if ((p - Points[^1]).Length >= 1.5) { Points.Add(p); Render(); }
    }

    public override bool EndDraw()
    {
        if (Points.Count < 2) return false;
        Points = Smooth(Points);
        Render();
        return true;
    }

    public override void Resize(HandleKind handle, Point p, bool constrain, object startState)
    {
        var (pts, _, _) = ((List<Point>, Color, double))startState;
        if (pts.Count == 0) return;
        double l = pts.Min(q => q.X), t = pts.Min(q => q.Y), r = pts.Max(q => q.X), b = pts.Max(q => q.Y);
        var start = new Rect(new Point(l, t), new Point(r, b));
        var target = ResizeRect(start, handle, p, constrain);
        double sx = start.Width < 1e-6 ? 1 : target.Width / start.Width;
        double sy = start.Height < 1e-6 ? 1 : target.Height / start.Height;
        Points = pts.Select(q => new Point(target.X + (q.X - start.X) * sx, target.Y + (q.Y - start.Y) * sy)).ToList();
        Render();
    }

    /// <summary>Отбросить близкие точки, одна итерация Чайкина, затем Catmull-Rom → плотная полилиния.</summary>
    private static List<Point> Smooth(List<Point> src)
    {
        var filtered = new List<Point> { src[0] };
        foreach (var p in src.Skip(1)) if ((p - filtered[^1]).Length >= 6) filtered.Add(p);
        if ((src[^1] - filtered[^1]).Length > 0.5) filtered.Add(src[^1]);
        if (filtered.Count < 3) return filtered;

        var chaikin = new List<Point> { filtered[0] };
        for (int i = 0; i < filtered.Count - 1; i++)
        {
            var a = filtered[i]; var b = filtered[i + 1];
            chaikin.Add(a + (b - a) * 0.25);
            chaikin.Add(a + (b - a) * 0.75);
        }
        chaikin.Add(filtered[^1]);

        var result = new List<Point>();
        for (int i = 0; i < chaikin.Count - 1; i++)
        {
            var p0 = chaikin[Math.Max(i - 1, 0)]; var p1 = chaikin[i]; var p2 = chaikin[i + 1]; var p3 = chaikin[Math.Min(i + 2, chaikin.Count - 1)];
            int steps = Math.Clamp((int)((p2 - p1).Length / 3), 1, 12);
            for (int s = 0; s < steps; s++)
            {
                double u = (double)s / steps, u2 = u * u, u3 = u2 * u;
                double x = 0.5 * (2 * p1.X + (-p0.X + p2.X) * u + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * u2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * u3);
                double y = 0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * u + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * u2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * u3);
                result.Add(new Point(x, y));
            }
        }
        result.Add(chaikin[^1]);
        return result;
    }
}

/// <summary>Маркер: карандаш с тройной толщиной и прозрачностью 0.4.</summary>
public sealed class MarkerObject : PencilObject
{
    public override DrawingTool Tool => DrawingTool.Marker;
    protected override double EffectiveThickness => Thickness * 3;

    public override void Render()
    {
        base.Render();
        Path.Opacity = 0.4;
        Path.Effect = null;
    }
}
