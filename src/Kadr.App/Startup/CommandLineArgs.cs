using Kadr.Common.Ipc;

namespace Kadr.App.Startup;

/// <summary>
/// Аргументы: -r скриншот области, -v запись видео, -c скриншот с прокруткой, -u закрыть работающий экземпляр,
/// -s тихий запуск (без окна настроек), --portable настройки рядом с exe. Без аргументов открывается окно настроек.
/// </summary>
public sealed record CommandLineArgs(string? Command, bool Silent, bool Portable)
{
    public static CommandLineArgs Parse(string[] args)
    {
        string? command = null;
        bool silent = false, portable = false;
        foreach (var a in args)
        {
            switch (a.ToLowerInvariant())
            {
                case "-r": case "--region": command = Ipc.Commands.ScreenshotRegion; break;
                case "-w": case "--window": command = Ipc.Commands.ScreenshotWindow; break;
                case "-m": case "--monitor": command = Ipc.Commands.ScreenshotMonitor; break;
                case "-d": case "--desktop": command = Ipc.Commands.ScreenshotDesktop; break;
                case "-v": case "--video": command = Ipc.Commands.RecordVideo; break;
                case "-c": case "--scroll": command = Ipc.Commands.ScrollingCapture; break;
                case "-u": case "--exit": command = Ipc.Commands.Exit; break;
                case "-s": case "--silent": silent = true; break;
                case "--portable": portable = true; break;
            }
        }
        return new CommandLineArgs(command, silent, portable);
    }

    /// <summary>Команда для передачи уже работающему экземпляру.</summary>
    public string ToIpcCommand() => Command ?? (Silent ? "" : Ipc.Commands.ShowSettings);
}
