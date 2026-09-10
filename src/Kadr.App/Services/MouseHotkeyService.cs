using System.Runtime.InteropServices;
using System.Windows;
using Kadr.Common.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kadr.App.Services;

/// <summary>
/// «Две кнопки мыши одновременно»: низкоуровневый хук ставится только когда включена хотя бы одна опция и выключен тихий режим.
/// Нажатие второй кнопки при зажатой первой даёт событие; повтор только после отпускания обеих. Клики не подавляются.
/// </summary>
public sealed class MouseHotkeyService : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern nint GetModuleHandle(string? name);

    private readonly SettingsStore _store;
    private readonly IServiceProvider _services;
    private readonly ILogger<MouseHotkeyService> _logger;
    private readonly HookProc _proc;
    private nint _hook;
    private bool _left, _right, _fired;

    public MouseHotkeyService(SettingsStore store, IServiceProvider services, ILogger<MouseHotkeyService> logger)
    {
        _store = store;
        _services = services;
        _logger = logger;
        _proc = Hook;
    }

    public void Initialize()
    {
        Apply(_store.Current);
        _store.Changed += (_, s) => Application.Current?.Dispatcher.BeginInvoke(() => Apply(s));
    }

    private void Apply(AppSettings s)
    {
        bool wanted = (s.UseMouseButtonsForScreenshot || s.UseMouseButtonsForVideo) && !s.SilentMode;
        if (wanted && _hook == 0)
        {
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            _logger.LogInformation(_hook != 0 ? "Хук мыши для двух кнопок установлен" : "Не удалось установить хук мыши");
        }
        else if (!wanted && _hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
            _logger.LogInformation("Хук мыши снят");
        }
    }

    private nint Hook(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            switch ((int)wParam)
            {
                case WM_LBUTTONDOWN: _left = true; Check(); break;
                case WM_RBUTTONDOWN: _right = true; Check(); break;
                case WM_LBUTTONUP: _left = false; if (!_right) _fired = false; break;
                case WM_RBUTTONUP: _right = false; if (!_left) _fired = false; break;
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void Check()
    {
        if (!_left || !_right || _fired) return;
        _fired = true;
        var s = _store.Current;
        bool ctrlShift = NativeMethods.IsKeyDown(0x11) && NativeMethods.IsKeyDown(0x10);
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var commands = _services.GetRequiredService<AppCommands>();
            if (s.UseMouseButtonsForVideo && ctrlShift) commands.ToggleVideo();
            else if (s.UseMouseButtonsForScreenshot) commands.ScreenshotRegion(fromHotkey: false);
        });
    }

    public void Dispose()
    {
        if (_hook != 0) { UnhookWindowsHookEx(_hook); _hook = 0; }
    }
}
