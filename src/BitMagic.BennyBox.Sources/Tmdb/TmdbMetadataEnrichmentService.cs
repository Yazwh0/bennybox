using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Tmdb;

public class TmdbMetadataEnrichmentService : IMetadataEnrichmentService
{
    private const string SettingsKey = "TmdbApiKey";
    private const string BaseUrl = "https://api.themoviedb.org/3";
    private const string PosterBaseUrl = "https://image.tmdb.org/t/p/w500";

    private readonly HttpClient _httpClient;
    private readonly ISettingsStore _settingsStore;

    public TmdbMetadataEnrichmentService(HttpClient httpClient, ISettingsStore settingsStore)
    {
        _httpClient = httpClient;
        _settingsStore = settingsStore;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        await ResolveApiKeyAsync(cancellationToken) is not null;

    public bool HasEmbeddedFallback => EmbeddedTmdbApiKey.Get() is not null;

    public async Task<EnrichedMetadata?> SearchMovieAsync(string title, int? year, CancellationToken cancellationToken = default)
    {
        var apiKey = await ResolveApiKeyAsync(cancellationToken);
        if (apiKey is null)
        {
            return null;
        }

        var url = $"{BaseUrl}/search/movie?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(title)}"
            + (year is { } y ? $"&year={y}" : "");

        var match = await SearchAsync(url, cancellationToken);
        return match is null ? null : new EnrichedMetadata(
            match.Overview,
            MapGenres(match.GenreIds, MovieGenres),
            match.ReleaseDate,
            match.PosterPath is null ? null : PosterBaseUrl + match.PosterPath,
            match.VoteAverage);
    }

    public async Task<EnrichedMetadata?> SearchTvShowAsync(string title, CancellationToken cancellationToken = default)
    {
        var apiKey = await ResolveApiKeyAsync(cancellationToken);
        if (apiKey is null)
        {
            return null;
        }

        var url = $"{BaseUrl}/search/tv?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(title)}";

        var match = await SearchAsync(url, cancellationToken);
        return match is null ? null : new EnrichedMetadata(
            match.Overview,
            MapGenres(match.GenreIds, TvGenres),
            match.FirstAirDate,
            match.PosterPath is null ? null : PosterBaseUrl + match.PosterPath,
            match.VoteAverage);
    }

    // A key the user typed into Settings always wins (lets anyone override the app's bundled key with
    // their own, e.g. if the shared one ever gets rate-limited or revoked) - EmbeddedTmdbApiKey is only
    // consulted when Settings has nothing, and is itself only non-null on an official release build
    // (see its own doc comment).
    private async Task<string?> ResolveApiKeyAsync(CancellationToken cancellationToken)
    {
        var userKey = await _settingsStore.GetAsync(SettingsKey, cancellationToken);
        return !string.IsNullOrWhiteSpace(userKey) ? userKey : EmbeddedTmdbApiKey.Get();
    }

    // A null return here means two different things to the caller, and they must NOT be conflated:
    // an HTTP failure (bad API key, rate limited, network blip) is transient and should be retried
    // later, so it's thrown, not returned - CachingMetadataEnrichmentService only writes to the cache
    // after a call here actually succeeds, so a thrown exception never gets persisted as a confirmed
    // "TMDb has nothing for this title" the way a clean empty result set does.
    private async Task<TmdbResult?> SearchAsync(string url, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"TMDb request failed with {(int)response.StatusCode} {response.StatusCode}");
        }

        var parsed = await response.Content.ReadFromJsonAsync<TmdbSearchResponse>(cancellationToken: cancellationToken);
        return parsed?.Results?.FirstOrDefault();
    }

    private static string? MapGenres(List<int>? genreIds, IReadOnlyDictionary<int, string> genreMap)
    {
        if (genreIds is null || genreIds.Count == 0)
        {
            return null;
        }

        var names = genreIds.Select(id => genreMap.GetValueOrDefault(id)).Where(n => n is not null).ToList();
        return names.Count > 0 ? string.Join(", ", names) : null;
    }

    // TMDb's genre lists are a small, stable, well-known set that essentially never changes - hardcoded
    // here rather than fetched from /genre/movie/list and /genre/tv/list to avoid a second API call
    // (and its own cache) just to resolve genre_ids to names.
    private static readonly Dictionary<int, string> MovieGenres = new()
    {
        [28] = "Action", [12] = "Adventure", [16] = "Animation", [35] = "Comedy", [80] = "Crime",
        [99] = "Documentary", [18] = "Drama", [10751] = "Family", [14] = "Fantasy", [36] = "History",
        [27] = "Horror", [10402] = "Music", [9648] = "Mystery", [10749] = "Romance",
        [878] = "Science Fiction", [10770] = "TV Movie", [53] = "Thriller", [10752] = "War", [37] = "Western"
    };

    private static readonly Dictionary<int, string> TvGenres = new()
    {
        [10759] = "Action & Adventure", [16] = "Animation", [35] = "Comedy", [80] = "Crime",
        [99] = "Documentary", [18] = "Drama", [10751] = "Family", [10762] = "Kids", [9648] = "Mystery",
        [10763] = "News", [10764] = "Reality", [10765] = "Sci-Fi & Fantasy", [10766] = "Soap",
        [10767] = "Talk", [10768] = "War & Politics", [37] = "Western"
    };

    private sealed class TmdbSearchResponse
    {
        [JsonPropertyName("results")]
        public List<TmdbResult>? Results { get; set; }
    }

    private sealed class TmdbResult
    {
        [JsonPropertyName("overview")]
        public string? Overview { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("first_air_date")]
        public string? FirstAirDate { get; set; }

        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }

        [JsonPropertyName("genre_ids")]
        public List<int>? GenreIds { get; set; }

        [JsonPropertyName("vote_average")]
        public double? VoteAverage { get; set; }
    }
}
