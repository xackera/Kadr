using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Kadr.Common.Settings;

namespace Kadr.App.Editor.Objects;

/// <summary>Линия по двум точкам. Shift ограничивает горизонталью/вертикалью.</summary>
public class LineObject : VisualObject
{
    private static readonly HandleKind[] TwoHandles = { HandleKind.Start, HandleKind.End };

    protected readonly Path Path = new() { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };

    public Point Start { get; protected set; }
    public Point End { get; protected set; }

    public override DrawingTool Tool => DrawingTool.Line;
    public override FrameworkElement View => Path;
    public override IReadOnlyList<HandleKind> Handles => TwoHandles;

    public override Rect Bounds
    {
        get
        {
            var r = new Rect(Start, End);
            r.Inflate(Thickness / 2, Thickness / 2);
            return r;
        }
    }

    public override Point HandlePosition(HandleKind handle) => handle == HandleKind.Start ? Start : End;

    public override bool HitTest(Point p)
    {
        // расстояние от точки до отрезка
        var v = End - Start; var w = p - Start;
        var len2 = v.LengthSquared;
        double t = len2 < 1e-6 ? 0 : Math.Clamp((w.X * v.X + w.Y * v.Y) / len2, 0, 1);
        var proj = Start + v * t;
        return (p - proj).Length <= Math.Max(6, Thickness / 2 + 3);
    }

    public override void Offset(Vector delta) { Start += delta; End += delta; Render(); }

    public override void Render()
    {
        Path.Stroke = new SolidColorBrush(Color);
        Path.StrokeThickness = Thickness;
        Path.Data = BuildGeometry();
        ApplyShadow(Path);
    }

    protected virtual Geometry BuildGeometry() => new LineGeometry(Start, End);

    public override object CaptureState() => (Start, End, Color, Thickness);
    public override void RestoreState(object state) { var (s, e, c, t) = ((Point, Point, Color, double))state; Start = s; End = e; Color = c; Thickness = t; Render(); }

    public override void BeginDraw(Point p) { Start = End = p; Render(); }
    public override void ContinueDraw(Point p, bool constrain) { End = constrain ? Constrain(Start, p) : p; Render(); }
    public override bool EndDraw() => (End - Start).Length >= 2;

    public override void Resize(HandleKind handle, Point p, bool constrain, object startState)
    {
        var (s, e, _, _) = ((Point, Point, Color, double))startState;
        if (handle == HandleKind.Start) { Start = constrain ? Constrain(e, p) : p; End = e; }
        else { End = constrain ? Constrain(s, p) : p; Start = s; }
        Render();
    }
}

/// <summary>Стрелка: линия с головкой, масштаб головки от толщины.</summary>
public sealed class ArrowObject : LineObject
{
    public override DrawingTool Tool => DrawingTool.Arrow;

    protected override Geometry BuildGeometry()
    {
        var v = End - Start;
        var len = v.Length;
        double scale = 0.5 + (Thickness - MinThickness) * 9.5 / (MaxThickness - MinThickness);   // 0.5..10
        double headLen = 16 * scale + 4, headWidth = 9 * scale + 3;
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        if (len < 1e-3) { g.Children.Add(new EllipseGeometry(Start, Thickness / 2, Thickness / 2)); return g; }
        var dir = v / len;
        var normal = new Vector(-dir.Y, dir.X);
        var tip = End;
        var basePoint = End - dir * headLen;
        // древко до основания головки (если стрелка достаточно длинная)
        if (len > headLen * 0.8) g.Children.Add(new LineGeometry(Start, basePoint + dir * (Thickness * 0.3)));
        var head = new PathFigure { StartPoint = tip, IsClosed = true, IsFilled = true };
        head.Segments.Add(new LineSegment(basePoint + normal * headWidth, true));
        head.Segments.Add(new LineSegment(basePoint - normal * headWidth, true));
        var headGeom = new PathGeometry(new[] { head });
        g.Children.Add(headGeom);
        return g;
    }

    public override void Render()
    {
        base.Render();
        Path.Fill = new SolidColorBrush(Color);
    }
}
