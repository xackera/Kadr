using System.Diagnostics;
using System.Windows;
using Kadr.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kadr.App.Views;

/// <summary>Окно «Доступно обновление»: список изменений, установка с загрузкой и проверкой, пропуск версии.</summary>
public partial class UpdateWindow : Window
{
    private static UpdateWindow? _current;
    private readonly UpdateService _service;
    private readonly UpdateInfo _info;
    private CancellationTokenSource? _download;

    private UpdateWindow(UpdateService service, UpdateInfo info)
    {
        _service = service;
        _info = info;
        InitializeComponent();
        var date = info.PublishedAt is { } d ? $" от {d.LocalDateTime:dd.MM.yyyy}" : "";
        Versions.Text = $"Установлена {UpdateService.CurrentVersion}, доступна {info.Version}{date}";
        Notes.Text = string.IsNullOrWhiteSpace(info.Notes) ? "Описание изменений не указано." : info.Notes.Trim();
        if (!service.CanInstall)
        {
            BtnInstall.Content = "Открыть страницу релиза";
            ShowMessage("Эта копия Kadr запущена не из установленной программы, поэтому обновить её автоматически нельзя. "
                        + "Скачайте новую версию со страницы релиза.", error: false);
        }
        Closed += (_, _) => { _download?.Cancel(); if (_current == this) _current = null; };
    }

    public static void ShowOrActivate(UpdateService service, UpdateInfo info)
    {
        if (_current is not null && _current._info.Version != info.Version) _current.Close();
        if (_current is null)
        {
            _current = new UpdateWindow(service, info);
            _current.Show();
        }
        if (_current.WindowState == WindowState.Minimized) _current.WindowState = WindowState.Normal;
        _current.Activate();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (!_service.CanInstall) { OpenPage(); Close(); return; }

        SetBusy(true);
        _download = new CancellationTokenSource();
        var progress = new Progress<(long Done, long Total)>(p =>
        {
            Progress.Value = p.Total > 0 ? (double)p.Done / p.Total : 0;
            ProgressText.Text = p.Total > 0 ? $"Загрузка: {Mb(p.Done)} из {Mb(p.Total)} МБ" : $"Загрузка: {Mb(p.Done)} МБ";
        });
        try
        {
            var msi = await _service.DownloadAsync(_info, progress, _download.Token);
            ProgressText.Text = "Файл проверен. Запуск установки…";
            if (!_service.StartInstall(msi, out var error))
            {
                SetBusy(false);
                ShowMessage(error ?? "Не удалось запустить установку.", error: true);
                return;
            }
            App.Services?.GetRequiredService<AppCommands>().Exit();
        }
        catch (OperationCanceledException)
        {
            SetBusy(false);
            ShowMessage("Загрузка отменена.", error: false);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            ShowMessage("Не удалось загрузить обновление: " + ex.Message, error: true);
        }
        finally
        {
            _download?.Dispose();
            _download = null;
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (_download is not null) { _download.Cancel(); return; }
        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        _service.Skip(_info);
        Close();
    }

    private void Page_Click(object sender, RoutedEventArgs e) => OpenPage();

    private void OpenPage()
    {
        try { Process.Start(new ProcessStartInfo(_info.PageUrl) { UseShellExecute = true }); }
        catch { /* браузер не открылся — пользователь увидит ссылку в окне */ }
    }

    private void SetBusy(bool busy)
    {
        ProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Message.Visibility = Visibility.Collapsed;
        BtnInstall.IsEnabled = !busy;
        BtnSkip.IsEnabled = !busy;
        BtnLater.Content = busy ? "Отмена" : "Позже";
        if (busy) { Progress.Value = 0; ProgressText.Text = "Загрузка…"; }
    }

    private void ShowMessage(string text, bool error)
    {
        Message.Text = text;
        Message.Foreground = (System.Windows.Media.Brush)FindResource(error ? "RecordBrush" : "TextMutedBrush");
        Message.Visibility = Visibility.Visible;
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0");
}
