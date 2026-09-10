using System.Windows;
using System.Windows.Media;
using Kadr.App.Editor.Objects;
using Kadr.App.Services;
using Kadr.Capture;
using Kadr.Common.Settings;
using Microsoft.Extensions.Logging;

namespace Kadr.App.Editor;

/// <summary>
/// Одна сессия выделения/редактирования: окна на всех мониторах, общая область, инструмент, цвет, толщина, undo.
/// Завершается результатом (изображение и область) либо null при отмене.
/// </summary>
public sealed class OverlaySession
{
    public static readonly Color[] DefaultPalette =
    {
        Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xFF, 0x3B, 0x30),
        Color.FromRgb(0xFF, 0x95, 0x00), Color.FromRgb(0xFF, 0xCC, 0x00), Color.FromRgb(0x34, 0xC7, 0x59),
        Color.FromRgb(0x5A, 0xC8, 0xFA), Color.FromRgb(0x00, 0x7A, 0xFF), Color.FromRgb(0xAF, 0x52, 0xDE),
    };

    private readonly TaskCompletionSource<OverlayResult?> _completion = new();
    private readonly ILogger _logger;
    private bool _finished;
    private Rect? _selectionBeforeSelectAll;

    public OverlayMode Mode { get; }
    public AppSettings Settings { get; }
    public bool EditorEnabled { get; }
    public List<OverlayWindow> Windows { get; } = new();

    public OverlayWindow? Owner { get; private set; }
    public Rect Selection { get; private set; } = Rect.Empty;
    public bool HasSelection => Owner is not null && !Selection.IsEmpty && Selection.Width >= 1 && Selection.Height >= 1;

    public OverlayState State { get; private set; } = OverlayState.Idle;
    public DrawingTool Tool { get; private set; } = DrawingTool.None;
    public Color Color { get; private set; }
    public double Thickness { get; private set; }
    public bool Shadows { get; }
    public List<Color> Palette { get; }
    public UndoManager Undo { get; } = new();
    public VisualObject? SelectedObject { get; private set; }
    public VisualObject? LastObject { get; set; }
    public System.Windows.Controls.PrintDialog? PrintDialog { get; private set; }

    /// <summary>Любое изменение состояния: окна перерисовывают рамку, панели, курсор.</summary>
    public event Action? Changed;

    private OverlaySession(OverlayMode mode, AppSettings settings, bool editorEnabled, ILogger logger)
    {
        Mode = mode;
        Settings = settings;
        EditorEnabled = editorEnabled;
        _logger = logger;
        Shadows = settings.DrawObjectShadows;
        Thickness = Math.Clamp(settings.EditorLineThickness, VisualObject.MinThickness, VisualObject.MaxThickness);
        Color = TryParseColor(settings.EditorColor) ?? DefaultPalette[2];
        Palette = settings.EditorAvailableColors.Select(TryParseColor).Where(c => c.HasValue).Select(c => c!.Value).ToList();
        if (Palette.Count == 0) Palette = DefaultPalette.ToList();
        Tool = settings.EditorDefaultElement switch
        {
            EditorDefaultElement.Arrow => DrawingTool.Arrow,
            EditorDefaultElement.LastUsed => settings.EditorSelectedTool,
            _ => DrawingTool.None,
        };
    }

    public static Task<OverlayResult?> RunAsync(OverlayMode mode, AppSettings settings, bool editorEnabled, ILogger logger)
    {
        var session = new OverlaySession(mode, settings, editorEnabled, logger);
        session.Open();
        return session._completion.Task;
    }

    private void Open()
    {
        var monitors = Monitors.All();
        var cursor = NativeMethods.CursorPosition();
        foreach (var m in monitors)
        {
            System.Drawing.Bitmap? background = null;
            if (Mode == OverlayMode.Screenshot)
            {
                try { background = ScreenCapture.CaptureMonitor(m, Settings.CaptureCursor); }
                catch (Exception ex) { _logger.LogError(ex, "Не удалось снять монитор {Monitor}", m); }
            }
            Windows.Add(new OverlayWindow(this, m, background));
        }
        foreach (var w in Windows) w.Show();

        // Восстановить прошлую область, если она целиком на одном мониторе.
        if (Settings.UsePreviouslySelectedRegion && TryParseRect(Settings.PreviouslySelectedRegion, out var prev))
        {
            var owner = Windows.FirstOrDefault(w => w.Monitor.Bounds.Contains(prev));
            if (owner is not null && prev.Width >= 11 && prev.Height >= 11)
            {
                Owner = owner;
                Selection = owner.FromScreenRect(prev);
                State = Tool == DrawingTool.None ? OverlayState.Selected : OverlayState.ToolSelected;
            }
        }

        var active = Windows.FirstOrDefault(w => w.Monitor.Bounds.Contains(cursor)) ?? Windows[0];
        active.ActivateAndFocus();
        NotifyChanged();
    }

    public void NotifyChanged() => Changed?.Invoke();

    public void SetState(OverlayState state)
    {
        State = state;
        NotifyChanged();
    }

    public void SetSelection(OverlayWindow owner, Rect rect)
    {
        if (Owner is not null && Owner != owner) SelectedObject = null;
        Owner = owner;
        Selection = rect;
        NotifyChanged();
    }

    public void ClearSelection()
    {
        Owner = null;
        Selection = Rect.Empty;
        SelectedObject = null;
        _selectionBeforeSelectAll = null;
        SetState(OverlayState.Idle);
    }

    /// <summary>Ctrl+A: весь монитор; повторно возвращает прежнюю область.</summary>
    public void SelectAll(OverlayWindow window)
    {
        var full = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
        if (Owner == window && Selection == full && _selectionBeforeSelectAll is { } prev)
        {
            Selection = prev;
            _selectionBeforeSelectAll = null;
        }
        else
        {
            _selectionBeforeSelectAll = Owner == window && HasSelection ? Selection : null;
            Owner = window;
            Selection = full;
        }
        if (State is OverlayState.Idle or OverlayState.Selecting) State = Tool == DrawingTool.None ? OverlayState.Selected : OverlayState.ToolSelected;
        NotifyChanged();
    }

    public void SelectTool(DrawingTool tool, bool fromClick)
    {
        if (!(State is OverlayState.Selected or OverlayState.ToolSelected or OverlayState.EditingText)) return;
        if (fromClick && Tool == tool) tool = DrawingTool.None;
        EndTextEditing();
        Tool = tool;
        SelectedObject = null;
        SetState(tool == DrawingTool.None ? OverlayState.Selected : OverlayState.ToolSelected);
    }

    public void SelectObject(VisualObject? obj)
    {
        SelectedObject = obj;
        if (obj is not null) LastObject = obj;
        NotifyChanged();
    }

    public void SetThickness(double thickness)
    {
        thickness = Math.Clamp(Math.Round(thickness), VisualObject.MinThickness, VisualObject.MaxThickness);
        if (Math.Abs(thickness - Thickness) < 0.5) return;
        Thickness = thickness;
        ApplyToTarget("thickness", o => o.Thickness = thickness);
        NotifyChanged();
    }

    public void SetColor(Color color)
    {
        Color = color;
        if (!Palette.Contains(color))
        {
            Palette.Insert(0, color);
            while (Palette.Count > 9) Palette.RemoveAt(Palette.Count - 1);
        }
        ApplyToTarget("color", o => o.Color = color);
        NotifyChanged();
    }

    private void ApplyToTarget(string kind, Action<VisualObject> apply)
    {
        var target = SelectedObject ?? LastObject;
        if (target is null) return;
        var before = target.CaptureState();
        apply(target);
        Undo.Push(new StateAction(kind, target, before, target.CaptureState()));
    }

    public void EndTextEditing()
    {
        foreach (var w in Windows) w.EndTextEditing();
    }

    public int NextNumber()
    {
        var used = Windows.SelectMany(w => w.Objects).OfType<NumberObject>().Select(n => n.Number).ToHashSet();
        int n = 1;
        while (used.Contains(n)) n++;
        return n;
    }

    public void DeleteSelected()
    {
        var obj = SelectedObject;
        if (obj is null || Owner is null) return;
        Owner.DeleteObject(obj, withUndo: true);
        SelectedObject = null;
        NotifyChanged();
    }

    public void UndoLast() { EndTextEditing(); SelectedObject = null; Undo.Undo(); NotifyChanged(); }
    public void RedoLast() { EndTextEditing(); SelectedObject = null; Undo.Redo(); NotifyChanged(); }

    /// <summary>Действие пользователя с панели или клавиатуры.</summary>
    public void Complete(EditorActionKind action)
    {
        if (_finished) return;
        if (action == EditorActionKind.Close) { Finish(null); return; }
        if (Owner is null || !HasSelection) return;

        EndTextEditing();
        string? savePath = null;
        if (action == EditorActionKind.SaveToFile)
        {
            savePath = Owner.ShowSaveDialog();
            if (savePath is null) return;
        }
        if (action == EditorActionKind.Print)
        {
            PrintDialog = PrintService.ShowDialog(Owner);
            if (PrintDialog is null) return;
        }

        var screenRect = Owner.ToScreenRect(Selection);
        System.Drawing.Bitmap? image = null;
        if (Mode == OverlayMode.Screenshot)
        {
            SelectedObject = null;
            NotifyChanged();
            image = Owner.Export(Selection);
        }
        Finish(new OverlayResult(action, image, screenRect, Owner.Monitor, savePath)
        {
            PrintDialog = PrintDialog,
            Tool = Tool,
            Thickness = Thickness,
            Color = ColorToString(Color),
            Palette = Palette.Select(ColorToString).ToList(),
        });
    }

    public void Cancel() => Finish(null);

    private void Finish(OverlayResult? result)
    {
        if (_finished) return;
        _finished = true;
        foreach (var w in Windows)
        {
            try { w.Close(); } catch (Exception ex) { _logger.LogWarning(ex, "Ошибка закрытия оверлея"); }
        }
        _completion.TrySetResult(result);
    }

    // ---- Хелперы

    public static Color? TryParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return (Color)ColorConverter.ConvertFromString(text.Trim()); }
        catch { return null; }
    }

    public static string ColorToString(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool TryParseRect(string? text, out System.Drawing.Rectangle rect)
    {
        rect = System.Drawing.Rectangle.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[0], out var x) || !int.TryParse(parts[1], out var y) || !int.TryParse(parts[2], out var w) || !int.TryParse(parts[3], out var h)) return false;
        rect = new System.Drawing.Rectangle(x, y, w, h);
        return true;
    }
}
