using System.Text;
using System.Text.RegularExpressions;

namespace Kadr.Common.Files;

/// <summary>
/// Шаблон имени файла: {YYYY} {MM} {DD} {hh} {mm} {ss}, {w} заголовок окна, {n}..{nnnnnnnnn} порядковый номер
/// (число букв = минимальное число цифр). Гарантирует уникальность имени в папке.
/// </summary>
public static partial class FileNameTemplate
{
    public const string DefaultScreenshot = "Скриншот-{YYYY}{MM}{DD}-{hh}{mm}{ss}";
    public const string DefaultVideo = "Видео-{YYYY}{MM}{DD}-{hh}{mm}{ss}";
    public const int MaxPathLength = 260;

    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly Dictionary<(string dir, string ext, string series), int> LastCounter = new();

    [GeneratedRegex(@"\{(YYYY|MM|DD|hh|mm|ss|w|n+)\}")]
    private static partial Regex Placeholder();

    /// <summary>Возвращает текст ошибки или null, если шаблон корректен.</summary>
    public static string? Validate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return "Пустой шаблон";
        var stripped = Placeholder().Replace(template, "");
        foreach (var c in stripped)
        {
            if (Array.IndexOf(InvalidChars, c) >= 0 || c == '{' || c == '}')
                return $"Шаблон содержит недопустимый символ '{c}'";
        }
        return null;
    }

    public static bool HasCounter(string template) => Placeholder().Matches(template).Any(m => m.Groups[1].Value[0] == 'n');

    public static string Resolve(string template, DateTime now, string? windowTitle, int counter)
    {
        var title = SanitizeTitle(windowTitle);
        var name = Placeholder().Replace(template, m => m.Groups[1].Value switch
        {
            "YYYY" => now.ToString("yyyy"),
            "MM" => now.ToString("MM"),
            "DD" => now.ToString("dd"),
            "hh" => now.ToString("HH"),
            "mm" => now.ToString("mm"),
            "ss" => now.ToString("ss"),
            "w" => title,
            var n when n[0] == 'n' => counter.ToString().PadLeft(n.Length, '0'),
            _ => m.Value,
        });
        return FinalizeName(name);
    }

    /// <summary>Полный путь к ещё не существующему файлу.</summary>
    public static string ResolveUniquePath(string directory, string template, string extension, DateTime now, string? windowTitle)
    {
        if (!extension.StartsWith('.')) extension = "." + extension;
        var title = SanitizeTitle(windowTitle);

        // Заголовок окна урезаем так, чтобы путь поместился в 260 символов.
        string Build(int counter, string t)
        {
            var name = Resolve(template.Replace("{w}", t), now, null, counter);
            return Path.Combine(directory, name + extension);
        }

        string path;
        if (HasCounter(template))
        {
            var key = (directory, extension, Placeholder().Replace(template, ""));
            int start;
            lock (LastCounter) start = LastCounter.TryGetValue(key, out var last) ? last + 1 : 1;
            int n = start;
            path = FitTitle(Build, n, title);
            while (File.Exists(path)) { n++; path = FitTitle(Build, n, title); }
            lock (LastCounter) LastCounter[key] = n;
        }
        else
        {
            path = FitTitle(Build, 0, title);
            if (File.Exists(path))
            {
                var dir = Path.GetDirectoryName(path)!;
                var stem = Path.GetFileNameWithoutExtension(path).TrimEnd('_');
                int n = 1;
                do { path = Path.Combine(dir, $"{stem}_{n:D3}{extension}"); n++; } while (File.Exists(path));
            }
        }
        return path;
    }

    private static string FitTitle(Func<int, string, string> build, int counter, string title)
    {
        var path = build(counter, title);
        while (path.Length > MaxPathLength && title.Length > 0)
        {
            title = title[..^1].TrimEnd('-', '.');
            path = build(counter, title);
        }
        return path;
    }

    public static string SanitizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var sb = new StringBuilder(title.Length);
        bool lastDash = true;
        foreach (var c in title)
        {
            bool bad = char.IsWhiteSpace(c) || char.IsControl(c) || c == '{' || c == '}' || Array.IndexOf(InvalidChars, c) >= 0;
            if (bad)
            {
                if (!lastDash) { sb.Append('-'); lastDash = true; }
            }
            else { sb.Append(c); lastDash = false; }
        }
        return sb.ToString().Trim('-', '.');
    }

    private static string FinalizeName(string name)
    {
        name = name.TrimEnd('.', ' ');
        if (name.Length == 0) name = "_";
        var stem = name.Contains('.') ? name[..name.IndexOf('.')] : name;
        if (ReservedNames.Contains(stem)) name = "_" + name;
        return name;
    }
}
