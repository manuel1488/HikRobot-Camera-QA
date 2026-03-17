using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TRVisionAI.Camera;
using TRVisionAI.Desktop.Settings;

namespace TRVisionAI.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    // -------------------------------------------------------------------------
    // Camera
    // -------------------------------------------------------------------------
    [ObservableProperty] private string _cameraIp       = string.Empty;
    [ObservableProperty] private string _cameraUser     = "Admin";
    [ObservableProperty] private string _cameraPassword = string.Empty;
    [ObservableProperty] private bool   _useEncryption  = true;

    // Cameras discovered on the network
    [ObservableProperty] private ObservableCollection<CameraInfo> _foundCameras = [];
    [ObservableProperty] private CameraInfo?                      _selectedFoundCamera;
    [ObservableProperty] private bool                             _hasCameras;

    // -------------------------------------------------------------------------
    // REST API
    // -------------------------------------------------------------------------
    [ObservableProperty] private string _apiBaseUrl = string.Empty;
    [ObservableProperty] private string _apiKey     = string.Empty;

    // -------------------------------------------------------------------------
    // Status
    // -------------------------------------------------------------------------
    [ObservableProperty] private string _statusMessage  = string.Empty;
    [ObservableProperty] private bool   _isScanning;

    public event EventHandler? Saved;

    // -------------------------------------------------------------------------
    // Initialization
    // -------------------------------------------------------------------------

    public void LoadFromSettings()
    {
        var s = SettingsService.Load();

        CameraIp       = s.Camera.IpAddress;
        CameraUser     = s.Camera.User;
        CameraPassword = SettingsService.Unprotect(s.Camera.PasswordDpapi);
        UseEncryption  = s.Camera.UseEncryption;

        ApiBaseUrl = s.Api.BaseUrl;
        ApiKey     = SettingsService.Unprotect(s.Api.ApiKeyDpapi);
    }

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------

    [RelayCommand]
    private async Task ScanCamerasAsync()
    {
        IsScanning    = true;
        StatusMessage = "Buscando cámaras en la red…";
        FoundCameras.Clear();
        HasCameras = false;

        try
        {
            var cameras = await Task.Run(CameraClient.EnumerateDevices);
            foreach (var c in cameras)
                FoundCameras.Add(c);

            if (cameras.Count == 0)
            {
                StatusMessage = "No se encontraron cámaras.";
            }
            else
            {
                HasCameras           = true;
                SelectedFoundCamera  = FoundCameras[0];
                // Single camera found — auto-fill its IP
                if (cameras.Count == 1)
                {
                    CameraIp      = cameras[0].IpAddress;
                    StatusMessage = $"Cámara encontrada: {cameras[0].IpAddress} — {cameras[0].ModelName}";
                }
                else
                {
                    StatusMessage = $"{cameras.Count} cámaras encontradas. Selecciona una.";
                }
            }
        }
        catch (HikrobotException ex)
        {
            StatusMessage = $"Error al buscar: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    partial void OnSelectedFoundCameraChanged(CameraInfo? value)
    {
        if (value is not null)
            CameraIp = value.IpAddress;
    }

    [RelayCommand]
    private void Save()
    {
        var s = new AppSettings
        {
            Camera = new CameraSettings
            {
                IpAddress     = CameraIp.Trim(),
                User          = CameraUser.Trim(),
                PasswordDpapi = SettingsService.Protect(CameraPassword),
                UseEncryption = UseEncryption,
            },
            Api = new ApiSettings
            {
                BaseUrl     = ApiBaseUrl.Trim(),
                ApiKeyDpapi = SettingsService.Protect(ApiKey),
            },
        };

        SettingsService.Save(s);
        StatusMessage = "Configuración guardada.";
        Saved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Saved?.Invoke(this, EventArgs.Empty);
}
