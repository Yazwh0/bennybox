using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

// Persists FolderMediaScanner's episode scan results (see FolderSeriesSource) so opening a show a
// second time - even after an app restart - doesn't repeat a full SFTP directory walk plus a fresh
// NFO read per episode. Scoped to LocalFolder/Sftp series only - Xtream/M3U's episode fetch is a
// single cheap API call and doesn't use this.
public interface IEpisodeCacheRepository
{
    // Null means "never cached" (go fetch live) - distinct from an empty list, which is itself a
    // valid (if unlikely) cached result for a show with no episode files.
    Task<IReadOnlyList<Episode>?> GetCachedEpisodesAsync(Guid profileId, string seriesId, CancellationToken cancellationToken = default);
    Task ReplaceCachedEpisodesAsync(Guid profileId, string seriesId, IReadOnlyList<Episode> episodes, CancellationToken cancellationToken = default);

    // Called on every rescan of a profile (see SettingsViewModel's per-profile Refresh) - a full wipe,
    // not just a prune of shows that disappeared. "Refresh" is a deliberate, user-initiated action
    // that means "go check the source now", so it should never keep serving old cached data for a
    // show that's still present but may have changed (new episodes, a fixed bug in how we parsed it,
    // etc.) - the cost is that the first show you open after a Refresh always pays a live fetch again,
    // which is the correct tradeoff for what Refresh is supposed to mean.
    Task ClearProfileAsync(Guid profileId, CancellationToken cancellationToken = default);
}
