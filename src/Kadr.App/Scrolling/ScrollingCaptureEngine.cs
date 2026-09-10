using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Kadr.Capture;
using Microsoft.Extensions.Logging;

namespace Kadr.App.Scrolling;

/// <summary>
/// Цикл захвата области при прокрутке: кадры через GDI до 30 к/с, детектор сдвига, склейка.
/// Работает в фоновом потоке; события приходят из него.
/// </summary>
public sealed class ScrollingCaptureEngine : IDisposable
{
    private const int MaxFps = 30;

    private readonly Rectangle _region;
    private readonly ShiftDetector _detector;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;

    public ScrollingImage Image { get; }
    public int FramesProcessed { get; private set; }
    public int ShiftsDetected { get; private set; }
    public int FramesSinceLastShift { get; private set; }

    public event Action? ReachedMaxHeight;
    public event Action<int>? ShiftAdded;

    public ScrollingCaptureEngine(Rectangle region, ILogger logger)
    {
        _region = region;
        _logger = logger;
        _detector = new ShiftDetector(region.Width, region.Height);
        Image = new ScrollingImage(region.Width);
    }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "ScrollingCapture", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop()
    {
        _cts.Cancel();
        _thread?.Join(2000);
    }

    private void Loop()
    {
        var ct = _cts.Token;
        var interval = TimeSpan.FromSeconds(1.0 / MaxFps);
        PreprocessedFrame? prev = null;
        var sw = Stopwatch.StartNew();
        var next = TimeSpan.Zero;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var wait = next - sw.Elapsed;
                if (wait > TimeSpan.Zero) Thread.Sleep(wait);
                next = sw.Elapsed + interval;

                byte[] data; int stride;
                using (var bmp = ScreenCapture.CaptureRectangle(_region, includeCursor: false))
                    (data, stride) = Extract(bmp);
                var cur = _detector.Preprocess(data, stride);
                FramesProcessed++;

                if (prev is null)
                {
                    Image.AddFragment(data, stride, 0, _region.Height, 0);
                    prev = cur;
                    continue;
                }

                var shift = _detector.FindVerticalShift(prev, cur);
                if (shift is > 0)
                {
                    int top = Math.Clamp(_detector.MovingCenter - shift.Value / 2, 0, _region.Height - shift.Value);
                    int lineCount = _region.Height - top;
                    int overwrite = Math.Max(0, lineCount - shift.Value);
                    int added = Image.AddFragment(data, stride, top, lineCount, overwrite);
                    ShiftsDetected++;
                    FramesSinceLastShift = 0;
                    ShiftAdded?.Invoke(shift.Value);
                    if (added < lineCount)
                    {
                        _logger.LogInformation("Достигнута максимальная высота {Height}", Image.Height);
                        ReachedMaxHeight?.Invoke();
                        return;
                    }
                }
                else
                {
                    FramesSinceLastShift++;
                }
                prev = cur;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка захвата с прокруткой");
        }
    }

    private static (byte[] Data, int Stride) Extract(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var data = new byte[bd.Stride * bd.Height];
            System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, data, 0, data.Length);
            return (data, bd.Stride);
        }
        finally { bmp.UnlockBits(bd); }
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
}
