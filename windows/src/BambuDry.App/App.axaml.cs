using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
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

    /// <summary>
    /// When the user pins the dropdown and drags it somewhere, this captures
    /// the position at hide-time so the next Show restores it. Cleared (or
    /// ignored) once the window unpins, so unpinning snaps back to the tray.
    /// </summary>
    private PixelPoint? _savedPinnedPosition;

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
            _mainWindow.Loaded += OnMainWindowLoaded;
            _mainWindow.SizeChanged += OnMainWindowSizeChanged;
            _mainWindow.Deactivated += OnMainWindowDeactivated;

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
        CapturePinnedPosition();
        _mainWindow.Hide();
    }

    /// <summary>If the dropdown is pinned, snapshot its position so the next
    /// Show restores it. No-op when unpinned (those re-anchor to the corner).</summary>
    private void CapturePinnedPosition()
    {
        if (_mainWindow is null) return;
        if (ViewModel?.IsPinned == true)
            _savedPinnedPosition = _mainWindow.Position;
    }

    private void OnMainWindowLoaded(object? sender, RoutedEventArgs e)
    {
        ApplyMaxHeight();
        AnchorToTrayCorner();
    }

    private void OnMainWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // While pinned, the user has chosen a position — don't fight it when
        // content reflows. Auto-anchoring is the unpinned dropdown behaviour.
        if (ViewModel?.IsPinned == true) return;
        AnchorToTrayCorner();
    }

    private void OnMainWindowDeactivated(object? sender, System.EventArgs e)
    {
        if (_mainWindow is null || _lifetime is null) return;
        if (ViewModel?.IsPinned == true) return;

        // Defer the focus check so Avalonia has a moment to set the new active
        // window. If focus moved to another window in OUR app (Settings dialog,
        // PrinterSetup), don't auto-hide. If focus left the app entirely, hide.
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow is null || _lifetime is null) return;
            if (ViewModel?.IsPinned == true) return;
            if (_isShuttingDown) return;
            // _mainWindow.IsActive flips back to true if focus came back to us.
            if (_mainWindow.IsActive) return;
            // A child dialog is active — leave the main window visible.
            if (_lifetime.Windows.Any(w => w != _mainWindow && w.IsActive)) return;
            CapturePinnedPosition();
            _mainWindow.Hide();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Cap the window so content can never demand more space than the working
    /// area. The inner ScrollViewer kicks in if there are more AMS rows than
    /// the screen can show — better than overflowing the taskbar.
    /// </summary>
    private void ApplyMaxHeight()
    {
        if (_mainWindow is null) return;
        var screen = _mainWindow.Screens.Primary ?? _mainWindow.Screens.ScreenFromVisual(_mainWindow);
        if (screen is null) return;
        // WorkingArea is physical pixels; MaxHeight is DIPs.
        _mainWindow.MaxHeight = (screen.WorkingArea.Height / screen.Scaling) - 24;
    }

    /// <summary>
    /// Snap the borderless dropdown to the bottom-right of the primary screen's
    /// working area, accounting for the taskbar AND DPI scaling. Called on
    /// initial Loaded, on every SizeChanged (so the window grows UPWARD as AMS
    /// rows arrive instead of overflowing the taskbar), and on every
    /// tray-driven Show.
    /// </summary>
    private void AnchorToTrayCorner()
    {
        if (_mainWindow is null) return;
        var screen = _mainWindow.Screens.Primary ?? _mainWindow.Screens.ScreenFromVisual(_mainWindow);
        if (screen is null) return;

        // Bounds is in DIPs; Position + WorkingArea are in physical pixels.
        // Convert before arithmetic, otherwise on a 150% scaled display the
        // window lands ~half off-screen.
        var wDip = _mainWindow.Bounds.Width;
        var hDip = _mainWindow.Bounds.Height;
        if (wDip <= 0 || hDip <= 0) return;

        var scale = screen.Scaling;
        var wPx       = (int)(wDip * scale);
        var hPx       = (int)(hDip * scale);
        var marginPx  = (int)(12   * scale);

        var wa = screen.WorkingArea;
        var newPos = new PixelPoint(
            wa.X + wa.Width  - wPx - marginPx,
            wa.Y + wa.Height - hPx - marginPx);
        if (_mainWindow.Position != newPos)
            _mainWindow.Position = newPos;
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

        // Deferred so layout has settled before we read Bounds / set Position.
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow is null) return;
            if (ViewModel?.IsPinned == true && _savedPinnedPosition is { } pos)
            {
                _mainWindow.Position = pos;
            }
            else
            {
                AnchorToTrayCorner();
            }
        }, DispatcherPriority.Loaded);
    }

    private void Quit()
    {
        _isShuttingDown = true;
        _lifetime?.Shutdown();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateTrayIcon();
        if (e.PropertyName == nameof(AppViewModel.IsPinned))
        {
            if (ViewModel?.IsPinned == false)
            {
                // Unpinning means "back to dropdown behaviour" — drop the saved
                // position and snap the window back to the tray corner now.
                _savedPinnedPosition = null;
                AnchorToTrayCorner();
            }
        }
    }
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
