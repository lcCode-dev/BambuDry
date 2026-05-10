using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using BambuDry.App.Storage;
using BambuDry.App.ViewModels;
using BambuDry.App.Views;
using BambuDry.Core;

namespace BambuDry.App;

public partial class App : Application
{
    public AppViewModel? ViewModel { get; private set; }

    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private IClassicDesktopStyleApplicationLifetime? _lifetime;
    private bool _isShuttingDown;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _lifetime = desktop;
            ViewModel = new AppViewModel(ConfigStore.Load());

            _mainWindow = new MainWindow { DataContext = ViewModel };
            desktop.MainWindow = _mainWindow;
            // Keep the app alive when the main window is closed — tray stays.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _mainWindow.Closing += OnMainWindowClosing;
            _mainWindow.Opened += OnMainWindowOpened;

            SetupTrayIcon();

            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ViewModel.Rows.CollectionChanged += OnSnapshotsChanged;
            UpdateTrayIcon();

            _ = ViewModel.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isShuttingDown || _mainWindow is null) return;
        e.Cancel = true;
        _mainWindow.Hide();
    }

    private void OnMainWindowOpened(object? sender, System.EventArgs e)
    {
        // First-show only: anchor to the bottom-right of the working area, near
        // the tray icon. After this fires once, Avalonia preserves the user's
        // dragged position across Hide()/Show() cycles.
        if (_mainWindow is null) return;
        var screen = _mainWindow.Screens.Primary ?? _mainWindow.Screens.ScreenFromVisual(_mainWindow);
        if (screen is null) return;
        var wa = screen.WorkingArea;
        var w  = (int)_mainWindow.Bounds.Width;
        var h  = (int)_mainWindow.Bounds.Height;
        if (w <= 0) w = 380;
        if (h <= 0) h = 480;
        var x = wa.X + wa.Width  - w - 12;
        var y = wa.Y + wa.Height - h - 12;
        _mainWindow.Position = new Avalonia.PixelPoint(x, y);
        _mainWindow.Opened -= OnMainWindowOpened; // first show only
    }

    public void QuitApp() => Quit();

    private void SetupTrayIcon()
    {
        _trayIcon = new TrayIcon { ToolTipText = "BambuDry" };

        var menu = new NativeMenu();

        var openItem = new NativeMenuItem("Open BambuDry");
        openItem.Click += (_, _) => ShowMainWindow();
        menu.Add(openItem);

        var settingsItem = new NativeMenuItem("Settings…");
        settingsItem.Click += (_, _) =>
        {
            if (ViewModel is null || _mainWindow is null) return;
            ShowMainWindow();
            SettingsWindow.For(ViewModel).ShowDialog(_mainWindow);
        };
        menu.Add(settingsItem);

        menu.Add(new NativeMenuItemSeparator());

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => Quit();
        menu.Add(quitItem);

        _trayIcon.Menu = menu;
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void Quit()
    {
        _isShuttingDown = true;
        _lifetime?.Shutdown();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateTrayIcon();
    private void OnSnapshotsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateTrayIcon();

    private void UpdateTrayIcon()
    {
        if (_trayIcon is null || ViewModel is null) return;

        var iconName = ResolveTrayIconName(ViewModel);
        try
        {
            using var stream = AssetLoader.Open(new System.Uri($"avares://BambuDry/Assets/{iconName}"));
            _trayIcon.Icon = new WindowIcon(stream);
        }
        catch
        {
            // Asset missing — leave whatever icon was there. Don't crash startup.
        }
        _trayIcon.ToolTipText = $"BambuDry — {ViewModel.State}";
    }

    private static string ResolveTrayIconName(AppViewModel vm)
    {
        if (vm.State is AppViewModel.ConnectionState.NotConfigured
                     or AppViewModel.ConnectionState.Disconnected
                     or AppViewModel.ConnectionState.Connecting)
            return "tray-offline.png";

        var hi = vm.Config.DefaultSettings.HighThreshold;
        if (vm.Rows.Any(r => r.IsDrying))
            return "tray-drying.png";
        if (vm.Rows.Any(r => r.HumidityPercent is int rh && rh >= hi))
            return "tray-warm.png";
        return "tray-idle.png";
    }
}
