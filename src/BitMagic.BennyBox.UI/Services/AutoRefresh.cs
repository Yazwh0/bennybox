using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.Messages;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;

namespace BitMagic.BennyBox.UI.Services;

// Keeps every source up to date without anyone pressing Refresh. Before this, nothing refreshed on its
// own: the guide quietly ran out after a few days (a desktop install was found a month out of date,
// with no "what's on" anywhere), and new films and episodes never appeared.
//
// Shortly after start-up, and then every half hour while the app is open, each enabled source is
// checked:
//  - the guide is fetched again when it runs out within the next day (or there's none);
//  - everything (channels, guide, shows, films, videos) is fetched again once a day.
// Pages reload afterwards through ChannelsUpdatedMessage, same as after a manual refresh.
public sealed class AutoRefresh(
    IProfileRepository profileRepository,
    IEpgRepository epgRepository,
    ProfileContentImporter importer,
    ILogger<AutoRefresh> logger)
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan GuideMustLast = TimeSpan.FromDays(1);
    private static readonly TimeSpan ContentMaxAge = TimeSpan.FromDays(1);

    // Some sources never record a refresh (Downloads, local folders) and some have no guide to fetch,
    // so attempts are remembered too - otherwise those would be "refreshed" every half hour forever.
    private static readonly TimeSpan GuideRetryAfter = TimeSpan.FromHours(6);
    private readonly Dictionary<Guid, DateTime> _contentTried = [];
    private readonly Dictionary<Guid, DateTime> _guideTried = [];

    private int _started;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _ = RunAsync();
        }
    }

    private async Task RunAsync()
    {
        await Task.Delay(FirstCheck);
        while (true)
        {
            try
            {
                await CheckAllAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Automatic refresh failed");
            }

            await Task.Delay(CheckEvery);
        }
    }

    private async Task CheckAllAsync()
    {
        var changed = false;
        foreach (var profile in await profileRepository.GetEnabledAsync())
        {
            var now = DateTime.UtcNow;
            var lastContent = Latest(profile.LastRefreshedUtc, _contentTried.GetValueOrDefault(profile.Id));
            if (lastContent is not { } last || now - last > ContentMaxAge)
            {
                _contentTried[profile.Id] = now;
                logger.LogInformation("Refreshing {Profile} (last refreshed {Last})", profile.Name, profile.LastRefreshedUtc);
                var summary = await importer.ImportAllAsync(profile);
                logger.LogInformation("Refreshed {Profile}: {Summary}", profile.Name, summary);
                changed = true;
                continue;
            }

            if (profile.EpgSourceType != Core.Models.EpgSourceType.None)
            {
                var guideEnds = await epgRepository.GetLatestProgrammeEndAsync(profile.Id);
                var guideRecentlyTried = _guideTried.TryGetValue(profile.Id, out var tried) && now - tried < GuideRetryAfter;
                if ((guideEnds is null || guideEnds < now + GuideMustLast) && !guideRecentlyTried)
                {
                    _guideTried[profile.Id] = now;
                    logger.LogInformation("Refreshing the guide for {Profile} (it ends {Ends})", profile.Name, guideEnds);
                    await importer.ImportGuideAsync(profile);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            // The pages rebuild their lists in response, which has to happen on the UI thread.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => WeakReferenceMessenger.Default.Send(new ChannelsUpdatedMessage()));
        }
    }

    private static DateTime? Latest(DateTime? a, DateTime b) =>
        b == default ? a : a is { } value && value > b ? value : b;
}
