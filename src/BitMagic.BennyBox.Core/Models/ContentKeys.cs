namespace BitMagic.BennyBox.Core.Models;

// Stable, provider-assigned content keys shared by everything that needs to identify a specific
// movie/episode without relying on the app's own Guid Ids (which get regenerated on every profile
// refresh - see WatchProgress/Reminder for why that matters). Movies key directly off
// Movie.SourceMovieId; episodes need this since Episode itself isn't persisted (see Episode.cs) and
// has no Guid of its own, only a SourceEpisodeId that's only unique within its own series.
public static class ContentKeys
{
    public static string ForEpisode(string sourceSeriesId, string sourceEpisodeId) => $"{sourceSeriesId}:{sourceEpisodeId}";
}
