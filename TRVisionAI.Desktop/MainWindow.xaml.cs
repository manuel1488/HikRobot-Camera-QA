using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private void ResultsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is not ResultRow row) return;

        var rows  = _vm.Results.ToList();
        var index = rows.IndexOf(row);
        if (index < 0) return;

        var detail = new FrameDetailWindow(rows, index, App.DbService) { Owner = this };
        detail.ShowDialog();
    }
}
