using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Microsoft.Extensions.Logging;

namespace Kadr.Recording;

/// <summary>
/// Захват монитора через Windows.Graphics.Capture. Каждый кадр области копируется в новую текстуру;
/// под блокировкой только обмен ссылками, вызовы Direct3D вне неё (иначе взаимная блокировка с Media Foundation).
/// </summary>
public sealed class ScreenSource : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDirect3DDevice _winrtDevice;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private readonly Box _box;

    private ID3D11Texture2D? _latest;   // свежий кадр, ещё не отданный кодеру
    private ID3D11Texture2D? _last;     // последний отданный кадр (для дублирования)
    private long _frameCounter;
    private SizeInt32 _lastSize;
    private bool _disposed;

    public int Width { get; }
    public int Height { get; }
    public ID3D11Device Device => _device;
    public IDirect3DDevice WinRTDevice => _winrtDevice;
    public event Action? FrameArrived;

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    public ScreenSource(IntPtr hMonitor, int x, int y, int width, int height, bool showCursor, ILogger logger)
    {
        _logger = logger;
        Width = width;
        Height = height;
        _box = new Box(x, y, 0, x + width, y + height, 1);

        _device = Direct3DInterop.CreateD3DDevice();
        _context = _device.ImmediateContext;
        _winrtDevice = Direct3DInterop.CreateWinRTDevice(_device);
        _item = Direct3DInterop.CreateItemForMonitor(hMonitor);
        _lastSize = _item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _item.Size);
        _session = _pool.CreateCaptureSession(_item);
        try { _session.IsCursorCaptureEnabled = showCursor; } catch (Exception ex) { _logger.LogDebug(ex, "IsCursorCaptureEnabled недоступно"); }
        try
        {
            if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
                _session.IsBorderRequired = false;
        }
        catch (Exception ex) { _logger.LogDebug(ex, "IsBorderRequired недоступно"); }
        _pool.FrameArrived += OnFrameArrived;
    }

    public void Start() => _session.StartCapture();

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (_disposed) return;
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame is null) return;
            var now = _frameClock.Elapsed;
            if (now - _lastAccepted < MinFrameInterval) return; // кадр пропускаем, пул захвата уже освобождён
            _lastAccepted = now;
            if (frame.ContentSize.Width != _lastSize.Width || frame.ContentSize.Height != _lastSize.Height)
            {
                _lastSize = frame.ContentSize;
                sender.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _lastSize);
            }
            using var source = Direct3DInterop.GetTexture(frame.Surface);
            var box = _box;
            box.Right = Math.Min(box.Right, (int)source.Description.Width);
            box.Bottom = Math.Min(box.Bottom, (int)source.Description.Height);
            if (box.Right <= box.Left || box.Bottom <= box.Top) return;

            var texture = CreateTexture(Width, Height, BindFlags.ShaderResource | BindFlags.RenderTarget);
            _context.CopySubresourceRegion(texture, 0u, 0u, 0u, 0u, source, 0u, box);

            ID3D11Texture2D? stale;
            lock (_sync)
            {
                stale = _latest;
                _latest = texture;
                _frameCounter++;
            }
            stale?.Dispose();
            FrameArrived?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка обработки кадра захвата");
        }
    }

    public long FrameCounter { get { lock (_sync) return _frameCounter; } }

    /// <summary>Кадр для кодера в текстуре из пула: свежий кадр либо копия последнего. null, если кадров ещё не было.</summary>
    public (ID3D11Texture2D Texture, object Token)? TakeFrameForEncoder(TexturePool pool)
    {
        ID3D11Texture2D? fresh;
        lock (_sync)
        {
            fresh = _latest;
            _latest = null;
        }
        if (fresh is not null)
        {
            _last?.Dispose();
            _last = fresh;
        }
        if (_last is null) return null;
        var (texture, token) = pool.Rent();
        _context.CopyResource(texture, _last);
        Highlighter?.Draw(texture);
        return (texture, token);
    }

    /// <summary>Минимальный интервал между принимаемыми кадрами захвата (защита от 120+ к/с при быстрых обновлениях экрана).</summary>
    public TimeSpan MinFrameInterval { get; set; } = TimeSpan.Zero;
    private readonly System.Diagnostics.Stopwatch _frameClock = System.Diagnostics.Stopwatch.StartNew();
    private TimeSpan _lastAccepted = TimeSpan.FromDays(-1);

    /// <summary>Подсветка курсора и кликов, рисуется поверх каждого кадра для кодера.</summary>
    public MouseHighlighter? Highlighter { get; set; }

    public ID3D11Texture2D CreateTexture(int width, int height, BindFlags bind)
    {
        var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1u, 1u, bind,
            ResourceUsage.Default, CpuAccessFlags.None, 1u, 0u, ResourceOptionFlags.None);
        return _device.CreateTexture2D(desc);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pool.FrameArrived -= OnFrameArrived;
        try { _session.Dispose(); } catch { }
        try { _pool.Dispose(); } catch { }
        ID3D11Texture2D? latest;
        lock (_sync) { latest = _latest; _latest = null; }
        latest?.Dispose();
        _last?.Dispose();
        _last = null;
        _context.Dispose();
        _device.Dispose();
    }
}
