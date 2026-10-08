using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Kadr.App.Startup;
using Kadr.App.Views;
using Kadr.Common.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Kadr.App.Services;

public sealed record UpdateAsset(string Name, string Url, long Size);

public sealed record UpdateInfo(Version Version, DateTimeOffset? PublishedAt, string Notes, string PageUrl, UpdateAsset? Msi, UpdateAsset? Checksum);

/// <summary>
/// Обновление из GitHub Releases: проверка, загрузка установщика с проверкой SHA-256, запуск установки и перезапуск.
/// Автоматическая проверка — только при включённой настройке; ручная работает всегда.
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string DefaultSource = "https://api.github.com/repos/xackera/Kadr/releases/latest";
    public const string ReleasesPage = "https://github.com/xackera/Kadr/releases/latest";
    private const string InstallRegistryKey = @"Software\Mike Alimov\Kadr";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly SettingsStore _store;
    private readonly AppPaths _paths;
    private readonly NotificationService _notify;
    private readonly OperationState _state;
    private readonly ILogger<UpdateService> _logger;
    private readonly HttpClient _http;
    private DispatcherTimer? _timer;
    private bool _checking;

    public UpdateService(SettingsStore store, AppPaths paths, NotificationService notify, OperationState state, ILogger<UpdateService> logger)
    {
        _store = store;
        _paths = paths;
        _notify = notify;
        _state = state;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Kadr/{CurrentVersion}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static Version CurrentVersion { get; } = Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public static string UpdateDirectory => Path.Combine(Path.GetTempPath(), "Kadr", "update");

    private static string ArchSuffix => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    /// <summary>Найденная новая версия; null — обновления нет или версия пропущена.</summary>
    public UpdateInfo? Available { get; private set; }
    public event Action? AvailableChanged;

    /// <summary>Автопроверка: первая через минуту после запуска, дальше раз в час смотрим, прошли ли сутки с прошлой.</summary>
    public void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(1);
            var s = _store.Current;
            if (!s.CheckForUpdates) return;
            if (s.UpdateLastCheck is { } last && DateTime.UtcNow - last < CheckInterval) return;
            await CheckAsync(manual: false);
        };
        _timer.Start();
    }

    /// <summary>Проверить обновления. При ручной проверке пользователь видит и «нет новее», и ошибки.</summary>
    public async Task CheckAsync(bool manual)
    {
        if (_checking) return;
        _checking = true;
        try
        {
            var info = await FetchAsync();
            _store.Update(s => s.UpdateLastCheck = DateTime.UtcNow);
            if (info is null || info.Version <= CurrentVersion)
            {
                _logger.LogInformation("Обновлений нет: последняя версия {Latest}, установлена {Current}", info?.Version, CurrentVersion);
                SetAvailable(null);
                if (manual) _notify.Info($"Установлена последняя версия Kadr {CurrentVersion}.");
                return;
            }
            bool skipped = _store.Current.UpdateSkippedVersion == info.Version.ToString();
            _logger.LogInformation("Доступна версия {Version}{Skipped}", info.Version, skipped ? " (пропущена пользователем)" : "");
            if (!manual && skipped) { SetAvailable(null); return; }
            SetAvailable(info);
            if (manual) ShowWindow();
            else _notify.Info($"Доступна новая версия Kadr {info.Version}. Нажмите, чтобы посмотреть.", ShowWindow, respectSilentMode: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось проверить обновления");
            if (manual) _notify.Error("Не удалось проверить обновления: " + Describe(ex));
        }
        finally
        {
            _checking = false;
        }
    }

    public void ShowWindow()
    {
        if (Available is { } info) Application.Current?.Dispatcher.BeginInvoke(() => UpdateWindow.ShowOrActivate(this, info));
    }

    public void Skip(UpdateInfo info)
    {
        _logger.LogInformation("Версия {Version} пропущена", info.Version);
        _store.Update(s => s.UpdateSkippedVersion = info.Version.ToString());
        SetAvailable(null);
    }

    private void SetAvailable(UpdateInfo? info)
    {
        Available = info;
        Application.Current?.Dispatcher.BeginInvoke(() => AvailableChanged?.Invoke());
    }

    // ---- Источник

    private async Task<UpdateInfo?> FetchAsync()
    {
        var custom = _store.Current.UpdateSourceUrl?.Trim();
        var source = string.IsNullOrEmpty(custom) ? DefaultSource : custom;
        var json = IsHttp(source) ? await _http.GetStringAsync(source) : await File.ReadAllTextAsync(source);
        return Parse(json);
    }

    internal static UpdateInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) return null;
        var version = Normalize(parsed);

        DateTimeOffset? published = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
                                    && DateTimeOffset.TryParse(p.GetString(), out var date) ? date : null;
        var notes = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";
        var page = root.TryGetProperty("html_url", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() ?? ReleasesPage : ReleasesPage;

        var assets = new List<UpdateAsset>();
        if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in list.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                var size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var len) ? len : -1;
                assets.Add(new UpdateAsset(name, url, size));
            }
        }
        var msiName = $"Kadr-{version}-{ArchSuffix}.msi";
        var msi = assets.FirstOrDefault(a => a.Name.Equals(msiName, StringComparison.OrdinalIgnoreCase));
        var checksum = assets.FirstOrDefault(a => a.Name.Equals(msiName + ".sha256", StringComparison.OrdinalIgnoreCase));
        return new UpdateInfo(version, published, notes.Replace("\r", ""), page, msi, checksum);
    }

    // ---- Установленная копия

    /// <summary>Обновить установщиком можно только копию, установленную из MSI и запущенную из папки установки.</summary>
    public bool CanInstall => !_paths.IsPortable && InstallFolder() is { } folder && SamePath(folder, AppContext.BaseDirectory);

    private static string? InstallFolder() => ReadInstallValue("InstallFolder") as string;

    /// <summary>Ярлык на рабочем столе ставится компонентом, ключ которого — это значение реестра.</summary>
    private static bool HasDesktopShortcut() => ReadInstallValue("desktopShortcut") is not null;

    private static object? ReadInstallValue(string name)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(InstallRegistryKey);
            return key?.GetValue(name);
        }
        catch { return null; }
    }

    // ---- Загрузка

    /// <summary>Скачать установщик и проверить размер и SHA-256. Возвращает путь к проверенному MSI.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<(long Done, long Total)> progress, CancellationToken ct)
    {
        if (info.Msi is null || info.Checksum is null)
            throw new InvalidOperationException($"В релизе нет установщика или контрольной суммы для {ArchSuffix}.");

        CleanDirectory();
        Directory.CreateDirectory(UpdateDirectory);
        var msiPath = Path.Combine(UpdateDirectory, info.Msi.Name);
        var sw = Stopwatch.StartNew();
        await DownloadFileAsync(info.Msi, msiPath, progress, ct);

        var expected = (await ReadTextAsync(info.Checksum.Url, ct))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "";
        var size = new FileInfo(msiPath).Length;
        if (info.Msi.Size > 0 && size != info.Msi.Size)
            throw new InvalidDataException($"Размер файла {size} байт не совпадает с ожидаемым {info.Msi.Size}.");
        string actual;
        await using (var stream = File.OpenRead(msiPath))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        if (actual != expected)
        {
            _logger.LogWarning("SHA-256 не совпадает: ожидалось {Expected}, получено {Actual}", expected, actual);
            throw new InvalidDataException("Контрольная сумма не совпадает: файл повреждён при загрузке или подменён.");
        }
        _logger.LogInformation("Установщик {File} загружен и проверен: {Size} байт за {Ms} мс", info.Msi.Name, size, sw.ElapsedMilliseconds);
        return msiPath;
    }

    private async Task DownloadFileAsync(UpdateAsset asset, string target, IProgress<(long, long)> progress, CancellationToken ct)
    {
        if (!IsHttp(asset.Url))
        {
            // Локальный источник — только для тестов (update_source_url).
            File.Copy(asset.Url, target, overwrite: true);
            var len = new FileInfo(target).Length;
            progress.Report((len, len));
            return;
        }
        using var response = await _http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? asset.Size;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(target);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            progress.Report((done, total));
        }
    }

    private async Task<string> ReadTextAsync(string url, CancellationToken ct)
        => IsHttp(url) ? await _http.GetStringAsync(url, ct) : await File.ReadAllTextAsync(url, ct);

    private void CleanDirectory()
    {
        try { if (Directory.Exists(UpdateDirectory)) Directory.Delete(UpdateDirectory, recursive: true); }
        catch (Exception ex) { _logger.LogDebug(ex, "Не удалось очистить папку обновления"); }
    }

    // ---- Установка

    /// <summary>
    /// Запустить установку и вернуть true — после этого Kadr должен завершиться. Вспомогательный PowerShell дождётся выхода,
    /// запустит msiexec с прежней папкой и ярлыком и снова откроет Kadr с кодом результата.
    /// </summary>
    public bool StartInstall(string msiPath, out string? error)
    {
        error = null;
        if (_state.IsRecording) { error = "Сначала завершите запись видео."; return false; }
        if (InstallFolder() is not { } folder) { error = "Не найдена папка установки Kadr."; return false; }

        var script = Path.Combine(UpdateDirectory, "install.ps1");
        File.WriteAllText(script, InstallScript, Encoding.ASCII);
        var log = Path.Combine(UpdateDirectory, "install.log");
        var exe = Path.Combine(folder, "Kadr.exe");

        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                     "-KadrPid", Environment.ProcessId.ToString(), "-Msi", msiPath, "-Log", log, "-Exe", exe,
                     "-Folder", folder.Length > 3 ? folder.TrimEnd('\\') : folder, "-Desktop", HasDesktopShortcut() ? "1" : "0",
                 })
            psi.ArgumentList.Add(arg);
        Process.Start(psi);
        _logger.LogInformation("Запущена установка {Msi} в {Folder}", msiPath, folder);
        return true;
    }

    // Только ASCII: Windows PowerShell 5.1 читает файл без BOM в кодировке ANSI.
    private const string InstallScript = """
        param([int]$KadrPid, [string]$Msi, [string]$Log, [string]$Exe, [string]$Folder, [string]$Desktop)
        try { Wait-Process -Id $KadrPid -Timeout 10 -ErrorAction Stop } catch { }
        $msiArgs = @('/i', ('"' + $Msi + '"'), '/passive', '/norestart', '/l*v', ('"' + $Log + '"'),
                     ('INSTALLFOLDER="' + $Folder + '"'), ('INSTALLDESKTOPSHORTCUT=' + $Desktop))
        $p = Start-Process -FilePath 'msiexec.exe' -ArgumentList $msiArgs -Wait -PassThru
        Start-Process -FilePath $Exe -ArgumentList @('--update-result', $p.ExitCode)
        """;

    /// <summary>Сообщить результат установки после перезапуска (ключ --update-result).</summary>
    public void ReportInstallResult(int code)
    {
        _logger.LogInformation("Результат установки обновления: код {Code}, версия {Version}", code, CurrentVersion);
        var log = Path.Combine(UpdateDirectory, "install.log");
        switch (code)
        {
            case 0 or 3010:
                _notify.Info($"Kadr обновлён до версии {CurrentVersion}.");
                break;
            case 1602 or 1625:
                _notify.Warning("Обновление отменено.");
                break;
            default:
                _notify.Error($"Обновление не установлено (код {code}). Нажмите, чтобы открыть журнал установки.",
                    () => { if (File.Exists(log)) Process.Start(new ProcessStartInfo("notepad.exe", $"\"{log}\"")); });
                break;
        }
    }

    // ---- Вспомогательное

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static bool IsHttp(string source)
        => source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden or (System.Net.HttpStatusCode)429 }
            => "GitHub временно ограничил запросы, попробуйте позже.",
        HttpRequestException => "нет связи с GitHub.",
        TaskCanceledException => "сервер не ответил вовремя.",
        _ => ex.Message,
    };

    public void Dispose()
    {
        _timer?.Stop();
        _http.Dispose();
    }
}
