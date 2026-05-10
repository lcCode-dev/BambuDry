using Avalonia.Controls;
using Avalonia.Interactivity;
using BambuDry.App.ViewModels;

namespace BambuDry.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnOpenSetup(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AppViewModel vm) return;
        new PrinterSetupWindow { DataContext = vm }.ShowDialog(this);
    }

    private void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AppViewModel vm) return;
        new SettingsWindow { DataContext = vm }.ShowDialog(this);
    }

    private async void OnReconnect(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AppViewModel vm) return;
        await vm.StartAsync();
    }
}
