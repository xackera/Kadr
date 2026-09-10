using System.Windows;
using System.Windows.Interop;

namespace Kadr.App.Views;

/// <summary>Рамка вокруг записываемой области: прозрачна для мыши, исключена из захвата.</summary>
public partial class RecordingFrameWindow : Window
{
    private readonly System.Drawing.Rectangle _region;

    public RecordingFrameWindow(System.Drawing.Rectangle region)
    {
        _region = region;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.AddExStyle(hwnd, NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE);
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            const int pad = 3;
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, _region.X - pad, _region.Y - pad, _region.Width + 2 * pad, _region.Height + 2 * pad,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        };
    }

    public void SetPaused(bool paused) => Outer.Opacity = paused ? 0.4 : 1.0;
}
