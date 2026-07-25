using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace BitMagic.BennyBox.Core.Services;

// Wraps DPAPI (Windows-only, matches this app's target platform) so Xtream Codes
// passwords are encrypted at rest and only ever held in memory as plaintext.
[SupportedOSPlatform("windows")]
public static class CredentialProtector
{
    public static string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string? Unprotect(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
        {
            return null;
        }

        var protectedBytes = Convert.FromBase64String(encrypted);
        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
