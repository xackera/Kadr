using System.IO;
using System.Windows;
using System.Windows.Threading;
using Kadr.App.Editor;
using Kadr.App.Views;
using Kadr.Capture;
using Kadr.Common.Files;
using Kadr.Common.Settings;
using Kadr.Recording;
using Microsoft.Extensions.Logging;

namespace Kadr.App.Services;

/// <summary>Запись видео: выбор области, панель управления, видеомодуль, контроль диска, завершение.</summary>
public sealed class VideoRecordingService
{
    private const long DoNotStartThreshold = 100L * 1024 * 1024;
    private const long WarnThreshold = 300L * 1024 * 1024;
    private const long StopThreshold = 90L * 1024 * 1024;

    private readonly SettingsStore _store;
    private readonly OperationState _state;
    private readonly NotificationService _notify;
    private readonly VideoModuleClient _module;
    private readonly ILogger<VideoRecordingService> _logger;

    private RecordingFrameWindow? _frame;
    private VideoControlWindow? _panel;
    private CameraWindow? _camera;
    private DispatcherTimer? _diskTimer;
    private bool _warnedDisk;
    private bool _paused;
    private string? _outputFile;
    private TaskCompletionSource<bool>? _finishedTcs;
    private bool _busy;

    public VideoRecordingService(SettingsStore store, OperationState state, NotificationService notify, VideoModuleClient module, ILogger<VideoRecordingService> logger)
    {
        _store = store;
        _state = state;
        _notify = notify;
        _module = module;
        _logger = logger;
        _module.Progress += (t, max) => Application.Current?.Dispatcher.BeginInvoke(() => _panel?.SetDuration(t, max));
        _module.Finished += (status, code, msg) => Application.Current?.Dispatcher.BeginInvoke(() => OnFinished(status, code, msg));
    }

    public bool IsRecording => _state.IsRecording;

    public async Task StartAsync()
    {
        if (_busy) return;
        if (!_state.TryStart(OperationType.Video)) { _logger.LogInformation("Запись пропущена: идёт другая операция"); return; }
        _busy = true;
        bool started = false;
        try
        {
            var settings = _store.Current;
            var result = await OverlaySession.RunAsync(OverlayMode.Video, settings, true, _logger);
            if (result is null || result.Action == EditorActionKind.Close) return;

            var region = result.ScreenRect;
            var monitor = result.Monitor;
            // Чётные размеры, внутри монитора.
            region.Intersect(monitor.Bounds);
            if (region.Width % 2 == 1) region.Width--;
            if (region.Height % 2 == 1) region.Height--;
            if (region.Width < 2 || region.Height < 2) { _notify.Error("Слишком маленькая область для записи."); return; }

            if (!CheckDisk(settings.VideoSavePath, DoNotStartThreshold, out var free))
            {
                _notify.Error($"На диске {Path.GetPathRoot(settings.VideoSavePath)} закончилось место ({free / 1024 / 1024} МБ). Запись не начата.");
                return;
            }

            _frame = new RecordingFrameWindow(region);
            _frame.Show();
            _panel = new VideoControlWindow(region, monitor.WorkArea, settings);
            var (fps0, _) = Quality(settings.VideoQualityLevel);
            var cameras = CameraCapture.List();
            _panel.SetCameraAvailable(cameras.Count > 0, settings.CameraDeviceId != AppSettings.NoCamera && cameras.Count > 0);
            _panel.CameraToggled += on =>
            {
                _store.Update(s => s.CameraDeviceId = on ? (cameras.Count > 0 ? cameras[0].Id : AppSettings.DefaultCamera) : AppSettings.NoCamera);
                if (on) ShowCamera(region, fps0); else HideCamera();
            };
            if (settings.CameraDeviceId != AppSettings.NoCamera && cameras.Count > 0) ShowCamera(region, fps0);
            var startTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _panel.StartRequested += () => startTcs.TrySetResult(true);
            _panel.CancelRequested += () => { if (!_state.IsRecording) startTcs.TrySetResult(false); else _ = CancelAsync(); };
            _panel.StopRequested += () => _ = StopAsync();
            _panel.PauseToggleRequested += () => _ = TogglePauseAsync();
            _panel.MicrophoneToggled += on => _store.Update(s => s.InputAudioDeviceId = on ? AppSettings.DefaultCommunications : AppSettings.NoSound);
            _panel.SystemAudioToggled += on => _store.Update(s => s.OutputAudioDeviceId = on ? AppSettings.DefaultConsole : AppSettings.NoSound);
            _panel.Closed += (_, _) => startTcs.TrySetResult(false);
            _panel.Show();
            _panel.Activate();

            if (!settings.StartRecordingImmediately)
            {
                if (!await startTcs.Task) return;
            }
            settings = _store.Current;

            var dir = settings.VideoSavePath;
            try { Directory.CreateDirectory(dir); }
            catch { dir = AppSettings.DefaultFolder(); Directory.CreateDirectory(dir); _notify.Warning($"Папка {settings.VideoSavePath} недоступна. Видео будет сохранено на Рабочий стол."); }
            _outputFile = FileNameTemplate.ResolveUniquePath(dir, settings.VideoFileNameTemplate, ".mp4", DateTime.Now, ForegroundWindow.Title(ForegroundWindow.Handle));

            var (fps, maxHeight) = Quality(settings.VideoQualityLevel);
            var options = new RecordingOptions
            {
                MonitorHandle = monitor.Handle,
                MonitorName = monitor.DeviceName,
                X = region.X - monitor.Bounds.X, Y = region.Y - monitor.Bounds.Y, Width = region.Width, Height = region.Height,
                MaxOutputHeight = maxHeight, FrameRate = fps, OutputFile = _outputFile,
                MicrophoneDeviceId = settings.InputAudioDeviceId, SystemAudioDeviceId = settings.OutputAudioDeviceId,
                MaxDurationMinutes = settings.MaxVideoDurationMinutes, ShowCursor = settings.CaptureCursorVideo,
                HighlightPointer = settings.HighlightMousePointer, HighlightClicks = settings.HighlightMouseClicks,
                MonitorLeft = monitor.Bounds.X, MonitorTop = monitor.Bounds.Y, Scale = monitor.Scale,
            };

            await _module.EnsureStartedAsync();
            _finishedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await _module.SendCheckedAsync("start", options.ToJson(), 15000);
            }
            catch (VideoModuleException ex) when (ex.Code == "MicrophoneAccessDenied")
            {
                _notify.Error("Доступ к микрофону запрещён в настройках Windows. Нажмите, чтобы открыть настройки.", OpenMicrophoneSettings);
                return;
            }
            catch (VideoModuleException ex) when (ex.Code == "CaptureUnsupported")
            {
                _notify.Error("Захват экрана не поддерживается: нужна Windows 10 2004 или новее.");
                return;
            }

            started = true;
            _module.MarkRecording(true);
            _state.SetRecording(true);
            _paused = false;
            _panel.SetRecording();
            _panel.SetDuration(TimeSpan.Zero, TimeSpan.FromMinutes(settings.MaxVideoDurationMinutes));
            if (!settings.ShowVideoControlPanel) _panel.Hide();
            StartDiskMonitor(dir);
            if (settings.PlaySound && !settings.SilentMode) SoundService.PlayRecordStart(settings.SoundRecordStartPath);
            _logger.LogInformation("Запись начата: {File}", _outputFile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось начать запись");
            _notify.Error("Не удалось начать запись видео: " + ex.Message);
        }
        finally
        {
            _busy = false;
            if (!started) Cleanup();
        }
    }

    public async Task StopAsync()
    {
        if (!_state.IsRecording) return;
        try { await _module.SendAsync("stop"); }
        catch (Exception ex) { _logger.LogError(ex, "Стоп"); _notify.Error("Не удалось остановить запись: " + ex.Message); Cleanup(); }
    }

    public async Task TogglePauseAsync()
    {
        if (!_state.IsRecording) return;
        try
        {
            var r = await _module.SendCheckedAsync("togglePause");
            _paused = r.Length > 1 && bool.TryParse(r[1], out var p) && p;
            _panel?.SetPaused(_paused);
            _frame?.SetPaused(_paused);
        }
        catch (Exception ex) { _logger.LogError(ex, "Пауза"); }
    }

    public async Task CancelAsync()
    {
        if (!_state.IsRecording) return;
        bool wasPaused = _paused;
        if (!wasPaused) await TogglePauseAsync();
        var answer = MessageBox.Show(_panel!, "Вы уверены, что хотите отменить запись?", "Отмена записи", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            try { await _module.SendAsync("cancel"); }
            catch (Exception ex) { _logger.LogError(ex, "Отмена"); Cleanup(); }
        }
        else if (!wasPaused)
        {
            await TogglePauseAsync();
        }
    }

    private void OnFinished(string status, string? code, string? message)
    {
        _logger.LogInformation("Запись завершена: {Status} {Code} {Message}", status, code, message);
        var file = _outputFile;
        Cleanup();
        switch (status)
        {
            case "ready" when file is not null && File.Exists(file):
                if (_store.Current.PlaySound && !_store.Current.SilentMode) SoundService.PlayRecordStop(_store.Current.SoundRecordStopPath);
                try { ClipboardService.SetFile(file); } catch (Exception ex) { _logger.LogWarning(ex, "Буфер обмена"); }
                var f = file;
                _notify.Info("Видео сохранено. Нажмите, чтобы открыть папку.", () => ScreenshotService.OpenFolderWithFile(f), respectSilentMode: true);
                if (_store.Current.OpenFileAfterSave && !_store.Current.SilentMode)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
                break;
            case "canceled":
                _logger.LogInformation("Запись отменена пользователем");
                break;
            default:
                _notify.Error("Ошибка записи видео: " + (message ?? "неизвестная ошибка"));
                break;
        }
    }

    private void StartDiskMonitor(string dir)
    {
        _warnedDisk = false;
        _diskTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _diskTimer.Tick += async (_, _) =>
        {
            if (!_state.IsRecording) return;
            if (!CheckDisk(dir, StopThreshold, out var free))
            {
                _logger.LogWarning("Мало места на диске ({Free} МБ), запись останавливается", free / 1024 / 1024);
                _notify.Warning("Запись остановлена: на диске закончилось место.");
                await StopAsync();
            }
            else if (!_warnedDisk && free < WarnThreshold)
            {
                _warnedDisk = true;
                _notify.Warning($"На диске {Path.GetPathRoot(dir)} заканчивается место ({free / 1024 / 1024} МБ). Рекомендуем завершить запись.");
            }
        };
        _diskTimer.Start();
    }

    private static bool CheckDisk(string dir, long threshold, out long free)
    {
        free = long.MaxValue;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dir));
            if (root is null) return true;
            free = new DriveInfo(root).AvailableFreeSpace;
            return free >= threshold;
        }
        catch { return true; }
    }

    private static (int Fps, int MaxHeight) Quality(VideoQualityLevel level) => level switch
    {
        VideoQualityLevel.SD => (15, 480),
        VideoQualityLevel.HD => (25, 720),
        VideoQualityLevel.FullHD => (30, 1080),
        VideoQualityLevel.FullHD60fps => (60, 1080),
        VideoQualityLevel.UHD4K => (30, 2160),
        _ => (25, 720),
    };

    private static void OpenMicrophoneSettings()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true }); } catch { }
    }

    private void ShowCamera(System.Drawing.Rectangle region, int fps)
    {
        if (_camera is not null) return;
        try
        {
            _camera = new CameraWindow(region, _store, _logger);
            _camera.Show();
            var deviceId = _store.Current.CameraDeviceId;
            _ = _camera.StartAsync(deviceId, fps);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Окно камеры");
            _notify.Error("Ошибка подключения к камере: " + ex.Message);
        }
    }

    private void HideCamera()
    {
        try { _camera?.Close(); } catch { }
        _camera = null;
    }

    private void Cleanup()
    {
        HideCamera();
        _diskTimer?.Stop();
        _diskTimer = null;
        _module.MarkRecording(false);
        _panel?.ForceClose();
        _panel = null;
        try { _frame?.Close(); } catch { }
        _frame = null;
        _paused = false;
        if (_state.Current == OperationType.Video) _state.End();
    }
}
