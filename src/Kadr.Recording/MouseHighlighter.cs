using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Microsoft.Extensions.Logging;

namespace Kadr.Recording;

/// <summary>
/// Подсветка курсора (жёлтый круг) и кликов (расходящееся красное кольцо 500 мс) поверх кадра через Direct2D.
/// Клики ловятся низкоуровневым хуком мыши в отдельном потоке с циклом сообщений.
/// </summary>
public sealed class MouseHighlighter : IDisposable
{
    private const double ClickAnimationMs = 500;

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData; public uint Flags; public uint Time; public nint ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public nint Hwnd; public uint Message; public nint WParam; public nint LParam; public uint Time; public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern nint GetModuleHandle(string? name);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_MBUTTONUP = 0x0208, WM_QUIT = 0x0012;

    private readonly bool _pointer, _clicks;
    private readonly int _originX, _originY; // экранные координаты левого верхнего угла области
    private readonly int _width, _height;
    private readonly float _scale;
    private readonly ILogger _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<(long Ms, int X, int Y)> _recentClicks = new();
    private readonly object _sync = new();
    private readonly HookProc? _proc;
    private ID2D1Factory1? _factory;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private nint _hook;
    private volatile bool _paused;

    public MouseHighlighter(bool pointer, bool clicks, int originX, int originY, int width, int height, double scale, ILogger logger)
    {
        _pointer = pointer;
        _clicks = clicks;
        _originX = originX;
        _originY = originY;
        _width = width;
        _height = height;
        _scale = (float)Math.Max(0.5, scale);
        _logger = logger;
        if (!pointer && !clicks) return;

        _factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.MultiThreaded);
        if (clicks)
        {
            _proc = Hook;
            _hookThread = new Thread(HookLoop) { IsBackground = true, Name = "MouseClickHook" };
            _hookThread.Start();
        }
    }

    public bool IsEnabled => _pointer || _clicks;
    public void SetPaused(bool paused) => _paused = paused;

    private void HookLoop()
    {
        _hookThreadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc!, GetModuleHandle(null), 0);
        if (_hook == 0) { _logger.LogWarning("Хук мыши для подсветки кликов не установлен"); return; }
        while (GetMessage(out var msg, 0, 0, 0) > 0) { }
        UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    private nint Hook(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && !_paused)
        {
            int msg = (int)wParam;
            if (msg is WM_LBUTTONUP or WM_RBUTTONUP or WM_MBUTTONUP)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                lock (_sync)
                {
                    _recentClicks.Add((_clock.ElapsedMilliseconds, info.X, info.Y));
                    if (_recentClicks.Count > 32) _recentClicks.RemoveAt(0);
                }
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>Нарисовать подсветку на текстуре кадра (BGRA, RenderTarget).</summary>
    public void Draw(ID3D11Texture2D texture)
    {
        if (_factory is null) return;
        try
        {
            using var surface = texture.QueryInterface<IDXGISurface>();
            var props = new RenderTargetProperties
            {
                Type = RenderTargetType.Default,
                PixelFormat = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                DpiX = 96, DpiY = 96,
            };
            using var target = _factory.CreateDxgiSurfaceRenderTarget(surface, props);
            target.BeginDraw();

            GetCursorPos(out var cursor);
            float cx = cursor.X - _originX, cy = cursor.Y - _originY;

            if (_pointer && cx >= -60 && cy >= -60 && cx <= _width + 60 && cy <= _height + 60)
            {
                using var brush = target.CreateSolidColorBrush(new Color4(1f, 1f, 0f, 0.5f));
                float r = 28f * _scale;
                target.FillEllipse(new Ellipse(new Vector2(cx, cy), r, r), brush);
            }

            if (_clicks)
            {
                (long Ms, int X, int Y)[] clicks;
                long now = _clock.ElapsedMilliseconds;
                lock (_sync)
                {
                    _recentClicks.RemoveAll(c => now - c.Ms > ClickAnimationMs);
                    clicks = _recentClicks.ToArray();
                }
                foreach (var c in clicks)
                {
                    float t = (float)((now - c.Ms) / ClickAnimationMs);
                    float radius = 24f * _scale * t;
                    float alpha = t < 0.75f ? 0.75f : 0.75f * (1 - (t - 0.75f) / 0.25f);
                    using var brush = target.CreateSolidColorBrush(new Color4(1f, 0.23f, 0.19f, Math.Max(0, alpha)));
                    target.DrawEllipse(new Ellipse(new Vector2(c.X - _originX, c.Y - _originY), radius, radius), brush, 4f * _scale);
                }
            }
            target.EndDraw();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Подсветка курсора не нарисована");
        }
    }

    public void Dispose()
    {
        if (_hookThreadId != 0) PostThreadMessage(_hookThreadId, WM_QUIT, 0, 0);
        _hookThread?.Join(1000);
        _factory?.Dispose();
        _factory = null;
    }
}
