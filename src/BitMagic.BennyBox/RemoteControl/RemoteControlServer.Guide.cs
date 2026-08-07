using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BitMagic.BennyBox.RemoteControl;

// Deliberately a flat "current + next programme per channel" list, not the desktop's full scrolling
// timeline grid - confirmed with the user as the right scope for a phone screen. No catch-up/timeshift
// browsing from here either; that stays a desktop-only feature for now.
public sealed partial class RemoteControlServer
{
    private void MapGuideRoutes(WebApplication app)
    {
        app.MapGet("/api/guide", HandleGuideList);
        app.MapPost("/api/guide/tune", HandleGuideTune);
        app.MapPost("/api/guide/reminder", HandleGuideReminder);
    }

    private async Task<IResult> HandleGuideList(HttpRequest request, string? search, string? category)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var favoriteIds = await _favoriteRepository.GetFavoriteChannelIdsAsync();
        var reminders = await _reminderRepository.GetAllAsync();
        var profiles = await _profileRepository.GetAllAsync();
        var nowUtc = DateTime.UtcNow;

        var allCategories = new List<string>();
        var items = new List<GuideItemResponse>();
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
                EpgNowNext? entry = !string.IsNullOrEmpty(channel.TvgId) && nowNext.TryGetValue(channel.TvgId, out var found) ? found : null;
                var hasReminder = entry?.Next is not null && reminders.Any(r =>
                    r.ProfileId == channel.ProfileId && r.ChannelTvgId == channel.TvgId && r.StartUtc == entry.Next.StartUtc);

                items.Add(new GuideItemResponse(
                    channel.Id, channel.ProfileId, channel.TvgId, channel.Name, channel.LogoUrl, categoryName, favoriteIds.Contains(channel.Id),
                    entry?.Now?.Title, entry?.Now?.StartUtc, entry?.Now?.EndUtc,
                    entry?.Next?.Title, entry?.Next?.StartUtc, entry?.Next?.EndUtc, hasReminder));
            }
        }

        var (filtered, truncated) = FilterAndCap(items, i =>
            (string.IsNullOrEmpty(category) || i.Category == category) &&
            (string.IsNullOrEmpty(search) ||
             i.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             (i.NowTitle?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
             (i.NextTitle?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)));

        return Results.Json(new GuideListResponse(allCategories.Distinct().OrderBy(c => c).ToList(), filtered, truncated));
    }

    private async Task<IResult> HandleGuideTune(HttpRequest request, IdRequest? body)
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

    // Mirrors GuideViewModel.ToggleReminderAsync exactly - same Reminder shape, same Add-for-set/
    // Remove-for-unset split.
    private async Task<IResult> HandleGuideReminder(HttpRequest request, ReminderRequest? body)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        if (body is null)
        {
            return Results.BadRequest();
        }

        if (body.Set)
        {
            await _reminderRepository.AddAsync(new Reminder
            {
                ProfileId = body.ProfileId,
                ChannelTvgId = body.ChannelTvgId,
                StartUtc = body.StartUtc,
                EndUtc = body.EndUtc,
                ChannelName = body.ChannelName,
                ProgrammeTitle = body.ProgrammeTitle
            });
        }
        else
        {
            await _reminderRepository.RemoveAsync(body.ProfileId, body.ChannelTvgId, body.StartUtc);
        }

        return Results.Ok();
    }

    private sealed record ReminderRequest(
        Guid ProfileId, string ChannelTvgId, string ChannelName, string ProgrammeTitle, DateTime StartUtc, DateTime EndUtc, bool Set);

    private sealed record GuideItemResponse(
        Guid Id, Guid ProfileId, string? TvgId, string Name, string? LogoUrl, string Category, bool IsFavorite,
        string? NowTitle, DateTime? NowStart, DateTime? NowEnd,
        string? NextTitle, DateTime? NextStart, DateTime? NextEnd, bool HasReminder);

    private sealed record GuideListResponse(List<string> Categories, List<GuideItemResponse> Items, bool Truncated);
}
