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
    private void MapMoviesRoutes(WebApplication app)
    {
        app.MapGet("/api/movies", HandleMoviesList);
        app.MapGet("/api/movies/{id:guid}", HandleMovieDetail);
        app.MapPost("/api/movies/{id:guid}/play", HandleMoviePlay);
        app.MapPost("/api/movies/{id:guid}/watched", HandleMovieWatched);
    }

    // Independent of MoviesViewModel - see the type-level comment on RemoteControlServer.cs.
    private async Task<IResult> HandleMoviesList(HttpRequest request, string? search, string? category)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var favoriteIds = await _favoriteRepository.GetFavoriteMovieIdsAsync();
        var watchedKeys = (await _watchedItemRepository.GetAllAsync())
            .Where(w => w.ContentType == WatchProgressContentType.Movie)
            .Select(w => (w.ProfileId, w.ContentKey))
            .ToHashSet();
        var profiles = await _profileRepository.GetAllAsync();

        var allCategories = new List<string>();
        var items = new List<MovieItemResponse>();
        foreach (var profile in profiles)
        {
            var categories = await _movieRepository.GetCategoriesAsync(profile.Id);
            var movies = await _movieRepository.GetMoviesAsync(profile.Id);
            var categoryNamesById = categories.ToDictionary(c => c.Id, c => c.Name);

            allCategories.AddRange(categories.Select(c => c.Name));

            foreach (var movie in movies)
            {
                var categoryName = movie.CategoryId is not null && categoryNamesById.TryGetValue(movie.CategoryId, out var name) ? name : "";
                items.Add(new MovieItemResponse(
                    movie.Id, movie.Name, movie.CoverUrl, categoryName,
                    favoriteIds.Contains(movie.Id), watchedKeys.Contains((movie.ProfileId, movie.SourceMovieId))));
            }
        }

        var (filtered, truncated) = FilterAndCap(items, i =>
            (string.IsNullOrEmpty(category) || i.Category == category) &&
            (string.IsNullOrEmpty(search) || i.Name.Contains(search, StringComparison.OrdinalIgnoreCase)));

        return Results.Json(new MovieListResponse(allCategories.Distinct().OrderBy(c => c).ToList(), filtered, truncated));
    }

    private async Task<IResult> HandleMovieDetail(HttpRequest request, Guid id)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var found = await FindMovieAsync(id);
        if (found is not ({ } movie, { } profile))
        {
            return Results.NotFound();
        }

        // Bulk import doesn't include plot/genre (Xtream only returns those per-title) - fetched on
        // demand here, same as MoviesViewModel.SelectMovieAsync does the first time a movie's opened.
        if (string.IsNullOrEmpty(movie.Plot))
        {
            var details = await _movieImportService.GetDetailsAsync(profile, movie);
            if (details is not null)
            {
                movie.Plot = details.Plot;
                movie.Genre = details.Genre;
                movie.ReleaseDate = details.ReleaseDate;
                movie.Duration = details.Duration;
                movie.Rating = details.Rating ?? movie.Rating;
            }
        }

        var isFavorite = (await _favoriteRepository.GetFavoriteMovieIdsAsync()).Contains(movie.Id);
        var isWatched = (await _watchedItemRepository.GetAllAsync())
            .Any(w => w.ContentType == WatchProgressContentType.Movie && w.ProfileId == movie.ProfileId && w.ContentKey == movie.SourceMovieId);

        return Results.Json(new MovieDetailResponse(
            movie.Id, movie.Name, movie.CoverUrl, movie.Plot, movie.Genre, movie.ReleaseDate, movie.Duration, movie.Rating, isFavorite, isWatched));
    }

    private async Task<IResult> HandleMoviePlay(HttpRequest request, Guid id)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var found = await FindMovieAsync(id);
        if (found is not ({ } movie, _))
        {
            return Results.NotFound();
        }

        await RunOnUiThreadAsync(() => _player.PlayMovie(movie));
        return Results.Ok();
    }

    private async Task<IResult> HandleMovieWatched(HttpRequest request, Guid id, WatchedRequest? body)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        if (body is null)
        {
            return Results.BadRequest();
        }

        var found = await FindMovieAsync(id);
        if (found is not ({ } movie, _))
        {
            return Results.NotFound();
        }

        if (body.Watched)
        {
            await _watchedItemRepository.MarkWatchedAsync(movie.ProfileId, WatchProgressContentType.Movie, movie.SourceMovieId);
        }
        else
        {
            await _watchedItemRepository.MarkUnwatchedAsync(movie.ProfileId, WatchProgressContentType.Movie, movie.SourceMovieId);
        }

        return Results.Ok();
    }

    // Movies are merged across every profile for browsing (same as MoviesViewModel), so finding one by
    // its own Id means checking each profile's movie list in turn - callers also need the owning
    // ProfileSource for on-demand plot/genre detail fetches.
    private async Task<(Movie Movie, ProfileSource Profile)?> FindMovieAsync(Guid id)
    {
        foreach (var profile in await _profileRepository.GetAllAsync())
        {
            var movie = (await _movieRepository.GetMoviesAsync(profile.Id)).FirstOrDefault(m => m.Id == id);
            if (movie is not null)
            {
                return (movie, profile);
            }
        }

        return null;
    }

    private sealed record MovieDetailResponse(
        Guid Id, string Name, string? CoverUrl, string? Plot, string? Genre, string? ReleaseDate, string? Duration, double? Rating,
        bool IsFavorite, bool IsWatched);
    private sealed record MovieItemResponse(Guid Id, string Name, string? CoverUrl, string Category, bool IsFavorite, bool IsWatched);
    private sealed record MovieListResponse(List<string> Categories, List<MovieItemResponse> Items, bool Truncated);
}
