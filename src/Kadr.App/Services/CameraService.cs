using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace Kadr.App.Services;

public sealed record CameraDeviceInfo(string Id, string Name);

/// <summary>Перечисление веб-камер и захват кадров через WinRT MediaCapture в WriteableBitmap.</summary>
public sealed class CameraCapture : IDisposable
{
    private readonly ILogger _logger;
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private WriteableBitmap? _bitmap;
    private byte[]? _buffer;
    private bool _disposed;

    public WriteableBitmap? Bitmap => _bitmap;
    public event Action? FrameReady;
    public event Action<string>? Failed;

    public CameraCapture(ILogger logger) => _logger = logger;

    public static async Task<IReadOnlyList<CameraDeviceInfo>> ListAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            return devices.Select(d => new CameraDeviceInfo(d.Id, d.Name)).ToList();
        }
        catch { return Array.Empty<CameraDeviceInfo>(); }
    }

    public static IReadOnlyList<CameraDeviceInfo> List()
    {
        try { return ListAsync().GetAwaiter().GetResult(); }
        catch { return Array.Empty<CameraDeviceInfo>(); }
    }

    /// <summary>Запуск камеры. deviceId: NoCamera → ничего; DefaultCamera → первая доступная; иначе конкретное устройство.</summary>
    public async Task StartAsync(string deviceId, int preferredWidth, int preferredFps)
    {
        var devices = await ListAsync();
        if (devices.Count == 0) throw new InvalidOperationException("Веб-камера не найдена");
        var id = devices.Any(d => d.Id == deviceId) ? deviceId : devices[0].Id;

        _capture = new MediaCapture();
        await _capture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            VideoDeviceId = id,
            StreamingCaptureMode = StreamingCaptureMode.Video,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            SharingMode = MediaCaptureSharingMode.SharedReadOnly,
        });
        _capture.Failed += (_, e) => Failed?.Invoke(e.Message);

        var source = _capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                     ?? throw new InvalidOperationException("У камеры нет видеопотока");

        // Формат: ближайший по частоте кадров, ширина ≥ 640, ближайший к желаемой ширине.
        var formats = source.SupportedFormats
            .Where(f => f.VideoFormat.Width >= 320)
            .OrderBy(f => Math.Abs(Fps(f) - preferredFps))
            .ThenByDescending(f => f.VideoFormat.Width >= 640)
            .ThenBy(f => Math.Abs((int)f.VideoFormat.Width - preferredWidth))
            .ToList();
        if (formats.Count > 0)
        {
            try { await source.SetFormatAsync(formats[0]); }
            catch (Exception ex) { _logger.LogDebug(ex, "Не удалось выбрать формат камеры"); }
        }

        _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
        _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
        _reader.FrameArrived += OnFrameArrived;
        var status = await _reader.StartAsync();
        if (status != MediaFrameReaderStartStatus.Success) throw new InvalidOperationException("Не удалось запустить камеру: " + status);
        _logger.LogInformation("Камера запущена: {Name}, {W}x{H}", devices.First(d => d.Id == id).Name, source.CurrentFormat.VideoFormat.Width, source.CurrentFormat.VideoFormat.Height);
    }

    private static double Fps(MediaFrameFormat f) => f.FrameRate.Denominator == 0 ? 0 : (double)f.FrameRate.Numerator / f.FrameRate.Denominator;

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (_disposed) return;
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var sb = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (sb is null) return;
            SoftwareBitmap? converted = null;
            if (sb.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || sb.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
                converted = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var src = converted ?? sb;
            int w = src.PixelWidth, h = src.PixelHeight;
            int size = w * h * 4;
            if (_buffer is null || _buffer.Length != size) _buffer = new byte[size];
            src.CopyToBuffer(_buffer.AsBuffer());
            converted?.Dispose();
            var data = _buffer;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                if (_bitmap is null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
                {
                    _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                    FrameReady?.Invoke();
                }
                _bitmap.WritePixels(new Int32Rect(0, 0, w, h), data, w * 4, 0);
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Кадр камеры");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_reader is not null)
            {
                _reader.FrameArrived -= OnFrameArrived;
                _reader.StopAsync().AsTask().Wait(2000);
                _reader.Dispose();
            }
            _capture?.Dispose();
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Остановка камеры"); }
        _reader = null;
        _capture = null;
    }
}
