using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

// The text inside the "set up your phone" QR code desktop shows (see SettingsView's QR button) and
// Android scans (see SettingsView's "Scan QR code" button) - one or more profiles, credentials
// included, so a phone can be set up without typing anything.
//
// Format: "BENNYBOX1:" + base64url(deflate(json)). Passwords travel in plain text inside it: each
// device protects them with its own ICredentialProtector (DPAPI / Android Keystore), so the stored
// ciphertext can't be carried across. Treat the QR like the passwords themselves.
//
// Deliberately left out: Id (a new one is minted on import - Jellyfin derives its device id from it,
// and a second login under the same device id would revoke the desktop's token), Jellyfin's user id/
// access token (the phone logs in for itself on first import), refresh/caching state, and LocalFolder
// profiles entirely (a Windows path means nothing on a phone).
public static class ProfileSetupCode
{
    public const string Prefix = "BENNYBOX1:";

    public static bool CanShare(ProfileSource profile) => profile.SourceType != ProfileSourceType.LocalFolder;

    public static string Encode(IEnumerable<ProfileSource> profiles, ICredentialProtector protector, string? tmdbApiKey = null)
    {
        var payload = new SetupPayload
        {
            TmdbApiKey = string.IsNullOrWhiteSpace(tmdbApiKey) ? null : tmdbApiKey.Trim(),
            Profiles = profiles.Where(CanShare).Select(p => SetupProfile.From(p, protector)).ToList()
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(json);
        }

        var base64 = Convert.ToBase64String(output.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Prefix + base64;
    }

    public static bool TryDecode(string? code, ICredentialProtector protector, out SetupResult result, out string? error)
    {
        result = new SetupResult([], null);
        error = null;

        var text = code?.Trim() ?? string.Empty;
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "That isn't a Benny Box setup code.";
            return false;
        }

        try
        {
            var base64 = text[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');

            using var input = new MemoryStream(Convert.FromBase64String(base64));
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            var payload = JsonSerializer.Deserialize<SetupPayload>(deflate, JsonOptions);
            if (payload?.Profiles is not { Count: > 0 } && string.IsNullOrWhiteSpace(payload?.TmdbApiKey))
            {
                error = "The setup code is empty.";
                return false;
            }

            var profiles = (payload!.Profiles ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => p.ToProfile(protector)).Where(CanShare).ToList();
            result = new SetupResult(profiles, payload.TmdbApiKey);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException)
        {
            error = "The setup code is damaged or from a newer version of Benny Box.";
            return false;
        }
    }

    // True if `existing` already points at the same account as `candidate` - used to skip profiles
    // the phone already has when the same QR is scanned twice.
    public static bool IsSameAccount(ProfileSource existing, ProfileSource candidate)
    {
        if (existing.SourceType != candidate.SourceType)
        {
            return false;
        }

        static bool Eq(string? a, string? b) => string.Equals(a?.Trim().TrimEnd('/'), b?.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

        return candidate.SourceType switch
        {
            ProfileSourceType.M3u => Eq(existing.M3uUrl, candidate.M3uUrl),
            ProfileSourceType.XtreamCodes => Eq(existing.XtreamServerUrl, candidate.XtreamServerUrl) && Eq(existing.XtreamUsername, candidate.XtreamUsername),
            ProfileSourceType.Sftp => Eq(existing.SftpHost, candidate.SftpHost) && existing.SftpPort == candidate.SftpPort && Eq(existing.SftpUsername, candidate.SftpUsername),
            ProfileSourceType.Jellyfin => Eq(existing.JellyfinServerUrl, candidate.JellyfinServerUrl) && Eq(existing.JellyfinUsername, candidate.JellyfinUsername),
            _ => false
        };
    }

    public sealed record SetupResult(IReadOnlyList<ProfileSource> Profiles, string? TmdbApiKey);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Short property names keep the QR code small (and so easier to scan) - this is a wire format,
    // not something anyone reads.
    private sealed class SetupPayload
    {
        [JsonPropertyName("t")] public string? TmdbApiKey { get; set; }
        [JsonPropertyName("p")] public List<SetupProfile>? Profiles { get; set; }
    }

    private sealed class SetupProfile
    {
        [JsonPropertyName("n")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("s")] public ProfileSourceType SourceType { get; set; }
        [JsonPropertyName("e")] public EpgSourceType EpgSourceType { get; set; }
        [JsonPropertyName("eu")] public string? EpgUrl { get; set; }
        [JsonPropertyName("m")] public string? M3uUrl { get; set; }
        [JsonPropertyName("xs")] public string? XtreamServerUrl { get; set; }
        [JsonPropertyName("xu")] public string? XtreamUsername { get; set; }
        [JsonPropertyName("xp")] public string? XtreamPassword { get; set; }
        [JsonPropertyName("sh")] public string? SftpHost { get; set; }
        [JsonPropertyName("so")] public int? SftpPort { get; set; }
        [JsonPropertyName("su")] public string? SftpUsername { get; set; }
        [JsonPropertyName("sp")] public string? SftpPassword { get; set; }
        [JsonPropertyName("sm")] public string? SftpMoviesRemotePath { get; set; }
        [JsonPropertyName("ss")] public string? SftpSeriesRemotePath { get; set; }
        [JsonPropertyName("sc")] public string? SftpClipsRemotePath { get; set; }
        [JsonPropertyName("js")] public string? JellyfinServerUrl { get; set; }
        [JsonPropertyName("ju")] public string? JellyfinUsername { get; set; }
        [JsonPropertyName("jp")] public string? JellyfinPassword { get; set; }
        [JsonPropertyName("jk")] public string? JellyfinApiKey { get; set; }
        [JsonPropertyName("jl")] public string? JellyfinLibraryMappings { get; set; }

        public static SetupProfile From(ProfileSource p, ICredentialProtector protector) => new()
        {
            Name = p.Name,
            SourceType = p.SourceType,
            EpgSourceType = p.EpgSourceType,
            EpgUrl = p.EpgUrl,
            M3uUrl = p.M3uUrl,
            XtreamServerUrl = p.XtreamServerUrl,
            XtreamUsername = p.XtreamUsername,
            XtreamPassword = protector.Unprotect(p.XtreamPasswordEncrypted),
            SftpHost = p.SftpHost,
            SftpPort = p.SftpPort,
            SftpUsername = p.SftpUsername,
            SftpPassword = protector.Unprotect(p.SftpPasswordEncrypted),
            SftpMoviesRemotePath = p.SftpMoviesRemotePath,
            SftpSeriesRemotePath = p.SftpSeriesRemotePath,
            SftpClipsRemotePath = p.SftpClipsRemotePath,
            JellyfinServerUrl = p.JellyfinServerUrl,
            JellyfinUsername = p.JellyfinUsername,
            JellyfinPassword = protector.Unprotect(p.JellyfinPasswordEncrypted),
            JellyfinApiKey = protector.Unprotect(p.JellyfinApiKeyEncrypted),
            JellyfinLibraryMappings = p.JellyfinLibraryMappings
        };

        public ProfileSource ToProfile(ICredentialProtector protector) => new()
        {
            Name = Name.Trim(),
            SourceType = SourceType,
            EpgSourceType = EpgSourceType,
            EpgUrl = EpgUrl,
            M3uUrl = M3uUrl,
            XtreamServerUrl = XtreamServerUrl,
            XtreamUsername = XtreamUsername,
            XtreamPasswordEncrypted = protector.Protect(XtreamPassword),
            SftpHost = SftpHost,
            SftpPort = SftpPort,
            SftpUsername = SftpUsername,
            SftpPasswordEncrypted = protector.Protect(SftpPassword),
            SftpMoviesRemotePath = SftpMoviesRemotePath,
            SftpSeriesRemotePath = SftpSeriesRemotePath,
            SftpClipsRemotePath = SftpClipsRemotePath,
            JellyfinServerUrl = JellyfinServerUrl,
            JellyfinUsername = JellyfinUsername,
            JellyfinPasswordEncrypted = protector.Protect(JellyfinPassword),
            JellyfinApiKeyEncrypted = protector.Protect(JellyfinApiKey),
            JellyfinLibraryMappings = JellyfinLibraryMappings
        };
    }
}
