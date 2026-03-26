using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
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

    /// <summary>
    /// Encrypts a SecureString without materializing it as a plain string in managed memory.
    /// Converts UTF-16LE (native SecureString encoding) to UTF-8 before encrypting so that
    /// Unprotect() — which always decodes as UTF-8 — returns the correct value.
    /// </summary>
    public static byte[] Protect(SecureString secureText)
    {
        if (secureText == null || secureText.Length == 0)
            return [];

        var ptr = IntPtr.Zero;
        byte[]? utf16Bytes = null;
        byte[]? utf8Bytes = null;
        try
        {
            ptr = Marshal.SecureStringToGlobalAllocUnicode(secureText);
            utf16Bytes = new byte[secureText.Length * 2];
            Marshal.Copy(ptr, utf16Bytes, 0, utf16Bytes.Length);

            // Convert UTF-16LE → UTF-8 so Unprotect() decodes correctly
            utf8Bytes = System.Text.Encoding.Convert(
                System.Text.Encoding.Unicode,
                System.Text.Encoding.UTF8,
                utf16Bytes);

            return ProtectedData.Protect(utf8Bytes, Entropy, DataProtectionScope.LocalMachine);
        }
        finally
        {
            if (utf16Bytes != null) Array.Clear(utf16Bytes, 0, utf16Bytes.Length);
            if (utf8Bytes  != null) Array.Clear(utf8Bytes,  0, utf8Bytes.Length);
            if (ptr != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
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
