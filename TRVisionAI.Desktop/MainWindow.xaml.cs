using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
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
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await _vm.AutoConnectAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
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

    // -------------------------------------------------------------------------
    // Overlay en vivo
    // -------------------------------------------------------------------------

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.LiveOverlay) or nameof(MainViewModel.LiveImage))
            DrawLiveOverlay();
    }

    private void DrawLiveOverlay()
    {
        LiveOverlayCanvas.Children.Clear();

        var od = _vm.LiveOverlay;
        if (od is null) return;
        if (LiveImageControl.Source is not BitmapSource bmp) return;

        double cw = LiveOverlayCanvas.ActualWidth;
        double ch = LiveOverlayCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;

        double iw    = bmp.PixelWidth;
        double ih    = bmp.PixelHeight;
        double scale = Math.Min(cw / iw, ch / ih);
        double rw    = iw * scale;
        double rh    = ih * scale;
        double ox    = (cw - rw) / 2;
        double oy    = (ch - rh) / 2;

        DrawInspectionOverlay(LiveOverlayCanvas, od, ox, oy, rw, rh, scale);
    }

    /// <summary>
    /// Dibuja el overlay estilo SCMVS sobre cualquier Canvas:
    ///   • Badge OK/NG top-left (fondo verde/rojo, texto blanco grande)
    ///   • Range text top-right ("OKRange:50-100")
    ///   • Si hay coords: crosshair + "Type:OK Score:77" junto al punto
    /// </summary>
    internal static void DrawInspectionOverlay(
        Canvas canvas, LiveOverlayInfo od,
        double ox, double oy, double rw, double rh, double scale)
    {
        var fgColor  = od.IsOk ? Color.FromRgb(0, 220, 80) : Color.FromRgb(255, 80, 80);
        var bgColor  = od.IsOk ? Color.FromRgb(0, 150, 50) : Color.FromRgb(170, 30, 30);
        var fgBrush  = new SolidColorBrush(fgColor);
        var bgBrush  = new SolidColorBrush(bgColor);
        var shadow   = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 1, BlurRadius = 3, Opacity = 0.85 };

        // ── Badge OK/NG top-left (igual al badge verde de SCMVS) ─────────────
        var badgeText = new System.Windows.Controls.TextBlock
        {
            Text       = od.IsOk ? "OK" : "NG",
            Foreground = Brushes.White,
            FontSize   = 22,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Segoe UI"),
        };
        var badge = new Border
        {
            Background = bgBrush,
            Padding    = new Thickness(14, 5, 14, 5),
            Child      = badgeText,
        };
        Canvas.SetLeft(badge, ox);
        Canvas.SetTop(badge,  oy);
        canvas.Children.Add(badge);

        // ── Range text top-right ("OKRange:50-100") ──────────────────────────
        if (!string.IsNullOrEmpty(od.RangeText))
        {
            var rangeTb = new System.Windows.Controls.TextBlock
            {
                Text       = od.RangeText,
                Foreground = fgBrush,
                FontFamily = new FontFamily("Consolas"),
                FontSize   = 13,
                FontWeight = FontWeights.Bold,
                Effect     = shadow,
            };
            rangeTb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(rangeTb, ox + rw - rangeTb.DesiredSize.Width - 6);
            Canvas.SetTop(rangeTb,  oy + 6);
            canvas.Children.Add(rangeTb);
        }

        // ── Punto de detección + "Type:OK Score:77" ──────────────────────────
        string scoreText = $"Type:{(od.IsOk ? "OK" : "NG")} Score:{od.Score:F0}";

        if (od.CenterX.HasValue && od.CenterY.HasValue)
        {
            double px = ox + od.CenterX.Value * scale;
            double py = oy + od.CenterY.Value * scale;

            const double arm = 10, r = 5;
            canvas.Children.Add(MakeLine(px - arm, py, px + arm, py, fgBrush));
            canvas.Children.Add(MakeLine(px, py - arm, px, py + arm, fgBrush));

            var dot = new Ellipse { Width = r * 2, Height = r * 2, Stroke = fgBrush, StrokeThickness = 1.5 };
            Canvas.SetLeft(dot, px - r);
            Canvas.SetTop(dot,  py - r);
            canvas.Children.Add(dot);

            var scoreTb = MakeOverlayText(scoreText, fgBrush, shadow);
            Canvas.SetLeft(scoreTb, px + 14);
            Canvas.SetTop(scoreTb,  py - 9);
            canvas.Children.Add(scoreTb);
        }
        else
        {
            // Sin coordenadas: mostrar score debajo del badge
            var scoreTb = MakeOverlayText(scoreText, fgBrush, shadow);
            Canvas.SetLeft(scoreTb, ox + 6);
            Canvas.SetTop(scoreTb,  oy + 46);
            canvas.Children.Add(scoreTb);
        }
    }

    private static System.Windows.Controls.TextBlock MakeOverlayText(string text, Brush brush, Effect effect) =>
        new()
        {
            Text       = text,
            Foreground = brush,
            FontFamily = new FontFamily("Consolas"),
            FontSize   = 13,
            FontWeight = FontWeights.Bold,
            Effect     = effect,
        };

    private static Line MakeLine(double x1, double y1, double x2, double y2, Brush brush) =>
        new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = 1.5 };
}
