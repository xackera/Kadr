namespace Kadr.Common.Hotkeys;

/// <summary>Имена виртуальных клавиш Windows для отображения хоткеев.</summary>
public static class VirtualKeys
{
    public const int Back = 0x08, Tab = 0x09, Enter = 0x0D, Shift = 0x10, Control = 0x11, Menu = 0x12,
        Pause = 0x13, CapsLock = 0x14, Escape = 0x1B, Space = 0x20, PageUp = 0x21, PageDown = 0x22,
        End = 0x23, Home = 0x24, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, Snapshot = 0x2C,
        Insert = 0x2D, Delete = 0x2E, LWin = 0x5B, RWin = 0x5C, NumLock = 0x90, ScrollLock = 0x91,
        LShift = 0xA0, RShift = 0xA1, LControl = 0xA2, RControl = 0xA3, LMenu = 0xA4, RMenu = 0xA5;

    private static readonly Dictionary<int, string> Names = new()
    {
        [Back] = "Backspace", [Tab] = "Tab", [Enter] = "Enter", [Pause] = "Pause", [CapsLock] = "CapsLock",
        [Escape] = "Esc", [Space] = "Space", [PageUp] = "PageUp", [PageDown] = "PageDown", [End] = "End",
        [Home] = "Home", [Left] = "Left", [Up] = "Up", [Right] = "Right", [Down] = "Down",
        [Snapshot] = "PrtScr", [Insert] = "Insert", [Delete] = "Delete", [NumLock] = "NumLock",
        [ScrollLock] = "ScrollLock",
        [0x6A] = "NumPad*", [0x6B] = "NumPad+", [0x6D] = "NumPad-", [0x6E] = "NumPad.", [0x6F] = "NumPad/",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
        [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
    };

    public static string Name(int vk)
    {
        if (Names.TryGetValue(vk, out var n)) return n;
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();          // 0-9
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();          // A-Z
        if (vk is >= 0x60 and <= 0x69) return "NumPad" + (vk - 0x60);          // NumPad0-9
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);           // F1-F24
        return "Key" + vk;
    }

    public static bool IsModifier(int vk) => vk is Shift or Control or Menu or LWin or RWin
        or LShift or RShift or LControl or RControl or LMenu or RMenu;

    /// <summary>Клавиши, которые разрешено использовать без модификаторов.</summary>
    public static bool IsStandaloneAllowed(int vk) =>
        vk is Snapshot or ScrollLock or Pause or Insert or Home or End or PageUp or PageDown
            or NumLock or CapsLock or Tab or 0x6A or 0x6B or 0x6D or 0x6F
        || vk is >= 0x70 and <= 0x87;
}
