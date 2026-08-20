using System.Text;
using Android.Security.Keystore;
using BitMagic.BennyBox.Core.Services;
using Java.Interop;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace BitMagic.BennyBox.Android.Services;

// AES/GCM with a key generated inside the Android Keystore - the key material never leaves secure
// hardware (where available) and isn't extractable, which is the closest Android equivalent to
// DPAPI's per-user protection on Windows (see CredentialProtector).
public class AndroidCredentialProtector : ICredentialProtector
{
    private const string KeyAlias = "BennyBoxCredentialKey";
    private const int GcmIvLengthBytes = 12;
    private const int GcmTagLengthBits = 128;

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return null;
        }

        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());
        var iv = cipher.GetIV()!;
        var ciphertext = cipher.DoFinal(Encoding.UTF8.GetBytes(plaintext))!;

        var combined = new byte[iv.Length + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, combined, 0, iv.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, iv.Length, ciphertext.Length);
        return Convert.ToBase64String(combined);
    }

    public string? Unprotect(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
        {
            return null;
        }

        var combined = Convert.FromBase64String(encrypted);
        var iv = combined[..GcmIvLengthBytes];
        var ciphertext = combined[GcmIvLengthBytes..];

        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.DecryptMode, GetOrCreateKey(), new GCMParameterSpec(GcmTagLengthBits, iv));
        var bytes = cipher.DoFinal(ciphertext)!;
        return Encoding.UTF8.GetString(bytes);
    }

    private static ISecretKey GetOrCreateKey()
    {
        var keyStore = KeyStore.GetInstance("AndroidKeyStore")!;
        keyStore.Load(null);

        if (keyStore.ContainsAlias(KeyAlias) && keyStore.GetKey(KeyAlias, null) is { } rawKey)
        {
            // A plain C# `is ISecretKey`/`as ISecretKey` pattern-match against the JNI-returned IKey
            // reference silently fails here (falls through as if the cast failed, with no exception) -
            // JavaCast<T>() goes through an actual JNI cast instead of relying on the binding's static
            // C# type, which is what's needed to reliably get back the same key across calls. Without
            // this, GetOrCreateKey silently generated a brand new key - which Android allows even when
            // an alias already exists (it just overwrites) - every single call, breaking decryption of
            // anything encrypted moments earlier under the previous key.
            return rawKey.JavaCast<ISecretKey>();
        }

        var keyGenerator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
        var spec = new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .Build();
        keyGenerator.Init(spec);
        return keyGenerator.GenerateKey()!;
    }
}
