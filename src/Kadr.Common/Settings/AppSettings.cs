using System.Text.Json.Serialization;
using Kadr.Common.Files;
using Kadr.Common.Hotkeys;

namespace Kadr.Common.Settings;

public enum StorageType { Clipboard, File, FileAndClipboard }
public enum ScreenshotFileType { Png, Jpeg }
public enum TrayClickAction { ShowPanel, MakeRegionScreenshot, RecordVideo, MakeScrollingCapture }
public enum EditorDefaultElement { None, Arrow, LastUsed }
public enum DrawingTool { None, Arrow, Line, Pensil, Marker, Rectangle, Oval, Text, Number, Blur }
public enum VideoQualityLevel { SD = 0, HD = 1, FullHD = 2, UHD4K = 3, FullHD60fps = 4 }
public enum CameraWindowShape { Tv, Circle }

/// <summary>Все настройки приложения. Имена ключей совместимы с settings.json оригинала, где это имеет смысл.</summary>
public sealed class AppSettings
{
    public const string NoSound = "NoSound";
    public const string DefaultConsole = "DefaultConsole";
    public const string DefaultCommunications = "DefaultCommunications";
    public const string NoCamera = "NoCamera";
    public const string DefaultCamera = "DefaultCamera";

    public static readonly int[] JpegQualityLevels = { 50, 60, 70, 80, 90, 95, 100 };

    // ---- Общие
    [JsonPropertyName("autostart_on")] public bool AutostartOn { get; set; } = false;
    [JsonPropertyName("silent_mode")] public bool SilentMode { get; set; } = false;
    [JsonPropertyName("tray_left_click_action")] public TrayClickAction TrayLeftClickAction { get; set; } = TrayClickAction.ShowPanel;
    [JsonPropertyName("check_for_updates")] public bool CheckForUpdates { get; set; } = false;

    // ---- Горячие клавиши
    [JsonPropertyName("hotkey_region_screenshot")] public Hotkey HotkeyRegionScreenshot { get; set; } = new(VirtualKeys.Snapshot);
    [JsonPropertyName("hotkey_active_window_screenshot")] public Hotkey HotkeyActiveWindowScreenshot { get; set; } = new(VirtualKeys.Snapshot, Alt: true);
    [JsonPropertyName("hotkey_full_screenshot")] public Hotkey HotkeyActiveMonitorScreenshot { get; set; } = new(VirtualKeys.Snapshot, Shift: true);
    [JsonPropertyName("hotkey_desktop_screenshot")] public Hotkey HotkeyDesktopScreenshot { get; set; } = new(VirtualKeys.Snapshot, Ctrl: true);
    [JsonPropertyName("hotkey_video_recoding_activation")] public Hotkey HotkeyVideoRecording { get; set; } = new(VirtualKeys.Snapshot, Ctrl: true, Shift: true);
    [JsonPropertyName("hotkey_video_recoding_pause")] public Hotkey HotkeyVideoPause { get; set; } = new(VirtualKeys.Pause, Shift: true);
    [JsonPropertyName("use_lr_mouse_for_regio_screenshot")] public bool UseMouseButtonsForScreenshot { get; set; } = false;
    [JsonPropertyName("use_lr_mouse_for_video_recording")] public bool UseMouseButtonsForVideo { get; set; } = false;

    // ---- Скриншоты
    [JsonPropertyName("storage_type")] public StorageType StorageType { get; set; } = StorageType.FileAndClipboard;
    [JsonPropertyName("screenshots_path")] public string ScreenshotsPath { get; set; } = DefaultFolder();
    [JsonPropertyName("screenshot_file_name_template")] public string ScreenshotFileNameTemplate { get; set; } = FileNameTemplate.DefaultScreenshot;
    [JsonPropertyName("screenshot_file_type")] public ScreenshotFileType ScreenshotFileType { get; set; } = ScreenshotFileType.Png;
    [JsonPropertyName("jpeg_quality")] public int JpegQuality { get; set; } = 80;
    [JsonPropertyName("capture_cursor")] public bool CaptureCursor { get; set; } = false;
    [JsonPropertyName("use_previously_selected_region")] public bool UsePreviouslySelectedRegion { get; set; } = false;
    [JsonPropertyName("screenshot_previously_selected_region")] public string PreviouslySelectedRegion { get; set; } = "";
    [JsonPropertyName("play_sound")] public bool PlaySound { get; set; } = true;
    /// <summary>Свои файлы звуков (.wav). Пусто — встроенный синтезированный звук.</summary>
    [JsonPropertyName("sound_shutter_path")] public string SoundShutterPath { get; set; } = "";
    [JsonPropertyName("sound_record_start_path")] public string SoundRecordStartPath { get; set; } = "";
    [JsonPropertyName("sound_record_stop_path")] public string SoundRecordStopPath { get; set; } = "";
    [JsonPropertyName("select_and_draw_mode")] public bool ShowEditor { get; set; } = true;
    [JsonPropertyName("editor_default_element")] public EditorDefaultElement EditorDefaultElement { get; set; } = EditorDefaultElement.None;
    [JsonPropertyName("editor_selected_tool")] public DrawingTool EditorSelectedTool { get; set; } = DrawingTool.None;
    [JsonPropertyName("editor_pen_radius")] public int EditorLineThickness { get; set; } = 6;
    [JsonPropertyName("editor_color")] public string EditorColor { get; set; } = "#FF3B30";
    [JsonPropertyName("editor_available_colors")] public List<string> EditorAvailableColors { get; set; } = new();
    [JsonPropertyName("draw_object_shadows")] public bool DrawObjectShadows { get; set; } = true;
    [JsonPropertyName("open_file_after_save")] public bool OpenFileAfterSave { get; set; } = false;

    // ---- Видео
    [JsonPropertyName("video_save_path")] public string VideoSavePath { get; set; } = DefaultFolder();
    [JsonPropertyName("video_file_name_template")] public string VideoFileNameTemplate { get; set; } = FileNameTemplate.DefaultVideo;
    [JsonPropertyName("video_quality_level")] public VideoQualityLevel VideoQualityLevel { get; set; } = VideoQualityLevel.HD;
    [JsonPropertyName("capture_cursor_video")] public bool CaptureCursorVideo { get; set; } = true;
    [JsonPropertyName("highlight_mouse_pointer")] public bool HighlightMousePointer { get; set; } = false;
    [JsonPropertyName("highlight_mouse_clicks")] public bool HighlightMouseClicks { get; set; } = true;
    [JsonPropertyName("show_video_control_panel")] public bool ShowVideoControlPanel { get; set; } = true;
    [JsonPropertyName("start_recording_video_immediately_after_selection")] public bool StartRecordingImmediately { get; set; } = false;
    [JsonPropertyName("capture_camera_device_id")] public string CameraDeviceId { get; set; } = NoCamera;
    [JsonPropertyName("capture_input_audio_device_id")] public string InputAudioDeviceId { get; set; } = DefaultCommunications;
    [JsonPropertyName("capture_output_audio_device_id")] public string OutputAudioDeviceId { get; set; } = DefaultConsole;
    [JsonPropertyName("camera_window_shape")] public CameraWindowShape CameraWindowShape { get; set; } = CameraWindowShape.Tv;
    [JsonPropertyName("camera_window_relative_rect")] public string CameraWindowRelativeRect { get; set; } = "0.05,0.7,0.15,0.25";
    [JsonPropertyName("max_video_duration_minutes")] public int MaxVideoDurationMinutes { get; set; } = 180;

    public static string DefaultFolder() => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
