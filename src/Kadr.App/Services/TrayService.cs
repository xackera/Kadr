using System.IO;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Kadr.Common.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Kadr.App.Services;

/// <summary>Иконка в трее, контекстное меню, действие по левому клику, смена иконки при записи и смене темы панели задач.</summary>
public sealed class TrayService : IDisposable
{
    private readonly SettingsStore _store;
    private readonly OperationState _state;
    private readonly IServiceProvider _services;
    private readonly ILogger<TrayService> _logger;

    private TaskbarIcon? _icon;
    private MenuItem? _miScreenshot, _miVideo, _miScroll, _miSilent;
    private bool _lastRecording;
    private bool _lastLight;

    public TrayService(SettingsStore store, OperationState state, IServiceProvider services, ILogger<TrayService> logger)
    {
        _store = store;
        _state = state;
        _services = services;
        _logger = logger;
    }

    public TaskbarIcon Icon => _icon ?? throw new InvalidOperationException("Трей не инициализирован");

    private AppCommands Commands => _services.GetRequiredService<AppCommands>();

    public void Initialize()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "Kadr",
            NoLeftClickDelay = true,
            MenuActivation = PopupActivationMode.RightClick,
            ContextMenu = BuildMenu(),
        };
        UpdateIcon(force: true);
        _icon.TrayLeftMouseUp += (_, _) => OnLeftClick();
        _icon.ForceCreate();

        _state.Changed += () => Application.Current?.Dispatcher.BeginInvoke(() => UpdateIcon(false));
        _store.Changed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(RefreshMenu);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _logger.LogInformation("Иконка в трее создана");
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
            Application.Current?.Dispatcher.BeginInvoke(() => UpdateIcon(false));
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        _miScreenshot = Item("Сделать скриншот", () => Commands.ScreenshotRegion());
        _miVideo = Item("Записать видео", () => Commands.ToggleVideo());
        _miScroll = Item("Скриншот с прокруткой", () => Commands.ScrollingCapture());
        _miSilent = new MenuItem { Header = "Тихий режим", IsCheckable = true };
        _miSilent.Click += (_, _) => _store.Update(s => s.SilentMode = _miSilent.IsChecked);

        menu.Items.Add(_miScreenshot);
        menu.Items.Add(_miVideo);
        menu.Items.Add(_miScroll);
        menu.Items.Add(new Separator());
        menu.Items.Add(_miSilent);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Настройки", () => Commands.ShowSettings()));
        menu.Items.Add(Item("О программе", () => Commands.ShowSettings(SettingsTab.About)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Выход", () => Commands.Exit()));
        RefreshMenu();
        return menu;

        static MenuItem Item(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            return mi;
        }
    }

    private void RefreshMenu()
    {
        var s = _store.Current;
        if (_miScreenshot != null) _miScreenshot.InputGestureText = s.HotkeyRegionScreenshot.ToString();
        if (_miVideo != null) _miVideo.InputGestureText = s.HotkeyVideoRecording.ToString();
        if (_miSilent != null) _miSilent.IsChecked = s.SilentMode;
    }

    private void OnLeftClick()
    {
        try
        {
            if (_state.IsRecording) { Commands.StopVideo(); return; }
            switch (_store.Current.TrayLeftClickAction)
            {
                case TrayClickAction.MakeRegionScreenshot: Commands.ScreenshotRegion(); break;
                case TrayClickAction.RecordVideo: Commands.ToggleVideo(); break;
                case TrayClickAction.MakeScrollingCapture: Commands.ScrollingCapture(); break;
                default: Commands.ShowTrayPanel(); break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки клика по иконке трея");
        }
    }

    private void UpdateIcon(bool force)
    {
        if (_icon is null) return;
        bool recording = _state.IsRecording;
        bool light = IsLightTaskbar();
        if (!force && recording == _lastRecording && light == _lastLight) return;
        _lastRecording = recording;
        _lastLight = light;
        var name = recording ? "kadr-rec.ico" : light ? "kadr-tray-dark.ico" : "kadr-tray.ico";
        try
        {
            var old = _icon.Icon;
            _icon.Icon = LoadIcon(name);
            old?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить иконку {Name}", name);
        }
    }

    public static System.Drawing.Icon LoadIcon(string fileName)
    {
        var uri = new Uri($"pack://application:,,,/Assets/{fileName}", UriKind.Absolute);
        using Stream stream = Application.GetResourceStream(uri)!.Stream;
        return new System.Drawing.Icon(stream);
    }

    private static bool IsLightTaskbar()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 0) is int i && i == 1;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _icon?.Dispose();
        _icon = null;
    }
}
