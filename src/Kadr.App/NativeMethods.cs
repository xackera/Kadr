using System.Runtime.InteropServices;

namespace Kadr.App;

internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const uint SWP_NOACTIVATE = 0x0010, SWP_NOZORDER = 0x0004, SWP_SHOWWINDOW = 0x0040;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    public static readonly nint HWND_TOPMOST = new(-1);

    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowLong(nint hWnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(nint hWnd, int index, int value);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(nint hWnd, uint affinity);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    public static System.Drawing.Point CursorPosition()
    {
        GetCursorPos(out var p);
        return new System.Drawing.Point(p.X, p.Y);
    }

    public static void AddExStyle(nint hwnd, int style) => SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | style);
    public static void RemoveExStyle(nint hwnd, int style) => SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) & ~style);
    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
}
