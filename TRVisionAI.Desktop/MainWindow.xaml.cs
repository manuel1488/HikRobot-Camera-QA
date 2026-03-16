using System.Windows;
using TRVisionAI.Desktop.ViewModels;
using TRVisionAI.Desktop.Views;

namespace TRVisionAI.Desktop;

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
