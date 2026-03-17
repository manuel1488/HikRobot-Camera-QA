using System.Security.Cryptography;
using System.Text;

namespace TRVisionAI.Camera;

/// <summary>
/// Password utilities for the encryption mode accepted by LoginEX(bEncryption=true).
/// Hikrobot uses MD5 of the plain-text password as the client-side "pre-encryption" step.
/// </summary>
public static class PasswordHelper
{
    /// <summary>
    /// Returns the MD5 hash of the password as a lowercase hex string (32 chars).
    /// This is what must be passed to LoginEX(..., bEncryption: true).
    /// </summary>
    public static string ToMd5Hex(string plainPassword)
    {
        byte[] bytes = MD5.HashData(Encoding.UTF8.GetBytes(plainPassword));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
