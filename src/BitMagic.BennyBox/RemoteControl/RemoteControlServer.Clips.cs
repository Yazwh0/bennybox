using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BitMagic.BennyBox.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BitMagic.BennyBox.RemoteControl;

// Mirrors RemoteControlServer.Movies.cs almost exactly - Clips reuse the Movie model/repository
// shape (see IClipRepository's own comment), just via _clipRepository instead of _movieRepository.
// One real difference: no on-demand plot/genre fetch on open - ClipsViewModel never does that
// either, since clips deliberately skip TMDb metadata matching (see README) and only ever show
// whatever an NFO sidecar already provided at scan time.
public sealed partial class RemoteControlServer
{
    private void MapClipsRoutes(WebApplication app)
    {
        app.MapGet("/api/clips", HandleClipsList);
        app.MapGet("/api/clips/{id:guid}", HandleClipDetail);
        app.MapPost("/api/clips/{id:guid}/play", HandleClipPlay);
        app.MapPost("/api/clips/{id:guid}/watched", HandleClipWatched);
    }

    private async Task<IResult> HandleClipsList(HttpRequest request, string? search, string? category)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var favoriteIds = await _favoriteRepository.GetFavoriteClipIdsAsync();
        var watchedKeys = (await _watchedItemRepository.GetAllAsync())
            .Where(w => w.ContentType == WatchProgressContentType.Clip)
            .Select(w => (w.ProfileId, w.ContentKey))
            .ToHashSet();
        var profiles = await _profileRepository.GetAllAsync();

        var allCategories = new List<string>();
        var items = new List<ClipItemResponse>();
        foreach (var profile in profiles)
        {
            var categories = await _clipRepository.GetCategoriesAsync(profile.Id);
            var clips = await _clipRepository.GetClipsAsync(profile.Id);
            var categoryNamesById = categories.ToDictionary(c => c.Id, c => c.Name);

            allCategories.AddRange(categories.Select(c => c.Name));

            foreach (var clip in clips)
            {
                var categoryName = clip.CategoryId is not null && categoryNamesById.TryGetValue(clip.CategoryId, out var name) ? name : "";
                items.Add(new ClipItemResponse(
                    clip.Id, clip.Name, clip.CoverUrl, categoryName,
                    favoriteIds.Contains(clip.Id), watchedKeys.Contains((clip.ProfileId, clip.SourceMovieId))));
            }
        }

        var (filtered, truncated) = FilterAndCap(items, i =>
            (string.IsNullOrEmpty(category) || i.Category == category) &&
            (string.IsNullOrEmpty(search) || i.Name.Contains(search, StringComparison.OrdinalIgnoreCase)));

        return Results.Json(new ClipListResponse(allCategories.Distinct().OrderBy(c => c).ToList(), filtered, truncated));
    }

    private async Task<IResult> HandleClipDetail(HttpRequest request, Guid id)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var clip = await FindClipAsync(id);
        if (clip is null)
        {
            return Results.NotFound();
        }

        var isFavorite = (await _favoriteRepository.GetFavoriteClipIdsAsync()).Contains(clip.Id);
        var isWatched = (await _watchedItemRepository.GetAllAsync())
            .Any(w => w.ContentType == WatchProgressContentType.Clip && w.ProfileId == clip.ProfileId && w.ContentKey == clip.SourceMovieId);

        return Results.Json(new ClipDetailResponse(
            clip.Id, clip.Name, clip.CoverUrl, clip.Plot, clip.Genre, clip.ReleaseDate, isFavorite, isWatched));
    }

    private async Task<IResult> HandleClipPlay(HttpRequest request, Guid id)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var clip = await FindClipAsync(id);
        if (clip is null)
        {
            return Results.NotFound();
        }

        await RunOnUiThreadAsync(() => _player.PlayClip(clip));
        return Results.Ok();
    }

    private async Task<IResult> HandleClipWatched(HttpRequest request, Guid id, WatchedRequest? body)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        if (body is null)
        {
            return Results.BadRequest();
        }

        var clip = await FindClipAsync(id);
        if (clip is null)
        {
            return Results.NotFound();
        }

        if (body.Watched)
        {
            await _watchedItemRepository.MarkWatchedAsync(clip.ProfileId, WatchProgressContentType.Clip, clip.SourceMovieId);
        }
        else
        {
            await _watchedItemRepository.MarkUnwatchedAsync(clip.ProfileId, WatchProgressContentType.Clip, clip.SourceMovieId);
        }

        return Results.Ok();
    }

    // Clips are merged across every profile for browsing (same as ClipsViewModel), so finding one by
    // its own Id means checking each profile's clip list in turn.
    private async Task<Movie?> FindClipAsync(Guid id)
    {
        foreach (var profile in await _profileRepository.GetAllAsync())
        {
            var clip = (await _clipRepository.GetClipsAsync(profile.Id)).FirstOrDefault(c => c.Id == id);
            if (clip is not null)
            {
                return clip;
            }
        }

        return null;
    }

    private sealed record ClipDetailResponse(
        Guid Id, string Name, string? CoverUrl, string? Plot, string? Genre, string? ReleaseDate, bool IsFavorite, bool IsWatched);
    private sealed record ClipItemResponse(Guid Id, string Name, string? CoverUrl, string Category, bool IsFavorite, bool IsWatched);
    private sealed record ClipListResponse(List<string> Categories, List<ClipItemResponse> Items, bool Truncated);
}
