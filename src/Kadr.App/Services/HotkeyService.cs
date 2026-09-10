using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Kadr.Common.Hotkeys;
using Kadr.Common.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kadr.App.Services;

/// <summary>Глобальные горячие клавиши через RegisterHotKey на скрытом message-only окне.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);

    public const int IdRegion = 1, IdActiveWindow = 2, IdActiveMonitor = 3, IdDesktop = 4, IdVideo = 5, IdPause = 6;

    private readonly SettingsStore _store;
    private readonly NotificationService _notify;
    private readonly IServiceProvider _services;
    private readonly ILogger<HotkeyService> _logger;
    private readonly Dictionary<int, (Hotkey Hotkey, Action Action)> _registered = new();

    private HwndSource? _source;
    private bool _suspended;
    private string? _lastFailureReport;

    public HotkeyService(SettingsStore store, NotificationService notify, IServiceProvider services, ILogger<HotkeyService> logger)
    {
        _store = store;
        _notify = notify;
        _services = services;
        _logger = logger;
    }

    private AppCommands Commands => _services.GetRequiredService<AppCommands>();

    public void Initialize()
    {
        var p = new HwndSourceParameters("KadrHotkeyWindow")
        {
            Width = 0, Height = 0, WindowStyle = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        RegisterAll();
        _store.Changed += (old, cur) =>
        {
            if (HotkeysDiffer(old, cur)) Application.Current?.Dispatcher.BeginInvoke(RegisterAll);
        };
    }

    /// <summary>На время ввода хоткея в настройках регистрация снимается, чтобы клавиша дошла до окна.</summary>
    public void Suspend()
    {
        _suspended = true;
        UnregisterAll();
    }

    public void Resume()
    {
        _suspended = false;
        RegisterAll();
    }

    private static bool HotkeysDiffer(AppSettings a, AppSettings b) =>
        a.HotkeyRegionScreenshot != b.HotkeyRegionScreenshot || a.HotkeyActiveWindowScreenshot != b.HotkeyActiveWindowScreenshot
        || a.HotkeyActiveMonitorScreenshot != b.HotkeyActiveMonitorScreenshot || a.HotkeyDesktopScreenshot != b.HotkeyDesktopScreenshot
        || a.HotkeyVideoRecording != b.HotkeyVideoRecording || a.HotkeyVideoPause != b.HotkeyVideoPause;

    private void RegisterAll()
    {
        UnregisterAll();
        if (_suspended || _source is null) return;
        var s = _store.Current;
        var defs = new (int Id, Hotkey Hotkey, Action Action)[]
        {
            (IdRegion, s.HotkeyRegionScreenshot, () => Commands.ScreenshotRegion(fromHotkey: true)),
            (IdActiveWindow, s.HotkeyActiveWindowScreenshot, () => Commands.ScreenshotActiveWindow()),
            (IdActiveMonitor, s.HotkeyActiveMonitorScreenshot, () => Commands.ScreenshotActiveMonitor()),
            (IdDesktop, s.HotkeyDesktopScreenshot, () => Commands.ScreenshotDesktop()),
            (IdVideo, s.HotkeyVideoRecording, () => Commands.ToggleVideo()),
            (IdPause, s.HotkeyVideoPause, () => Commands.PauseVideo()),
        };

        var failed = new List<string>();
        foreach (var (id, hk, action) in defs)
        {
            if (!hk.IsValid) continue;
            if (RegisterHotKey(_source.Handle, id, hk.NativeModifiers | MOD_NOREPEAT, (uint)hk.Key))
            {
                _registered[id] = (hk, action);
                _logger.LogInformation("Хоткей {Hotkey} зарегистрирован (id {Id})", hk, id);
            }
            else
            {
                failed.Add(hk.ToString());
                _logger.LogWarning("Не удалось зарегистрировать хоткей {Hotkey} (id {Id}), код {Error}", hk, id, Marshal.GetLastWin32Error());
            }
        }

        if (failed.Count > 0)
        {
            var report = string.Join(", ", failed);
            if (report != _lastFailureReport)
            {
                _lastFailureReport = report;
                _notify.Warning($"Сочетание {report} занято другой программой (например, «Ножницами» Windows или облачным клиентом). Нажмите, чтобы выбрать другое.",
                    () => Commands.ShowSettings(SettingsTab.Hotkeys));
            }
        }
        else
        {
            _lastFailureReport = null;
        }
    }

    private void UnregisterAll()
    {
        if (_source is null) return;
        foreach (var id in _registered.Keys) UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _registered.TryGetValue((int)wParam, out var entry))
        {
            handled = true;
            _logger.LogInformation("Хоткей: {Hotkey}", entry.Hotkey);
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try { entry.Action(); }
                catch (Exception ex) { _logger.LogError(ex, "Ошибка обработки хоткея {Hotkey}", entry.Hotkey); }
            });
        }
        return 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.Dispose();
        _source = null;
    }
}
