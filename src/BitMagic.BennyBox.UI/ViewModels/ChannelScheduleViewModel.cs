using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using Microsoft.Extensions.Logging;

namespace BitMagic.BennyBox.ViewModels;

// One channel's own page, opened from its row on Live TV: watch it live, see what's coming up, and -
// the main reason for it - watch earlier programmes again where the provider keeps catch-up (Xtream's
// tv_archive, typically 3-7 days on the main channels). Rows are day headings and programmes, from the
// start of the catch-up window (or the start of today, without catch-up) to two days ahead.
public partial class ChannelScheduleViewModel : ObservableObject
{
    private static readonly TimeSpan Ahead = TimeSpan.FromDays(2);

    private readonly IEpgRepository _epgRepository;
    private readonly TimeshiftUrlService _timeshiftUrlService;
    private readonly ILogger _logger;

    public ChannelListItemViewModel Channel { get; }
    public PlayerViewModel Player { get; }

    public ObservableCollection<object> Rows { get; } = [];

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _hasNoGuide;

    public bool HasCatchup => Channel.Channel.HasCatchup;

    public string CatchupNote => HasCatchup
        ? $"Programmes from the last {Channel.Channel.CatchupDays} day{(Channel.Channel.CatchupDays == 1 ? "" : "s")} can be watched again."
        : "This channel doesn't keep earlier programmes to watch again.";

    public ChannelScheduleViewModel(ChannelListItemViewModel channel, PlayerViewModel player, IEpgRepository epgRepository,
        TimeshiftUrlService timeshiftUrlService, ILogger logger)
    {
        Channel = channel;
        Player = player;
        _epgRepository = epgRepository;
        _timeshiftUrlService = timeshiftUrlService;
        _logger = logger;
    }

    // The row for the programme on now, for a view to scroll to once loaded.
    public ScheduleProgrammeViewModel? NowRow => Rows.OfType<ScheduleProgrammeViewModel>().FirstOrDefault(p => p.IsNow);

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var channel = Channel.Channel;
            if (string.IsNullOrEmpty(channel.TvgId))
            {
                HasNoGuide = true;
                return;
            }

            var now = DateTime.UtcNow;
            var catchupFrom = channel.HasCatchup && channel.CatchupDays > 0 ? now.AddDays(-channel.CatchupDays) : (DateTime?)null;
            var from = catchupFrom ?? DateTime.Today.ToUniversalTime();
            var programmes = await _epgRepository.GetChannelProgrammesAsync(channel.ProfileId, channel.TvgId, from, now + Ahead);

            Rows.Clear();
            string? lastDay = null;
            foreach (var programme in programmes)
            {
                var start = programme.StartUtc.ToLocalTime();
                var day = DayName(start.Date);
                if (day != lastDay)
                {
                    Rows.Add(new ScheduleDayHeader(day));
                    lastDay = day;
                }

                var isNow = programme.StartUtc <= now && programme.EndUtc > now;
                var isPast = programme.EndUtc <= now;
                var canCatchUp = isPast && catchupFrom is { } earliest && programme.StartUtc >= earliest;
                Rows.Add(new ScheduleProgrammeViewModel(programme, isNow, isPast, canCatchUp));
            }

            HasNoGuide = Rows.Count == 0;
            OnPropertyChanged(nameof(NowRow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the schedule for {Channel}", Channel.Name);
            HasNoGuide = Rows.Count == 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string DayName(DateTime day)
    {
        var today = DateTime.Today;
        if (day == today)
        {
            return "Today";
        }

        if (day == today.AddDays(-1))
        {
            return "Yesterday";
        }

        if (day == today.AddDays(1))
        {
            return "Tomorrow";
        }

        return day.ToString("dddd d MMMM", CultureInfo.CurrentCulture);
    }

    [RelayCommand]
    private void WatchLive() => Player.PlayChannel(Channel.Channel);

    // On now plays live; an earlier programme plays from catch-up where it's available; anything else
    // (later, or too long ago) does nothing - its row shows why.
    [RelayCommand]
    private async Task PlayProgrammeAsync(ScheduleProgrammeViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.IsNow)
        {
            WatchLive();
            return;
        }

        if (!row.CanCatchUp)
        {
            return;
        }

        var programme = row.Programme;
        try
        {
            var url = await _timeshiftUrlService.BuildTimeshiftUrlAsync(Channel.Channel, programme.StartUtc, programme.EndUtc - programme.StartUtc);
            if (url is not null)
            {
                Player.PlayTimeshift(Channel.Channel, url, programme.Title);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build catch-up URL for {Channel} / {Programme}", Channel.Name, programme.Title);
        }
    }
}

public sealed record ScheduleDayHeader(string Name);

public sealed class ScheduleProgrammeViewModel
{
    public EpgProgramme Programme { get; }
    public bool IsNow { get; }
    public bool IsPast { get; }
    public bool CanCatchUp { get; }

    public ScheduleProgrammeViewModel(EpgProgramme programme, bool isNow, bool isPast, bool canCatchUp)
    {
        Programme = programme;
        IsNow = isNow;
        IsPast = isPast;
        CanCatchUp = canCatchUp;
    }

    public string Title => Programme.Title;
    public string? Description => Programme.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string TimeLabel => Programme.StartUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    public string EndLabel => Programme.EndUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    // Something to tap: live now, or catch-up.
    public bool CanPlay => IsNow || CanCatchUp;

    // Earlier programmes that can't be watched again are dimmed.
    public double Opacity => IsPast && !CanCatchUp ? 0.45 : 1;

    public string Status => IsNow ? "On now" : CanCatchUp ? "Watch again" : IsPast ? "" : "";

    public double? NowProgress => IsNow && Programme.EndUtc > Programme.StartUtc
        ? Math.Clamp((DateTime.UtcNow - Programme.StartUtc) / (Programme.EndUtc - Programme.StartUtc), 0, 1)
        : null;
}
