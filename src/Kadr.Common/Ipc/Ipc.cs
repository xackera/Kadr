using System.Security.Principal;

namespace Kadr.Common.Ipc;

/// <summary>Имена системных объектов и команды межпроцессного взаимодействия.</summary>
public static class Ipc
{
    public static string UserSid => WindowsIdentity.GetCurrent().User?.Value ?? "default";

    public static string MainMutexName => $"Kadr_Mutex_{UserSid}";
    public static string MainPipeName => $"Kadr_Main_Pipe_{UserSid}";
    public static string VideoModulePipeName => $"KadrVideoModulePipe_{UserSid}";

    /// <summary>Команды второго экземпляра первому.</summary>
    public static class Commands
    {
        public const string ScreenshotRegion = "screenshot-region";
        public const string ScreenshotWindow = "screenshot-window";
        public const string ScreenshotMonitor = "screenshot-monitor";
        public const string ScreenshotDesktop = "screenshot-desktop";
        public const string RecordVideo = "record-video";
        public const string ScrollingCapture = "scrolling-capture";
        public const string ShowSettings = "show-settings";
        public const string Exit = "exit";
    }
}
