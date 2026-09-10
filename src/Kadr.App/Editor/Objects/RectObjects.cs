using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Kadr.Common.Settings;

namespace Kadr.App.Editor.Objects;

/// <summary>Объект, ограниченный прямоугольником: 8 ручек, Shift даёт квадрат.</summary>
public abstract class RectObject : VisualObject
{
    private static readonly HandleKind[] EightHandles =
    {
        HandleKind.TopLeft, HandleKind.Top, HandleKind.TopRight, HandleKind.Right,
        HandleKind.BottomRight, HandleKind.Bottom, HandleKind.BottomLeft, HandleKind.Left,
    };

    protected readonly Path Path = new() { StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
    private Point _anchor;

    public Rect Rect { get; protected set; }

    public override FrameworkElement View => Path;
    public override IReadOnlyList<HandleKind> Handles => EightHandles;
    public override Rect Bounds => Rect;

    public override void Offset(Vector delta) { Rect = Rect.IsEmpty ? Rect : new Rect(Rect.TopLeft + delta, Rect.Size); Render(); }

    public override object CaptureState() => (Rect, Color, Thickness);
    public override void RestoreState(object state) { var (r, c, t) = ((Rect, Color, double))state; Rect = r; Color = c; Thickness = t; Render(); }

    public override void BeginDraw(Point p) { _anchor = p; Rect = new Rect(p, new Size(0, 0)); Render(); }
    public override void ContinueDraw(Point p, bool constrain) { Rect = RectFromPoints(_anchor, p, constrain); Render(); }
    public override bool EndDraw() => Rect.Width >= 2 && Rect.Height >= 2;

    public override void Resize(HandleKind handle, Point p, bool constrain, object startState)
    {
        var (r, _, _) = ((Rect, Color, double))startState;
        Rect = ResizeRect(r, handle, p, constrain);
        Render();
    }
}

public sealed class RectangleObject : RectObject
{
    public override DrawingTool Tool => DrawingTool.Rectangle;

    public override bool HitTest(Point p)
    {
        var outer = Rect; outer.Inflate(Thickness / 2 + 4, Thickness / 2 + 4);
        var inner = Rect; inner.Inflate(-Thickness / 2 - 4, -Thickness / 2 - 4);
        return outer.Contains(p) && !(inner.Width > 0 && inner.Height > 0 && inner.Contains(p));
    }

    public override void Render()
    {
        Path.Stroke = new SolidColorBrush(Color);
        Path.StrokeThickness = Thickness;
        var radius = Math.Min(Thickness * 0.6, Math.Min(Rect.Width, Rect.Height) / 4);
        Path.Data = new RectangleGeometry(Rect, radius, radius);
        ApplyShadow(Path);
    }
}

public sealed class OvalObject : RectObject
{
    public override DrawingTool Tool => DrawingTool.Oval;

    public override void Render()
    {
        Path.Stroke = new SolidColorBrush(Color);
        Path.StrokeThickness = Thickness;
        Path.Data = new EllipseGeometry(Rect);
        ApplyShadow(Path);
    }
}

/// <summary>Размытие: прямоугольник, залитый заранее размытым снимком экрана. Всегда под остальными объектами.</summary>
public sealed class BlurObject : RectObject
{
    private readonly BitmapSource _blurred;
    private readonly Size _surface;

    public BlurObject(BitmapSource blurred, Size surfaceSize)
    {
        _blurred = blurred;
        _surface = surfaceSize;
        Path.IsHitTestVisible = false;
    }

    public override DrawingTool Tool => DrawingTool.Blur;

    public override void Render()
    {
        Path.Stroke = null;
        Path.Data = new RectangleGeometry(Rect);
        if (Rect.Width <= 0 || Rect.Height <= 0 || _surface.Width <= 0) { Path.Fill = null; return; }
        Path.Fill = new ImageBrush(_blurred)
        {
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = new Rect(Rect.X / _surface.Width, Rect.Y / _surface.Height, Rect.Width / _surface.Width, Rect.Height / _surface.Height),
            Stretch = Stretch.Fill,
        };
        Path.Effect = null;
    }
}
