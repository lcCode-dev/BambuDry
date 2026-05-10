using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using BambuDry.App.ViewModels;

namespace BambuDry.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        // Drag the borderless window by its header — but skip when the press
        // landed on a child Button / ToggleButton (pin, ✕) so their clicks
        // don't initiate a drag instead of toggling.
        if (e.Handled) return;
        if (e.Source is Control c && (c is Button || c is ToggleButton ||
            c.GetVisualAncestors().OfType<Button>().Any() ||
            c.GetVisualAncestors().OfType<ToggleButton>().Any()))
            return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnHide(object? sender, RoutedEventArgs e)
    {
        // Route through Close() so our Closing handler in App.axaml.cs runs:
        // it captures the pinned position before flipping Cancel=true + Hide.
        // Calling Hide() directly skipped that path and lost the saved position.
        Close();
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

    private void OnStopRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.DataContext is AmsRowViewModel row)
            row.RequestManualStop();
    }
}
