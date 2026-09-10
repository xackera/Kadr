using Kadr.Capture;

namespace Kadr.App.Editor;

public enum OverlayMode { Screenshot, Video, Scrolling }

public enum OverlayState
{
    Idle, Selecting, Selected, MovingFrame, ResizingFrame,
    ToolSelected, DrawingObject, MovingObject, ResizingObject, EditingText,
    Recording, Scrolling, Canceled,
}

public enum EditorActionKind { None, Default, CopyToClipboard, SaveToFile, Print, DoOcr, Close, Start }

/// <summary>Результат сессии оверлея: изображение области (для скриншота) либо только область (видео, прокрутка).</summary>
public sealed record OverlayResult(
    EditorActionKind Action,
    System.Drawing.Bitmap? Image,
    System.Drawing.Rectangle ScreenRect,
    MonitorInfo Monitor,
    string? SaveFilePath)
{
    /// <summary>Диалог печати, подтверждённый пользователем поверх оверлея (для действия Print).</summary>
    public System.Windows.Controls.PrintDialog? PrintDialog { get; init; }

    /// <summary>Состояние редактора на момент завершения, чтобы запомнить его в настройках.</summary>
    public Kadr.Common.Settings.DrawingTool Tool { get; init; }
    public double Thickness { get; init; }
    public string Color { get; init; } = "";
    public List<string> Palette { get; init; } = new();
}
