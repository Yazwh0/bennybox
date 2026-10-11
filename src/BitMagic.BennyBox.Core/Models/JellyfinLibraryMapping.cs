using System.Text.Json;

namespace BitMagic.BennyBox.Core.Models;

// Which Benny Box section a Jellyfin library is imported into. Chosen per library in the profile
// dialog, since Jellyfin's own library type doesn't always match - a "mixed content" library of sport
// or one-off specials reports every file as a Movie, for instance, but belongs under Clips.
public enum JellyfinLibraryTarget
{
    Skip,
    Movies,
    Series,
    Clips
}

// Name is stored alongside the Id so the edit dialog can list the mappings without a server round trip.
public sealed record JellyfinLibraryMapping(string Id, string Name, JellyfinLibraryTarget Target)
{
    public static IReadOnlyList<JellyfinLibraryMapping> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<JellyfinLibraryMapping>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string? Serialize(IReadOnlyCollection<JellyfinLibraryMapping> mappings) =>
        mappings.Count == 0 ? null : JsonSerializer.Serialize(mappings);

    // For a library with no saved choice - one created on the server after the profile was set up, or
    // any library on a profile whose dialog never listed them. Based on Jellyfin's CollectionType;
    // mixed-content (null) libraries default to Clips since Jellyfin labels every loose file in them a
    // Movie regardless of what it actually is. Box sets, music, playlists etc. aren't video libraries
    // this app can show (box sets just regroup movies already imported from their own library).
    public static JellyfinLibraryTarget DefaultTarget(string? collectionType) => collectionType?.ToLowerInvariant() switch
    {
        "movies" => JellyfinLibraryTarget.Movies,
        "tvshows" => JellyfinLibraryTarget.Series,
        null or "" or "homevideos" or "musicvideos" => JellyfinLibraryTarget.Clips,
        _ => JellyfinLibraryTarget.Skip
    };
}
