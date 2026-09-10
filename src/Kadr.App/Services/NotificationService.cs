using System.Windows;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Kadr.Common.Settings;
using Microsoft.Extensions.Logging;

namespace Kadr.App.Services;

/// <summary>Всплывающие уведомления трея. Клик выполняет привязанное действие, иначе открывает настройки.</summary>
public sealed class NotificationService
{
    private readonly SettingsStore _store;
    private readonly ILogger<NotificationService> _logger;
    private TaskbarIcon? _icon;
    private Action? _pendingClick;
    private Action? _defaultClick;

    public NotificationService(SettingsStore store, ILogger<NotificationService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public void Attach(TaskbarIcon icon, Action defaultClick)
    {
        _icon = icon;
        _defaultClick = defaultClick;
        icon.TrayBalloonTipClicked += (_, _) =>
        {
            var action = _pendingClick ?? _defaultClick;
            _pendingClick = null;
            try { action?.Invoke(); }
            catch (Exception ex) { _logger.LogError(ex, "Ошибка обработчика клика по уведомлению"); }
        };
        icon.TrayBalloonTipClosed += (_, _) => _pendingClick = null;
    }

    /// <param name="respectSilentMode">Уведомление об успехе: в тихом режиме не показывается.</param>
    public void Info(string text, Action? onClick = null, bool respectSilentMode = false)
        => Show(text, NotificationIcon.Info, onClick, respectSilentMode);

    public void Warning(string text, Action? onClick = null) => Show(text, NotificationIcon.Warning, onClick, false);

    public void Error(string text, Action? onClick = null) => Show(text, NotificationIcon.Error, onClick, false);

    private void Show(string text, NotificationIcon icon, Action? onClick, bool respectSilentMode)
    {
        if (respectSilentMode && _store.Current.SilentMode) return;
        _logger.LogInformation("Уведомление: {Text}", text);
        var app = Application.Current;
        if (app is null || _icon is null) return;
        app.Dispatcher.BeginInvoke(() =>
        {
            _pendingClick = onClick;
            try { _icon.ShowNotification("Kadr", text, icon); }
            catch (Exception ex) { _logger.LogWarning(ex, "Не удалось показать уведомление"); }
        });
    }
}
