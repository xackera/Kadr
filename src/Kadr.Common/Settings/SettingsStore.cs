using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kadr.Common.Files;
using Kadr.Common.Hotkeys;
using Microsoft.Extensions.Logging;

namespace Kadr.Common.Settings;

/// <summary>
/// Хранение настроек в settings.json: устойчивая загрузка, нормализация, атомарная запись, событие изменения.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new TolerantEnumConverterFactory() },
    };

    private readonly ILogger _logger;
    private readonly object _sync = new();
    private string _lastSavedJson = "";

    public string FilePath { get; }
    public AppSettings Current { get; private set; } = new();

    /// <summary>Вызывается после сохранения, если JSON изменился. Аргументы: старые и новые настройки.</summary>
    public event Action<AppSettings, AppSettings>? Changed;

    public SettingsStore(string directory, ILogger<SettingsStore> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "settings.json");
    }

    public void Load()
    {
        lock (_sync)
        {
            AppSettings loaded;
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                }
                else
                {
                    loaded = new AppSettings();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось прочитать настройки, используются значения по умолчанию");
                loaded = new AppSettings();
            }

            Current = loaded;
            Normalize(Current, _logger);
            SaveCore(raiseChanged: false);
        }
    }

    /// <summary>Изменить настройки и сохранить.</summary>
    public void Update(Action<AppSettings> mutate)
    {
        lock (_sync)
        {
            var old = Current.Clone();
            mutate(Current);
            Normalize(Current, _logger);
            SaveCore(raiseChanged: true, old);
        }
    }

    private void SaveCore(bool raiseChanged, AppSettings? old = null)
    {
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        if (json == _lastSavedJson) return;
        try
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
            _lastSavedJson = json;
            _logger.LogDebug("Настройки сохранены");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить настройки в {Path}", FilePath);
        }
        if (raiseChanged && old is not null) Changed?.Invoke(old, Current);
    }

    public static void Normalize(AppSettings s, ILogger? logger = null)
    {
        s.JpegQuality = AppSettings.JpegQualityLevels.MinBy(q => Math.Abs(q - s.JpegQuality));
        if (!Enum.IsDefined(s.VideoQualityLevel)) s.VideoQualityLevel = VideoQualityLevel.HD;
        if (s.MaxVideoDurationMinutes <= 0) s.MaxVideoDurationMinutes = 180;
        if (s.EditorLineThickness < 2 || s.EditorLineThickness > 60) s.EditorLineThickness = 6;

        s.ScreenshotsPath = NormalizePath(s.ScreenshotsPath, logger);
        s.VideoSavePath = NormalizePath(s.VideoSavePath, logger);

        if (FileNameTemplate.Validate(s.ScreenshotFileNameTemplate) is not null) s.ScreenshotFileNameTemplate = FileNameTemplate.DefaultScreenshot;
        if (FileNameTemplate.Validate(s.VideoFileNameTemplate) is not null) s.VideoFileNameTemplate = FileNameTemplate.DefaultVideo;

        // Дубликаты хоткеев: приоритет область, окно, монитор, рабочий стол, видео, пауза.
        var seen = new HashSet<Hotkey>();
        s.HotkeyRegionScreenshot = Dedupe(s.HotkeyRegionScreenshot, seen);
        s.HotkeyActiveWindowScreenshot = Dedupe(s.HotkeyActiveWindowScreenshot, seen);
        s.HotkeyActiveMonitorScreenshot = Dedupe(s.HotkeyActiveMonitorScreenshot, seen);
        s.HotkeyDesktopScreenshot = Dedupe(s.HotkeyDesktopScreenshot, seen);
        s.HotkeyVideoRecording = Dedupe(s.HotkeyVideoRecording, seen);
        s.HotkeyVideoPause = Dedupe(s.HotkeyVideoPause, seen);
    }

    private static Hotkey Dedupe(Hotkey h, HashSet<Hotkey> seen)
    {
        if (h.IsEmpty) return h;
        if (!h.IsValid || !seen.Add(h)) return Hotkey.Empty;
        return h;
    }

    private static string NormalizePath(string? path, ILogger? logger)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path)) return Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Некорректный путь '{Path}', используется Рабочий стол", path);
        }
        return AppSettings.DefaultFolder();
    }
}
