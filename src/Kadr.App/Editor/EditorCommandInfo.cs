using Kadr.Common.Settings;

namespace Kadr.App.Editor;

/// <summary>Подписи команд редактора для подсказок на панели и списка клавиш в настройках.</summary>
public static class EditorCommandInfo
{
    public static string Title(EditorCommand command) => command switch
    {
        EditorCommand.Arrow => "Стрелка",
        EditorCommand.Line => "Линия",
        EditorCommand.Pensil => "Карандаш",
        EditorCommand.Marker => "Маркер",
        EditorCommand.Rectangle => "Прямоугольник",
        EditorCommand.FilledRectangle => "Прямоугольник с заливкой",
        EditorCommand.Oval => "Овал",
        EditorCommand.Text => "Текст",
        EditorCommand.Number => "Числовая метка",
        EditorCommand.Blur => "Размытие",
        EditorCommand.Invert => "Инвертор",
        EditorCommand.ThicknessUp => "Увеличить толщину линии",
        EditorCommand.ThicknessDown => "Уменьшить толщину линии",
        _ => command.ToString(),
    };

    public static string Title(EditorPanelItem item) => item switch
    {
        EditorPanelItem.Thickness => "Толщина линии",
        EditorPanelItem.Color => "Цвет",
        EditorPanelItem.Undo => "Отмена и повтор",
        EditorPanelItem.Ocr => "Распознать текст",
        EditorPanelItem.Copy => "Копировать",
        EditorPanelItem.Print => "Печать",
        EditorPanelItem.Save => "Сохранить",
        _ => Enum.TryParse<EditorCommand>(item.ToString(), out var command) ? Title(command) : item.ToString(),
    };

    /// <summary>Встроенные клавиши редактора: показываются в настройках для справки, переназначить их нельзя.</summary>
    public static IReadOnlyList<(string Action, string Keys)> FixedKeys { get; } = new[]
    {
        ("Готово: сохранить или скопировать по настройкам", "Enter"),
        ("Закрыть без сохранения", "Esc"),
        ("Копировать в буфер обмена", "Ctrl+C"),
        ("Сохранить в файл", "Ctrl+S"),
        ("Печать", "Ctrl+P"),
        ("Отменить / повторить", "Ctrl+Z / Ctrl+Y"),
        ("Выделить весь экран", "Ctrl+A"),
        ("Удалить выбранный объект", "Delete"),
        ("Сдвинуть область на пиксель", "Стрелки"),
        ("Изменить размер области на пиксель", "Shift+стрелки"),
        ("Толщина линии", "Колесо мыши"),
    };
}
