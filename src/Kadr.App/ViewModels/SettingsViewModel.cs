using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kadr.App.Services;
using Kadr.App.Startup;
using Kadr.Common.Files;
using Kadr.Common.Hotkeys;
using Kadr.Common.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Kadr.App.ViewModels;

public sealed record Option<T>(T Key, string Value);

/// <summary>Модель окна настроек: каждое свойство читает текущие настройки и сохраняет их сразу при изменении.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly AppPaths _paths;
    private readonly NotificationService _notify;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(SettingsStore store, AppPaths paths, NotificationService notify, ILogger<SettingsViewModel> logger)
    {
        _store = store;
        _paths = paths;
        _notify = notify;
        _logger = logger;
    }

    private AppSettings S => _store.Current;

    private void Set<T>(T value, Action<AppSettings, T> apply, [CallerMemberName] string? name = null)
    {
        _store.Update(s => apply(s, value));
        OnPropertyChanged(name);
    }

    // ---- Списки для выпадающих меню

    public IReadOnlyList<Option<TrayClickAction>> TrayActions { get; } = new[]
    {
        new Option<TrayClickAction>(TrayClickAction.ShowPanel, "Показать панель выбора"),
        new Option<TrayClickAction>(TrayClickAction.MakeRegionScreenshot, "Скриншот области экрана"),
        new Option<TrayClickAction>(TrayClickAction.RecordVideo, "Запись видео"),
        new Option<TrayClickAction>(TrayClickAction.MakeScrollingCapture, "Скриншот с прокруткой"),
    };

    public IReadOnlyList<Option<ScreenshotFileType>> FileTypes { get; } = new[]
    {
        new Option<ScreenshotFileType>(ScreenshotFileType.Png, "PNG"),
        new Option<ScreenshotFileType>(ScreenshotFileType.Jpeg, "JPEG"),
    };

    public IReadOnlyList<Option<int>> JpegQualities { get; } = AppSettings.JpegQualityLevels.Select(q => new Option<int>(q, q.ToString())).ToList();

    public IReadOnlyList<Option<EditorDefaultElement>> EditorDefaults { get; } = new[]
    {
        new Option<EditorDefaultElement>(EditorDefaultElement.None, "Перемещение рамки"),
        new Option<EditorDefaultElement>(EditorDefaultElement.Arrow, "Стрелка"),
        new Option<EditorDefaultElement>(EditorDefaultElement.LastUsed, "Запоминать последний выбор"),
    };

    public IReadOnlyList<Option<VideoQualityLevel>> VideoQualities { get; } = new[]
    {
        new Option<VideoQualityLevel>(VideoQualityLevel.SD, "Низкое (SD/480p, 15 fps)"),
        new Option<VideoQualityLevel>(VideoQualityLevel.HD, "Среднее (HD/720p, 25 fps)"),
        new Option<VideoQualityLevel>(VideoQualityLevel.FullHD, "Высокое (FullHD/1080p, 30 fps)"),
        new Option<VideoQualityLevel>(VideoQualityLevel.FullHD60fps, "Высокое+ (FullHD/1080p, 60 fps)"),
        new Option<VideoQualityLevel>(VideoQualityLevel.UHD4K, "Максимальное (4K/2160p, 30 fps)"),
    };

    // ---- Общие

    public bool AutostartOn
    {
        get => S.AutostartOn;
        set
        {
            try
            {
                AutostartService.Apply(value);
                Set(value, (s, v) => s.AutostartOn = v);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось изменить автозапуск");
                _notify.Error("Не удалось изменить настройки автозапуска: " + ex.Message);
                Set(false, (s, v) => s.AutostartOn = v);
            }
        }
    }

    public bool SilentMode { get => S.SilentMode; set => Set(value, (s, v) => s.SilentMode = v); }
    public TrayClickAction TrayLeftClickAction { get => S.TrayLeftClickAction; set => Set(value, (s, v) => s.TrayLeftClickAction = v); }

    // ---- Горячие клавиши

    public Hotkey HotkeyRegion { get => S.HotkeyRegionScreenshot; set => SetHotkey(value, (s, v) => s.HotkeyRegionScreenshot = v); }
    public Hotkey HotkeyActiveWindow { get => S.HotkeyActiveWindowScreenshot; set => SetHotkey(value, (s, v) => s.HotkeyActiveWindowScreenshot = v); }
    public Hotkey HotkeyActiveMonitor { get => S.HotkeyActiveMonitorScreenshot; set => SetHotkey(value, (s, v) => s.HotkeyActiveMonitorScreenshot = v); }
    public Hotkey HotkeyDesktop { get => S.HotkeyDesktopScreenshot; set => SetHotkey(value, (s, v) => s.HotkeyDesktopScreenshot = v); }
    public Hotkey HotkeyVideo { get => S.HotkeyVideoRecording; set => SetHotkey(value, (s, v) => s.HotkeyVideoRecording = v); }
    public Hotkey HotkeyPause { get => S.HotkeyVideoPause; set => SetHotkey(value, (s, v) => s.HotkeyVideoPause = v); }
    public bool UseMouseButtonsForScreenshot { get => S.UseMouseButtonsForScreenshot; set => Set(value, (s, v) => s.UseMouseButtonsForScreenshot = v); }
    public bool UseMouseButtonsForVideo { get => S.UseMouseButtonsForVideo; set => Set(value, (s, v) => s.UseMouseButtonsForVideo = v); }

    /// <summary>Windows отдаёт PrtScr «Ножницам». Kadr перехватывает клавишу раньше, но перехват можно и отключить.</summary>
    public bool PrintScreenTakenBySystem => WindowsKeyboardSettings.PrintScreenOpensSnippingTool();

    [RelayCommand]
    private void DisableSystemPrintScreen()
    {
        try
        {
            WindowsKeyboardSettings.DisablePrintScreenForSnipping();
            _logger.LogInformation("Системный перехват PrtScr отключён");
            _notify.Info("Перехват PrtScr «Ножницами» отключён. Если клавиша всё ещё открывает их, перезайдите в Windows.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось отключить перехват PrtScr");
            _notify.Error("Не удалось изменить настройку Windows: " + ex.Message);
        }
        OnPropertyChanged(nameof(PrintScreenTakenBySystem));
    }

    private void SetHotkey(Hotkey value, Action<AppSettings, Hotkey> apply, [CallerMemberName] string? name = null)
    {
        if (!value.IsEmpty)
        {
            var all = new[] { S.HotkeyRegionScreenshot, S.HotkeyActiveWindowScreenshot, S.HotkeyActiveMonitorScreenshot,
                              S.HotkeyDesktopScreenshot, S.HotkeyVideoRecording, S.HotkeyVideoPause };
            var probe = S.Clone(); apply(probe, Hotkey.Empty);
            var others = new[] { probe.HotkeyRegionScreenshot, probe.HotkeyActiveWindowScreenshot, probe.HotkeyActiveMonitorScreenshot,
                                 probe.HotkeyDesktopScreenshot, probe.HotkeyVideoRecording, probe.HotkeyVideoPause };
            if (others.Contains(value))
            {
                _notify.Warning($"Сочетание {value} уже используется другим действием.");
                OnPropertyChanged(name);
                return;
            }
        }
        Set(value, apply, name);
    }

    // ---- Скриншоты

    public bool SaveToFile
    {
        get => S.StorageType != StorageType.Clipboard;
        set => SetStorage(value, CopyToClipboard);
    }

    public bool CopyToClipboard
    {
        get => S.StorageType != StorageType.File;
        set => SetStorage(SaveToFile, value);
    }

    private void SetStorage(bool file, bool clipboard)
    {
        var type = file && clipboard ? StorageType.FileAndClipboard : file ? StorageType.File : StorageType.Clipboard;
        _store.Update(s => s.StorageType = type);
        OnPropertyChanged(nameof(SaveToFile));
        OnPropertyChanged(nameof(CopyToClipboard));
    }

    public string ScreenshotsPath { get => S.ScreenshotsPath; set => Set(value, (s, v) => s.ScreenshotsPath = v); }

    public string ScreenshotFileNameTemplate
    {
        get => S.ScreenshotFileNameTemplate;
        set
        {
            var error = FileNameTemplate.Validate(value);
            TemplateError = error ?? "";
            if (error is null) Set(value, (s, v) => s.ScreenshotFileNameTemplate = v);
            else OnPropertyChanged();
        }
    }

    [ObservableProperty] private string _templateError = "";

    public string TemplatePreview => FileNameTemplate.Resolve(S.ScreenshotFileNameTemplate, DateTime.Now, "Заголовок окна", 1)
                                     + (S.ScreenshotFileType == ScreenshotFileType.Jpeg ? ".jpg" : ".png");

    public ScreenshotFileType ScreenshotFileType
    {
        get => S.ScreenshotFileType;
        set { Set(value, (s, v) => s.ScreenshotFileType = v); OnPropertyChanged(nameof(IsJpeg)); OnPropertyChanged(nameof(TemplatePreview)); }
    }

    public bool IsJpeg => S.ScreenshotFileType == ScreenshotFileType.Jpeg;
    public int JpegQuality { get => S.JpegQuality; set => Set(value, (s, v) => s.JpegQuality = v); }
    public bool CaptureCursor { get => S.CaptureCursor; set => Set(value, (s, v) => s.CaptureCursor = v); }
    public bool UsePreviouslySelectedRegion { get => S.UsePreviouslySelectedRegion; set => Set(value, (s, v) => s.UsePreviouslySelectedRegion = v); }
    public bool PlaySound { get => S.PlaySound; set => Set(value, (s, v) => s.PlaySound = v); }

    // ---- Свои файлы звуков (.wav). Пусто — встроенный звук.

    public string ShutterSoundDisplay => Describe(S.SoundShutterPath);
    public string RecordStartSoundDisplay => Describe(S.SoundRecordStartPath);
    public string RecordStopSoundDisplay => Describe(S.SoundRecordStopPath);

    private static string Describe(string? path)
        => string.IsNullOrWhiteSpace(path) ? "Встроенный звук"
           : File.Exists(path) ? Path.GetFileName(path)
           : Path.GetFileName(path) + " (файл не найден)";

    private string GetSoundPath(string kind) => kind switch
    {
        "start" => S.SoundRecordStartPath,
        "stop" => S.SoundRecordStopPath,
        _ => S.SoundShutterPath,
    };

    private void SetSoundPath(string kind, string value)
    {
        _store.Update(s =>
        {
            switch (kind)
            {
                case "start": s.SoundRecordStartPath = value; break;
                case "stop": s.SoundRecordStopPath = value; break;
                default: s.SoundShutterPath = value; break;
            }
        });
        OnPropertyChanged(nameof(ShutterSoundDisplay));
        OnPropertyChanged(nameof(RecordStartSoundDisplay));
        OnPropertyChanged(nameof(RecordStopSoundDisplay));
    }

    [RelayCommand]
    private void BrowseSound(string kind)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите звуковой файл",
            Filter = "Звук WAV|*.wav",
            CheckFileExists = true,
        };
        var current = GetSoundPath(kind);
        if (!string.IsNullOrWhiteSpace(current) && File.Exists(current)) dlg.FileName = current;
        if (dlg.ShowDialog() == true) SetSoundPath(kind, dlg.FileName);
    }

    [RelayCommand]
    private void ClearSound(string kind) => SetSoundPath(kind, "");

    [RelayCommand]
    private void TestSound(string kind)
    {
        var path = GetSoundPath(kind);
        switch (kind)
        {
            case "start": SoundService.PlayRecordStart(path); break;
            case "stop": SoundService.PlayRecordStop(path); break;
            default: SoundService.PlayShutter(path); break;
        }
    }
    public bool OpenFileAfterSave { get => S.OpenFileAfterSave; set => Set(value, (s, v) => s.OpenFileAfterSave = v); }
    public bool ShowEditor { get => S.ShowEditor; set => Set(value, (s, v) => s.ShowEditor = v); }
    public EditorDefaultElement EditorDefaultElement { get => S.EditorDefaultElement; set => Set(value, (s, v) => s.EditorDefaultElement = v); }
    public bool DrawObjectShadows { get => S.DrawObjectShadows; set => Set(value, (s, v) => s.DrawObjectShadows = v); }

    // ---- Видео

    public IReadOnlyList<Option<string>> MicrophoneDevices { get; } = BuildAudioList(input: true);
    public IReadOnlyList<Option<string>> SystemAudioDevices { get; } = BuildAudioList(input: false);

    private static IReadOnlyList<Option<string>> BuildAudioList(bool input)
    {
        var list = new List<Option<string>> { new(AppSettings.NoSound, input ? "Без микрофона" : "Без системного звука") };
        list.Add(input ? new Option<string>(AppSettings.DefaultCommunications, "Микрофон по умолчанию") : new Option<string>(AppSettings.DefaultConsole, "Устройство вывода по умолчанию"));
        foreach (var d in Kadr.Recording.AudioDevices.List(input)) list.Add(new Option<string>(d.Id, d.Name));
        return list;
    }

    public IReadOnlyList<Option<string>> CameraDevices { get; } = BuildCameraList();

    private static IReadOnlyList<Option<string>> BuildCameraList()
    {
        var list = new List<Option<string>> { new(AppSettings.NoCamera, "Без камеры") };
        var cams = CameraCapture.List();
        if (cams.Count > 0) list.Add(new Option<string>(AppSettings.DefaultCamera, "Камера по умолчанию"));
        foreach (var c in cams) list.Add(new Option<string>(c.Id, c.Name));
        return list;
    }

    public string CameraDeviceId
    {
        get => CameraDevices.Any(o => o.Key == S.CameraDeviceId) ? S.CameraDeviceId : AppSettings.NoCamera;
        set => Set(value, (s, v) => s.CameraDeviceId = v);
    }

    public string InputAudioDeviceId
    {
        get => MicrophoneDevices.Any(o => o.Key == S.InputAudioDeviceId) ? S.InputAudioDeviceId : AppSettings.DefaultCommunications;
        set => Set(value, (s, v) => s.InputAudioDeviceId = v);
    }

    public string OutputAudioDeviceId
    {
        get => SystemAudioDevices.Any(o => o.Key == S.OutputAudioDeviceId) ? S.OutputAudioDeviceId : AppSettings.DefaultConsole;
        set => Set(value, (s, v) => s.OutputAudioDeviceId = v);
    }

    public string VideoSavePath { get => S.VideoSavePath; set => Set(value, (s, v) => s.VideoSavePath = v); }
    public string VideoFileNameTemplate
    {
        get => S.VideoFileNameTemplate;
        set { if (FileNameTemplate.Validate(value) is null) Set(value, (s, v) => s.VideoFileNameTemplate = v); else OnPropertyChanged(); }
    }
    public VideoQualityLevel VideoQualityLevel { get => S.VideoQualityLevel; set => Set(value, (s, v) => s.VideoQualityLevel = v); }
    public bool CaptureCursorVideo { get => S.CaptureCursorVideo; set => Set(value, (s, v) => s.CaptureCursorVideo = v); }
    public bool HighlightMousePointer { get => S.HighlightMousePointer; set => Set(value, (s, v) => s.HighlightMousePointer = v); }
    public bool HighlightMouseClicks { get => S.HighlightMouseClicks; set => Set(value, (s, v) => s.HighlightMouseClicks = v); }
    public bool ShowVideoControlPanel { get => S.ShowVideoControlPanel; set => Set(value, (s, v) => s.ShowVideoControlPanel = v); }
    public bool StartRecordingImmediately { get => S.StartRecordingImmediately; set => Set(value, (s, v) => s.StartRecordingImmediately = v); }
    public int MaxVideoDurationMinutes { get => S.MaxVideoDurationMinutes; set => Set(Math.Clamp(value, 1, 600), (s, v) => s.MaxVideoDurationMinutes = v); }

    // ---- О программе

    public string Version => "Kadr " + (typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
    public string DataDirectory => _paths.DataDirectory;
    public string SettingsFile => _store.FilePath;

    // ---- Команды

    [RelayCommand]
    private void BrowseScreenshotsFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Куда сохранять скриншоты", InitialDirectory = ScreenshotsPath };
        if (dlg.ShowDialog() == true) ScreenshotsPath = dlg.FolderName;
    }

    [RelayCommand]
    private void BrowseVideoFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Куда сохранять видео", InitialDirectory = VideoSavePath };
        if (dlg.ShowDialog() == true) VideoSavePath = dlg.FolderName;
    }

    [RelayCommand]
    private void OpenLogFolder() => Process.Start(new ProcessStartInfo(_paths.LogDirectory) { UseShellExecute = true });

    [RelayCommand]
    private void OpenDataFolder() => Process.Start(new ProcessStartInfo(_paths.DataDirectory) { UseShellExecute = true });

    [RelayCommand]
    private void ResetTemplate() => ScreenshotFileNameTemplate = FileNameTemplate.DefaultScreenshot;

    public void RefreshAll()
    {
        OnPropertyChanged(string.Empty);
    }
}
