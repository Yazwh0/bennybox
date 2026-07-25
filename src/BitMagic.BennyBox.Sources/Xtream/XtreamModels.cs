using System.Text.Json;
using System.Text.Json.Serialization;

namespace BitMagic.BennyBox.Sources.Xtream;

public sealed class XtreamUserInfo
{
    public string? Username { get; set; }
    public int Auth { get; set; }
    public string? Status { get; set; }
    public string? Message { get; set; }
    public string? ExpDate { get; set; }
    public string? MaxConnections { get; set; }
}

public sealed class XtreamServerInfo
{
    public string? Url { get; set; }
    public string? Port { get; set; }
    public string? HttpsPort { get; set; }
    public string? ServerProtocol { get; set; }
}

public sealed record XtreamAuthResult(XtreamUserInfo UserInfo, XtreamServerInfo? ServerInfo);

public sealed class XtreamAuthException(string message) : Exception(message);

internal sealed class XtreamAuthResponse
{
    public XtreamUserInfo? UserInfo { get; set; }
    public XtreamServerInfo? ServerInfo { get; set; }
}

internal sealed class XtreamCategory
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string CategoryId { get; set; } = "";
    public string CategoryName { get; set; } = "";
}

internal sealed class XtreamLiveStream
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string StreamId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? StreamIcon { get; set; }
    public string? EpgChannelId { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? CategoryId { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string Num { get; set; } = "0";
    // 1 if the panel keeps a rolling catch-up buffer for this channel, 0/absent otherwise.
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int TvArchive { get; set; }
    // How many days of catch-up the panel retains for this channel.
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int TvArchiveDuration { get; set; }
}

internal sealed class XtreamSeries
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string SeriesId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Cover { get; set; }
    public string? Plot { get; set; }
    public string? Genre { get; set; }
    // Xtream panels inconsistently use camelCase here instead of the snake_case every other field
    // uses - this is a documented quirk of the API, not a typo.
    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }
    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double? Rating { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? CategoryId { get; set; }
}

internal sealed class XtreamSeriesInfoResponse
{
    public XtreamSeriesInfoDetails? Info { get; set; }
    public Dictionary<string, List<XtreamSeriesEpisode>>? Episodes { get; set; }
}

internal sealed class XtreamSeriesInfoDetails
{
    public string? Name { get; set; }
    public string? Plot { get; set; }
}

internal sealed class XtreamSeriesEpisode
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string Id { get; set; } = "";
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int EpisodeNum { get; set; }
    public string Title { get; set; } = "";
    public string ContainerExtension { get; set; } = "mp4";
    public XtreamEpisodeInfo? Info { get; set; }
}

internal sealed class XtreamEpisodeInfo
{
    public string? Plot { get; set; }
}

// Unlike XtreamSeries, the bulk get_vod_streams listing does NOT include plot/genre/release date -
// only get_vod_info (per-title) does. Rating is the one detail field genuinely available in bulk.
internal sealed class XtreamVodStream
{
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string StreamId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? StreamIcon { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? CategoryId { get; set; }
    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double? Rating { get; set; }
    public string ContainerExtension { get; set; } = "mp4";
}

internal sealed class XtreamVodInfoResponse
{
    public XtreamVodInfoDetails? Info { get; set; }
}

internal sealed class XtreamVodInfoDetails
{
    public string? Plot { get; set; }
    public string? Genre { get; set; }
    // Unlike XtreamSeries.ReleaseDate, this field is genuinely all-lowercase with no separating
    // underscore/camelCase - the default snake_case-lower policy would otherwise look for
    // "release_date" and miss it entirely.
    [JsonPropertyName("releasedate")]
    public string? ReleaseDate { get; set; }
    public string? Duration { get; set; }
    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double? Rating { get; set; }
}

// Xtream Codes panels are notoriously inconsistent about whether IDs are JSON strings or numbers.
// This accepts either and normalizes to a string, since IDs are only ever used for lookup/matching, not arithmetic.
internal sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetInt64().ToString(),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for a string-or-number field.")
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}

// Same inconsistency as FlexibleStringConverter, but for a field that's genuinely used as a number
// (episode_num) rather than an opaque ID.
internal sealed class FlexibleIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt32(),
            JsonTokenType.String => int.TryParse(reader.GetString(), out var value) ? value : 0,
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for a string-or-number field.")
        };

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}

internal sealed class FlexibleDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String => double.TryParse(reader.GetString(), out var value) ? value : null,
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for a string-or-number field.")
        };

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(value.Value);
        }
    }
}
