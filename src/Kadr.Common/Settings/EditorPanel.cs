namespace Kadr.Common.Settings;

/// <summary>Элементы панелей редактора, которые можно скрыть. Скрытый элемент отключается целиком, вместе со своими клавишами.</summary>
public enum EditorPanelItem
{
    // Панель инструментов
    Arrow, Line, Pensil, Marker, Rectangle, FilledRectangle, Oval, Text, Number, Blur, Invert,
    Thickness, Color, Undo,
    // Панель действий («Готово» и «Закрыть» есть всегда)
    Ocr, Copy, Print, Save,
}

/// <summary>Состав панелей редактора: что скрыто в настройках и к какому элементу относится инструмент или команда.</summary>
public static class EditorPanel
{
    public static IReadOnlyList<EditorPanelItem> ToolbarItems { get; } = Enum.GetValues<EditorPanelItem>()
        .Where(i => i <= EditorPanelItem.Undo).ToList();

    public static IReadOnlyList<EditorPanelItem> ActionItems { get; } = Enum.GetValues<EditorPanelItem>()
        .Where(i => i > EditorPanelItem.Undo).ToList();

    public static bool IsHidden(AppSettings settings, EditorPanelItem item) => settings.EditorHiddenItems.Contains(item.ToString());

    /// <summary>Инструменты рисования называются так же, как элементы панели.</summary>
    public static EditorPanelItem? ItemOf(DrawingTool tool)
        => tool != DrawingTool.None && Enum.TryParse<EditorPanelItem>(tool.ToString(), out var item) ? item : null;

    public static EditorPanelItem? ItemOf(EditorCommand command) => command switch
    {
        EditorCommand.ThicknessUp or EditorCommand.ThicknessDown => EditorPanelItem.Thickness,
        _ => EditorKeys.ToolOf(command) is { } tool ? ItemOf(tool) : null,
    };

    /// <summary>Оставить только известные имена без повторов.</summary>
    public static void Normalize(AppSettings settings)
        => settings.EditorHiddenItems = settings.EditorHiddenItems
            .Where(name => Enum.GetNames<EditorPanelItem>().Contains(name))
            .Distinct().ToList();
}
