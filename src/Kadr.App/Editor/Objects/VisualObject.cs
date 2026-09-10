using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Kadr.Common.Settings;

namespace Kadr.App.Editor.Objects;

public enum HandleKind { None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Start, End }

/// <summary>Базовый объект редактора: модель + WPF-элемент на канве объектов. Координаты в DIP окна оверлея.</summary>
public abstract class VisualObject
{
    public const double MinThickness = 2, MaxThickness = 60;

    protected static readonly DropShadowEffect ShadowEffect = Frozen(new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.45, Direction = 315 });

    private Color _color = Colors.Red;
    private double _thickness = 6;
    private bool _shadow;

    public abstract DrawingTool Tool { get; }
    public abstract FrameworkElement View { get; }

    public Color Color { get => _color; set { _color = value; Render(); } }
    public double Thickness { get => _thickness; set { _thickness = Math.Clamp(value, MinThickness, MaxThickness); Render(); } }
    public bool Shadow { get => _shadow; set { _shadow = value; Render(); } }

    /// <summary>Границы для выделения и хит-теста.</summary>
    public abstract Rect Bounds { get; }
    public abstract IReadOnlyList<HandleKind> Handles { get; }
    public virtual bool CanResize => Handles.Count > 0;

    public abstract void Offset(Vector delta);
    public abstract void Render();
    public abstract object CaptureState();
    public abstract void RestoreState(object state);

    // Рисование
    public abstract void BeginDraw(Point p);
    public abstract void ContinueDraw(Point p, bool constrain);
    /// <summary>Возвращает false, если объект пустой и должен быть удалён.</summary>
    public abstract bool EndDraw();

    /// <summary>Изменение размера за ручку относительно состояния на момент начала.</summary>
    public abstract void Resize(HandleKind handle, Point p, bool constrain, object startState);

    public virtual Point HandlePosition(HandleKind handle)
    {
        var b = Bounds;
        return handle switch
        {
            HandleKind.TopLeft => b.TopLeft,
            HandleKind.Top => new Point(b.Left + b.Width / 2, b.Top),
            HandleKind.TopRight => b.TopRight,
            HandleKind.Right => new Point(b.Right, b.Top + b.Height / 2),
            HandleKind.BottomRight => b.BottomRight,
            HandleKind.Bottom => new Point(b.Left + b.Width / 2, b.Bottom),
            HandleKind.BottomLeft => b.BottomLeft,
            HandleKind.Left => new Point(b.Left, b.Top + b.Height / 2),
            _ => b.TopLeft,
        };
    }

    public virtual bool HitTest(Point p)
    {
        var b = Bounds;
        b.Inflate(Math.Max(4, Thickness / 2), Math.Max(4, Thickness / 2));
        return b.Contains(p);
    }

    protected void ApplyShadow(UIElement element) => element.Effect = Shadow ? ShadowEffect : null;

    protected static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    protected static Point Constrain(Point origin, Point p)
    {
        var dx = p.X - origin.X; var dy = p.Y - origin.Y;
        return Math.Abs(dx) >= Math.Abs(dy) ? new Point(p.X, origin.Y) : new Point(origin.X, p.Y);
    }

    protected static Rect RectFromPoints(Point a, Point b, bool square)
    {
        var w = b.X - a.X; var h = b.Y - a.Y;
        if (square)
        {
            var s = Math.Min(Math.Abs(w), Math.Abs(h));
            w = Math.Sign(w) * s; h = Math.Sign(h) * s;
        }
        return new Rect(new Point(Math.Min(a.X, a.X + w), Math.Min(a.Y, a.Y + h)), new Size(Math.Abs(w), Math.Abs(h)));
    }

    /// <summary>Общий ресайз прямоугольника за одну из 8 ручек.</summary>
    protected static Rect ResizeRect(Rect start, HandleKind handle, Point p, bool square)
    {
        double l = start.Left, t = start.Top, r = start.Right, b = start.Bottom;
        switch (handle)
        {
            case HandleKind.TopLeft: l = p.X; t = p.Y; break;
            case HandleKind.Top: t = p.Y; break;
            case HandleKind.TopRight: r = p.X; t = p.Y; break;
            case HandleKind.Right: r = p.X; break;
            case HandleKind.BottomRight: r = p.X; b = p.Y; break;
            case HandleKind.Bottom: b = p.Y; break;
            case HandleKind.BottomLeft: l = p.X; b = p.Y; break;
            case HandleKind.Left: l = p.X; break;
        }
        if (square && handle is HandleKind.TopLeft or HandleKind.TopRight or HandleKind.BottomLeft or HandleKind.BottomRight)
        {
            var w = r - l; var h = b - t;
            var s = Math.Min(Math.Abs(w), Math.Abs(h));
            if (handle is HandleKind.TopLeft or HandleKind.BottomLeft) l = r - Math.Sign(w) * s; else r = l + Math.Sign(w) * s;
            if (handle is HandleKind.TopLeft or HandleKind.TopRight) t = b - Math.Sign(h) * s; else b = t + Math.Sign(h) * s;
        }
        return new Rect(new Point(Math.Min(l, r), Math.Min(t, b)), new Point(Math.Max(l, r), Math.Max(t, b)));
    }
}
