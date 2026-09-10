using System.IO;
using System.IO.Pipes;
using System.Windows;
using Kadr.Common.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kadr.App.Services;

/// <summary>Принимает команды от повторно запущенных экземпляров и выполняет их в UI-потоке.</summary>
public sealed class MainPipeServer : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MainPipeServer> _logger;

    public MainPipeServer(IServiceProvider services, ILogger<MainPipeServer> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Pipe-сервер {Name} запущен", Ipc.MainPipeName);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(Ipc.MainPipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server);
                var command = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(command)) continue;
                var cmd = command.Trim();
                Application.Current?.Dispatcher.BeginInvoke(() => _services.GetRequiredService<AppCommands>().Execute(cmd));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка pipe-сервера");
                await Task.Delay(500, ct);
            }
        }
    }
}
