using System.Windows;
using System.Windows.Controls;
using HikrobotDesktop.ViewModels;

namespace HikrobotDesktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Escanear cámaras al arrancar
        _vm.ScanCommand.Execute(null);
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Dispose();
        base.OnClosed(e);
    }

    // PasswordBox no soporta data binding nativo — se actualiza manualmente
    private void PwdBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _vm.Password = pb.Password;
    }
}
