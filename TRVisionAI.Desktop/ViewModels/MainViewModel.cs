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
    // Estado
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
    // Datos en vivo
    // -------------------------------------------------------------------------

    [ObservableProperty] private BitmapSource?                   _liveImage;
    [ObservableProperty] private ObservableCollection<ResultRow> _results = [];
    [ObservableProperty] private int _okCount;
    [ObservableProperty] private int _ngCount;
    public int TotalCount => OkCount + NgCount;

    // -------------------------------------------------------------------------
    // Internos
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
    // Auto-conexión al abrir la ventana
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
    // Comando Detener
    // -------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private void Disconnect() => _cts?.Cancel();

    // -------------------------------------------------------------------------
    // Loop de adquisición (hilo de fondo)
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

            // Persistir en BD (desde el hilo de fondo está bien — factory crea su propio context)
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
    // Helper: bytes JPEG → BitmapSource (thread-safe via Freeze)
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
