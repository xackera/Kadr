using Kadr.Common.Hotkeys;

namespace Kadr.Common.Settings;

/// <summary>Команды редактора скриншотов, которым можно назначить клавишу.</summary>
public enum EditorCommand
{
    Arrow, Line, Pensil, Marker, Rectangle, FilledRectangle, Oval, Text, Number, Blur, Invert,
    ThicknessUp, ThicknessDown,
}

/// <summary>Клавиши редактора: значения по умолчанию, свои назначения из настроек и занятые встроенными действиями сочетания.</summary>
public static class EditorKeys
{
    private const int OemPlus = 0xBB, OemMinus = 0xBD;

    public static IReadOnlyList<EditorCommand> All { get; } = Enum.GetValues<EditorCommand>();

    private static readonly Dictionary<EditorCommand, Hotkey> Defaults = new()
    {
        [EditorCommand.Arrow] = new('A'),
        [EditorCommand.Line] = new('L'),
        [EditorCommand.Pensil] = new('P'),
        [EditorCommand.Marker] = new('M'),
        [EditorCommand.Rectangle] = new('R'),
        [EditorCommand.FilledRectangle] = new('F'),
        [EditorCommand.Oval] = new('O'),
        [EditorCommand.Text] = new('T'),
        [EditorCommand.Number] = new('N'),
        [EditorCommand.Blur] = new('B'),
        [EditorCommand.Invert] = new('I'),
        [EditorCommand.ThicknessUp] = new(OemPlus),
        [EditorCommand.ThicknessDown] = new(OemMinus),
    };

    public static Hotkey Default(EditorCommand command) => Defaults[command];

    /// <summary>Клавиша команды: своя из настроек, иначе по умолчанию. Пустая — у команды нет клавиши.</summary>
    public static Hotkey Get(AppSettings settings, EditorCommand command)
        => settings.EditorHotkeys.TryGetValue(command.ToString(), out var key) ? key : Defaults[command];

    public static DrawingTool? ToolOf(EditorCommand command) => command switch
    {
        EditorCommand.Arrow => DrawingTool.Arrow,
        EditorCommand.Line => DrawingTool.Line,
        EditorCommand.Pensil => DrawingTool.Pensil,
        EditorCommand.Marker => DrawingTool.Marker,
        EditorCommand.Rectangle => DrawingTool.Rectangle,
        EditorCommand.FilledRectangle => DrawingTool.FilledRectangle,
        EditorCommand.Oval => DrawingTool.Oval,
        EditorCommand.Text => DrawingTool.Text,
        EditorCommand.Number => DrawingTool.Number,
        EditorCommand.Blur => DrawingTool.Blur,
        EditorCommand.Invert => DrawingTool.Invert,
        _ => null,
    };

    /// <summary>Сочетания встроенных действий редактора (Enter, Esc, Ctrl+C, стрелки и т. п.) — их назначать нельзя.</summary>
    public static bool IsReserved(Hotkey key)
    {
        if (key.Alt || key.Win) return false;
        int vk = key.Key;
        if (!key.Ctrl && !key.Shift)
            return vk is VirtualKeys.Enter or VirtualKeys.Escape or VirtualKeys.Delete or VirtualKeys.Back
                or VirtualKeys.Left or VirtualKeys.Right or VirtualKeys.Up or VirtualKeys.Down;
        if (key.Shift && !key.Ctrl)
            return vk is VirtualKeys.Left or VirtualKeys.Right or VirtualKeys.Up or VirtualKeys.Down;
        if (key.Ctrl && !key.Shift)
            return vk is 'C' or 'S' or 'P' or 'A' or 'Z' or 'Y' or VirtualKeys.Insert;
        return false;
    }

    /// <summary>Можно ли назначить сочетание команде редактора: обычная клавиша, можно без модификаторов, но не встроенная.</summary>
    public static bool IsAssignable(Hotkey key) => !key.IsEmpty && !VirtualKeys.IsModifier(key.Key) && !IsReserved(key);

    /// <summary>
    /// Привести назначения к корректному виду: убрать неизвестные команды, недопустимые сочетания, повторы
    /// и сочетания, занятые глобальными хоткеями. В настройках остаются только отличия от значений по умолчанию.
    /// </summary>
    public static Dictionary<EditorCommand, Hotkey> Normalize(AppSettings settings, IEnumerable<Hotkey> globalHotkeys)
    {
        var taken = new HashSet<Hotkey>(globalHotkeys.Where(h => !h.IsEmpty));
        var resolved = new Dictionary<EditorCommand, Hotkey>();
        var overrides = new Dictionary<string, Hotkey>();
        foreach (var command in All)
        {
            var key = Get(settings, command);
            if (!key.IsEmpty && (!IsAssignable(key) || !taken.Add(key))) key = Hotkey.Empty;
            resolved[command] = key;
            if (key != Defaults[command]) overrides[command.ToString()] = key;
        }
        settings.EditorHotkeys = overrides;
        return resolved;
    }
}
