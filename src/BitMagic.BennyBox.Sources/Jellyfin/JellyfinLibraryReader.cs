using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Sources.Jellyfin;

// Shared by the Movie/Series/Clip sources: each Jellyfin library ("user view") is imported into
// whichever section the profile assigned it to (JellyfinLibraryMapping - defaulting by Jellyfin's
// library type when unassigned), and becomes one category there, named after the library.
internal static class JellyfinLibraryReader
{
    // Series is the one section built from containers (shows) rather than playable files; the other two
    // take every playable video in the library, whatever Jellyfin labelled it - in a mixed-content
    // library everything is a "Movie", and a TV library assigned to Clips should list its episodes.
    private const string PlayableItemTypes = "Movie,Video,MusicVideo,Episode";

    public static async Task<(IReadOnlyList<Category> Categories, IReadOnlyList<(JellyfinItem Item, string CategoryId)> Items, JellyfinSession Session)> ReadAsync(
        JellyfinClient client, ProfileSource profile, JellyfinLibraryTarget target, CancellationToken cancellationToken)
    {
        var itemTypes = target == JellyfinLibraryTarget.Series ? "Series" : PlayableItemTypes;
        var mappings = JellyfinLibraryMapping.Parse(profile.JellyfinLibraryMappings).ToDictionary(m => m.Id, m => m.Target);
        var (views, session) = await client.GetAsync<JellyfinItemsResponse>(profile, "UserViews?userId={userId}", cancellationToken);

        var categories = new List<Category>();
        var items = new List<(JellyfinItem, string)>();
        foreach (var view in views.Items.Where(v => (mappings.TryGetValue(v.Id, out var t) ? t : JellyfinLibraryMapping.DefaultTarget(v.CollectionType)) == target))
        {
            (var libraryItems, session) = await client.GetAsync<JellyfinItemsResponse>(
                profile,
                $"Items?userId={{userId}}&parentId={view.Id}&recursive=true&includeItemTypes={itemTypes}" +
                "&fields=Overview,Genres&enableUserData=false&imageTypeLimit=1&enableImageTypes=Primary&sortBy=SortName",
                cancellationToken);

            if (libraryItems.Items.Count == 0)
            {
                continue;
            }

            categories.Add(new Category
            {
                Id = view.Id,
                ProfileId = profile.Id,
                Name = view.Name ?? "Library",
                SortOrder = categories.Count
            });
            items.AddRange(libraryItems.Items.Select(i => (i, view.Id)));
        }

        return (categories, items, session);
    }

    public static Movie ToMovie(JellyfinItem item, string categoryId, ProfileSource profile, JellyfinSession session) => new()
    {
        ProfileId = profile.Id,
        SourceMovieId = item.Id,
        CategoryId = categoryId,
        Name = item.Name ?? "Untitled",
        CoverUrl = JellyfinClient.BuildImageUrl(session, item.Id, item.PrimaryImageTag),
        StreamUrl = JellyfinClient.BuildStreamUrl(session, item.Id, item.Container),
        Rating = item.CommunityRating,
        Plot = item.Overview,
        Genre = item.Genres is { Count: > 0 } genres ? string.Join(", ", genres) : null,
        ReleaseDate = JellyfinClient.FormatReleaseDate(item),
        Duration = JellyfinClient.FormatDuration(item.RunTimeTicks)
    };
}
