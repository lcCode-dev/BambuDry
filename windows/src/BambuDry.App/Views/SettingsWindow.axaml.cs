using Avalonia.Controls;
using Avalonia.Interactivity;
using BambuDry.App.ViewModels;

namespace BambuDry.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async void OnReconnect(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AppViewModel vm) return;
        await vm.StartAsync();
    }
}
