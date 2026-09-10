using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using Kadr.App.Startup;
using Kadr.Common.Ipc;
using Microsoft.Extensions.Logging;

namespace Kadr.App.Services;

/// <summary>
/// Управляет процессом Kadr.Recorder.exe: pipe-сервер, команды с ответами, события прогресса и завершения.
/// Падение процесса превращается в событие Finished("error").
/// </summary>
public sealed class VideoModuleClient : IDisposable
{
    private readonly AppPaths _paths;
    private readonly ILogger<VideoModuleClient> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, TaskCompletionSource<string[]>> _pending = new();

    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private StreamWriter? _writer;
    private Task? _readLoop;
    private bool _recordingActive;

    public event Action<TimeSpan, TimeSpan>? Progress;
    /// <summary>status: ready | canceled | error; затем код и сообщение для error.</summary>
    public event Action<string, string?, string?>? Finished;

    public VideoModuleClient(AppPaths paths, ILogger<VideoModuleClient> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public bool IsConnected => _process is { HasExited: false } && _pipe is { IsConnected: true };

    public async Task EnsureStartedAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;
        Shutdown();

        var exe = Path.Combine(_paths.ExeDirectory, "Kadr.Recorder.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("Не найден видеомодуль Kadr.Recorder.exe", exe);

        _pipe = new NamedPipeServerStream(Ipc.VideoModulePipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connect = _pipe.WaitForConnectionAsync(ct);

        _process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _paths.ExeDirectory })
                   ?? throw new InvalidOperationException("Не удалось запустить видеомодуль");
        _process.EnableRaisingEvents = true;
        _process.Exited += OnProcessExited;
        _logger.LogInformation("Видеомодуль запущен, pid {Pid}", _process.Id);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await connect.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            Shutdown();
            throw new TimeoutException("Видеомодуль не подключился за 10 секунд");
        }

        _writer = new StreamWriter(_pipe) { AutoFlush = true };
        _readLoop = Task.Run(() => ReadLoopAsync(_pipe));
        _logger.LogInformation("Видеомодуль подключён");
    }

    public async Task<string[]> SendAsync(string command, string? argument = null, int timeoutMs = 5000)
    {
        if (_writer is null || !IsConnected) throw new InvalidOperationException("Видеомодуль не подключён");
        var tcs = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync) _pending[command] = tcs;
        var line = argument is null ? command : $"{command}|{argument}";
        lock (_sync) _writer.WriteLine(line);
        _logger.LogInformation("→ видеомодуль: {Command}", command);
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            lock (_sync) _pending.Remove(command);
            throw new TimeoutException($"Видеомодуль не ответил на команду {command}");
        }
    }

    /// <summary>Отправить команду и бросить исключение, если ответ error.</summary>
    public async Task<string[]> SendCheckedAsync(string command, string? argument = null, int timeoutMs = 5000)
    {
        var r = await SendAsync(command, argument, timeoutMs);
        if (r.Length >= 1 && r[0] == "error")
            throw new VideoModuleException(r.Length > 1 ? r[1] : "General", r.Length > 2 ? r[2] : "Ошибка видеомодуля");
        return r;
    }

    public void MarkRecording(bool active) => _recordingActive = active;

    private async Task ReadLoopAsync(NamedPipeServerStream pipe)
    {
        try
        {
            using var reader = new StreamReader(pipe);
            while (await reader.ReadLineAsync() is { } line)
            {
                var parts = line.Split('|');
                if (parts.Length < 2) continue;
                if (parts[0] == "response")
                {
                    TaskCompletionSource<string[]>? tcs;
                    lock (_sync) { _pending.Remove(parts[1], out tcs); }
                    tcs?.TrySetResult(parts.Skip(2).ToArray());
                }
                else if (parts[0] == "event")
                {
                    switch (parts[1])
                    {
                        case "recordingProgress" when parts.Length >= 4 && long.TryParse(parts[2], out var t) && long.TryParse(parts[3], out var max):
                            Progress?.Invoke(TimeSpan.FromTicks(t), TimeSpan.FromTicks(max));
                            break;
                        case "finished":
                            _recordingActive = false;
                            Finished?.Invoke(parts.Length > 2 ? parts[2] : "error", parts.Length > 3 ? parts[3] : null, parts.Length > 4 ? parts[4] : null);
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Чтение из видеомодуля прервано");
        }
        FailPending("Соединение с видеомодулем потеряно");
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _logger.LogWarning("Видеомодуль завершился с кодом {Code}", _process?.ExitCode);
        FailPending("Видеомодуль завершился");
        if (_recordingActive)
        {
            _recordingActive = false;
            Finished?.Invoke("error", "General", "Видеомодуль неожиданно завершился");
        }
    }

    private void FailPending(string reason)
    {
        List<TaskCompletionSource<string[]>> list;
        lock (_sync) { list = _pending.Values.ToList(); _pending.Clear(); }
        foreach (var t in list) t.TrySetException(new InvalidOperationException(reason));
    }

    public void Shutdown()
    {
        try
        {
            if (_writer is not null && IsConnected)
            {
                lock (_sync) _writer.WriteLine("terminate");
                _process?.WaitForExit(3000);
            }
        }
        catch { }
        try { if (_process is { HasExited: false }) _process.Kill(); } catch { }
        _process?.Dispose();
        _process = null;
        _writer = null;
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;
    }

    public void Dispose() => Shutdown();
}

public sealed class VideoModuleException : Exception
{
    public string Code { get; }
    public VideoModuleException(string code, string message) : base(message) => Code = code;
}
