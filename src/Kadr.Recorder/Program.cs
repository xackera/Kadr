using System.IO.Pipes;
using Kadr.Common.Ipc;
using Kadr.Recording;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using NLog.Targets;

// Видеомодуль Kadr: отдельный процесс, чтобы падение драйвера или кодера не роняло главное приложение.
// Протокол (текст, построчно): команда `name|args`, ответ `response|name|ok[|data]` или
// `response|name|error|code|message`, события `event|name|...`.
// Режим отладки: Kadr.Recorder --test "<json параметров>" [секунды]

var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kadr", "log");
Directory.CreateDirectory(logDir);
var config = new NLog.Config.LoggingConfiguration();
config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, new FileTarget("file")
{
    FileName = Path.Combine(logDir, "video-${shortdate}.log"),
    Layout = "${longdate} | ${level:uppercase=true} | ${message} ${exception:format=tostring}",
    MaxArchiveDays = 30, KeepFileOpen = false, Encoding = System.Text.Encoding.UTF8,
});
config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, new ConsoleTarget("console") { Layout = "${time} ${level} ${message} ${exception:format=tostring}" });
LogManager.Configuration = config;
using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug).AddNLog());
var log = loggerFactory.CreateLogger("Recorder");
log.LogInformation("==================== Видеомодуль запущен ====================");

if (args.Length >= 2 && args[0] == "--test")
{
    var opts = RecordingOptions.FromJson(File.Exists(args[1]) ? File.ReadAllText(args[1]) : args[1]);
    var seconds = args.Length >= 3 && int.TryParse(args[2], out var s) ? s : 5;
    using var testEngine = new RecorderEngine(opts, log);
    testEngine.Progress += (t, max) => Console.WriteLine($"progress {t:mm\\:ss} / {max:mm\\:ss}");
    // Та же схема запуска, что и в режиме pipe: движок стартует в пуле потоков.
    var run = Task.Run(async () => await testEngine.RunAsync());
    while (testEngine.State == RecorderState.Idle && !run.IsCompleted) await Task.Delay(50);
    Console.WriteLine($"state: {testEngine.State}");
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    testEngine.Stop();
    await run;
    Console.WriteLine($"done: {testEngine.State}, {new FileInfo(opts.OutputFile).Length} bytes");
    return 0;
}

using var pipe = new NamedPipeClientStream(".", Ipc.VideoModulePipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
try
{
    pipe.Connect(5000);
}
catch (TimeoutException)
{
    log.LogError("Главное приложение не найдено (pipe {Name})", Ipc.VideoModulePipeName);
    return 2;
}
log.LogInformation("Подключено к главному приложению");

using var reader = new StreamReader(pipe);
var writer = new StreamWriter(pipe) { AutoFlush = true };
var writeLock = new object();
void Send(string line) { lock (writeLock) { try { writer.WriteLine(line); } catch (Exception ex) { log.LogWarning(ex, "Не удалось отправить: {Line}", line); } } }

RecorderEngine? engine = null;
Task? runTask = null;

try
{
    while (await reader.ReadLineAsync() is { } line)
    {
        var sep = line.IndexOf('|');
        var name = sep < 0 ? line : line[..sep];
        var arg = sep < 0 ? "" : line[(sep + 1)..];
        log.LogInformation("Команда: {Name}", name);
        try
        {
            switch (name)
            {
                case "start":
                {
                    if (engine is not null && engine.State is RecorderState.Recording or RecorderState.Paused)
                    { Send("response|start|error|General|Запись уже идёт"); break; }
                    var options = RecordingOptions.FromJson(arg);
                    engine?.Dispose();
                    engine = new RecorderEngine(options, log);
                    engine.Progress += (t, max) => Send($"event|recordingProgress|{t.Ticks}|{max.Ticks}");
                    var current = engine;
                    runTask = Task.Run(async () =>
                    {
                        try
                        {
                            await current.RunAsync();
                            Send(current.State == RecorderState.Canceled ? "event|finished|canceled" : "event|finished|ready");
                        }
                        catch (RecorderException ex)
                        {
                            log.LogError(ex, "Ошибка записи");
                            Send($"event|finished|error|{ex.Code}|{ex.Message.Replace('|', '/')}");
                        }
                        catch (Exception ex)
                        {
                            log.LogError(ex, "Ошибка записи");
                            Send($"event|finished|error|General|{ex.Message.Replace('|', '/')}");
                        }
                    });
                    // Ждём фактического старта или ошибки, но не дольше 10 с.
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (current.State == RecorderState.Idle && !runTask.IsCompleted && DateTime.UtcNow < deadline) await Task.Delay(50);
                    Send(current.State is RecorderState.Recording ? "response|start|ok" : runTask.IsCompleted ? "response|start|error|General|Запись не запустилась" : "response|start|error|General|Таймаут запуска");
                    break;
                }
                case "stop": engine?.Stop(); Send("response|stop|ok"); break;
                case "cancel": engine?.Cancel(); Send("response|cancel|ok"); break;
                case "pause": engine?.Pause(); Send("response|pause|ok"); break;
                case "resume": engine?.Resume(); Send("response|resume|ok"); break;
                case "togglePause": Send($"response|togglePause|ok|{engine?.TogglePause() ?? false}"); break;
                case "ping": Send("response|ping|ok"); break;
                case "terminate":
                    engine?.Stop();
                    if (runTask is not null) await Task.WhenAny(runTask, Task.Delay(5000));
                    Send("response|terminate|ok");
                    return 0;
                default: Send($"response|{name}|error|General|Неизвестная команда"); break;
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Ошибка команды {Name}", name);
            Send($"response|{name}|error|General|{ex.Message.Replace('|', '/')}");
        }
    }
}
catch (IOException)
{
    log.LogInformation("Соединение закрыто");
}
engine?.Stop();
if (runTask is not null) await Task.WhenAny(runTask, Task.Delay(5000));
log.LogInformation("==================== Видеомодуль завершён ====================");
return 0;
