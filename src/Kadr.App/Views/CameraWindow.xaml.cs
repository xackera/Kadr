using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Kadr.App.Services;
using Kadr.Common.Settings;
using Microsoft.Extensions.Logging;

namespace Kadr.App.Views;

/// <summary>
/// Окно веб-камеры поверх записываемой области: форма «телевизор» или круг, перетаскивание внутри области,
/// ресайз за правый нижний угол с сохранением пропорции, двойной клик меняет форму. Попадает в запись как обычное окно.
/// </summary>
public partial class CameraWindow : Window
{
    private const double TvAspect = 1.153466;
    private const double MinWidthPx = 130;

    private readonly System.Drawing.Rectangle _region;
    private readonly SettingsStore _store;
    private readonly ILogger _logger;
    private readonly CameraCapture _capture;
    private CameraWindowShape _shape;
    private double _scale = 1;
    private bool _placing;

    public CameraWindow(System.Drawing.Rectangle region, SettingsStore store, ILogger logger)
    {
        _region = region;
        _store = store;
        _logger = logger;
        _shape = store.Current.CameraWindowShape;
        _capture = new CameraCapture(logger);
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.AddExStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
            var src = HwndSource.FromHwnd(hwnd);
            _scale = src?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            ApplyInitialRect();
        };
        MouseLeftButtonDown += OnDrag;
        MouseDoubleClick += (_, _) => ToggleShape();
        LocationChanged += (_, _) => { if (!_placing) ClampAndSave(); };
        SizeChanged += (_, _) => UpdateShape();
        _capture.FrameReady += () => { Preview.Source = _capture.Bitmap; Status.Visibility = Visibility.Collapsed; };
        _capture.Failed += msg => Dispatcher.BeginInvoke(() => { Status.Text = "Камера отключена"; Status.Visibility = Visibility.Visible; });
    }

    public async Task StartAsync(string deviceId, int fps)
    {
        try
        {
            await _capture.StartAsync(deviceId, (int)(ActualWidth * _scale), fps);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Камера не запустилась");
            Status.Text = ex.HResult == unchecked((int)0xC00D3E82) ? "Камера используется другой программой" : "Ошибка подключения к камере";
            Status.Visibility = Visibility.Visible;
        }
    }

    // ---- геометрия

    private void ApplyInitialRect()
    {
        _placing = true;
        var rel = ParseRel(_store.Current.CameraWindowRelativeRect);
        double w = rel.Width * _region.Width, h = rel.Height * _region.Height;
        (w, h) = FitAspect(w, h);
        double x = _region.X + rel.X * _region.Width, y = _region.Y + rel.Y * _region.Height;
        if (rel.X > 0.1 && rel.X + rel.Width >= 0.9) x = _region.Right - w;
        SetPixelRect(ClampRect(x, y, w, h));
        _placing = false;
    }

    private (double W, double H) FitAspect(double w, double h)
    {
        double aspect = _shape == CameraWindowShape.Circle ? 1.0 : TvAspect;
        if (_shape == CameraWindowShape.Circle) { var s = Math.Min(w, h); w = s; h = s; }
        else { if (w / h > aspect) w = h * aspect; else h = w / aspect; }
        double minW = MinWidthPx * _scale;
        if (w < minW) { w = minW; h = w / aspect; }
        w = Math.Min(w, _region.Width); h = Math.Min(h, _region.Height);
        return (w, h);
    }

    private (double X, double Y, double W, double H) ClampRect(double x, double y, double w, double h)
    {
        x = Math.Clamp(x, _region.X, Math.Max(_region.X, _region.Right - w));
        y = Math.Clamp(y, _region.Y, Math.Max(_region.Y, _region.Bottom - h));
        return (x, y, w, h);
    }

    private void SetPixelRect((double X, double Y, double W, double H) r)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, (int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.W), (int)Math.Round(r.H),
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private (double X, double Y, double W, double H) CurrentPixelRect()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var b = Kadr.Capture.ForegroundWindow.Bounds(hwnd);
        if (b is { } r) return (r.X, r.Y, r.Width, r.Height);
        return (Left * _scale, Top * _scale, ActualWidth * _scale, ActualHeight * _scale);
    }

    private void ClampAndSave()
    {
        var r = CurrentPixelRect();
        var c = ClampRect(r.X, r.Y, r.W, r.H);
        if (Math.Abs(c.X - r.X) > 0.5 || Math.Abs(c.Y - r.Y) > 0.5)
        {
            _placing = true;
            SetPixelRect(c);
            _placing = false;
        }
        _store.Update(s => s.CameraWindowRelativeRect = FormattableString.Invariant(
            $"{(c.X - _region.X) / _region.Width:0.###},{(c.Y - _region.Y) / _region.Height:0.###},{c.W / _region.Width:0.###},{c.H / _region.Height:0.###}"));
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1) return;
        try { DragMove(); } catch { }
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var r = CurrentPixelRect();
        double w = r.W + e.HorizontalChange * _scale;
        double h = r.H + e.VerticalChange * _scale;
        (w, h) = FitAspect(Math.Max(w, h * (_shape == CameraWindowShape.Circle ? 1 : TvAspect)), h);
        _placing = true;
        SetPixelRect(ClampRect(r.X, r.Y, w, h));
        _placing = false;
        ClampAndSave();
    }

    private void ToggleShape()
    {
        _shape = _shape == CameraWindowShape.Tv ? CameraWindowShape.Circle : CameraWindowShape.Tv;
        _store.Update(s => s.CameraWindowShape = _shape);
        var r = CurrentPixelRect();
        var (w, h) = FitAspect(r.W, r.H);
        _placing = true;
        SetPixelRect(ClampRect(r.X, r.Y, w, h));
        _placing = false;
        UpdateShape();
        ClampAndSave();
    }

    private void UpdateShape()
    {
        double radius = _shape == CameraWindowShape.Circle ? Math.Min(ActualWidth, ActualHeight) / 2 : 14;
        Shape.CornerRadius = new CornerRadius(radius);
        ClipGeometry.Rect = new Rect(0, 0, Math.Max(0, ActualWidth - 6), Math.Max(0, ActualHeight - 6));
        ClipGeometry.RadiusX = ClipGeometry.RadiusY = Math.Max(0, radius - 3);
    }

    private static Rect ParseRel(string? text)
    {
        var parts = (text ?? "").Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 4
            && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)
            && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)
            && double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h)
            && w > 0.02 && h > 0.02)
            return new Rect(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1), Math.Clamp(w, 0.02, 1), Math.Clamp(h, 0.02, 1));
        return new Rect(0.05, 0.7, 0.15, 0.25);
    }

    protected override void OnClosed(EventArgs e)
    {
        _capture.Dispose();
        base.OnClosed(e);
    }
}
