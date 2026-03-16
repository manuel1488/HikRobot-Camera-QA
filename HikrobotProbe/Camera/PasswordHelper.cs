using System.Security.Cryptography;
using System.Text;

namespace HikrobotProbe.Camera;

/// <summary>
/// Utilidades para el cifrado de contraseña que acepta LoginEX(bEncryption=true).
/// Hikrobot usa MD5 del texto plano como forma de "pre-cifrado" del cliente.
/// </summary>
public static class PasswordHelper
{
    /// <summary>
    /// Retorna el hash MD5 de la contraseña en formato hexadecimal en minúsculas (32 chars).
    /// Esto es lo que se pasa a LoginEX(..., bEncryption: true).
    /// </summary>
    public static string ToMd5Hex(string plainPassword)
    {
        byte[] bytes = MD5.HashData(Encoding.UTF8.GetBytes(plainPassword));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
