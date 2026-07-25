using System.Net.Http.Json;
using System.Text.Json;

namespace Iptv.Sources.Xtream;

public class XtreamClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public XtreamClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<XtreamAuthResult> AuthenticateAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var url = BuildApiUrl(serverUrl, username, password, action: null);

        XtreamAuthResponse? response;
        try
        {
            response = await _httpClient.GetFromJsonAsync<XtreamAuthResponse>(url, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Real-world panels are inconsistent here: some return 200 with auth:0 for bad
            // credentials (handled below), others return a bare 401/403/404 - StatusCode being set
            // means the server was reached and responded, it just rejected the request/credentials.
            throw ex.StatusCode is not null
                ? new XtreamAuthException("Invalid username or password, or the server rejected the request.")
                : new XtreamAuthException($"Could not reach server: {ex.Message}");
        }
        catch (JsonException ex)
        {
            throw new XtreamAuthException($"Server response was not valid: {ex.Message}");
        }

        if (response?.UserInfo is null || response.UserInfo.Auth != 1)
        {
            var reason = response?.UserInfo?.Message is { Length: > 0 } message ? message : response?.UserInfo?.Status ?? "Authentication failed";
            throw new XtreamAuthException(reason);
        }

        return new XtreamAuthResult(response.UserInfo, response.ServerInfo);
    }

    internal async Task<IReadOnlyList<XtreamCategory>> GetLiveCategoriesAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var url = BuildApiUrl(serverUrl, username, password, "get_live_categories");
        var result = await _httpClient.GetFromJsonAsync<List<XtreamCategory>>(url, JsonOptions, cancellationToken);
        return result ?? [];
    }

    internal async Task<IReadOnlyList<XtreamLiveStream>> GetLiveStreamsAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var url = BuildApiUrl(serverUrl, username, password, "get_live_streams");
        var result = await _httpClient.GetFromJsonAsync<List<XtreamLiveStream>>(url, JsonOptions, cancellationToken);
        return result ?? [];
    }

    internal async Task<IReadOnlyList<XtreamCategory>> GetSeriesCategoriesAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var url = BuildApiUrl(serverUrl, username, password, "get_series_categories");
        var result = await _httpClient.GetFromJsonAsync<List<XtreamCategory>>(url, JsonOptions, cancellationToken);
        return result ?? [];
    }

    internal async Task<IReadOnlyList<XtreamSeries>> GetSeriesAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var url = BuildApiUrl(serverUrl, username, password, "get_series");
        var result = await _httpClient.GetFromJsonAsync<List<XtreamSeries>>(url, JsonOptions, cancellationToken);
        return result ?? [];
    }

    internal async Task<XtreamSeriesInfoResponse> GetSeriesInfoAsync(string serverUrl, string username, string password, string seriesId, CancellationToken cancellationToken = default)
    {
        var url = $"{BuildApiUrl(serverUrl, username, password, "get_series_info")}&series_id={Uri.EscapeDataString(seriesId)}";
        var result = await _httpClient.GetFromJsonAsync<XtreamSeriesInfoResponse>(url, JsonOptions, cancellationToken);
        return result ?? new XtreamSeriesInfoResponse();
    }

    internal async Task<IReadOnlyList<XtreamCategory>> GetVodCategoriesAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var url = BuildApiUrl(serverUrl, username, password, "get_vod_categories");
        var result = await _httpClient.GetFromJsonAsync<List<XtreamCategory>>(url, JsonOptions, cancellationToken);
        return result ?? [];
    }

    internal async Task<IReadOnlyList<XtreamVodStream>> GetVodStreamsAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var url = BuildApiUrl(serverUrl, username, password, "get_vod_streams");
        var result = await _httpClient.GetFromJsonAsync<List<XtreamVodStream>>(url, JsonOptions, cancellationToken);
        return result ?? [];
    }

    internal async Task<XtreamVodInfoResponse> GetVodInfoAsync(string serverUrl, string username, string password, string vodId, CancellationToken cancellationToken = default)
    {
        var url = $"{BuildApiUrl(serverUrl, username, password, "get_vod_info")}&vod_id={Uri.EscapeDataString(vodId)}";
        var result = await _httpClient.GetFromJsonAsync<XtreamVodInfoResponse>(url, JsonOptions, cancellationToken);
        return result ?? new XtreamVodInfoResponse();
    }

    public static string BuildStreamUrl(string serverUrl, string username, string password, string streamId, string extension = "m3u8") =>
        $"{serverUrl.TrimEnd('/')}/live/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}/{streamId}.{extension}";

    public static string BuildSeriesEpisodeUrl(string serverUrl, string username, string password, string episodeId, string extension) =>
        $"{serverUrl.TrimEnd('/')}/series/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}/{episodeId}.{extension}";

    public static string BuildVodStreamUrl(string serverUrl, string username, string password, string streamId, string extension) =>
        $"{serverUrl.TrimEnd('/')}/movie/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}/{streamId}.{extension}";

    // Verified against a real panel: {server}/timeshift/{user}/{pass}/{duration-minutes}/{start:yyyy-MM-dd:HH-mm}/{id}.{ext}
    // 302s to a tokenized CDN URL serving the requested slice starting at startUtc. Xtream's APIs are
    // UTC throughout (EPG times included), so startUtc is used as-is with no local conversion.
    public static string BuildTimeshiftUrl(string serverUrl, string username, string password, string streamId, DateTime startUtc, TimeSpan duration, string extension = "ts")
    {
        var durationMinutes = Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes));
        var start = startUtc.ToString("yyyy-MM-dd:HH-mm");
        return $"{serverUrl.TrimEnd('/')}/timeshift/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(password)}/{durationMinutes}/{start}/{streamId}.{extension}";
    }

    private static string BuildApiUrl(string serverUrl, string username, string password, string? action)
    {
        var baseUrl = $"{serverUrl.TrimEnd('/')}/player_api.php?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}";
        return action is null ? baseUrl : $"{baseUrl}&action={action}";
    }
}
