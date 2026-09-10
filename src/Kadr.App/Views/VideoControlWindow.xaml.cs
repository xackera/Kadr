using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Kadr.Common.Settings;

namespace Kadr.App.Views;

/// <summary>Панель управления записью: до старта (старт, источники, отмена) и во время записи (стоп, таймер, пауза, отмена).</summary>
public partial class VideoControlWindow : Window
{
    private readonly System.Drawing.Rectangle _region;
    private readonly System.Drawing.Rectangle _monitorWorkArea;
    private bool _placed;

    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? PauseToggleRequested;
    public event Action? CancelRequested;
    public event Action<bool>? MicrophoneToggled;
    public event Action<bool>? SystemAudioToggled;
    public event Action<bool>? CameraToggled;

    public void SetCameraAvailable(bool available, bool on)
    {
        CameraToggle.IsEnabled = available;
        CameraToggle.IsChecked = available && on;
        CameraToggle.ToolTip = available ? "Веб-камера" : "Веб-камера не найдена";
    }

    public VideoControlWindow(System.Drawing.Rectangle region, System.Drawing.Rectangle monitorWorkArea, AppSettings settings)
    {
        _region = region;
        _monitorWorkArea = monitorWorkArea;
        InitializeComponent();
        MicToggle.IsChecked = settings.InputAudioDeviceId != AppSettings.NoSound;
        SysToggle.IsChecked = settings.OutputAudioDeviceId != AppSettings.NoSound;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.AddExStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW);
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
        };
        Loaded += (_, _) => Place();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && PreStartPanel.Visibility == Visibility.Visible) { CancelRequested?.Invoke(); e.Handled = true; }
            else if (e.Key == Key.Enter && PreStartPanel.Visibility == Visibility.Visible) { StartRequested?.Invoke(); e.Handled = true; }
        };
    }

    /// <summary>Снизу снаружи → сверху снаружи → внутри снизу, в пределах рабочей области монитора.</summary>
    private void Place()
    {
        if (_placed) return;
        _placed = true;
        var source = PresentationSource.FromVisual(this);
        var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double w = ActualWidth * scale, h = ActualHeight * scale;
        const int gap = 5;
        double x = _region.Right - w + 10 * scale; // учитываем внешний Margin=10
        double y = _region.Bottom + gap;
        if (y + h > _monitorWorkArea.Bottom) y = _region.Top - gap - h;
        if (y < _monitorWorkArea.Top) y = _region.Bottom - h - gap;
        x = Math.Max(_monitorWorkArea.Left, Math.Min(x, _monitorWorkArea.Right - w));
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, (int)x, (int)y, 0, 0, NativeMethods.SWP_NOACTIVATE | 0x0001 /*SWP_NOSIZE*/);
    }

    public void SetRecording()
    {
        PreStartPanel.Visibility = Visibility.Collapsed;
        RecordingPanel.Visibility = Visibility.Visible;
    }

    public void SetPaused(bool paused)
    {
        PauseButton.Content = paused ? "" : "";
        PauseButton.ToolTip = paused ? "Продолжить запись" : "Пауза";
    }

    public void SetDuration(TimeSpan elapsed, TimeSpan max)
    {
        var shown = max <= TimeSpan.FromMinutes(5) ? max - elapsed : elapsed;
        if (shown < TimeSpan.Zero) shown = TimeSpan.Zero;
        Timer.Text = shown.TotalHours >= 1 ? shown.ToString(@"h\:mm\:ss") : shown.ToString(@"m\:ss");
    }

    private void Drag_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe && (fe is System.Windows.Controls.Primitives.ButtonBase || fe.TemplatedParent is System.Windows.Controls.Primitives.ButtonBase)) return;
        try { DragMove(); } catch { }
    }

    private void Start_Click(object sender, RoutedEventArgs e) => StartRequested?.Invoke();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
    private void Pause_Click(object sender, RoutedEventArgs e) => PauseToggleRequested?.Invoke();
    private void CancelPre_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
    private void CancelRec_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
    private void Mic_Click(object sender, RoutedEventArgs e) => MicrophoneToggled?.Invoke(MicToggle.IsChecked == true);
    private void Camera_Click(object sender, RoutedEventArgs e) => CameraToggled?.Invoke(CameraToggle.IsChecked == true);
    private void Sys_Click(object sender, RoutedEventArgs e) => SystemAudioToggled?.Invoke(SysToggle.IsChecked == true);

    public void ForceClose()
    {
        try { Close(); } catch { }
    }
}
