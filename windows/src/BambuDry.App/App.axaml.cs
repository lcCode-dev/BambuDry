using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BambuDry.App.Storage;
using BambuDry.App.ViewModels;
using BambuDry.App.Views;

namespace BambuDry.App;

public partial class App : Application
{
    public AppViewModel? ViewModel { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ViewModel = new AppViewModel(ConfigStore.Load());
            desktop.MainWindow = new MainWindow { DataContext = ViewModel };

            // Tray-only UX is the eventual target; for now the main window stands in.
            // ShutdownMode stays default so closing the main window quits the app.
            _ = ViewModel.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
