using Avalonia.Controls;
using Avalonia.Interactivity;
using BambuDry.App.ViewModels;

namespace BambuDry.App.Views;

public partial class PrinterSetupWindow : Window
{
    public PrinterSetupWindow() => InitializeComponent();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AppViewModel vm) { Close(); return; }

        var newConfig = vm.Config with
        {
            PrinterName = NameField.Text ?? "My Bambu Printer",
            LanIp       = (IpField.Text ?? "").Trim(),
            Serial      = (SerialField.Text ?? "").Trim(),
        };
        vm.SaveAndPersistConfig(newConfig);
        if (!string.IsNullOrEmpty(CodeField.Text))
            vm.SaveAccessCode(CodeField.Text!);

        await vm.StartAsync();
        Close();
    }
}
