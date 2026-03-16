using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HikrobotDesktop.Settings;

/// <summary>
/// Persiste la configuración en %AppData%\HikrobotDesktop\settings.json.
/// Las contraseñas se cifran con Windows DPAPI (solo el usuario actual puede descifrarlas).
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HikrobotDesktop",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // -------------------------------------------------------------------------
    // Carga / Guardado
    // -------------------------------------------------------------------------

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json, Encoding.UTF8);
    }

    // -------------------------------------------------------------------------
    // DPAPI — cifrado de credenciales
    // -------------------------------------------------------------------------

    /// <summary>Cifra un texto con DPAPI y retorna base64. Retorna "" si el texto es vacío.</summary>
    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        byte[] data      = Encoding.UTF8.GetBytes(plainText);
        byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>Descifra un valor DPAPI en base64. Retorna "" si falla o está vacío.</summary>
    public static string Unprotect(string base64Cipher)
    {
        if (string.IsNullOrEmpty(base64Cipher)) return string.Empty;
        try
        {
            byte[] encrypted = Convert.FromBase64String(base64Cipher);
            byte[] data      = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            return string.Empty;
        }
    }
}
