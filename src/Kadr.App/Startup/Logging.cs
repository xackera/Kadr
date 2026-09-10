using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Kadr.Capture;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Targets;

namespace Kadr.App.Startup;

public static class Logging
{
    /// <summary>Файлы log\main-YYYY-MM-DD.log, формат «дата | УРОВЕНЬ | сообщение», хранение 30 дней.</summary>
    public static void Configure(string logDirectory)
    {
        var config = new NLog.Config.LoggingConfiguration();
        var file = new FileTarget("file")
        {
            FileName = Path.Combine(logDirectory, "main-${shortdate}.log"),
            Layout = "${longdate} | ${level:uppercase=true} | ${message} ${exception:format=tostring}",
            MaxArchiveDays = 30,
            ArchiveEvery = FileArchivePeriod.Day,
            KeepFileOpen = false,
            Encoding = System.Text.Encoding.UTF8,
        };
        config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, file);
        config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, new DebuggerTarget("debugger"));
        LogManager.Configuration = config;
    }

    public static void LogSystemInfo(Microsoft.Extensions.Logging.ILogger logger, AppPaths paths)
    {
        var version = typeof(Logging).Assembly.GetName().Version;
        logger.LogInformation("Kadr {Version} запущен. Данные: {Data}. Portable: {Portable}", version, paths.DataDirectory, paths.IsPortable);
        logger.LogInformation("OS: {Os}, x64: {X64}, {Runtime}", Environment.OSVersion.VersionString, Environment.Is64BitProcess, RuntimeInformation.FrameworkDescription);
        logger.LogInformation("CPU: {Cpu}, логических ядер: {Cores}", Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), Environment.ProcessorCount);
        try
        {
            logger.LogInformation("Мониторы: {Monitors}", string.Join("; ", Monitors.All()));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось получить список мониторов");
        }
        logger.LogInformation("WPF rendering tier: {Tier}", System.Windows.Media.RenderCapability.Tier >> 16);
    }
}
