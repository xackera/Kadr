using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Kadr.Capture;
using Kadr.Common.Files;
using Kadr.Common.Settings;
using Microsoft.Extensions.Logging;

namespace Kadr.App.Services;

public enum ScreenshotMode { ActiveWindow, ActiveMonitor, Desktop }

/// <summary>Скриншоты без редактора (окно, монитор, все мониторы) и общая пост-обработка: файл, буфер, уведомление.</summary>
public sealed class ScreenshotService
{
    private readonly SettingsStore _store;
    private readonly OperationState _state;
    private readonly NotificationService _notify;
    private readonly ILogger<ScreenshotService> _logger;

    public ScreenshotService(SettingsStore store, OperationState state, NotificationService notify, ILogger<ScreenshotService> logger)
    {
        _store = store;
        _state = state;
        _notify = notify;
        _logger = logger;
    }

    public void Capture(ScreenshotMode mode)
    {
        if (!_state.TryStart(OperationType.Screenshot))
        {
            _logger.LogInformation("Скриншот {Mode} пропущен: идёт другая операция", mode);
            return;
        }
        var sw = Stopwatch.StartNew();
        try
        {
            var hwnd = ForegroundWindow.Handle;
            var title = ForegroundWindow.Title(hwnd);
            var rect = ResolveRectangle(mode, hwnd);
            if (rect is null || rect.Value.Width <= 0 || rect.Value.Height <= 0)
            {
                _notify.Error("Не удалось определить область захвата.");
                return;
            }

            using var bitmap = ScreenCapture.CaptureRectangle(rect.Value, _store.Current.CaptureCursor);
            _logger.LogInformation("Скриншот {Mode}: {Rect} за {Ms} мс", mode, rect, sw.ElapsedMilliseconds);
            if (ScreenCapture.IsBlack(bitmap))
                _notify.Warning("Снимок полностью чёрный: содержимое защищено от захвата.");

            if (_store.Current.PlaySound && !_store.Current.SilentMode) SoundService.PlayShutter(_store.Current.SoundShutterPath);
            Publish(bitmap, title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка скриншота {Mode}", mode);
            _notify.Error("Не удалось сделать скриншот: " + ex.Message);
        }
        finally
        {
            _state.End();
        }
    }

    private static Rectangle? ResolveRectangle(ScreenshotMode mode, nint hwnd)
    {
        switch (mode)
        {
            case ScreenshotMode.ActiveWindow:
            {
                var bounds = ForegroundWindow.Bounds(hwnd);
                if (bounds is null) return null;
                var monitors = Monitors.All().Where(m => m.Bounds.IntersectsWith(bounds.Value)).ToList();
                var clip = monitors.Count == 1 ? monitors[0].Bounds : Monitors.VirtualScreen();
                return Rectangle.Intersect(bounds.Value, clip);
            }
            case ScreenshotMode.ActiveMonitor:
                return (Monitors.FromWindow(hwnd) ?? Monitors.Primary())?.Bounds;
            default:
                return Monitors.VirtualScreen();
        }
    }

    /// <summary>Сохранение по настройкам «куда сохранять», копирование в буфер и уведомление.</summary>
    public string? Publish(Bitmap bitmap, string? windowTitle)
    {
        var s = _store.Current;
        bool toFile = s.StorageType != StorageType.Clipboard;
        bool toClipboard = s.StorageType != StorageType.File;
        string? path = null;

        if (toFile)
        {
            var dir = s.ScreenshotsPath;
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Папка {Dir} недоступна", dir);
                dir = AppSettings.DefaultFolder();
                Directory.CreateDirectory(dir);
                _notify.Warning($"Папка {s.ScreenshotsPath} недоступна. Скриншот сохранён на Рабочий стол.");
            }

            var ext = s.ScreenshotFileType == ScreenshotFileType.Jpeg ? ".jpg" : ".png";
            path = FileNameTemplate.ResolveUniquePath(dir, s.ScreenshotFileNameTemplate, ext, DateTime.Now, windowTitle);
            SaveBitmap(bitmap, path, s);
            _logger.LogInformation("Скриншот сохранён: {Path}", path);
        }

        if (toClipboard)
        {
            try
            {
                ClipboardService.SetImage(bitmap, path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось скопировать в буфер обмена");
                _notify.Error("Не удалось скопировать скриншот в буфер обмена. Проверьте, не блокирует ли антивирус.");
            }
        }

        if (path is not null)
        {
            var p = path;
            _notify.Info("Скриншот сохранён. Нажмите, чтобы открыть папку.", () => OpenFolderWithFile(p), respectSilentMode: true);
            if (s.OpenFileAfterSave && !s.SilentMode)
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else
        {
            _notify.Info("Скриншот скопирован в буфер обмена.", null, respectSilentMode: true);
        }
        return path;
    }

    private static void SaveBitmap(Bitmap bitmap, string path, AppSettings s)
    {
        if (s.ScreenshotFileType == ScreenshotFileType.Jpeg)
        {
            using var rgb = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(rgb)) g.DrawImageUnscaled(bitmap, 0, 0);
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, (long)s.JpegQuality);
            rgb.Save(path, codec, parameters);
        }
        else
        {
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    public static void OpenFolderWithFile(string path)
        => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
}
