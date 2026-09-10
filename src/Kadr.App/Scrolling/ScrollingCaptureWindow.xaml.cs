using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kadr.App.Editor;
using Kadr.Capture;

namespace Kadr.App.Scrolling;

/// <summary>
/// Окно на весь монитор во время захвата с прокруткой: затемнение вокруг области (область прозрачна и пропускает мышь),
/// рамка, стартовая панель, панель действий, превью, сообщение. Исключено из захвата.
/// </summary>
public partial class ScrollingCaptureWindow : Window
{
    private const double PreviewMaxWidth = 192;

    private readonly MonitorInfo _monitor;
    private readonly System.Drawing.Rectangle _region;
    private double _scale = 1;
    private Rect _regionDip;

    public event Action? StartRequested;
    public event Action<EditorActionKind>? ActionRequested;

    public ScrollingCaptureWindow(System.Drawing.Rectangle region, MonitorInfo monitor)
    {
        _region = region;
        _monitor = monitor;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.AddExStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW);
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            var b = monitor.Bounds;
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, NativeMethods.SWP_SHOWWINDOW);
        };
        Loaded += (_, _) =>
        {
            var src = PresentationSource.FromVisual(this);
            _scale = src?.CompositionTarget?.TransformToDevice.M11 ?? monitor.Scale;
            Layout();
            Root.Focus();
            Keyboard.Focus(Root);
        };
        PreviewKeyDown += OnKeyDown;
        Root.Focusable = true;
    }

    private void Layout()
    {
        var b = _monitor.Bounds;
        double w = b.Width / _scale, h = b.Height / _scale;
        _regionDip = new Rect((_region.X - b.X) / _scale, (_region.Y - b.Y) / _scale, _region.Width / _scale, _region.Height / _scale);
        Root.Width = w; Root.Height = h;
        // Затемнение четырьмя прямоугольниками вокруг области; сама область прозрачна и пропускает мышь.
        SetRect(DimTop, 0, 0, w, Math.Max(0, _regionDip.Y));
        SetRect(DimBottom, 0, _regionDip.Bottom, w, Math.Max(0, h - _regionDip.Bottom));
        SetRect(DimLeft, 0, _regionDip.Y, Math.Max(0, _regionDip.X), _regionDip.Height);
        SetRect(DimRight, _regionDip.Right, _regionDip.Y, Math.Max(0, w - _regionDip.Right), _regionDip.Height);
        System.Windows.Controls.Canvas.SetLeft(Frame, _regionDip.X - 2); System.Windows.Controls.Canvas.SetTop(Frame, _regionDip.Y - 2);
        Frame.Width = _regionDip.Width + 4; Frame.Height = _regionDip.Height + 4;
        PlaceBottomPanel(StartPanel);
        PlaceBottomPanel(ActionPanel);
        PlacePreview();
        PlaceMessage();
    }

    private static void SetRect(FrameworkElement e, double x, double y, double w, double h)
    {
        System.Windows.Controls.Canvas.SetLeft(e, x);
        System.Windows.Controls.Canvas.SetTop(e, y);
        e.Width = w;
        e.Height = h;
    }

    private void PlaceBottomPanel(FrameworkElement panel)
    {
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pw = panel.DesiredSize.Width, ph = panel.DesiredSize.Height;
        double x = _regionDip.X + (_regionDip.Width - pw) / 2;
        double y = _regionDip.Bottom + 6;
        if (y + ph > Root.Height) y = _regionDip.Y - 6 - ph;
        if (y < 0) y = _regionDip.Bottom - ph - 6;
        x = Math.Clamp(x, 0, Math.Max(0, Root.Width - pw));
        System.Windows.Controls.Canvas.SetLeft(panel, x); System.Windows.Controls.Canvas.SetTop(panel, y);
    }

    private void PlacePreview()
    {
        double pw = PreviewMaxWidth + 6;
        double maxH = Math.Max(60, _regionDip.Height - 16);
        PreviewPanel.Width = pw;
        PreviewPanel.MaxHeight = maxH;
        double x = _regionDip.Right + 8;
        if (x + pw > Root.Width) x = _regionDip.X - 8 - pw;
        if (x < 0) x = _regionDip.Right - pw - 8;
        System.Windows.Controls.Canvas.SetLeft(PreviewPanel, x);
        System.Windows.Controls.Canvas.SetTop(PreviewPanel, _regionDip.Y + 8);
    }

    private void PlaceMessage()
    {
        Message.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        System.Windows.Controls.Canvas.SetLeft(Message, _regionDip.X + (_regionDip.Width - Message.DesiredSize.Width) / 2);
        System.Windows.Controls.Canvas.SetTop(Message, _regionDip.Y + (_regionDip.Height - Message.DesiredSize.Height) / 2);
    }

    public void SetCapturing()
    {
        StartPanel.Visibility = Visibility.Collapsed;
        ActionPanel.Visibility = Visibility.Visible;
        PreviewPanel.Visibility = Visibility.Visible;
    }

    public void UpdatePreview(BitmapSource image)
    {
        Preview.Source = image;
        double k = Math.Min(1, PreviewMaxWidth / image.PixelWidth);
        Preview.Width = image.PixelWidth * k;
        Preview.Height = Math.Min(image.PixelHeight * k, PreviewPanel.MaxHeight - 6);
    }

    public void ShowMessage(string? text)
    {
        if (string.IsNullOrEmpty(text)) { Message.Visibility = Visibility.Collapsed; return; }
        MessageText.Text = text;
        Message.Visibility = Visibility.Visible;
        PlaceMessage();
    }

    public bool IsPointOverPanels(System.Drawing.Point screenPoint)
    {
        var p = new Point((screenPoint.X - _monitor.Bounds.X) / _scale, (screenPoint.Y - _monitor.Bounds.Y) / _scale);
        foreach (var panel in new FrameworkElement[] { StartPanel, ActionPanel, PreviewPanel })
        {
            if (panel.Visibility != Visibility.Visible) continue;
            var r = new Rect(System.Windows.Controls.Canvas.GetLeft(panel), System.Windows.Controls.Canvas.GetTop(panel), panel.ActualWidth, panel.ActualHeight);
            if (r.Contains(p)) return true;
        }
        return false;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool capturing = ActionPanel.Visibility == Visibility.Visible;
        switch (e.Key)
        {
            case Key.Escape: ActionRequested?.Invoke(EditorActionKind.Close); break;
            case Key.Enter: if (capturing) ActionRequested?.Invoke(EditorActionKind.Default); else StartRequested?.Invoke(); break;
            case Key.C when ctrl && capturing: ActionRequested?.Invoke(EditorActionKind.CopyToClipboard); break;
            case Key.Insert when ctrl && capturing: ActionRequested?.Invoke(EditorActionKind.CopyToClipboard); break;
            case Key.S when ctrl && capturing: ActionRequested?.Invoke(EditorActionKind.SaveToFile); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Start_Click(object sender, RoutedEventArgs e) => StartRequested?.Invoke();
    private void Done_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.Default);
    private void Copy_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.CopyToClipboard);
    private void Save_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.SaveToFile);
    private void Close_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(EditorActionKind.Close);
}
