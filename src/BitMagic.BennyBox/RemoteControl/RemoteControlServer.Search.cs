using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BitMagic.BennyBox.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BitMagic.BennyBox.RemoteControl;

// Independent of LiveTvViewModel/SeriesViewModel/MoviesViewModel - same rationale as every other
// content-area route file (see the type-level comment on RemoteControlServer.cs).
public sealed partial class RemoteControlServer
{
    // A combined 3-type search implies a more targeted query than browsing one full list, so this
    // caps tighter than the 200-row MaxListResults used elsewhere - keeps the response (and the
    // phone screen) usable.
    private const int MaxSearchResultsPerType = 25;

    private void MapSearchRoutes(WebApplication app)
    {
        app.MapGet("/api/search", HandleSearch);
    }

    private async Task<IResult> HandleSearch(HttpRequest request, string? q)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var query = q?.Trim() ?? "";
        if (query.Length == 0)
        {
            // Searching implies you've typed something - an empty query skips the repository reads
            // entirely rather than dumping the whole combined catalog.
            return Results.Json(new SearchResponse([], false, [], false, [], false, [], false));
        }

        var favoriteChannelIds = await _favoriteRepository.GetFavoriteChannelIdsAsync();
        var favoriteSeriesIds = await _favoriteRepository.GetFavoriteSeriesIdsAsync();
        var favoriteMovieIds = await _favoriteRepository.GetFavoriteMovieIdsAsync();
        var favoriteClipIds = await _favoriteRepository.GetFavoriteClipIdsAsync();
        var watchedItems = await _watchedItemRepository.GetAllAsync();
        var watchedSeriesKeys = watchedItems
            .Where(w => w.ContentType == WatchProgressContentType.Series)
            .Select(w => (w.ProfileId, w.ContentKey))
            .ToHashSet();
        var watchedMovieKeys = watchedItems
            .Where(w => w.ContentType == WatchProgressContentType.Movie)
            .Select(w => (w.ProfileId, w.ContentKey))
            .ToHashSet();
        var watchedClipKeys = watchedItems
            .Where(w => w.ContentType == WatchProgressContentType.Clip)
            .Select(w => (w.ProfileId, w.ContentKey))
            .ToHashSet();
        var profiles = await _profileRepository.GetAllAsync();

        var channels = new List<LiveTvItemResponse>();
        var series = new List<SeriesItemResponse>();
        var movies = new List<MovieItemResponse>();
        var clips = new List<ClipItemResponse>();

        foreach (var profile in profiles)
        {
            var profileChannels = await _channelRepository.GetChannelsAsync(profile.Id);
            channels.AddRange(profileChannels
                .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(c => new LiveTvItemResponse(c.Id, c.Name, c.LogoUrl, "", null, favoriteChannelIds.Contains(c.Id))));

            var profileSeries = await _seriesRepository.GetSeriesAsync(profile.Id);
            series.AddRange(profileSeries
                .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(s => new SeriesItemResponse(
                    s.Id, s.Name, s.CoverUrl, "", favoriteSeriesIds.Contains(s.Id), watchedSeriesKeys.Contains((s.ProfileId, s.SourceSeriesId)))));

            var profileMovies = await _movieRepository.GetMoviesAsync(profile.Id);
            movies.AddRange(profileMovies
                .Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(m => new MovieItemResponse(
                    m.Id, m.Name, m.CoverUrl, "", favoriteMovieIds.Contains(m.Id), watchedMovieKeys.Contains((m.ProfileId, m.SourceMovieId)))));

            var profileClips = await _clipRepository.GetClipsAsync(profile.Id);
            clips.AddRange(profileClips
                .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(c => new ClipItemResponse(
                    c.Id, c.Name, c.CoverUrl, "", favoriteClipIds.Contains(c.Id), watchedClipKeys.Contains((c.ProfileId, c.SourceMovieId)))));
        }

        var (cappedChannels, channelsTruncated) = Cap(channels);
        var (cappedSeries, seriesTruncated) = Cap(series);
        var (cappedMovies, moviesTruncated) = Cap(movies);
        var (cappedClips, clipsTruncated) = Cap(clips);

        return Results.Json(new SearchResponse(
            cappedChannels, channelsTruncated, cappedSeries, seriesTruncated, cappedMovies, moviesTruncated, cappedClips, clipsTruncated));
    }

    private static (List<T> Items, bool Truncated) Cap<T>(List<T> items) =>
        items.Count > MaxSearchResultsPerType
            ? (items.Take(MaxSearchResultsPerType).ToList(), true)
            : (items, false);

    private sealed record SearchResponse(
        List<LiveTvItemResponse> Channels, bool ChannelsTruncated,
        List<SeriesItemResponse> Series, bool SeriesTruncated,
        List<MovieItemResponse> Movies, bool MoviesTruncated,
        List<ClipItemResponse> Clips, bool ClipsTruncated);
}
