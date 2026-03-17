using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TRVisionAI.Camera;
using TRVisionAI.Models;
using TRVisionAI.Data;
using TRVisionAI.Desktop.Settings;

namespace TRVisionAI.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    private bool _isConnected;

    public bool IsDisconnected => !IsConnected;

    [ObservableProperty] private string _statusMessage  = "Conectando…";
    [ObservableProperty] private string _cameraIp       = string.Empty;
    [ObservableProperty] private string _cameraModel    = string.Empty;
    [ObservableProperty] private string _sessionUser    = string.Empty;

    public string AppVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    // -------------------------------------------------------------------------
    // Live data
    // -------------------------------------------------------------------------

    [ObservableProperty] private BitmapSource?                   _liveImage;
    [ObservableProperty] private ObservableCollection<ResultRow> _results = [];
    [ObservableProperty] private int _okCount;
    [ObservableProperty] private int _ngCount;
    public int    TotalCount  => OkCount + NgCount;
    public string OkRatioText => TotalCount == 0 ? "—%" : $"{(double)OkCount / TotalCount * 100:F0}%";

    /// <summary>Overlay data for the live image (score, range, detection coordinates).</summary>
    [ObservableProperty] private LiveOverlayInfo? _liveOverlay;

    // -------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------

    private CameraClient?            _client;
    private CancellationTokenSource? _cts;
    private int                      _rowNum;
    private int                      _sessionId;
    private readonly InspectionDbService _db;

    public MainViewModel()
    {
        _db = App.DbService;
    }

    // -------------------------------------------------------------------------
    // Auto-connect on window open
    // -------------------------------------------------------------------------

    public async Task AutoConnectAsync()
    {
        var s = SettingsService.Load();
        string user          = s.Camera.User;
        string plainPassword = SettingsService.Unprotect(s.Camera.PasswordDpapi);
        bool   useEncryption = s.Camera.UseEncryption;
        string savedIp       = s.Camera.IpAddress;

        StatusMessage = "Buscando cámara…";

        List<CameraInfo> cameras;
        try
        {
            cameras = await Task.Run(CameraClient.EnumerateDevices);
        }
        catch (HikrobotException ex)
        {
            StatusMessage = $"Error al buscar cámaras: {ex.Message}";
            return;
        }

        if (cameras.Count == 0)
        {
            StatusMessage = "No se encontraron cámaras. Verifica la conexión de red.";
            return;
        }

        var camera = cameras.FirstOrDefault(c => c.IpAddress == savedIp) ?? cameras[0];
        StatusMessage = $"Conectando a {camera.IpAddress}…";

        try
        {
            _client = new CameraClient();
            string loginPassword = useEncryption
                ? PasswordHelper.ToMd5Hex(plainPassword)
                : plainPassword;

            _client.Connect(camera, user, loginPassword, encryptPassword: useEncryption);
            _client.StartAcquisition();

            _sessionId = await _db.BeginSessionAsync(camera.IpAddress, camera.ModelName, user);

            IsConnected   = true;
            CameraIp      = camera.IpAddress;
            CameraModel   = camera.ModelName;
            SessionUser   = user;
            StatusMessage = "Inspección en curso";
            DisconnectCommand.NotifyCanExecuteChanged();

            _cts = new CancellationTokenSource();
            await Task.Run(() => AcquisitionLoop(_cts.Token));
        }
        catch (HikrobotException ex)
        {
            StatusMessage = $"Error al conectar: {ex.Message}";
            _client?.Dispose();
            _client = null;
        }
    }

    // -------------------------------------------------------------------------
    // Stop command
    // -------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private void Disconnect() => _cts?.Cancel();

    // -------------------------------------------------------------------------
    // Acquisition loop (background thread)
    // -------------------------------------------------------------------------

    private void AcquisitionLoop(CancellationToken token)
    {
        int errors = 0;

        while (!token.IsCancellationRequested)
        {
            InspectionFrame? frame;
            try
            {
                frame = _client!.TryGetFrame(timeoutMs: 1000);
            }
            catch (HikrobotException ex)
            {
                errors++;
                App.Current.Dispatcher.Invoke(() =>
                    StatusMessage = $"Error de frame ({errors}): {ex.Message}");

                if (errors >= 5)
                {
                    App.Current.Dispatcher.Invoke(() =>
                        StatusMessage = "Demasiados errores consecutivos. Deteniéndose.");
                    break;
                }
                continue;
            }

            if (frame is null) continue;
            errors = 0;

            // Persist to DB (safe from background thread — factory creates its own context)
            if (_sessionId > 0)
                _ = _db.SaveFrameAsync(_sessionId, frame);

            App.Current.Dispatcher.Invoke(() => ProcessFrame(frame));
        }

        App.Current.Dispatcher.Invoke(OnDisconnected);
    }

    private void ProcessFrame(InspectionFrame frame)
    {
        if (frame.ImageBytes is { Length: > 0 })
            LiveImage = ToBitmapSource(frame.ImageBytes);

        if      (frame.Verdict == InspectionVerdict.Ok) OkCount++;
        else if (frame.Verdict == InspectionVerdict.Ng) NgCount++;
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(OkRatioText));

        _rowNum++;
        Results.Insert(0, new ResultRow
        {
            RowNum       = _rowNum,
            ReceivedAt   = frame.ReceivedAt,
            Verdict      = frame.Verdict,
            SolutionName = frame.SolutionName,
            TotalCount   = frame.TotalCount,
            NgCount      = frame.NgCount,
            FrameNumber  = (long)frame.FrameNumber,
            SessionId    = _sessionId,
        });

        while (Results.Count > 500)
            Results.RemoveAt(Results.Count - 1);

        StatusMessage = $"Frame {_rowNum} — {frame.Verdict}";
        LiveOverlay   = ExtractLiveOverlay(frame);
    }

    private void OnDisconnected()
    {
        _client?.Dispose();
        _client       = null;
        _cts          = null;
        IsConnected   = false;
        StatusMessage = "Detenido.";
        DisconnectCommand.NotifyCanExecuteChanged();

        if (_sessionId > 0)
            _ = _db.EndSessionAsync(_sessionId);
        _sessionId = 0;
    }

    // -------------------------------------------------------------------------
    // Helper: extract overlay data from the live frame
    // -------------------------------------------------------------------------

    private static LiveOverlayInfo? ExtractLiveOverlay(InspectionFrame frame)
    {
        var module = frame.ModuleResults.FirstOrDefault(m => m.ModuleName == "anomalyjudge")
                  ?? frame.ModuleResults.FirstOrDefault(m => m.ModuleName == "format");
        if (module is null) return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(module.RawJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("pInfo", out var pInfo) ||
                pInfo.ValueKind != System.Text.Json.JsonValueKind.Array)
                return null;

            string? statusString = null;
            float   score        = 0;
            string? rstStringEn  = null;
            float?  limitL       = null, limitH = null;
            float?  cx           = null, cy     = null;
            float?  rectX = null, rectY = null, rectW = null, rectH = null;

            foreach (var info in pInfo.EnumerateArray())
            {
                if (!info.TryGetProperty("strEnName", out var en)) continue;
                switch (en.GetString())
                {
                    case "param_status_string":  statusString = GetFirstStr(info); break;
                    case "rst_similarity_float": score = GetFirstFlt(info) ?? 0;  break;
                    case "rst_string_en":        rstStringEn = GetFirstStr(info); break;
                    case "rst_similarity_limit_l": limitL = GetFirstFlt(info) ?? GetFirstInt(info); break;
                    case "rst_similarity_limit_h": limitH = GetFirstFlt(info) ?? GetFirstInt(info); break;
                    case "det_box_cx": case "show_text_x":
                    case "rst_center_x": case "rst_pos_x": case "obj_x":
                        cx ??= GetFirstFlt(info); break;
                    case "det_box_cy": case "show_text_y":
                    case "rst_center_y": case "rst_pos_y": case "obj_y":
                        cy ??= GetFirstFlt(info); break;
                    case "rst_rect_x": rectX = GetFirstFlt(info); break;
                    case "rst_rect_y": rectY = GetFirstFlt(info); break;
                    case "rst_rect_w": rectW = GetFirstFlt(info); break;
                    case "rst_rect_h": rectH = GetFirstFlt(info); break;
                }
            }

            if (cx is null && rectX.HasValue && rectW.HasValue) cx = rectX + rectW / 2;
            if (cy is null && rectY.HasValue && rectH.HasValue) cy = rectY + rectH / 2;

            bool isOk = statusString == "OK";
            string rangeText = !string.IsNullOrEmpty(rstStringEn)
                ? rstStringEn.Replace(" ", "")
                : limitL.HasValue && limitH.HasValue
                    ? $"{(isOk ? "OK" : "NG")}Range:{limitL:F0}-{limitH:F0}"
                    : isOk ? "OK" : "NG";

            return new LiveOverlayInfo(isOk, score, rangeText, cx, cy);
        }
        catch { return null; }
    }

    private static string? GetFirstStr(System.Text.Json.JsonElement info)
    {
        if (!info.TryGetProperty("pStringValue", out var sv) ||
            sv.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
        var first = sv.EnumerateArray().FirstOrDefault();
        return first.ValueKind == System.Text.Json.JsonValueKind.Object &&
               first.TryGetProperty("strValue", out var v) ? v.GetString() : null;
    }

    private static float? GetFirstFlt(System.Text.Json.JsonElement info)
    {
        if (!info.TryGetProperty("pFloatValue", out var fv) ||
            fv.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
        var first = fv.EnumerateArray().FirstOrDefault();
        return first.ValueKind == System.Text.Json.JsonValueKind.Number ? first.GetSingle() : null;
    }

    private static float? GetFirstInt(System.Text.Json.JsonElement info)
    {
        if (!info.TryGetProperty("pIntValue", out var iv) ||
            iv.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
        var first = iv.EnumerateArray().FirstOrDefault();
        return first.ValueKind == System.Text.Json.JsonValueKind.Number ? (float?)first.GetInt32() : null;
    }

    // -------------------------------------------------------------------------
    // Helper: JPEG bytes → BitmapSource (thread-safe via Freeze)
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

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        _cts?.Cancel();
        _client?.Dispose();

        if (_sessionId > 0)
            _ = _db.EndSessionAsync(_sessionId);
        _sessionId = 0;
    }
}
