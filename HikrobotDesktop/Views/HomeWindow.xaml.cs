using System.Reflection;
using System.Windows;
using HikrobotDesktop.Settings;

namespace HikrobotDesktop.Views;

public partial class HomeWindow : Window
{
    public HomeWindow()
    {
        InitializeComponent();

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        string ver = v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        TxtVersion.Text        = ver;
        TxtFooterVersion.Text  = ver;
    }

    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Load();
        if (string.IsNullOrWhiteSpace(s.Camera.PasswordDpapi))
        {
            var warn = new SettingsWindow { Owner = this };
            warn.ShowDialog();
        }

        var main = new MainWindow();
        main.Show();
        Close();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow { Owner = this };
        win.ShowDialog();
    }
}
