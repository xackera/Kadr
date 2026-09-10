using System.Diagnostics;
using System.Runtime.InteropServices;
using Kadr.Capture;

namespace Kadr.App.Scrolling;

/// <summary>
/// Низкоуровневый хук мыши на время захвата с прокруткой: настоящие клики и колесо на мониторе захвата
/// блокируются (кроме окон нашего процесса), наши инжектированные события колеса блокируются при зажатых модификаторах.
/// Устанавливать на потоке с циклом сообщений (UI-поток).
/// </summary>
public sealed class MouseInputSuppressor : IDisposable
{
    public const nint InjectedMarker = 0x4B414452; // "KADR"

    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200, WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;
    private const int LLMHF_INJECTED = 0x01;

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData; public uint Flags; public uint Time; public nint ExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(System.Drawing.Point p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll")] private static extern nint GetModuleHandle(string? name);

    private readonly HookProc _proc;
    private readonly MonitorInfo _monitor;
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private nint _hook;

    public MouseInputSuppressor(MonitorInfo monitor)
    {
        _monitor = monitor;
        _proc = Hook;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Не удалось установить хук мыши");
    }

    private nint Hook(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            int msg = (int)wParam;
            if (msg != WM_MOUSEMOVE)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                bool injected = (info.Flags & LLMHF_INJECTED) != 0;
                if (injected)
                {
                    if (info.ExtraInfo == InjectedMarker && (msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL) && ModifierDown())
                        return 1; // наше колесо с модификатором дало бы масштабирование страницы
                }
                else if (_monitor.Bounds.Contains(info.X, info.Y))
                {
                    var hwnd = WindowFromPoint(new System.Drawing.Point(info.X, info.Y));
                    GetWindowThreadProcessId(hwnd, out var pid);
                    if (pid != _ownPid) return 1;
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool ModifierDown() =>
        (GetAsyncKeyState(0x11) & 0x8000) != 0 || (GetAsyncKeyState(0x10) & 0x8000) != 0
        || (GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook != 0) { UnhookWindowsHookEx(_hook); _hook = 0; }
    }
}

/// <summary>Инжекция прокрутки колесом мыши.</summary>
public static class WheelInjector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint Type; public MOUSEINPUT Mi; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int Dx, Dy; public uint MouseData; public uint Flags; public uint Time; public nint ExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    private const uint INPUT_MOUSE = 0, MOUSEEVENTF_WHEEL = 0x0800;

    /// <summary>Один щелчок колеса вниз (delta -120).</summary>
    public static bool ScrollDown()
    {
        var input = new INPUT { Type = INPUT_MOUSE, Mi = new MOUSEINPUT { MouseData = unchecked((uint)-120), Flags = MOUSEEVENTF_WHEEL, ExtraInfo = MouseInputSuppressor.InjectedMarker } };
        return SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 1;
    }
}
