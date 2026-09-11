using Microsoft.Win32;

namespace Kadr.App.Services;

/// <summary>
/// Системная настройка Windows «Использовать клавишу PRINT SCREEN для открытия средства создания снимков».
/// В Windows 11 она включена по умолчанию и отдаёт PrtScr «Ножницам».
/// </summary>
public static class WindowsKeyboardSettings
{
    private const string SubKey = @"Control Panel\Keyboard";
    private const string ValueName = "PrintScreenKeyForSnippingEnabled";

    /// <summary>PrtScr открывает «Ножницы» Windows.</summary>
    public static bool PrintScreenOpensSnippingTool()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey);
            return key?.GetValue(ValueName) is int v && v != 0;
        }
        catch { return false; }
    }

    /// <summary>Выключает системный перехват PrtScr. Права администратора не нужны, значение пользовательское.</summary>
    public static void DisablePrintScreenForSnipping()
    {
        using var key = Registry.CurrentUser.CreateSubKey(SubKey, writable: true)
                        ?? throw new InvalidOperationException("Не удалось открыть параметры клавиатуры в реестре");
        key.SetValue(ValueName, 0, RegistryValueKind.DWord);
    }
}
