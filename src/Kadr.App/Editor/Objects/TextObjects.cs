using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Kadr.Common.Settings;

namespace Kadr.App.Editor.Objects;

/// <summary>Текст: многострочное поле, размер шрифта от толщины (14..130), тень.</summary>
public sealed class TextObject : VisualObject
{
    private readonly Grid _root = new();
    private readonly TextBox _box;
    private readonly TextBlock _block;
    private bool _editing;

    public Point Position { get; private set; }
    public string Text { get; private set; } = "";

    public event Action<TextObject>? EditEnded;

    public TextObject()
    {
        _box = new TextBox
        {
            AcceptsReturn = true, AcceptsTab = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(2), MinWidth = 24, FontFamily = new FontFamily("Arial"), FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.NoWrap,
        };
        _block = new TextBlock { FontFamily = new FontFamily("Arial"), FontWeight = FontWeights.Bold, Padding = new Thickness(2), IsHitTestVisible = false };
        TextOptions.SetTextRenderingMode(_box, TextRenderingMode.ClearType);
        TextOptions.SetTextRenderingMode(_block, TextRenderingMode.ClearType);
        _root.Children.Add(_block);
        _root.Children.Add(_box);
        _root.IsHitTestVisible = false;
        _box.LostKeyboardFocus += (_, _) => { if (_editing) EndEdit(); };
        _box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { EndEdit(); e.Handled = true; } };
        _box.TextChanged += (_, _) => { Text = _box.Text; };
    }

    public override DrawingTool Tool => DrawingTool.Text;
    public override FrameworkElement View => _root;
    public override IReadOnlyList<HandleKind> Handles => Array.Empty<HandleKind>();
    public bool IsEditing => _editing;

    public double FontSize => Math.Round(14 + 116 * Math.Pow(Math.Clamp((Thickness - MinThickness) / (MaxThickness - MinThickness), 0, 1), 1.5));

    public override Rect Bounds
    {
        get
        {
            _root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = _root.DesiredSize;
            return new Rect(Position, new Size(Math.Max(size.Width, 24), Math.Max(size.Height, FontSize * 1.3)));
        }
    }

    public override bool HitTest(Point p) => Bounds.Contains(p);
    public override void Offset(Vector delta) { Position += delta; Render(); }

    public override void Render()
    {
        var brush = new SolidColorBrush(Color);
        _box.Foreground = brush; _block.Foreground = brush;
        _box.FontSize = FontSize; _block.FontSize = FontSize;
        _box.CaretBrush = brush;
        _block.Text = Text;
        _block.Visibility = _editing ? Visibility.Hidden : Visibility.Visible;
        _box.Visibility = _editing ? Visibility.Visible : Visibility.Hidden;
        Canvas.SetLeft(_root, Position.X);
        Canvas.SetTop(_root, Position.Y);
        ApplyShadow(_block);
    }

    public override object CaptureState() => (Position, Text, Color, Thickness);
    public override void RestoreState(object state)
    {
        var (p, t, c, th) = ((Point, string, Color, double))state;
        Position = p; Text = t; _box.Text = t; Color = c; Thickness = th; Render();
    }

    public override void BeginDraw(Point p) { Position = new Point(p.X, p.Y - FontSize * 0.65); Render(); }
    public override void ContinueDraw(Point p, bool constrain) { }
    public override bool EndDraw() => true; // редактирование начинается после размещения
    public override void Resize(HandleKind handle, Point p, bool constrain, object startState) { }

    public void BeginEdit()
    {
        _editing = true;
        _root.IsHitTestVisible = true;
        Render();
        _box.Dispatcher.BeginInvoke(() => { _box.Focus(); _box.CaretIndex = _box.Text.Length; }, System.Windows.Threading.DispatcherPriority.Input);
    }

    public void EndEdit()
    {
        if (!_editing) return;
        _editing = false;
        _root.IsHitTestVisible = false;
        Text = _box.Text;
        Render();
        EditEnded?.Invoke(this);
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}

/// <summary>Числовая метка: круг с номером. Номер = наименьший незанятый.</summary>
public sealed class NumberObject : VisualObject
{
    private readonly Grid _root = new() { IsHitTestVisible = false };
    private readonly Ellipse _circle = new();
    private readonly TextBlock _label = new() { FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public Point Center { get; private set; }
    public int Number { get; set; } = 1;

    public NumberObject()
    {
        _root.Children.Add(_circle);
        _root.Children.Add(_label);
    }

    public override DrawingTool Tool => DrawingTool.Number;
    public override FrameworkElement View => _root;
    public override IReadOnlyList<HandleKind> Handles => Array.Empty<HandleKind>();

    private double Radius => Thickness + 8;

    public override Rect Bounds => new(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
    public override bool HitTest(Point p) => (p - Center).Length <= Radius + 3;
    public override void Offset(Vector delta) { Center += delta; Render(); }

    public override void Render()
    {
        var brush = new SolidColorBrush(Color);
        var d = Radius * 2;
        _root.Width = d; _root.Height = d;
        _circle.Width = d; _circle.Height = d;
        _circle.Fill = Brushes.White;
        _circle.Stroke = brush;
        _circle.StrokeThickness = Thickness / 3 + 2;
        _label.Text = Number.ToString();
        _label.FontSize = Thickness + 10;
        _label.Foreground = brush;
        Canvas.SetLeft(_root, Center.X - Radius);
        Canvas.SetTop(_root, Center.Y - Radius);
        ApplyShadow(_circle);
    }

    public override object CaptureState() => (Center, Number, Color, Thickness);
    public override void RestoreState(object state) { var (c, n, col, t) = ((Point, int, Color, double))state; Center = c; Number = n; Color = col; Thickness = t; Render(); }

    public override void BeginDraw(Point p) { Center = p; Render(); }
    public override void ContinueDraw(Point p, bool constrain) { Center = p; Render(); }
    public override bool EndDraw() => true;
    public override void Resize(HandleKind handle, Point p, bool constrain, object startState) { }
}
