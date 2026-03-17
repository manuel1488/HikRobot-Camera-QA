namespace TRVisionAI.Desktop.Settings;

public sealed class AppSettings
{
    public CameraSettings Camera { get; set; } = new();
    public ApiSettings    Api    { get; set; } = new();
}

public sealed class CameraSettings
{
    public string IpAddress     { get; set; } = string.Empty;
    public string User          { get; set; } = "Admin";
    /// <summary>DPAPI-encrypted password (base64). Empty if not yet saved.</summary>
    public string PasswordDpapi { get; set; } = string.Empty;
    public bool   UseEncryption { get; set; } = true;
}

public sealed class ApiSettings
{
    public string BaseUrl     { get; set; } = string.Empty;
    /// <summary>DPAPI-encrypted API key (base64). Empty if not yet configured.</summary>
    public string ApiKeyDpapi { get; set; } = string.Empty;
}
