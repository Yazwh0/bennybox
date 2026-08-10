using System.Reflection;

namespace BitMagic.BennyBox.Sources.Tmdb;

// Populated at build time (see BitMagic.BennyBox.Sources.csproj) from the TMDB_API_KEY_ENCODED
// environment variable, which the GitHub Actions release workflow supplies from a repo secret -
// never committed to source. Absent entirely on a local dev build (no such variable set), which is
// fine: TmdbMetadataEnrichmentService just falls back to whatever the user enters in Settings, same
// as before this existed - so "get the app running with working enrichment regardless of the user"
// only actually applies to the officially published installer build.
internal static class EmbeddedTmdbApiKey
{
    private static readonly Lazy<string?> Value = new(() =>
    {
        var encoded = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "EmbeddedTmdbApiKeyEncoded")
            ?.Value;

        return EmbeddedKeyObfuscator.Deobfuscate(encoded);
    });

    public static string? Get() => Value.Value;
}
