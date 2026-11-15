using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using Microsoft.Extensions.Logging;

namespace BitMagic.BennyBox.UI.Services;

// Everything a source provides, fetched again: channels and the guide, then shows, films and videos.
// Shared by Settings' Refresh button and AutoRefresh, so a manual refresh and a background one do
// exactly the same thing. One refresh at a time (a manual one waits for a background one to finish
// and vice versa), since two imports of the same source would fight over the same tables.
public sealed class ProfileContentImporter(
    PlaylistImportService playlistImportService,
    EpgImportService epgImportService,
    SeriesImportService seriesImportService,
    MovieImportService movieImportService,
    ClipImportService clipImportService,
    ILogger<ProfileContentImporter> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Returns a short summary for a status line ("1234 channels in 56 categories, 300 series, ...").
    public async Task<string> ImportAllAsync(ProfileSource profile)
    {
        await _gate.WaitAsync();
        try
        {
            var messages = new List<string>();

            try
            {
                var result = await playlistImportService.ImportAsync(profile);
                messages.Add(result.NotModified
                    ? "channels up to date"
                    : $"{result.Channels.Count} channels in {result.Categories.Count} categories");

                if (profile.EpgSourceType != EpgSourceType.None)
                {
                    await epgImportService.ImportAsync(profile);
                    if (!result.NotModified)
                    {
                        messages.Add("EPG updated");
                    }
                }
            }
            catch (Exception ex)
            {
                messages.Add($"channels/EPG failed: {ex.Message}");
                logger.LogError(ex, "Failed to import channels/EPG for profile {ProfileName}", profile.Name);
            }

            await Try(messages, "series", profile, async () => (await seriesImportService.ImportAsync(profile))?.SeriesList.Count);
            await Try(messages, "movies", profile, async () => (await movieImportService.ImportAsync(profile))?.Movies.Count);
            await Try(messages, "clips", profile, async () => (await clipImportService.ImportAsync(profile))?.Clips.Count);

            return string.Join(", ", messages);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Just the guide - it runs out after a few days, far sooner than channel lists change.
    public async Task ImportGuideAsync(ProfileSource profile)
    {
        if (profile.EpgSourceType == EpgSourceType.None)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            await epgImportService.ImportAsync(profile);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task Try(List<string> messages, string what, ProfileSource profile, Func<Task<int?>> import)
    {
        try
        {
            if (await import() is { } count)
            {
                messages.Add($"{count} {what}");
            }
        }
        catch (Exception ex)
        {
            messages.Add($"{what} failed: {ex.Message}");
            logger.LogError(ex, "Failed to import {What} for profile {ProfileName}", what, profile.Name);
        }
    }
}
