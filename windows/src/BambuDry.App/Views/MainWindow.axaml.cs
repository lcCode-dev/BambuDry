using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BambuDry.App.ViewModels;

namespace BambuDry.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        // Drag the borderless window by its header.
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnHide(object? sender, RoutedEventArgs e) => Hide();

    private void OnOpenSetup(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AppViewModel vm) return;
        new PrinterSetupWindow { DataContext = vm }.ShowDialog(this);
    }

    private void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AppViewModel vm) return;
        SettingsWindow.For(vm).ShowDialog(this);
    }

    private void OnQuit(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is App app) app.QuitApp();
    }
}
