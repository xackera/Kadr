using System.Windows;
using Kadr.App.Views;
using Kadr.Common.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kadr.App.Services;

public enum SettingsTab { General = 0, Hotkeys = 1, Screenshots = 2, Video = 3, About = 4 }

/// <summary>Все действия приложения в одном месте: трей, хоткеи, командная строка и второй экземпляр вызывают их.</summary>
public sealed class AppCommands
{
    private readonly OperationState _state;
    private readonly ScreenshotService _screenshots;
    private readonly NotificationService _notify;
    private readonly IServiceProvider _services;
    private readonly ILogger<AppCommands> _logger;

    public AppCommands(OperationState state, ScreenshotService screenshots, NotificationService notify,
        IServiceProvider services, ILogger<AppCommands> logger)
    {
        _state = state;
        _screenshots = screenshots;
        _notify = notify;
        _services = services;
        _logger = logger;
    }

    public void Execute(string ipcCommand)
    {
        _logger.LogInformation("Команда: {Command}", ipcCommand);
        switch (ipcCommand)
        {
            case Ipc.Commands.ScreenshotRegion: ScreenshotRegion(); break;
            case Ipc.Commands.ScreenshotWindow: ScreenshotActiveWindow(); break;
            case Ipc.Commands.ScreenshotMonitor: ScreenshotActiveMonitor(); break;
            case Ipc.Commands.ScreenshotDesktop: ScreenshotDesktop(); break;
            case Ipc.Commands.RecordVideo: ToggleVideo(); break;
            case Ipc.Commands.ScrollingCapture: ScrollingCapture(); break;
            case Ipc.Commands.ShowSettings: ShowSettings(); break;
            case Ipc.Commands.Exit: Exit(); break;
            default: _logger.LogWarning("Неизвестная команда: {Command}", ipcCommand); break;
        }
    }

    // ---- Скриншоты

    /// <param name="fromHotkey">При вызове не с клавиатуры зажатый Ctrl инвертирует показ редактора.</param>
    public void ScreenshotRegion(bool fromHotkey = false)
    {
        bool invert = !fromHotkey && NativeMethods.IsKeyDown(0x11);
        var service = _services.GetRequiredService<RegionScreenshotService>();
        _ = service.RunAsync(invert).ContinueWith(t => _logger.LogError(t.Exception, "Скриншот области"), TaskContinuationOptions.OnlyOnFaulted);
    }

    public void ScreenshotActiveWindow() => _screenshots.Capture(ScreenshotMode.ActiveWindow);
    public void ScreenshotActiveMonitor() => _screenshots.Capture(ScreenshotMode.ActiveMonitor);
    public void ScreenshotDesktop() => _screenshots.Capture(ScreenshotMode.Desktop);

    // ---- Видео (этап 3)

    public void ToggleVideo()
    {
        if (_state.IsRecording) StopVideo();
        else RecordVideo();
    }

    private VideoRecordingService Video => _services.GetRequiredService<VideoRecordingService>();

    public void RecordVideo() => Fire(Video.StartAsync(), "Запись видео");
    public void StopVideo() => Fire(Video.StopAsync(), "Остановка записи");
    public void PauseVideo() { if (_state.IsRecording) Fire(Video.TogglePauseAsync(), "Пауза записи"); }

    private void Fire(Task task, string name)
        => _ = task.ContinueWith(t => _logger.LogError(t.Exception, "{Name}", name), TaskContinuationOptions.OnlyOnFaulted);

    // ---- Прокрутка (этап 4)

    public void ScrollingCapture() => Fire(_services.GetRequiredService<ScrollingCaptureService>().RunAsync(), "Скриншот с прокруткой");

    // ---- Окна

    public void ShowSettings(SettingsTab tab = SettingsTab.General) => SettingsWindow.ShowOrActivate(_services, tab);

    public void ShowTrayPanel() => TrayPanelWindow.ShowNearCursor(this);

    public void Exit()
    {
        _logger.LogInformation("Выход по команде пользователя");
        Application.Current.Shutdown();
    }
}
