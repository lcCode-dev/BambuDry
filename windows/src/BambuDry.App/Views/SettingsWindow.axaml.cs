using Avalonia.Controls;
using Avalonia.Interactivity;
using BambuDry.App.ViewModels;

namespace BambuDry.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    public static SettingsWindow For(AppViewModel appVm) => new()
    {
        DataContext = new SettingsViewModel(appVm),
    };

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
