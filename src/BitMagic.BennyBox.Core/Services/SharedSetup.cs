using System.Text.Json.Serialization;

namespace BitMagic.BennyBox.Core.Services;

// App settings carried by a setup code (see ProfileSetupCode). Only device-independent ones: no
// downloads folder (a path on this device), window opacity (desktop only) or remote-control key.
// Every field is optional so a code only changes what it actually sets. Short JSON names keep the QR
// small; this is a wire format.
public sealed class SharedSettings
{
    [JsonPropertyName("th")] public string? Theme { get; set; }
    [JsonPropertyName("sk")] public int? SkipIntervalSeconds { get; set; }
    [JsonPropertyName("lt")] public int? LoadTimeoutSeconds { get; set; }
    [JsonPropertyName("sd")] public int? StallThresholdSeconds { get; set; }
    [JsonPropertyName("pr")] public int? PauseReconnectThresholdSeconds { get; set; }
    [JsonPropertyName("al")] public string? PreferredAudioLanguage { get; set; }
    [JsonPropertyName("sl")] public string? PreferredSubtitleLanguage { get; set; }
    [JsonPropertyName("cu")] public bool? CheckForUpdatesAutomatically { get; set; }
    [JsonPropertyName("sm")] public bool? SimpleMode { get; set; }
    [JsonPropertyName("pp")] public string? PriorityPrefixes { get; set; }
    // Favourite categories (CategoryFavourites), as its own JSON list.
    [JsonPropertyName("fc")] public string? FavouriteCategories { get; set; }
}

public enum FavoriteKind
{
    Channel = 0,
    Series = 1,
    Movie = 2,
    Clip = 3,
}

// A favourite in a device-independent form: which account it belongs to (ProfileSetupCode.AccountKey)
// and the provider's own id for the item (Channel.SourceChannelId, Series.SourceSeriesId,
// Movie.SourceMovieId).
public sealed record SharedFavorite(string AccountKey, FavoriteKind Kind, string SourceId);
