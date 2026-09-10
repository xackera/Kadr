using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kadr.Common.Hotkeys;

/// <summary>
/// Сочетание клавиш. Сериализуется как список VK-кодов через запятую:
/// модификаторы в порядке Ctrl(17), Alt(18), Shift(16), Win(91), затем клавиша. Пример: "17,16,44" = Ctrl+Shift+PrtScr.
/// </summary>
[JsonConverter(typeof(HotkeyJsonConverter))]
public readonly record struct Hotkey(int Key, bool Ctrl = false, bool Alt = false, bool Shift = false, bool Win = false)
{
    public static readonly Hotkey Empty = new(0);

    public bool IsEmpty => Key == 0;
    public bool HasModifiers => Ctrl || Alt || Shift || Win;

    /// <summary>Флаги для RegisterHotKey: MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8.</summary>
    public uint NativeModifiers => (Alt ? 1u : 0) | (Ctrl ? 2u : 0) | (Shift ? 4u : 0) | (Win ? 8u : 0);

    public bool IsValid => !IsEmpty && !VirtualKeys.IsModifier(Key)
                           && (HasModifiers || VirtualKeys.IsStandaloneAllowed(Key));

    public string Serialize()
    {
        if (IsEmpty) return "";
        var parts = new List<int>(5);
        if (Ctrl) parts.Add(VirtualKeys.Control);
        if (Alt) parts.Add(VirtualKeys.Menu);
        if (Shift) parts.Add(VirtualKeys.Shift);
        if (Win) parts.Add(VirtualKeys.LWin);
        parts.Add(Key);
        return string.Join(",", parts);
    }

    public static Hotkey Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Empty;
        bool ctrl = false, alt = false, shift = false, win = false;
        int key = 0;
        foreach (var raw in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(raw, out var vk)) return Empty;
            switch (vk)
            {
                case VirtualKeys.Control or VirtualKeys.LControl or VirtualKeys.RControl: ctrl = true; break;
                case VirtualKeys.Menu or VirtualKeys.LMenu or VirtualKeys.RMenu: alt = true; break;
                case VirtualKeys.Shift or VirtualKeys.LShift or VirtualKeys.RShift: shift = true; break;
                case VirtualKeys.LWin or VirtualKeys.RWin: win = true; break;
                default:
                    if (key != 0) return Empty; // две обычные клавиши недопустимы
                    key = vk;
                    break;
            }
        }
        return key == 0 ? Empty : new Hotkey(key, ctrl, alt, shift, win);
    }

    public override string ToString()
    {
        if (IsEmpty) return "";
        var parts = new List<string>(5);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(VirtualKeys.Name(Key));
        return string.Join("+", parts);
    }
}

public sealed class HotkeyJsonConverter : JsonConverter<Hotkey>
{
    public override Hotkey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String ? Hotkey.Parse(reader.GetString()) : Hotkey.Empty;

    public override void Write(Utf8JsonWriter writer, Hotkey value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Serialize());
}
