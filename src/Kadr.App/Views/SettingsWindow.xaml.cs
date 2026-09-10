using System.Windows;
using Kadr.App.Services;
using Kadr.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Kadr.App.Views;

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => { if (_instance == this) _instance = null; };
    }

    public static void ShowOrActivate(IServiceProvider services, SettingsTab tab = SettingsTab.General)
    {
        if (_instance is null)
        {
            _instance = new SettingsWindow(services.GetRequiredService<SettingsViewModel>());
            _instance.Show();
        }
        else
        {
            ((SettingsViewModel)_instance.DataContext).RefreshAll();
            if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
            _instance.Show();
            _instance.Activate();
        }
        _instance.Tabs.SelectedIndex = (int)tab;
    }
}
