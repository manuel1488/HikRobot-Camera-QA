using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using TRVisionAI.Data;
using TRVisionAI.Data.Entities;
using TRVisionAI.Desktop.ViewModels;
using TRVisionAI.Models;

// Alias para evitar ambigüedad con System.Windows.Visibility

namespace TRVisionAI.Desktop.Views;

public partial class FrameDetailWindow : Window
{
    private readonly IReadOnlyList<ResultRow> _rows;
    private readonly InspectionDbService      _db;
    private int                               _index;
    private OverlayData?                      _overlayData;

    private record OverlayData(
        bool   IsOk,
        float  Score,
        string RangeText,   // e.g. "OKRange:50-100"
        float? CenterX,
        float? CenterY);

    private ResultRow CurrentRow => _rows[_index];

    public FrameDetailWindow(IReadOnlyList<ResultRow> rows, int index, InspectionDbService db)
    {
        InitializeComponent();
        _rows  = rows;
        _index = index;
        _db    = db;

        UpdateNavControls();
        PopulateHeader(CurrentRow);
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await LoadDetailAsync();
    }

    // -------------------------------------------------------------------------
    // Navegación
    // -------------------------------------------------------------------------

    private async void BtnPrev_Click(object sender, RoutedEventArgs e) => await NavigateAsync(_index - 1);
    private async void BtnNext_Click(object sender, RoutedEventArgs e) => await NavigateAsync(_index + 1);

    private async void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Left)  { e.Handled = true; await NavigateAsync(_index - 1); }
        if (e.Key == System.Windows.Input.Key.Right) { e.Handled = true; await NavigateAsync(_index + 1); }
    }

    private async Task NavigateAsync(int newIndex)
    {
        if (newIndex < 0 || newIndex >= _rows.Count) return;
        _index = newIndex;
        UpdateNavControls();
        PopulateHeader(CurrentRow);

        // Limpiar estado del contenido anterior
        OverlayCanvas.Children.Clear();
        _overlayData = null;
        FrameImage.Source  = null;
        MaskImage.Source   = null;
        MaskImage.Visibility  = Visibility.Collapsed;
        MaskToggle.Visibility = Visibility.Collapsed;
        MaskToggle.IsChecked  = false;
        ModulesList.ItemsSource = null;
        RawJsonBox.Text         = string.Empty;
        ErrorText.Visibility    = Visibility.Collapsed;

        await LoadDetailAsync();
    }

    private void UpdateNavControls()
    {
        BtnPrev.IsEnabled = _index > 0;
        BtnNext.IsEnabled = _index < _rows.Count - 1;
        NavLabel.Text     = $"{_index + 1} / {_rows.Count}";
    }

    // -------------------------------------------------------------------------
    // Header: datos ya en ResultRow (sin latencia)
    // -------------------------------------------------------------------------

    private void PopulateHeader(ResultRow row)
    {
        bool isNg = row.Verdict == InspectionVerdict.Ng;
        VerdictBadge.Background = new SolidColorBrush(isNg
            ? Color.FromRgb(58, 26, 26)
            : Color.FromRgb(26, 58, 42));
        VerdictLabel.Text       = row.VerdictText;
        VerdictLabel.Foreground = new SolidColorBrush(isNg
            ? Color.FromRgb(224, 80, 80)
            : Color.FromRgb(127, 186, 0));
        SolutionLabel.Text    = row.SolutionName;
        TimestampLabel.Text   = $"Frame #{row.RowNum}  ·  {row.ReceivedAt:dd/MM/yyyy HH:mm:ss.fff}";
    }

    // -------------------------------------------------------------------------
    // Carga lazy desde BD + disco
    // -------------------------------------------------------------------------

    private async Task LoadDetailAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        ContentPanel.Visibility   = Visibility.Collapsed;

        FrameDetail? detail = null;
        var row = CurrentRow;
        try
        {
            // Pequeño retry: el frame puede estar en plena escritura si se abrió
            // inmediatamente después de aparecer en el grid.
            for (int attempt = 0; attempt < 3 && detail is null; attempt++)
            {
                detail = await _db.GetFrameDetailAsync(row.SessionId, row.ReceivedAt);
                if (detail is null) await Task.Delay(300);
            }
        }
        catch (Exception ex)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            ErrorText.Visibility      = Visibility.Visible;
            ErrorText.Text            = $"Error al cargar detalle: {ex.Message}";
            return;
        }

        LoadingOverlay.Visibility = Visibility.Collapsed;

        if (detail is null)
        {
            ErrorText.Visibility = Visibility.Visible;
            ErrorText.Text       = "No se encontró el frame en la base de datos.";
            return;
        }

        PopulateDetail(detail);
        ContentPanel.Visibility = Visibility.Visible;
    }

    private void PopulateDetail(FrameDetail detail)
    {
        // Imagen principal
        if (detail.ImageBytes is { Length: > 0 })
            FrameImage.Source = ToBitmapSource(detail.ImageBytes);

        // Máscara de fallo (overlay)
        if (detail.MaskBytes is { Length: > 16 })
        {
            var maskSource = TryDecodeMask(detail.MaskBytes);
            if (maskSource is not null)
            {
                MaskImage.Source      = maskSource;
                MaskToggle.Visibility = Visibility.Visible;
            }
        }

        // Módulos
        var modules = detail.Entity.Modules.ToList();
        var items   = modules.Count > 0
            ? modules.Select(m => new ModuleDisplayRow(m)).ToList()
            : [new ModuleDisplayRow(null)];
        ModulesList.ItemsSource = items;

        // JSON — pretty-print si es posible
        RawJsonBox.Text = PrettyJson(detail.Entity.RawJson);

        // Overlay de inspección sobre la imagen
        _overlayData = ExtractOverlayData(detail.Entity);
        Dispatcher.InvokeAsync(DrawOverlay, DispatcherPriority.Background);
    }

    private static string PrettyJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "(sin datos)";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return raw;
        }
    }

    private void MaskToggle_Checked(object sender, RoutedEventArgs e)
        => MaskImage.Visibility = Visibility.Visible;

    private void MaskToggle_Unchecked(object sender, RoutedEventArgs e)
        => MaskImage.Visibility = Visibility.Collapsed;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static BitmapSource ToBitmapSource(byte[] jpegBytes)
    {
        using var ms = new MemoryStream(jpegBytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = ms;
        bmp.CacheOption  = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Intenta decodificar la máscara. Si falla (header de 16B embebido en archivos viejos),
    /// reintenta saltando los primeros 16 bytes. Retorna null si no se puede decodificar.
    /// </summary>
    private static BitmapSource? TryDecodeMask(byte[] bytes)
    {
        try
        {
            return ToBitmapSource(bytes);
        }
        catch
        {
            // Fallback: los archivos guardados antes del fix tienen header 16B al inicio
            if (bytes.Length <= 16) return null;
            try
            {
                var stripped = new byte[bytes.Length - 16];
                Array.Copy(bytes, 16, stripped, 0, stripped.Length);
                return ToBitmapSource(stripped);
            }
            catch
            {
                return null;
            }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // -------------------------------------------------------------------------
    // Overlay de inspección: texto verde + punto central
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extrae datos del módulo anomalyjudge (o formato) para el overlay.
    /// Busca rst_similarity_float, rst_string_en, rst_center_x/y en pInfo.
    /// </summary>
    private static OverlayData? ExtractOverlayData(FrameEntity entity)
    {
        var module = entity.Modules.FirstOrDefault(m =>
            m.ModuleName is "anomalyjudge" or "format");
        if (module is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(module.RawJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("pInfo", out var pInfo) || pInfo.ValueKind != JsonValueKind.Array)
                return null;

            string? statusString = null;
            float   score        = 0;
            string? rstStringEn  = null;
            float?  limitL       = null, limitH = null;
            float?  cx           = null, cy     = null;
            float?  rectX        = null, rectY  = null, rectW = null, rectH = null;

            foreach (var info in pInfo.EnumerateArray())
            {
                if (!info.TryGetProperty("strEnName", out var en)) continue;
                switch (en.GetString())
                {
                    case "param_status_string":
                        statusString = GetFirstStringVal(info);
                        break;
                    case "rst_similarity_float":
                        score = GetFirstFloatVal(info) ?? 0;
                        break;
                    case "rst_string_en":
                        rstStringEn = GetFirstStringVal(info);
                        break;
                    case "rst_similarity_limit_l":
                        limitL = GetFirstFloatVal(info);
                        break;
                    case "rst_similarity_limit_h":
                        limitH = GetFirstFloatVal(info);
                        break;
                    // Coordenadas del centro de detección (varios nombres posibles según firmware)
                    case "rst_center_x": case "rst_pos_x": case "obj_x":
                        cx = GetFirstFloatVal(info);
                        break;
                    case "rst_center_y": case "rst_pos_y": case "obj_y":
                        cy = GetFirstFloatVal(info);
                        break;
                    case "rst_rect_x":
                        rectX = GetFirstFloatVal(info);
                        break;
                    case "rst_rect_y":
                        rectY = GetFirstFloatVal(info);
                        break;
                    case "rst_rect_w":
                        rectW = GetFirstFloatVal(info);
                        break;
                    case "rst_rect_h":
                        rectH = GetFirstFloatVal(info);
                        break;
                }
            }

            // Centro desde rect si no había campos directos
            if (cx is null && rectX.HasValue && rectW.HasValue) cx = rectX + rectW / 2;
            if (cy is null && rectY.HasValue && rectH.HasValue) cy = rectY + rectH / 2;

            bool isOk = statusString == "OK";

            // Texto de rango: primero intenta parsear rst_string_en ("OK Range:50-100" → "OKRange:50-100")
            // Si no, construye desde los límites numéricos
            string rangeText;
            if (!string.IsNullOrEmpty(rstStringEn))
            {
                rangeText = rstStringEn.Replace(" ", "");  // "OK Range:50-100" → "OKRange:50-100"
            }
            else if (limitL.HasValue && limitH.HasValue)
            {
                rangeText = $"{(isOk ? "OK" : "NG")}Range:{limitL:F0}-{limitH:F0}";
            }
            else
            {
                rangeText = isOk ? "OK" : "NG";
            }

            return new OverlayData(isOk, score, rangeText, cx, cy);
        }
        catch { return null; }
    }

    /// <summary>
    /// Dibuja el overlay en el Canvas sobre la imagen.
    /// Corre en background priority (después del layout pass) para tener ActualWidth/Height correctos.
    /// </summary>
    private void DrawOverlay()
    {
        OverlayCanvas.Children.Clear();

        if (_overlayData is null) return;
        if (FrameImage.Source is not BitmapSource bmp) return;

        double cw = OverlayCanvas.ActualWidth;
        double ch = OverlayCanvas.ActualHeight;
        if (cw <= 0 || ch <= 0) return;

        // Calcula el rectángulo real renderizado de la imagen (Stretch=Uniform, centrado)
        double iw    = bmp.PixelWidth;
        double ih    = bmp.PixelHeight;
        double scale = Math.Min(cw / iw, ch / ih);
        double rw    = iw * scale;
        double rh    = ih * scale;
        double ox    = (cw - rw) / 2;   // offset X (pillarbox)
        double oy    = (ch - rh) / 2;   // offset Y (letterbox)

        var od    = _overlayData;
        var color = od.IsOk
            ? Color.FromRgb(0, 220, 80)
            : Color.FromRgb(255, 80, 80);
        var brush = new SolidColorBrush(color);

        // ── Texto top-right: "OKRange:50-100" ──────────────────────────────
        var rangeLabel = MakeOverlayLabel(od.RangeText, brush);
        rangeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(rangeLabel, ox + rw - rangeLabel.DesiredSize.Width - 6);
        Canvas.SetTop(rangeLabel,  oy + 6);
        OverlayCanvas.Children.Add(rangeLabel);

        // ── Punto central + etiqueta ────────────────────────────────────────
        double px, py;
        if (od.CenterX.HasValue && od.CenterY.HasValue)
        {
            px = ox + od.CenterX.Value * scale;
            py = oy + od.CenterY.Value * scale;
        }
        else
        {
            // Sin coords: coloca la etiqueta en top-left de la imagen
            px = ox + 8;
            py = oy + 26;
            var scoreLabel = MakeOverlayLabel($"Type:{(od.IsOk ? "OK" : "NG")} Score:{od.Score:F0}", brush);
            Canvas.SetLeft(scoreLabel, px);
            Canvas.SetTop(scoreLabel,  py);
            OverlayCanvas.Children.Add(scoreLabel);
            return;
        }

        // Crosshair
        const double arm = 10;
        OverlayCanvas.Children.Add(MakeOverlayLine(px - arm, py, px + arm, py, brush));
        OverlayCanvas.Children.Add(MakeOverlayLine(px, py - arm, px, py + arm, brush));

        // Círculo central
        const double r = 5;
        var dot = new Ellipse { Width = r * 2, Height = r * 2, Stroke = brush, StrokeThickness = 1.5 };
        Canvas.SetLeft(dot, px - r);
        Canvas.SetTop(dot,  py - r);
        OverlayCanvas.Children.Add(dot);

        // Etiqueta al lado del punto
        var label = MakeOverlayLabel($"Type:{(od.IsOk ? "OK" : "NG")} Score:{od.Score:F0}", brush);
        Canvas.SetLeft(label, px + 14);
        Canvas.SetTop(label,  py - 9);
        OverlayCanvas.Children.Add(label);
    }

    private static System.Windows.Controls.TextBlock MakeOverlayLabel(string text, Brush brush) =>
        new()
        {
            Text       = text,
            Foreground = brush,
            FontFamily = new FontFamily("Consolas"),
            FontSize   = 12,
            FontWeight = FontWeights.Bold,
            Effect     = new DropShadowEffect
            {
                Color       = Colors.Black,
                ShadowDepth = 1,
                BlurRadius  = 3,
                Opacity     = 0.85,
            },
        };

    private static Line MakeOverlayLine(double x1, double y1, double x2, double y2, Brush brush) =>
        new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = 1.5 };

    private static string? GetFirstStringVal(JsonElement info)
    {
        if (!info.TryGetProperty("pStringValue", out var sv) || sv.ValueKind != JsonValueKind.Array) return null;
        var first = sv.EnumerateArray().FirstOrDefault();
        return first.ValueKind == JsonValueKind.Object && first.TryGetProperty("strValue", out var v)
            ? v.GetString() : null;
    }

    private static float? GetFirstFloatVal(JsonElement info)
    {
        if (!info.TryGetProperty("pFloatValue", out var fv) || fv.ValueKind != JsonValueKind.Array) return null;
        var first = fv.EnumerateArray().FirstOrDefault();
        return first.ValueKind == JsonValueKind.Number ? first.GetSingle() : null;
    }

    // -------------------------------------------------------------------------
    // Row model para el ListBox de módulos
    // -------------------------------------------------------------------------

    private sealed class ModuleDisplayRow
    {
        public string  ModuleName      { get; }
        public string  VerdictText     { get; }
        public Brush   ForegroundColor { get; }
        public Brush   Background      { get; }
        public string  DetailLine      { get; }
        public bool    HasDetail       => !string.IsNullOrEmpty(DetailLine);

        public ModuleDisplayRow(ModuleEntity? m)
        {
            if (m is null)
            {
                ModuleName      = "(sin módulos)";
                VerdictText     = "";
                ForegroundColor = Brushes.Gray;
                Background      = Brushes.Transparent;
                DetailLine      = string.Empty;
                return;
            }

            ModuleName = m.ModuleName;

            // Re-parsear veredicto desde RawJson para cubrir registros guardados antes del fix
            var verdict = ParseVerdictFromJson(m.RawJson);
            bool ng     = verdict == InspectionVerdict.Ng;
            bool ok     = verdict == InspectionVerdict.Ok;

            VerdictText     = verdict switch
            {
                InspectionVerdict.Ok => "OK",
                InspectionVerdict.Ng => "NG",
                _                    => "?",
            };
            ForegroundColor = ng ? new SolidColorBrush(Color.FromRgb(224, 80, 80))
                            : ok ? new SolidColorBrush(Color.FromRgb(127, 186, 0))
                            :      new SolidColorBrush(Color.FromRgb(100, 100, 100));
            Background = ng ? new SolidColorBrush(Color.FromArgb(80, 80, 20, 20))
                       : ok ? new SolidColorBrush(Color.FromArgb(80, 20, 60, 30))
                       :      Brushes.Transparent;

            DetailLine = ng ? ExtractDetailLine(m.RawJson) : string.Empty;
        }

        // -------------------------------------------------------------------------
        // Parseo de veredicto desde el JSON del módulo (pInfo → param_status_string)
        // -------------------------------------------------------------------------

        private static InspectionVerdict ParseVerdictFromJson(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson)) return InspectionVerdict.Unknown;
            try
            {
                using var doc  = JsonDocument.Parse(rawJson);
                var root       = doc.RootElement;

                if (!root.TryGetProperty("pInfo", out var pInfo) || pInfo.ValueKind != JsonValueKind.Array)
                    return InspectionVerdict.Unknown;

                string? statusString = null;
                int?    statusInt    = null;

                foreach (var info in pInfo.EnumerateArray())
                {
                    if (!info.TryGetProperty("strEnName", out var enNameEl)) continue;
                    var enName = enNameEl.GetString();

                    if (enName == "param_status_string" && statusString is null)
                    {
                        if (info.TryGetProperty("pStringValue", out var sv) && sv.ValueKind == JsonValueKind.Array)
                        {
                            var first = sv.EnumerateArray().FirstOrDefault();
                            if (first.ValueKind == JsonValueKind.Object &&
                                first.TryGetProperty("strValue", out var sval))
                                statusString = sval.GetString();
                        }
                    }
                    else if (enName == "param_status" && statusInt is null)
                    {
                        if (info.TryGetProperty("pIntValue", out var iv) && iv.ValueKind == JsonValueKind.Array)
                        {
                            var first = iv.EnumerateArray().FirstOrDefault();
                            if (first.ValueKind == JsonValueKind.Number)
                                statusInt = first.GetInt32();
                        }
                    }

                    if (statusString is not null && statusInt is not null) break;
                }

                if (statusString is "NG") return InspectionVerdict.Ng;
                if (statusString is "OK") return InspectionVerdict.Ok;
                return statusInt == 1 ? InspectionVerdict.Ok : InspectionVerdict.Unknown;
            }
            catch { return InspectionVerdict.Unknown; }
        }

        // -------------------------------------------------------------------------
        // Extrae una línea de detalle útil para módulos NG
        // Busca: rst_string_en (anomalyjudge), rst_similarity_float, obj_string, etc.
        // -------------------------------------------------------------------------

        private static string ExtractDetailLine(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson)) return string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                var root      = doc.RootElement;
                if (!root.TryGetProperty("pInfo", out var pInfo) || pInfo.ValueKind != JsonValueKind.Array)
                    return string.Empty;

                string? rstStringEn  = null;
                float?  similarity   = null;
                string? objString    = null;

                foreach (var info in pInfo.EnumerateArray())
                {
                    if (!info.TryGetProperty("strEnName", out var enNameEl)) continue;
                    var enName = enNameEl.GetString();

                    if (enName == "rst_string_en" && rstStringEn is null)
                        rstStringEn = GetFirstString(info);
                    else if (enName == "rst_similarity_float" && similarity is null)
                        similarity = GetFirstFloat(info);
                    else if (enName == "obj_string" && objString is null)
                    {
                        var s = GetFirstString(info);
                        if (!string.IsNullOrEmpty(s) && s != "0;null;") objString = s;
                    }
                }

                // Construir la línea de detalle más informativa disponible
                if (similarity.HasValue && rstStringEn is not null)
                    return $"Score: {similarity:F1}  |  {rstStringEn}";
                if (similarity.HasValue)
                    return $"Score: {similarity:F1}";
                if (rstStringEn is not null)
                    return rstStringEn;
                if (objString is not null)
                    return objString;

                return string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string? GetFirstString(JsonElement info)
        {
            if (!info.TryGetProperty("pStringValue", out var sv) || sv.ValueKind != JsonValueKind.Array)
                return null;
            var first = sv.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("strValue", out var v))
                return v.GetString();
            return null;
        }

        private static float? GetFirstFloat(JsonElement info)
        {
            if (!info.TryGetProperty("pFloatValue", out var fv) || fv.ValueKind != JsonValueKind.Array)
                return null;
            var first = fv.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Number)
                return first.GetSingle();
            return null;
        }
    }
}
