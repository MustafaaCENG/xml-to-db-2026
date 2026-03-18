using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace AdmXmlDb.Core;

/// <summary>
/// Wrapper for Windows DPAPI to encrypt/decrypt sensitive data at rest.
/// Uses LocalMachine scope so the Windows Service (LocalSystem) and the
/// Management UI (interactive user) can both encrypt and decrypt.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DataProtectionHelper
{
    private static readonly byte[] Entropy = [0x41, 0x64, 0x6D, 0x58, 0x6D, 0x6C, 0x44, 0x62]; // "AdmXmlDb"

    public static byte[] Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return [];

        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        return ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.LocalMachine);
    }

    public static string Unprotect(byte[]? encryptedData)
    {
        if (encryptedData == null || encryptedData.Length == 0)
            return string.Empty;

        try
        {
            var decryptedBytes = ProtectedData.Unprotect(encryptedData, Entropy, DataProtectionScope.LocalMachine);
            return System.Text.Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (CryptographicException)
        {
            // Fallback: try decrypting with the old CurrentUser scope (migration scenario)
            try
            {
                var decryptedBytes = ProtectedData.Unprotect(encryptedData, Entropy, DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(decryptedBytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
