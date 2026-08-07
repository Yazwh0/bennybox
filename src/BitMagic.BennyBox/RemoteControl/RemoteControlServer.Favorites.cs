using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BitMagic.BennyBox.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BitMagic.BennyBox.RemoteControl;

public sealed partial class RemoteControlServer
{
    private void MapFavoritesRoutes(WebApplication app)
    {
        app.MapGet("/api/favorites", HandleFavoritesList);
        app.MapPost("/api/favorites/resume", HandleFavoritesResume);
        app.MapPost("/api/favorites/remove-progress", HandleFavoritesRemoveProgress);
        app.MapPost("/api/favorite", HandleFavoriteToggle);
    }

    // Mirrors FavoritesViewModel.LoadAsync's shape (same sections, same repos) - independent of that
    // ViewModel's own Rows, same reasoning as every other content-area route here.
    private async Task<IResult> HandleFavoritesList(HttpRequest request)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var favoriteChannelIds = await _favoriteRepository.GetFavoriteChannelIdsAsync();
        var favoriteSeriesIds = await _favoriteRepository.GetFavoriteSeriesIdsAsync();
        var favoriteMovieIds = await _favoriteRepository.GetFavoriteMovieIdsAsync();
        var recentProgress = await _watchProgressRepository.GetRecentAsync();
        var profiles = await _profileRepository.GetAllAsync();

        var channelItems = new List<LiveTvItemResponse>();
        var seriesItems = new List<SeriesItemResponse>();
        var movieItems = new List<MovieItemResponse>();

        foreach (var profile in profiles)
        {
            var channels = await _channelRepository.GetChannelsAsync(profile.Id);
            var favoriteChannels = channels.Where(c => favoriteChannelIds.Contains(c.Id)).ToList();
            if (favoriteChannels.Count > 0)
            {
                var nowNext = await _epgRepository.GetNowNextAsync(profile.Id, DateTime.UtcNow);
                foreach (var channel in favoriteChannels)
                {
                    var nowTitle = !string.IsNullOrEmpty(channel.TvgId) && nowNext.TryGetValue(channel.TvgId, out var entry) ? entry.Now?.Title : null;
                    channelItems.Add(new LiveTvItemResponse(channel.Id, channel.Name, channel.LogoUrl, "", nowTitle, true));
                }
            }

            var series = await _seriesRepository.GetSeriesAsync(profile.Id);
            seriesItems.AddRange(series
                .Where(s => favoriteSeriesIds.Contains(s.Id))
                .Select(s => new SeriesItemResponse(s.Id, s.Name, s.CoverUrl, "", true, false)));

            var movies = await _movieRepository.GetMoviesAsync(profile.Id);
            movieItems.AddRange(movies
                .Where(m => favoriteMovieIds.Contains(m.Id))
                .Select(m => new MovieItemResponse(m.Id, m.Name, m.CoverUrl, "", true, false)));
        }

        var continueWatching = recentProgress.Select(p => new ContinueWatchingResponse(
            p.ProfileId, p.ContentType.ToString(), p.ContentKey, p.Title, p.CoverUrl,
            p.PositionSeconds, p.DurationSeconds,
            p.DurationSeconds > 0 ? p.PositionSeconds / p.DurationSeconds : 0)).ToList();

        return Results.Json(new FavoritesResponse(
            continueWatching,
            channelItems.OrderBy(c => c.Name).ToList(),
            seriesItems.OrderBy(s => s.Name).ToList(),
            movieItems.OrderBy(m => m.Name).ToList()));
    }

    private async Task<IResult> HandleFavoritesResume(HttpRequest request, ProgressKeyRequest? body)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        if (body is null || !Enum.TryParse<WatchProgressContentType>(body.ContentType, ignoreCase: true, out var contentType))
        {
            return Results.BadRequest();
        }

        var progress = await _watchProgressRepository.GetAsync(body.ProfileId, contentType, body.ContentKey);
        if (progress is null)
        {
            return Results.NotFound();
        }

        await RunOnUiThreadAsync(() => _player.ResumeFromProgress(progress));
        return Results.Ok();
    }

    private async Task<IResult> HandleFavoritesRemoveProgress(HttpRequest request, ProgressKeyRequest? body)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        if (body is null || !Enum.TryParse<WatchProgressContentType>(body.ContentType, ignoreCase: true, out var contentType))
        {
            return Results.BadRequest();
        }

        await _watchProgressRepository.RemoveAsync(body.ProfileId, contentType, body.ContentKey);
        return Results.Ok();
    }

    // One endpoint for all three content types, used from every tab that shows a favorite star (Live
    // TV/Guide/Series/Movies/Favorites alike) rather than three near-identical routes.
    private async Task<IResult> HandleFavoriteToggle(HttpRequest request, FavoriteToggleRequest? body)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        if (body is null)
        {
            return Results.BadRequest();
        }

        switch (body.Type)
        {
            case "channel":
                if (body.Favorite)
                {
                    var channel = await FindChannelAsync(body.Id);
                    if (channel is null)
                    {
                        return Results.NotFound();
                    }
                    await _favoriteRepository.AddAsync(channel.ProfileId, channel.Id);
                }
                else
                {
                    await _favoriteRepository.RemoveAsync(body.Id);
                }
                break;

            case "series":
                if (body.Favorite)
                {
                    var found = await FindSeriesAsync(body.Id);
                    if (found is not ({ } series, _))
                    {
                        return Results.NotFound();
                    }
                    await _favoriteRepository.AddSeriesAsync(series.ProfileId, series.Id);
                }
                else
                {
                    await _favoriteRepository.RemoveSeriesAsync(body.Id);
                }
                break;

            case "movie":
                if (body.Favorite)
                {
                    var found = await FindMovieAsync(body.Id);
                    if (found is not ({ } movie, _))
                    {
                        return Results.NotFound();
                    }
                    await _favoriteRepository.AddMovieAsync(movie.ProfileId, movie.Id);
                }
                else
                {
                    await _favoriteRepository.RemoveMovieAsync(body.Id);
                }
                break;

            default:
                return Results.BadRequest();
        }

        return Results.Ok();
    }

    private sealed record ProgressKeyRequest(Guid ProfileId, string ContentType, string ContentKey);
    private sealed record FavoriteToggleRequest(string Type, Guid Id, bool Favorite);
    private sealed record ContinueWatchingResponse(
        Guid ProfileId, string ContentType, string ContentKey, string Title, string? CoverUrl,
        double PositionSeconds, double DurationSeconds, double ProgressFraction);
    private sealed record FavoritesResponse(
        List<ContinueWatchingResponse> ContinueWatching,
        List<LiveTvItemResponse> Channels,
        List<SeriesItemResponse> Series,
        List<MovieItemResponse> Movies);
}
