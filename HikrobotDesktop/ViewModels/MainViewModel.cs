using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hikrobot.Camera;
using Hikrobot.Models;

namespace HikrobotDesktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    // -------------------------------------------------------------------------
    // Propiedades de conexión
    // -------------------------------------------------------------------------

    [ObservableProperty] private ObservableCollection<CameraInfo> _cameras = [];
    [ObservableProperty] private CameraInfo?                      _selectedCamera;
    [ObservableProperty] private string                           _user     = "Admin";
    [ObservableProperty] private string                           _password = string.Empty;
    [ObservableProperty] private bool                             _useEncryption = true;

    // -------------------------------------------------------------------------
    // Estado
    // -------------------------------------------------------------------------

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    private bool _isConnected;

    public bool IsDisconnected => !IsConnected;

    [ObservableProperty] private string _statusMessage = "Listo";

    // -------------------------------------------------------------------------
    // Datos en vivo
    // -------------------------------------------------------------------------

    [ObservableProperty] private BitmapSource? _liveImage;
    [ObservableProperty] private ObservableCollection<ResultRow> _results = [];
    [ObservableProperty] private int _okCount;
    [ObservableProperty] private int _ngCount;
    public int TotalCount => OkCount + NgCount;

    // -------------------------------------------------------------------------
    // Internos
    // -------------------------------------------------------------------------

    private CameraClient?         _client;
    private CancellationTokenSource? _cts;
    private int                   _rowNum;

    // -------------------------------------------------------------------------
    // Comandos
    // -------------------------------------------------------------------------

    [RelayCommand]
    private void Scan()
    {
        StatusMessage = "Buscando cámaras…";
        try
        {
            var found = CameraClient.EnumerateDevices();
            Cameras = new ObservableCollection<CameraInfo>(found);
            SelectedCamera = Cameras.FirstOrDefault();
            StatusMessage = found.Count == 0
                ? "No se encontraron cámaras."
                : $"{found.Count} cámara(s) encontrada(s).";
        }
        catch (HikrobotException ex)
        {
            StatusMessage = $"Error al buscar cámaras: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (SelectedCamera is null) return;

        StatusMessage = $"Conectando a {SelectedCamera.IpAddress}…";
        try
        {
            _client = new CameraClient();
            string loginPassword = UseEncryption
                ? PasswordHelper.ToMd5Hex(Password)
                : Password;

            _client.Connect(SelectedCamera, User, loginPassword, encryptPassword: UseEncryption);
            _client.StartAcquisition();

            IsConnected   = true;
            StatusMessage = $"Conectado a {SelectedCamera.IpAddress}";

            _cts = new CancellationTokenSource();
            await Task.Run(() => AcquisitionLoop(_cts.Token));
        }
        catch (HikrobotException ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _client?.Dispose();
            _client = null;
        }
    }

    private bool CanConnect() => SelectedCamera is not null && !IsConnected;

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private void Disconnect()
    {
        _cts?.Cancel();
    }

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
                        StatusMessage = "Demasiados errores. Desconectando.");
                    break;
                }
                continue;
            }

            if (frame is null) continue;
            errors = 0;

            App.Current.Dispatcher.Invoke(() => ProcessFrame(frame));
        }

        App.Current.Dispatcher.Invoke(OnDisconnected);
    }

    private void ProcessFrame(InspectionFrame frame)
    {
        // Imagen en vivo
        if (frame.ImageBytes is { Length: > 0 })
            LiveImage = ToBitmapSource(frame.ImageBytes);

        // Contadores
        if      (frame.Verdict == InspectionVerdict.Ok) OkCount++;
        else if (frame.Verdict == InspectionVerdict.Ng) NgCount++;
        OnPropertyChanged(nameof(TotalCount));

        // Fila en el grid
        _rowNum++;
        Results.Insert(0, new ResultRow
        {
            RowNum       = _rowNum,
            ReceivedAt   = frame.ReceivedAt,
            Verdict      = frame.Verdict,
            SolutionName = frame.SolutionName,
            TotalCount   = frame.TotalCount,
            NgCount      = frame.NgCount,
        });

        // Limitar historial a 500 filas
        while (Results.Count > 500)
            Results.RemoveAt(Results.Count - 1);

        StatusMessage = $"Frame {_rowNum} — {frame.Verdict}";
    }

    private void OnDisconnected()
    {
        _client?.Dispose();
        _client    = null;
        _cts       = null;
        IsConnected = false;
        StatusMessage = "Desconectado.";
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    // -------------------------------------------------------------------------
    // Helper: bytes JPEG → BitmapSource (thread-safe via Freeze)
    // -------------------------------------------------------------------------

    private static BitmapSource ToBitmapSource(byte[] jpegBytes)
    {
        using var ms = new MemoryStream(jpegBytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource  = ms;
        bmp.CacheOption   = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze(); // permite uso cross-thread
        return bmp;
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        _cts?.Cancel();
        _client?.Dispose();
    }
}
