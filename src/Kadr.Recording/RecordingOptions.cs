using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kadr.Recording;

/// <summary>Параметры записи; передаются видеомодулю одной JSON-строкой.</summary>
public sealed class RecordingOptions
{
    [JsonPropertyName("monitor")] public long MonitorHandle { get; set; }
    [JsonPropertyName("monitorName")] public string MonitorName { get; set; } = "";
    /// <summary>Область в физических пикселях относительно левого верхнего угла монитора; ширина и высота чётные.</summary>
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("maxHeight")] public int MaxOutputHeight { get; set; } = 720;
    [JsonPropertyName("fps")] public int FrameRate { get; set; } = 25;
    [JsonPropertyName("output")] public string OutputFile { get; set; } = "";
    [JsonPropertyName("mic")] public string MicrophoneDeviceId { get; set; } = "NoSound";
    [JsonPropertyName("sys")] public string SystemAudioDeviceId { get; set; } = "NoSound";
    [JsonPropertyName("maxMinutes")] public int MaxDurationMinutes { get; set; } = 180;
    [JsonPropertyName("cursor")] public bool ShowCursor { get; set; } = true;
    [JsonPropertyName("highlightPointer")] public bool HighlightPointer { get; set; }
    [JsonPropertyName("highlightClicks")] public bool HighlightClicks { get; set; }
    /// <summary>Левый верхний угол монитора в экранных координатах и масштаб DPI: для подсветки курсора.</summary>
    [JsonPropertyName("monitorLeft")] public int MonitorLeft { get; set; }
    [JsonPropertyName("monitorTop")] public int MonitorTop { get; set; }
    [JsonPropertyName("scale")] public double Scale { get; set; } = 1.0;

    public string ToJson() => JsonSerializer.Serialize(this);
    public static RecordingOptions FromJson(string json) => JsonSerializer.Deserialize<RecordingOptions>(json) ?? throw new FormatException("Некорректные параметры записи");

    public (int Width, int Height) OutputSize()
    {
        if (MaxOutputHeight <= 0 || Height <= MaxOutputHeight) return (Width, Height);
        var h = MaxOutputHeight;
        var w = 2 * (int)Math.Round((double)Width * h / Height / 2);
        return (Math.Max(2, w), h);
    }
}

public enum RecorderErrorCode { General, CaptureUnsupported, MicrophoneAccessDenied, EncoderFailed }

public sealed class RecorderException : Exception
{
    public RecorderErrorCode Code { get; }
    public RecorderException(RecorderErrorCode code, string message, Exception? inner = null) : base(message, inner) => Code = code;
}
