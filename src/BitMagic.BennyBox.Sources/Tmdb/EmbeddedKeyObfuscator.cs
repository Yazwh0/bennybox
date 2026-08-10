using System.Text;

namespace BitMagic.BennyBox.Sources.Tmdb;

// Not real encryption - a client-distributed desktop app can never truly hide a bundled secret from a
// determined reverse-engineer (the pad below is right here in source), so this only exists to keep
// the raw key from being a plaintext string a casual `strings` pass over the binary would surface.
// See EmbeddedTmdbApiKey for where the obfuscated value actually comes from at build time.
internal static class EmbeddedKeyObfuscator
{
    private static readonly byte[] Pad = "BennyBoxTmdbEmbeddedKeyPad!"u8.ToArray();

    public static string Obfuscate(string plainText) => Convert.ToBase64String(Xor(Encoding.UTF8.GetBytes(plainText)));

    public static string? Deobfuscate(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Xor(Convert.FromBase64String(encoded)));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[] Xor(byte[] data)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ Pad[i % Pad.Length]);
        }

        return result;
    }
}
