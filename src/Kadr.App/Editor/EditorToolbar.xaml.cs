using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Kadr.Common.Settings;

namespace Kadr.App.Editor;

public partial class EditorToolbar : UserControl
{
    private readonly Dictionary<DrawingTool, ToggleButton> _buttons;
    private bool _updating;

    public event Action<DrawingTool>? ToolClicked;        // клик по кнопке: активный инструмент снимается
    public event Action<double>? ThicknessChanged;
    public event Action<Color>? ColorChanged;
    public event Action? CustomColorRequested;
    public event Action? UndoRequested;

    public EditorToolbar()
    {
        InitializeComponent();
        _buttons = new()
        {
            [DrawingTool.Arrow] = BtnArrow, [DrawingTool.Line] = BtnLine, [DrawingTool.Pensil] = BtnPensil,
            [DrawingTool.Marker] = BtnMarker, [DrawingTool.Rectangle] = BtnRectangle, [DrawingTool.Oval] = BtnOval,
            [DrawingTool.Text] = BtnText, [DrawingTool.Number] = BtnNumber, [DrawingTool.Blur] = BtnBlur,
        };
    }

    public void SetTool(DrawingTool tool)
    {
        _updating = true;
        foreach (var (t, b) in _buttons) b.IsChecked = t == tool;
        _updating = false;
    }

    public void SetThickness(double thickness)
    {
        _updating = true;
        ThicknessSlider.Value = thickness;
        ThicknessLabel.Text = $"{thickness:0}px";
        ThicknessPreview.Height = Math.Min(thickness, 30);
        ThicknessPreview.RadiusX = ThicknessPreview.RadiusY = ThicknessPreview.Height / 2;
        _updating = false;
    }

    public void SetColor(Color color)
    {
        ColorDot.Fill = new SolidColorBrush(color);
        ThicknessPreview.Fill = new SolidColorBrush(color);
    }

    public void SetPalette(IReadOnlyList<Color> palette, Color selected)
    {
        PaletteGrid.Children.Clear();
        foreach (var c in palette)
        {
            var dot = new Ellipse
            {
                Width = 28, Height = 28, Margin = new Thickness(4), Cursor = System.Windows.Input.Cursors.Hand,
                Fill = new SolidColorBrush(c),
                Stroke = c == selected ? (Brush)FindResource("AccentBrush") : new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
                StrokeThickness = c == selected ? 3 : 1,
            };
            var color = c;
            dot.MouseLeftButtonUp += (_, _) => { ColorPopup.IsOpen = false; ColorChanged?.Invoke(color); };
            PaletteGrid.Children.Add(dot);
        }
    }

    public void SetCanUndo(bool canUndo) => BtnUndo.IsEnabled = canUndo;

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        var tool = Enum.Parse<DrawingTool>((string)((ToggleButton)sender).Tag);
        ToolClicked?.Invoke(tool);
    }

    private void Thickness_Click(object sender, RoutedEventArgs e) => ThicknessPopup.IsOpen = !ThicknessPopup.IsOpen;
    private void Color_Click(object sender, RoutedEventArgs e) => ColorPopup.IsOpen = !ColorPopup.IsOpen;
    private void Undo_Click(object sender, RoutedEventArgs e) => UndoRequested?.Invoke();

    private void CustomColor_Click(object sender, RoutedEventArgs e)
    {
        ColorPopup.IsOpen = false;
        CustomColorRequested?.Invoke();
    }

    private void ThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        var v = Math.Round(e.NewValue);
        ThicknessLabel.Text = $"{v:0}px";
        ThicknessPreview.Height = Math.Min(v, 30);
        ThicknessPreview.RadiusX = ThicknessPreview.RadiusY = ThicknessPreview.Height / 2;
        ThicknessChanged?.Invoke(v);
    }
}
