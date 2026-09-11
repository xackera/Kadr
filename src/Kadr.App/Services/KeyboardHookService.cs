using System.Runtime.InteropServices;
using System.Windows;
using Kadr.Common.Hotkeys;
using Microsoft.Extensions.Logging;

namespace Kadr.App.Services;

/// <summary>
/// Перехват горячих клавиш низкоуровневым хуком клавиатуры. В отличие от RegisterHotKey видит нажатие
/// раньше других программ и системных обработчиков (например, «Ножниц» Windows 11 на PrtScr) и поглощает его.
/// Работает на отдельном потоке с циклом сообщений; хук периодически переустанавливается, если система его сняла.
/// </summary>
public sealed class KeyboardHookService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_QUIT = 0x0012, WM_TIMER = 0x0113;
    private const uint RearmTimerId = 1;
    // Система молча снимает медленные хуки, а свежеустановленный хук вызывается в цепочке первым:
    // частая переустановка возвращает Kadr в начало очереди, если клавишу перехватывает другая программа.
    private const uint RearmIntervalMs = 2000;

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint VkCode, ScanCode, Flags, Time; public nint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public nint Hwnd; public uint Message; public nint WParam, LParam; public uint Time; public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint SetTimer(nint hwnd, nuint id, uint interval, nint proc);
    [DllImport("user32.dll")] private static extern bool KillTimer(nint hwnd, nuint id);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);

    private readonly ILogger _logger;
    private readonly object _sync = new();
    private readonly HookProc _proc;            // держим ссылку, иначе делегат соберёт GC
    private List<(Hotkey Hotkey, Action Action)> _bindings = new();

    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private volatile bool _suspended;
    private volatile bool _stopping;

    public KeyboardHookService(ILogger<KeyboardHookService> logger)
    {
        _logger = logger;
        _proc = Hook;
    }

    /// <summary>Хук установлен и принимает нажатия.</summary>
    public bool IsActive => _hook != 0;

    /// <summary>Запускает поток хука. Возвращает false, если установить хук не удалось.</summary>
    public bool Start()
    {
        if (_thread is not null) return IsActive;
        var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "KadrKeyboardHook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(3000);
        return IsActive;
    }

    public void SetBindings(IEnumerable<(Hotkey Hotkey, Action Action)> bindings)
    {
        lock (_sync) _bindings = bindings.Where(b => b.Hotkey.IsValid).ToList();
    }

    /// <summary>На время ввода сочетания в настройках перехват отключается, чтобы клавиша дошла до окна.</summary>
    public void Suspend() => _suspended = true;
    public void Resume() => _suspended = false;

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        Install();
        SetTimer(0, RearmTimerId, RearmIntervalMs, 0);
        ready.Set();

        while (!_stopping && GetMessage(out var msg, 0, 0, 0) > 0)
        {
            // Система молча снимает медленные низкоуровневые хуки: переустанавливаем на всякий случай.
            if (msg.Message == WM_TIMER && !_stopping && !_suspended) Reinstall();
        }

        KillTimer(0, RearmTimerId);
        Uninstall();
    }

    private void Install()
    {
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == 0)
            _logger.LogWarning("Не удалось установить хук клавиатуры, код {Error}", Marshal.GetLastWin32Error());
        else
            _logger.LogInformation("Хук клавиатуры установлен");
    }

    private void Reinstall()
    {
        var old = _hook;
        var fresh = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (fresh == 0) return;
        _hook = fresh;
        if (old != 0) UnhookWindowsHookEx(old);
    }

    private void Uninstall()
    {
        var h = _hook;
        _hook = 0;
        if (h != 0) UnhookWindowsHookEx(h);
    }

    private nint Hook(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && !_suspended)
        {
            var msg = (int)wParam;
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vk = (int)info.VkCode;
                if (!VirtualKeys.IsModifier(vk))
                {
                    var pressed = new Hotkey(vk,
                        Ctrl: NativeMethods.IsKeyDown(VirtualKeys.Control),
                        Alt: NativeMethods.IsKeyDown(VirtualKeys.Menu),
                        Shift: NativeMethods.IsKeyDown(VirtualKeys.Shift),
                        Win: NativeMethods.IsKeyDown(VirtualKeys.LWin) || NativeMethods.IsKeyDown(VirtualKeys.RWin));

                    Action? action = null;
                    lock (_sync)
                    {
                        foreach (var b in _bindings)
                            if (b.Hotkey == pressed) { action = b.Action; break; }
                    }

                    if (action is not null)
                    {
                        _logger.LogInformation("Хоткей: {Hotkey}", pressed);
                        Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            try { action(); }
                            catch (Exception ex) { _logger.LogError(ex, "Ошибка обработки хоткея {Hotkey}", pressed); }
                        });
                        return 1;   // поглощаем: до «Ножниц» и других программ нажатие не дойдёт
                    }
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_thread is null) return;
        _stopping = true;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread.Join(2000);
        _thread = null;
    }
}
