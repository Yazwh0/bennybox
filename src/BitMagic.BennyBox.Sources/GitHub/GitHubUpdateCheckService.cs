using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using BitMagic.BennyBox.Core.Services;
using Microsoft.Extensions.Logging;

namespace BitMagic.BennyBox.Sources.GitHub;

// Compares the latest tag on github.com/Yazwh0/bennybox against the running app's own version (see
// .github/workflows/release.yml / installer/BennyBox.iss for the "git tag vX.Y.Z -> InformationalVersion
// X.Y.Z" convention this relies on). Reads the ENTRY assembly's version, not this assembly's - the
// calling code lives in the main app, but this service itself is built into BitMagic.BennyBox.Sources,
// so GetExecutingAssembly() here would be the wrong assembly.
public class GitHubUpdateCheckService : IUpdateCheckService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Yazwh0/bennybox/releases/latest";

    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubUpdateCheckService> _logger;

    public GitHubUpdateCheckService(HttpClient httpClient, ILogger<GitHubUpdateCheckService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string CurrentVersion => GetCurrentVersionRaw() ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(LatestReleaseUrl, cancellationToken);
            if (release?.TagName is not { } tagName || !Version.TryParse(tagName.TrimStart('v'), out var latestVersion))
            {
                return null;
            }

            var currentVersion = GetCurrentVersion();
            if (currentVersion is not null && latestVersion <= currentVersion)
            {
                return null;
            }

            var releaseUrl = release.HtmlUrl ?? $"https://github.com/Yazwh0/bennybox/releases/tag/{tagName}";
            return new UpdateInfo(tagName.TrimStart('v'), releaseUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Offline, GitHub rate-limited/down, malformed response - none of this is worth bothering
            // the user about, it's just "no update banner this launch, try again next time".
            _logger.LogDebug(ex, "Update check failed");
            return null;
        }
    }

    private static string? GetCurrentVersionRaw() =>
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

    // Null (rather than throwing/defaulting to 0.0.0.0) on a local dev build with no InformationalVersion
    // override - Version.TryParse fails on the SDK's implicit default informational version string
    // (which can carry a +commitsha suffix, hence stripping everything from '+' on before parsing), and
    // a null here means CheckForUpdateAsync never claims an update is available, which is the right
    // behavior for a dev build anyway.
    private static Version? GetCurrentVersion()
    {
        if (GetCurrentVersionRaw() is { } informational)
        {
            var plusIndex = informational.IndexOf('+');
            var core = plusIndex >= 0 ? informational[..plusIndex] : informational;
            if (Version.TryParse(core, out var parsed))
            {
                return parsed;
            }
        }

        return Assembly.GetEntryAssembly()?.GetName().Version;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}
