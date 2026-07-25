using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Iptv.App.Messages;
using Iptv.Core.Models;
using Iptv.Core.Services;
using Microsoft.Extensions.Logging;

namespace Iptv.App.ViewModels;

public partial class GuideViewModel : ViewModelBase
{
    private const double PixelsPerMinuteValue = 4;
    private const string AllCategoriesLabel = "All Categories";

    private readonly IProfileRepository _profileRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IEpgRepository _epgRepository;
    private readonly ILogger<GuideViewModel> _logger;

    public string Title => "Guide";

    public PlayerViewModel Player { get; }

    public ObservableCollection<GuideRowViewModel> Rows { get; } = [];
    public ObservableCollection<string> Categories { get; } = [AllCategoriesLabel];

    public double PixelsPerMinute => PixelsPerMinuteValue;

    [ObservableProperty]
    private string _selectedCategory = AllCategoriesLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentDayLabel))]
    private int _dayOffset;

    [ObservableProperty]
    private DateTime _windowStartUtc;

    [ObservableProperty]
    private DateTime _windowEndUtc;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _loadError;

    public string CurrentDayLabel => DayOffset switch
    {
        0 => "Today",
        1 => "Tomorrow",
        _ => DateTime.Now.Date.AddDays(DayOffset).ToString("dddd, MMM d")
    };

    public GuideViewModel(
        IProfileRepository profileRepository,
        IChannelRepository channelRepository,
        IEpgRepository epgRepository,
        PlayerViewModel player,
        ILogger<GuideViewModel> logger)
    {
        _profileRepository = profileRepository;
        _channelRepository = channelRepository;
        _epgRepository = epgRepository;
        Player = player;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<ChannelsUpdatedMessage>(this, (_, _) => _ = LoadAsync());

        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task GoToTodayAsync()
    {
        DayOffset = 0;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task GoToNextDayAsync()
    {
        DayOffset++;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task GoToPreviousDayAsync()
    {
        if (DayOffset > 0)
        {
            DayOffset--;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    partial void OnSelectedCategoryChanged(string value) => _ = LoadAsync();

    private void TuneChannel(Channel channel) => Player.PlayChannel(channel);

    private async Task LoadAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var nowUtc = DateTime.UtcNow;
            var dayStartUtc = DateTime.UtcNow.Date.AddDays(DayOffset);
            var windowStart = DayOffset == 0 ? nowUtc.AddMinutes(-30) : dayStartUtc;
            var windowEnd = windowStart.AddHours(6);
            WindowStartUtc = windowStart;
            WindowEndUtc = windowEnd;

            var profiles = await _profileRepository.GetAllAsync();

            var profileData = new List<(IReadOnlyList<Category> Categories, IReadOnlyList<Channel> Channels, IReadOnlyList<EpgProgramme> Programmes)>();
            foreach (var profile in profiles)
            {
                var categories = await _channelRepository.GetCategoriesAsync(profile.Id);
                var channels = await _channelRepository.GetChannelsAsync(profile.Id);
                var programmes = await _epgRepository.GetProgrammesInRangeAsync(profile.Id, windowStart, windowEnd);
                profileData.Add((categories, channels, programmes));
            }

            var selectedCategoryAtLoad = SelectedCategory;

            // Grouping programmes/channels and building a GuideRowViewModel per channel is real CPU
            // work over potentially thousands of rows - keep it off the UI thread.
            var (categoryNames, rows) = await Task.Run(() =>
            {
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var builtRows = new List<GuideRowViewModel>();

                foreach (var (categories, channels, programmes) in profileData)
                {
                    var programmesByChannel = programmes
                        .GroupBy(p => p.ChannelTvgId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            g => g.Key,
                            g => (IReadOnlyList<EpgProgramme>)g.OrderBy(p => p.StartUtc).ToList(),
                            StringComparer.OrdinalIgnoreCase);

                    var categoryNameById = categories.ToDictionary(c => c.Id, c => c.Name);
                    foreach (var name in categoryNameById.Values)
                    {
                        names.Add(name);
                    }

                    var filteredChannels = selectedCategoryAtLoad == AllCategoriesLabel
                        ? channels
                        : channels.Where(c => c.CategoryId is not null &&
                                               categoryNameById.TryGetValue(c.CategoryId, out var name) &&
                                               name == selectedCategoryAtLoad);

                    foreach (var channel in filteredChannels)
                    {
                        var channelProgrammes = !string.IsNullOrEmpty(channel.TvgId) &&
                                                 programmesByChannel.TryGetValue(channel.TvgId, out var found)
                            ? found
                            : [];

                        builtRows.Add(new GuideRowViewModel(channel, channelProgrammes, windowStart, windowEnd, nowUtc, PixelsPerMinute)
                        {
                            TuneRequested = TuneChannel
                        });
                    }
                }

                return (names, builtRows);
            });

            var selectedCategory = SelectedCategory;
            Categories.Clear();
            Categories.Add(AllCategoriesLabel);
            foreach (var name in categoryNames)
            {
                Categories.Add(name);
            }

            if (!Categories.Contains(selectedCategory))
            {
                selectedCategory = AllCategoriesLabel;
            }
            SelectedCategory = selectedCategory;

            Rows.Clear();
            foreach (var row in rows.OrderBy(r => r.ChannelName))
            {
                Rows.Add(row);
            }
        }
        catch (Exception ex)
        {
            LoadError = "Failed to load guide.";
            _logger.LogError(ex, "Failed to load EPG guide");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
