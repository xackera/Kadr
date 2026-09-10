using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kadr.App.Services;
using Kadr.Common.Hotkeys;
using Microsoft.Extensions.DependencyInjection;

namespace Kadr.App.Controls;

/// <summary>
/// Поле ввода хоткея: клик, нажатие комбинации, превью модификаторов, Esc отменяет, кнопка очистки.
/// На время ввода глобальные хоткеи снимаются, чтобы PrtScr дошёл до окна.
/// </summary>
public partial class HotkeyBox : UserControl
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(Hotkey), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(Hotkey.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).Render()));

    public Hotkey Hotkey
    {
        get => (Hotkey)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    private bool _capturing;
    private string? _preview;

    public HotkeyBox()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, e) => { Focus(); e.Handled = true; };
        GotKeyboardFocus += (_, _) => BeginCapture();
        LostKeyboardFocus += (_, _) => EndCapture();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        Render();
    }

    private static HotkeyService? Hotkeys
    {
        get { try { return App.Services?.GetService<HotkeyService>(); } catch { return null; } }
    }

    private void BeginCapture()
    {
        _capturing = true;
        _preview = null;
        Hotkeys?.Suspend();
        Render();
    }

    private void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        _preview = null;
        Hotkeys?.Resume();
        Render();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { Keyboard.ClearFocus(); MoveFocusAway(); return; }
        if (key == Key.Snapshot) return; // PrtScr приходит только в KeyUp

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (VirtualKeys.IsModifier(vk))
        {
            _preview = BuildPreview();
            Render();
            return;
        }
        Commit(vk);
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Snapshot) { Commit(VirtualKeys.Snapshot); return; }
        _preview = BuildPreview();
        Render();
    }

    private void Commit(int vk)
    {
        var mods = Keyboard.Modifiers;
        var hk = new Hotkey(vk,
            Ctrl: mods.HasFlag(ModifierKeys.Control),
            Alt: mods.HasFlag(ModifierKeys.Alt),
            Shift: mods.HasFlag(ModifierKeys.Shift),
            Win: mods.HasFlag(ModifierKeys.Windows));
        if (!hk.IsValid)
        {
            _preview = "Недопустимое сочетание";
            Render();
            return;
        }
        Hotkey = hk;   // модель может отклонить значение (конфликт) и вернуть прежнее через привязку
        MoveFocusAway();
    }

    private static string BuildPreview()
    {
        var mods = Keyboard.Modifiers;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return parts.Count == 0 ? "" : string.Join("+", parts) + "+";
    }

    private void MoveFocusAway()
    {
        var scope = FocusManager.GetFocusScope(this);
        FocusManager.SetFocusedElement(scope, null);
        Keyboard.ClearFocus();
        (Window.GetWindow(this))?.Focus();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Hotkey = Hotkey.Empty;
        MoveFocusAway();
    }

    private void Render()
    {
        if (Text is null) return;
        var accent = (Brush)FindResource("AccentBrush");
        var muted = (Brush)FindResource("TextMutedBrush");
        var text = (Brush)FindResource("TextBrush");
        Root.BorderBrush = _capturing ? accent : (Brush)FindResource("BorderBrush");
        Root.Background = _capturing ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7));

        if (_capturing)
        {
            Text.Text = string.IsNullOrEmpty(_preview) ? "Введите сочетание клавиш" : _preview;
            Text.Foreground = string.IsNullOrEmpty(_preview) ? muted : text;
        }
        else if (Hotkey.IsEmpty)
        {
            Text.Text = "Сочетание клавиш не задано";
            Text.Foreground = muted;
        }
        else
        {
            Text.Text = Hotkey.ToString();
            Text.Foreground = text;
        }
        ClearButton.Visibility = Hotkey.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
    }
}
