using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BitMagic.BennyBox.RemoteControl;

public sealed partial class RemoteControlServer
{
    private void MapSeriesRoutes(WebApplication app)
    {
        app.MapGet("/api/series", HandleSeriesList);
        app.MapGet("/api/series/{id:guid}", HandleSeriesDetail);
        app.MapPost("/api/series/{id:guid}/episodes/{sourceEpisodeId}/play", HandleEpisodePlay);
        app.MapPost("/api/series/{id:guid}/episodes/{sourceEpisodeId}/watched", HandleEpisodeWatched);
    }

    // Independent of SeriesViewModel - see the type-level comment on RemoteControlServer.cs.
    private async Task<IResult> HandleSeriesList(HttpRequest request, string? search, string? category)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var favoriteIds = await _favoriteRepository.GetFavoriteSeriesIdsAsync();
        var watchedKeys = (await _watchedItemRepository.GetAllAsync())
            .Where(w => w.ContentType == WatchProgressContentType.Series)
            .Select(w => (w.ProfileId, w.ContentKey))
            .ToHashSet();
        var profiles = await _profileRepository.GetAllAsync();

        var allCategories = new List<string>();
        var items = new List<SeriesItemResponse>();
        foreach (var profile in profiles)
        {
            var categories = await _seriesRepository.GetCategoriesAsync(profile.Id);
            var seriesList = await _seriesRepository.GetSeriesAsync(profile.Id);
            var categoryNamesById = categories.ToDictionary(c => c.Id, c => c.Name);

            allCategories.AddRange(categories.Select(c => c.Name));

            foreach (var series in seriesList)
            {
                var categoryName = series.CategoryId is not null && categoryNamesById.TryGetValue(series.CategoryId, out var name) ? name : "";
                items.Add(new SeriesItemResponse(
                    series.Id, series.Name, series.CoverUrl, categoryName,
                    favoriteIds.Contains(series.Id), watchedKeys.Contains((series.ProfileId, series.SourceSeriesId))));
            }
        }

        var (filtered, truncated) = FilterAndCap(items, i =>
            (string.IsNullOrEmpty(category) || i.Category == category) &&
            (string.IsNullOrEmpty(search) || i.Name.Contains(search, StringComparison.OrdinalIgnoreCase)));

        return Results.Json(new SeriesListResponse(allCategories.Distinct().OrderBy(c => c).ToList(), filtered, truncated));
    }

    private async Task<IResult> HandleSeriesDetail(HttpRequest request, Guid id)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var found = await FindSeriesAsync(id);
        if (found is not ({ } series, { } profile))
        {
            return Results.NotFound();
        }

        var episodes = await _seriesImportService.GetEpisodesAsync(profile, series);
        var watchedKeys = (await _watchedItemRepository.GetAllAsync())
            .Where(w => w.ContentType == WatchProgressContentType.Episode && w.ProfileId == profile.Id)
            .Select(w => w.ContentKey)
            .ToHashSet();
        var isFavorite = (await _favoriteRepository.GetFavoriteSeriesIdsAsync()).Contains(series.Id);

        var seasons = episodes
            .GroupBy(e => e.Season)
            .OrderBy(g => g.Key)
            .Select(g => new SeasonResponse(g.Key, g
                .OrderBy(e => e.EpisodeNumber)
                .Select(e => new EpisodeResponse(
                    e.SourceEpisodeId, e.Title, e.EpisodeNumber,
                    watchedKeys.Contains(ContentKeys.ForEpisode(series.SourceSeriesId, e.SourceEpisodeId))))
                .ToList()))
            .ToList();

        return Results.Json(new SeriesDetailResponse(
            series.Id, series.Name, series.CoverUrl, series.Plot, series.Genre, series.ReleaseDate, series.Rating, isFavorite, seasons));
    }

    private async Task<IResult> HandleEpisodePlay(HttpRequest request, Guid id, string sourceEpisodeId)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var found = await FindSeriesAsync(id);
        if (found is not ({ } series, { } profile))
        {
            return Results.NotFound();
        }

        var episode = (await _seriesImportService.GetEpisodesAsync(profile, series))
            .FirstOrDefault(e => e.SourceEpisodeId == sourceEpisodeId);
        if (episode is null)
        {
            return Results.NotFound();
        }

        await RunOnUiThreadAsync(() => _player.PlayEpisode(episode, series));
        return Results.Ok();
    }

    private async Task<IResult> HandleEpisodeWatched(HttpRequest request, Guid id, string sourceEpisodeId, WatchedRequest? body)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        if (body is null)
        {
            return Results.BadRequest();
        }

        var found = await FindSeriesAsync(id);
        if (found is not ({ } series, { } profile))
        {
            return Results.NotFound();
        }

        var contentKey = ContentKeys.ForEpisode(series.SourceSeriesId, sourceEpisodeId);
        if (body.Watched)
        {
            await _watchedItemRepository.MarkWatchedAsync(profile.Id, WatchProgressContentType.Episode, contentKey);
        }
        else
        {
            await _watchedItemRepository.MarkUnwatchedAsync(profile.Id, WatchProgressContentType.Episode, contentKey);
        }

        // A series counts as watched once every one of its episodes does - mirrors
        // SeriesViewModel.RecomputeSeriesWatchedStatusAsync, just re-fetching episodes fresh instead
        // of reading from an already-loaded collection, since this request has none.
        var episodes = await _seriesImportService.GetEpisodesAsync(profile, series);
        var watchedKeys = (await _watchedItemRepository.GetAllAsync())
            .Where(w => w.ContentType == WatchProgressContentType.Episode && w.ProfileId == profile.Id)
            .Select(w => w.ContentKey)
            .ToHashSet();
        var allWatched = episodes.Count > 0 && episodes.All(e => watchedKeys.Contains(ContentKeys.ForEpisode(series.SourceSeriesId, e.SourceEpisodeId)));
        if (allWatched)
        {
            await _watchedItemRepository.MarkWatchedAsync(series.ProfileId, WatchProgressContentType.Series, series.SourceSeriesId);
        }
        else
        {
            await _watchedItemRepository.MarkUnwatchedAsync(series.ProfileId, WatchProgressContentType.Series, series.SourceSeriesId);
        }

        return Results.Ok();
    }

    // Series are merged across every profile for browsing (same as SeriesViewModel), so finding one by
    // its own Id means checking each profile's series list in turn - and unlike channels, callers here
    // also need the owning ProfileSource itself (episode fetches need its credentials).
    private async Task<(Series Series, ProfileSource Profile)?> FindSeriesAsync(Guid id)
    {
        foreach (var profile in await _profileRepository.GetAllAsync())
        {
            var series = (await _seriesRepository.GetSeriesAsync(profile.Id)).FirstOrDefault(s => s.Id == id);
            if (series is not null)
            {
                return (series, profile);
            }
        }

        return null;
    }

    private sealed record WatchedRequest(bool Watched);
    private sealed record EpisodeResponse(string SourceEpisodeId, string Title, int EpisodeNumber, bool IsWatched);
    private sealed record SeasonResponse(int Season, List<EpisodeResponse> Episodes);
    private sealed record SeriesDetailResponse(
        Guid Id, string Name, string? CoverUrl, string? Plot, string? Genre, string? ReleaseDate, double? Rating,
        bool IsFavorite, List<SeasonResponse> Seasons);
    private sealed record SeriesItemResponse(Guid Id, string Name, string? CoverUrl, string Category, bool IsFavorite, bool IsWatched);
    private sealed record SeriesListResponse(List<string> Categories, List<SeriesItemResponse> Items, bool Truncated);
}
