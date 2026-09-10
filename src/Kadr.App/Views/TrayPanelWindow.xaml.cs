using System.Windows;
using System.Windows.Input;
using Kadr.App.Services;

namespace Kadr.App.Views;

/// <summary>Панель по левому клику в трее: три кнопки над курсором. Закрывается по Esc, клику или потере фокуса.</summary>
public partial class TrayPanelWindow : Window
{
    private static TrayPanelWindow? _current;
    private readonly AppCommands _commands;

    private TrayPanelWindow(AppCommands commands)
    {
        _commands = commands;
        InitializeComponent();
        Deactivated += (_, _) => Close();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => { if (_current == this) _current = null; };
    }

    public static void ShowNearCursor(AppCommands commands)
    {
        if (_current is not null) { _current.Close(); return; }
        var w = new TrayPanelWindow(commands);
        _current = w;
        w.Show();
        var pos = System.Windows.Forms.Cursor.Position;
        var source = PresentationSource.FromVisual(w);
        var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var wa = System.Windows.Forms.Screen.FromPoint(pos).WorkingArea;
        double left = pos.X / scale - w.ActualWidth / 2;
        double top = pos.Y / scale - w.ActualHeight - 4;
        left = Math.Max(wa.Left / scale, Math.Min(left, wa.Right / scale - w.ActualWidth));
        top = Math.Max(wa.Top / scale, top);
        w.Left = left;
        w.Top = top;
        w.Activate();
    }

    private void Screenshot_Click(object sender, RoutedEventArgs e) { Close(); _commands.ScreenshotRegion(); }
    private void Video_Click(object sender, RoutedEventArgs e) { Close(); _commands.ToggleVideo(); }
    private void Scroll_Click(object sender, RoutedEventArgs e) { Close(); _commands.ScrollingCapture(); }
}
