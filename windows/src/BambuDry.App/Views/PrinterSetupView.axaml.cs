using Avalonia.Controls;
using Avalonia.Interactivity;
using BambuDry.App.ViewModels;

namespace BambuDry.App.Views;

public partial class PrinterSetupView : UserControl
{
    public PrinterSetupView() => InitializeComponent();

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AppViewModel vm) return;

        var newConfig = vm.Config with
        {
            PrinterName = string.IsNullOrWhiteSpace(NameField.Text) ? "My Bambu Printer" : NameField.Text!,
            LanIp       = (IpField.Text     ?? string.Empty).Trim(),
            Serial      = (SerialField.Text ?? string.Empty).Trim(),
        };
        vm.SaveAndPersistConfig(newConfig);
        if (!string.IsNullOrEmpty(CodeField.Text))
            vm.SaveAccessCode(CodeField.Text!);

        await vm.StartAsync();
    }
}
