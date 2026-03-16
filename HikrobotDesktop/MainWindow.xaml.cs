using System.Windows;
using HikrobotDesktop.ViewModels;
using HikrobotDesktop.Views;

namespace HikrobotDesktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await _vm.AutoConnectAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Dispose();
        base.OnClosed(e);
    }

    private void BtnHome_Click(object sender, RoutedEventArgs e)
    {
        _vm.Dispose();
        var home = new HomeWindow();
        home.Show();
        Close();
    }
}
