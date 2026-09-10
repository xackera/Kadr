using System.Windows;
using System.Windows.Threading;
using Kadr.App.Services;
using Kadr.App.Startup;
using Kadr.App.ViewModels;
using Kadr.Common.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace Kadr.App;

public partial class App : Application
{
    private IHost? _host;
    private SingleInstance? _instance;
    private ILogger<App>? _logger;

    public static IServiceProvider? Services => (Current as App)?._host?.Services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = CommandLineArgs.Parse(e.Args);
        var paths = AppPaths.Resolve(args.Portable);
        Logging.Configure(paths.LogDirectory);

        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            // Уже работает: передаём команду и выходим.
            SingleInstance.SendCommand(args.ToIpcCommand());
            Shutdown();
            return;
        }

        _host = new HostBuilder()
            .ConfigureLogging(b => { b.ClearProviders(); b.SetMinimumLevel(LogLevel.Debug); b.AddNLog(); })
            .ConfigureServices(services =>
            {
                services.AddSingleton(paths);
                services.AddSingleton(sp => new SettingsStore(paths.DataDirectory, sp.GetRequiredService<ILogger<SettingsStore>>()));
                services.AddSingleton<OperationState>();
                services.AddSingleton<NotificationService>();
                services.AddSingleton<TrayService>();
                services.AddSingleton<HotkeyService>();
                services.AddSingleton<ScreenshotService>();
                services.AddSingleton<RegionScreenshotService>();
                services.AddSingleton<VideoModuleClient>();
                services.AddSingleton<VideoRecordingService>();
                services.AddSingleton<ScrollingCaptureService>();
                services.AddSingleton<MouseHotkeyService>();
                services.AddSingleton<AppCommands>();
                services.AddTransient<SettingsViewModel>();
                services.AddHostedService<MainPipeServer>();
            })
            .Build();

        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => _logger.LogCritical(ex.ExceptionObject as Exception, "Необработанное исключение в фоновом потоке");
        TaskScheduler.UnobservedTaskException += (_, ex) => { _logger.LogError(ex.Exception, "Необработанное исключение задачи"); ex.SetObserved(); };

        try
        {
            Logging.LogSystemInfo(_logger, paths);
            var store = _host.Services.GetRequiredService<SettingsStore>();
            store.Load();
            _logger.LogInformation("Настройки загружены из {Path}", store.FilePath);

            _host.Start();

            var tray = _host.Services.GetRequiredService<TrayService>();
            tray.Initialize();
            _host.Services.GetRequiredService<NotificationService>().Attach(tray.Icon,
                () => _host.Services.GetRequiredService<AppCommands>().ShowSettings());
            _host.Services.GetRequiredService<HotkeyService>().Initialize();
            _host.Services.GetRequiredService<MouseHotkeyService>().Initialize();

            var commands = _host.Services.GetRequiredService<AppCommands>();
            if (args.Command is not null) commands.Execute(args.Command);
            else if (!args.Silent) commands.ShowSettings();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Ошибка запуска");
            MessageBox.Show("Не удалось запустить Kadr: " + ex.Message, "Kadr", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Необработанное исключение в UI-потоке");
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                _logger?.LogInformation("Завершение работы");
                _host.Services.GetService<HotkeyService>()?.Dispose();
                _host.Services.GetService<MouseHotkeyService>()?.Dispose();
                _host.Services.GetService<TrayService>()?.Dispose();
                _host.Services.GetService<VideoModuleClient>()?.Dispose();
                _host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при завершении");
        }
        finally
        {
            _instance?.Dispose();
            NLog.LogManager.Shutdown();
        }
        base.OnExit(e);
    }
}
