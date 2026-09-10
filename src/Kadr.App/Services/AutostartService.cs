using System.IO;
using Microsoft.Win32;

namespace Kadr.App.Services;

/// <summary>Автозапуск через HKCU\Software\Microsoft\Windows\CurrentVersion\Run со значением "&lt;exe&gt;" -s.</summary>
public static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Kadr";

    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Kadr.exe");

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string v && v.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Включает или выключает автозапуск. Бросает исключение при ошибке доступа к реестру.</summary>
    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                        ?? throw new InvalidOperationException("Не удалось открыть ветку автозапуска");
        if (enabled) key.SetValue(ValueName, $"\"{ExePath}\" -s");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
