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
    private void MapLiveTvRoutes(WebApplication app)
    {
        app.MapGet("/api/livetv", HandleLiveTvList);
        app.MapPost("/api/livetv/play", HandleLiveTvPlay);
    }

    // Independent of LiveTvViewModel - see the type-level comment on why this re-queries repositories
    // directly instead of reading/filtering that page's live-bound Rows/SearchText.
    private async Task<IResult> HandleLiveTvList(HttpRequest request, string? search, string? category)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var favoriteIds = await _favoriteRepository.GetFavoriteChannelIdsAsync();
        var profiles = await _profileRepository.GetAllAsync();
        var nowUtc = DateTime.UtcNow;

        var allCategories = new List<string>();
        var items = new List<LiveTvItemResponse>();
        foreach (var profile in profiles)
        {
            var categories = await _channelRepository.GetCategoriesAsync(profile.Id);
            var channels = await _channelRepository.GetChannelsAsync(profile.Id);
            var nowNext = await _epgRepository.GetNowNextAsync(profile.Id, nowUtc);
            var categoryNamesById = categories.ToDictionary(c => c.Id, c => c.Name);

            allCategories.AddRange(categories.Select(c => c.Name));

            foreach (var channel in channels)
            {
                var categoryName = channel.CategoryId is not null && categoryNamesById.TryGetValue(channel.CategoryId, out var name) ? name : "";
                var nowTitle = !string.IsNullOrEmpty(channel.TvgId) && nowNext.TryGetValue(channel.TvgId, out var entry) ? entry.Now?.Title : null;
                items.Add(new LiveTvItemResponse(channel.Id, channel.Name, channel.LogoUrl, categoryName, nowTitle, favoriteIds.Contains(channel.Id)));
            }
        }

        var (filtered, truncated) = FilterAndCap(items, i =>
            (string.IsNullOrEmpty(category) || i.Category == category) &&
            (string.IsNullOrEmpty(search) ||
             i.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             (i.NowTitle?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)));

        return Results.Json(new LiveTvListResponse(allCategories.Distinct().OrderBy(c => c).ToList(), filtered, truncated));
    }

    private async Task<IResult> HandleLiveTvPlay(HttpRequest request, IdRequest? body)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        if (body is null)
        {
            return Results.BadRequest();
        }

        var channel = await FindChannelAsync(body.Id);
        if (channel is null)
        {
            return Results.NotFound();
        }

        await RunOnUiThreadAsync(() => _player.PlayChannel(channel));
        return Results.Ok();
    }

    // Channels are merged across every profile for browsing (same as LiveTvViewModel), so finding one
    // by its own Id means checking each profile's channel list in turn.
    private async Task<Channel?> FindChannelAsync(Guid id)
    {
        foreach (var profile in await _profileRepository.GetAllAsync())
        {
            var channel = (await _channelRepository.GetChannelsAsync(profile.Id)).FirstOrDefault(c => c.Id == id);
            if (channel is not null)
            {
                return channel;
            }
        }

        return null;
    }

    private sealed record LiveTvItemResponse(Guid Id, string Name, string? LogoUrl, string Category, string? NowTitle, bool IsFavorite);
    private sealed record LiveTvListResponse(List<string> Categories, List<LiveTvItemResponse> Items, bool Truncated);
}
