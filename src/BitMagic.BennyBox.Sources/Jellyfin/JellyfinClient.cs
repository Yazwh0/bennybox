using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Jellyfin;

public sealed record JellyfinSession(string ServerUrl, string UserId, string AccessToken);

public class JellyfinClient
{
    private const string ClientName = "Benny Box";
    private const string ClientVersion = "1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IProfileRepository _profileRepository;
    private readonly ICredentialProtector _credentialProtector;

    public JellyfinClient(HttpClient httpClient, IProfileRepository profileRepository, ICredentialProtector credentialProtector)
    {
        _httpClient = httpClient;
        _profileRepository = profileRepository;
        _credentialProtector = credentialProtector;
    }

    // The device id is what Jellyfin keys a login session on - a new AuthenticateByName for the same
    // device id replaces (revokes) that device's previous token. Using the profile's own Id keeps one
    // stable session per profile, rather than piling up a new "device" on the server every refresh.
    public static string DeviceIdFor(Guid profileId) => profileId.ToString("N");

    public async Task<JellyfinPublicSystemInfo?> GetPublicSystemInfoAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<JellyfinPublicSystemInfo>($"{serverUrl.TrimEnd('/')}/System/Info/Public", JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return null;
        }
    }

    public async Task<JellyfinAuthResult> AuthenticateAsync(string serverUrl, string username, string password, string deviceId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{serverUrl.TrimEnd('/')}/Users/AuthenticateByName")
        {
            Content = JsonContent.Create(new { Username = username, Pw = password })
        };
        request.Headers.TryAddWithoutValidation("Authorization", BuildAuthorizationHeader(deviceId, token: null));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new JellyfinAuthException($"Could not reach server: {ex.Message}");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            {
                throw new JellyfinAuthException("Invalid username or password.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new JellyfinAuthException($"Server returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            JellyfinAuthResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<JellyfinAuthResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new JellyfinAuthException($"Server response was not valid: {ex.Message}");
            }

            if (body?.User is null || string.IsNullOrEmpty(body.User.Id) || string.IsNullOrEmpty(body.AccessToken))
            {
                throw new JellyfinAuthException("Server response was not a Jellyfin login result.");
            }

            return new JellyfinAuthResult(body.User.Id, body.AccessToken, body.User.Name);
        }
    }

    // API keys are server-wide rather than per-user, but browsing (libraries, parental limits, which
    // Live TV is visible) is per-user - so the key is used to list the server's users and pick the one
    // named. With no username given, a server with a single (visible, enabled) user just uses that one.
    public async Task<JellyfinUser> ResolveUserAsync(string serverUrl, string apiKey, string? username, string deviceId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{serverUrl.TrimEnd('/')}/Users");
        request.Headers.TryAddWithoutValidation("Authorization", BuildAuthorizationHeader(deviceId, apiKey));

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new JellyfinAuthException($"Could not reach server: {ex.Message}");
        }

        List<JellyfinUser>? users;
        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new JellyfinAuthException("The server rejected the API key.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new JellyfinAuthException($"Server returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            users = await response.Content.ReadFromJsonAsync<List<JellyfinUser>>(JsonOptions, cancellationToken);
        }

        var candidates = (users ?? []).Where(u => u.Policy?.IsDisabled != true).ToList();
        if (!string.IsNullOrWhiteSpace(username))
        {
            return candidates.FirstOrDefault(u => string.Equals(u.Name, username.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new JellyfinAuthException($"No user named '{username.Trim()}' on this server. Users: {string.Join(", ", candidates.Select(u => u.Name))}.");
        }

        var visible = candidates.Where(u => u.Policy?.IsHidden != true).ToList();
        return visible.Count == 1
            ? visible[0]
            : throw new JellyfinAuthException($"This server has more than one user - enter which one to use as the username ({string.Join(", ", candidates.Select(u => u.Name))}).");
    }

    // For the profile dialog, before there's a saved profile to hang a session on - the caller already
    // holds a token (from AuthenticateAsync, or an API key) and the user id to list libraries for.
    public async Task<IReadOnlyList<JellyfinItem>> GetLibrariesAsync(string serverUrl, string userId, string token, string deviceId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{serverUrl.TrimEnd('/')}/UserViews?userId={Uri.EscapeDataString(userId)}");
        request.Headers.TryAddWithoutValidation("Authorization", BuildAuthorizationHeader(deviceId, token));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JellyfinItemsResponse>(JsonOptions, cancellationToken);
        return result?.Items ?? [];
    }

    // Reuses the stored token when there is one; only logs in (and persists the new token/user id
    // back onto the profile) when there isn't, or when forceReauthenticate is set after a 401.
    public async Task<JellyfinSession> GetSessionAsync(ProfileSource profile, bool forceReauthenticate = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profile.JellyfinServerUrl))
        {
            throw new InvalidOperationException("Profile has no Jellyfin server configured.");
        }

        var serverUrl = profile.JellyfinServerUrl.TrimEnd('/');

        // An API key never expires on its own and can't be renewed by logging in again - a 401 here
        // means it was deleted on the server, so the only fix is a new key.
        var apiKey = _credentialProtector.Unprotect(profile.JellyfinApiKeyEncrypted);
        if (!string.IsNullOrEmpty(apiKey))
        {
            if (forceReauthenticate)
            {
                throw new JellyfinAuthException("The server rejected the API key - create a new one under Dashboard → API Keys.");
            }

            if (string.IsNullOrEmpty(profile.JellyfinUserId))
            {
                var user = await ResolveUserAsync(serverUrl, apiKey, profile.JellyfinUsername, DeviceIdFor(profile.Id), cancellationToken);
                profile.JellyfinUserId = user.Id;
                await _profileRepository.UpdateAsync(profile, cancellationToken);
            }

            return new JellyfinSession(serverUrl, profile.JellyfinUserId, apiKey);
        }

        var token = _credentialProtector.Unprotect(profile.JellyfinAccessTokenEncrypted);
        if (!forceReauthenticate && !string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(profile.JellyfinUserId))
        {
            return new JellyfinSession(serverUrl, profile.JellyfinUserId, token);
        }

        if (string.IsNullOrWhiteSpace(profile.JellyfinUsername))
        {
            throw new InvalidOperationException("Profile has no Jellyfin username or API key configured.");
        }

        var password = _credentialProtector.Unprotect(profile.JellyfinPasswordEncrypted) ?? string.Empty;
        var result = await AuthenticateAsync(serverUrl, profile.JellyfinUsername, password, DeviceIdFor(profile.Id), cancellationToken);

        profile.JellyfinUserId = result.UserId;
        profile.JellyfinAccessTokenEncrypted = _credentialProtector.Protect(result.AccessToken);
        await _profileRepository.UpdateAsync(profile, cancellationToken);

        return new JellyfinSession(serverUrl, result.UserId, result.AccessToken);
    }

    // GETs an authenticated API path (relative to the server root, with {userId} substituted), logging
    // in again once if the stored token has been revoked (e.g. the session was signed out from the
    // Jellyfin dashboard). Returns the session actually used, so callers build stream URLs with the
    // token that's valid right now rather than the one that just got rejected.
    internal async Task<(T Result, JellyfinSession Session)> GetAsync<T>(ProfileSource profile, string pathAndQuery, CancellationToken cancellationToken = default)
        where T : new()
    {
        var session = await GetSessionAsync(profile, cancellationToken: cancellationToken);
        var response = await SendGetAsync(session, profile, pathAndQuery, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            session = await GetSessionAsync(profile, forceReauthenticate: true, cancellationToken);
            response = await SendGetAsync(session, profile, pathAndQuery, cancellationToken);
        }

        using (response)
        {
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return (result ?? new T(), session);
        }
    }

    private async Task<HttpResponseMessage> SendGetAsync(JellyfinSession session, ProfileSource profile, string pathAndQuery, CancellationToken cancellationToken)
    {
        var url = $"{session.ServerUrl}/{pathAndQuery.Replace("{userId}", Uri.EscapeDataString(session.UserId)).TrimStart('/')}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", BuildAuthorizationHeader(DeviceIdFor(profile.Id), session.AccessToken));
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static string BuildAuthorizationHeader(string deviceId, string? token)
    {
        var header = $"MediaBrowser Client=\"{ClientName}\", Device=\"{Environment.MachineName}\", DeviceId=\"{deviceId}\", Version=\"{ClientVersion}\"";
        return token is null ? header : $"{header}, Token=\"{token}\"";
    }

    // static=true serves the original file untouched (no server-side transcode), with Range support -
    // what both the player and DownloadManager want. The container extension is part of the path so
    // DownloadManager can name the downloaded file from it the same way it does for Xtream URLs.
    public static string BuildStreamUrl(JellyfinSession session, string itemId, string? container)
    {
        var extension = container?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(extension))
        {
            extension = "mkv";
        }

        return $"{session.ServerUrl}/Videos/{itemId}/stream.{extension}?static=true&api_key={Uri.EscapeDataString(session.AccessToken)}";
    }

    public static string BuildLiveStreamUrl(JellyfinSession session, string channelId) =>
        $"{session.ServerUrl}/Videos/{channelId}/stream?static=true&api_key={Uri.EscapeDataString(session.AccessToken)}";

    // Image endpoints don't require auth; the tag makes the URL change whenever the artwork does, so
    // ChannelLogoCache/Avalonia's own caching never serves a stale image.
    public static string? BuildImageUrl(JellyfinSession session, string itemId, string? imageTag, int maxHeight = 600) =>
        imageTag is null ? null : $"{session.ServerUrl}/Items/{itemId}/Images/Primary?maxHeight={maxHeight}&tag={Uri.EscapeDataString(imageTag)}";

    public static string? FormatDuration(long? runTimeTicks) =>
        runTimeTicks is > 0 ? TimeSpan.FromTicks(runTimeTicks.Value).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : null;

    public static string? FormatReleaseDate(JellyfinItem item) =>
        item.PremiereDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? item.ProductionYear?.ToString(CultureInfo.InvariantCulture);
}
