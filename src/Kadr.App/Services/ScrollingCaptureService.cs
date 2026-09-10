using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kadr.App.Editor;
using Kadr.App.Scrolling;
using Kadr.Capture;
using Kadr.Common.Files;
using Kadr.Common.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Kadr.App.Services;

/// <summary>Скриншот с прокруткой: область, автопрокрутка колесом, склейка кадров, действие с результатом.</summary>
public sealed class ScrollingCaptureService
{
    private const int ScrollIntervalMs = 80;
    private const int InsideTicksBeforeScroll = 2;
    private const int OutsideTicksBeforeHint = 3;
    private const int IdleFramesToAutoStop = 45; // ~1.5 с без сдвига после начала прокрутки

    private readonly SettingsStore _store;
    private readonly OperationState _state;
    private readonly ScreenshotService _screenshots;
    private readonly NotificationService _notify;
    private readonly ILogger<ScrollingCaptureService> _logger;

    public ScrollingCaptureService(SettingsStore store, OperationState state, ScreenshotService screenshots,
        NotificationService notify, ILogger<ScrollingCaptureService> logger)
    {
        _store = store;
        _state = state;
        _screenshots = screenshots;
        _notify = notify;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        if (!_state.TryStart(OperationType.Screenshot)) return;
        ScrollingCaptureWindow? window = null;
        ScrollingCaptureEngine? engine = null;
        MouseInputSuppressor? suppressor = null;
        DispatcherTimer? scrollTimer = null;
        DispatcherTimer? previewTimer = null;
        try
        {
            var settings = _store.Current;
            var title = ForegroundWindow.Title(ForegroundWindow.Handle);
            var result = await OverlaySession.RunAsync(OverlayMode.Scrolling, settings, true, _logger);
            if (result is null || result.Action == EditorActionKind.Close) return;
            var region = result.ScreenRect;
            var monitor = result.Monitor;
            region.Intersect(monitor.Bounds);
            if (region.Width < 64 || region.Height < 64) { _notify.Error("Область для прокрутки слишком мала."); return; }

            window = new ScrollingCaptureWindow(region, monitor);
            var actionTcs = new TaskCompletionSource<EditorActionKind>(TaskCreationOptions.RunContinuationsAsynchronously);
            var startTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            window.StartRequested += () => startTcs.TrySetResult(true);
            window.ActionRequested += a => { startTcs.TrySetResult(false); actionTcs.TrySetResult(a); };
            window.Closed += (_, _) => { startTcs.TrySetResult(false); actionTcs.TrySetResult(EditorActionKind.Close); };
            window.Show();
            window.Activate();

            if (!await startTcs.Task) return;

            engine = new ScrollingCaptureEngine(region, _logger);
            bool reachedMax = false, autoStopped = false;
            engine.ReachedMaxHeight += () => Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                reachedMax = true;
                scrollTimer?.Stop();
                window.ShowMessage("Достигнута максимальная высота скриншота. Создание скриншота завершено.");
            });
            engine.Start();
            window.SetCapturing();

            try { suppressor = new MouseInputSuppressor(monitor); }
            catch (Exception ex) { _logger.LogWarning(ex, "Хук мыши не установлен"); }

            // Превью не чаще 10 раз в секунду.
            var image = engine.Image;
            int lastPreviewHeight = -1;
            previewTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            previewTimer.Tick += (_, _) =>
            {
                if (image.Height == lastPreviewHeight) return;
                lastPreviewHeight = image.Height;
                window.UpdatePreview(BuildPreview(image));
            };
            previewTimer.Start();

            // Автопрокрутка: курсор внутри области на мониторе захвата.
            int inside = 0, outside = 0;
            bool scrolledOnce = false;
            scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ScrollIntervalMs) };
            scrollTimer.Tick += (_, _) =>
            {
                var p = NativeMethods.CursorPosition();
                bool onMonitor = monitor.Bounds.Contains(p);
                bool inRegion = onMonitor && region.Contains(p);
                if (inRegion)
                {
                    outside = 0;
                    inside++;
                    window.ShowMessage(null);
                    if (inside > InsideTicksBeforeScroll) { WheelInjector.ScrollDown(); scrolledOnce = true; }
                }
                else
                {
                    inside = 0;
                    outside++;
                    if (outside >= OutsideTicksBeforeHint && !window.IsPointOverPanels(p) && !reachedMax && !autoStopped)
                        window.ShowMessage("Для автопрокрутки держите курсор внутри области");
                }

                // Автостоп: прокрутка шла, сдвигов давно нет — страница закончилась.
                if (scrolledOnce && engine.ShiftsDetected > 0 && engine.FramesSinceLastShift >= IdleFramesToAutoStop && !autoStopped)
                {
                    autoStopped = true;
                    scrollTimer!.Stop();
                    engine.Stop();
                    window.ShowMessage("Конец страницы. Выберите действие на панели.");
                }
            };
            scrollTimer.Start();

            var action = await actionTcs.Task;
            scrollTimer.Stop();
            previewTimer.Stop();
            suppressor?.Dispose(); suppressor = null;
            engine.Stop();
            window.ShowMessage(null);
            _logger.LogInformation("Прокрутка: {Action}, кадров {Frames}, сдвигов {Shifts}, высота {Height}", action, engine.FramesProcessed, engine.ShiftsDetected, image.Height);

            string? savePath = null;
            if (action == EditorActionKind.SaveToFile)
            {
                savePath = ShowSaveDialog(window, settings);
                if (savePath is null) action = EditorActionKind.Close;
            }
            try { window.Close(); } catch { }
            window = null;
            if (action == EditorActionKind.Close || image.Height <= 0) return;

            using var bitmap = image.ToBitmap();
            if (settings.PlaySound && !settings.SilentMode) SoundService.PlayShutter(settings.SoundShutterPath);
            switch (action)
            {
                case EditorActionKind.CopyToClipboard:
                    ClipboardService.SetImage(bitmap, null);
                    _notify.Info("Скриншот скопирован в буфер обмена.", null, respectSilentMode: true);
                    break;
                case EditorActionKind.SaveToFile when savePath is not null:
                    Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                    bitmap.Save(savePath, Path.GetExtension(savePath).ToLowerInvariant() is ".jpg" or ".jpeg" ? ImageFormat.Jpeg : ImageFormat.Png);
                    var p2 = savePath;
                    _notify.Info("Скриншот сохранён. Нажмите, чтобы открыть папку.", () => ScreenshotService.OpenFolderWithFile(p2), respectSilentMode: true);
                    break;
                default:
                    _screenshots.Publish(bitmap, title);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка скриншота с прокруткой");
            _notify.Error("Не удалось сделать скриншот с прокруткой: " + ex.Message);
        }
        finally
        {
            scrollTimer?.Stop();
            previewTimer?.Stop();
            suppressor?.Dispose();
            engine?.Dispose();
            try { window?.Close(); } catch { }
            _state.End();
        }
    }

    private static string? ShowSaveDialog(Window owner, AppSettings s)
    {
        var jpeg = s.ScreenshotFileType == ScreenshotFileType.Jpeg;
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить скриншот", Filter = "PNG|*.png|JPEG|*.jpg", FilterIndex = jpeg ? 2 : 1,
            InitialDirectory = s.ScreenshotsPath, FileName = FileNameTemplate.Resolve(s.ScreenshotFileNameTemplate, DateTime.Now, "", 1),
            AddExtension = true, DefaultExt = jpeg ? ".jpg" : ".png",
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    /// <summary>Уменьшенная копия накопленного изображения (ближайший сосед) для панели превью.</summary>
    private static BitmapSource BuildPreview(ScrollingImage image)
    {
        int k = Math.Max(1, (int)Math.Ceiling(image.Width / 180.0));
        int h = Math.Max(1, image.Height);
        var rows = image.CopyRows(0, h);
        int pw = Math.Max(1, image.Width / k), ph = Math.Max(1, h / k);
        var data = new byte[pw * ph * 4];
        int stride = image.Stride;
        for (int y = 0; y < ph; y++)
        {
            int srcRow = Math.Min(h - 1, y * k) * stride;
            for (int x = 0; x < pw; x++)
            {
                int src = srcRow + Math.Min(image.Width - 1, x * k) * 4;
                int dst = (y * pw + x) * 4;
                data[dst] = rows[src]; data[dst + 1] = rows[src + 1]; data[dst + 2] = rows[src + 2]; data[dst + 3] = 255;
            }
        }
        var bmp = BitmapSource.Create(pw, ph, 96, 96, PixelFormats.Bgra32, null, data, pw * 4);
        bmp.Freeze();
        return bmp;
    }
}
