using System.Windows;
using System.Windows.Controls;
using TRVisionAI.Desktop.ViewModels;

namespace TRVisionAI.Desktop.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow()
    {
        InitializeComponent();
        _vm = new SettingsViewModel();
        _vm.LoadFromSettings();
        DataContext = _vm;

        // Pre-populate PasswordBoxes (they do not support data binding)
        CameraPwd.Password = _vm.CameraPassword;
        ApiKeyPwd.Password = _vm.ApiKey;

        _vm.Saved += (_, _) => Close();
    }

    private void CameraPwd_Changed(object sender, RoutedEventArgs e)
        => _vm.CameraPassword = ((PasswordBox)sender).Password;

    private void ApiKey_Changed(object sender, RoutedEventArgs e)
        => _vm.ApiKey = ((PasswordBox)sender).Password;
}
