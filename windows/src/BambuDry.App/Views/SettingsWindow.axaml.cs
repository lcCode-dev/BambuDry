using Avalonia.Controls;
using Avalonia.Interactivity;
using BambuDry.App.ViewModels;

namespace BambuDry.App.Views;

public partial class SettingsWindow : Window
{
    private AppViewModel? _appVm;

    public SettingsWindow() => InitializeComponent();

    public static SettingsWindow For(AppViewModel appVm)
    {
        var window = new SettingsWindow
        {
            _appVm = appVm,
            DataContext = new SettingsViewModel(appVm.Config.DefaultSettings),
        };
        return window;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || _appVm is null) { Close(); return; }
        var settings = vm.ToSettings().Sanitized();
        _appVm.UpdateDefaultSettings(settings);
        Close();
    }
}
