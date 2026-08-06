using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using BitMagic.BennyBox.Messages;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using Microsoft.Extensions.Logging;

namespace BitMagic.BennyBox.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IProfileRepository _profileRepository;
    private readonly PlaylistImportService _importService;
    private readonly EpgImportService _epgImportService;
    private readonly SeriesImportService _seriesImportService;
    private readonly MovieImportService _movieImportService;
    private readonly AccountInfoService _accountInfoService;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _isApplyingSavedTheme;
    private bool _isApplyingSavedPlaybackTiming;
    private bool _isApplyingSavedTrackPreferences;

    public string Title => "Settings";

    public ObservableCollection<ProfileSource> Profiles { get; } = [];

    public string[] ThemeOptions { get; } = ["System", "Light", "Dark"];

    // Just a starting point for the AutoCompleteBox's suggestion list on SettingsView - matching is a
    // loose substring match against whatever the stream itself calls the track (see
    // PlayerViewModel.FindPreferredTrack), so typing anything not in this list still works fine.
    public string[] CommonLanguages { get; } =
    [
        "English", "Spanish", "French", "German", "Italian", "Portuguese", "Dutch", "Russian",
        "Arabic", "Hindi", "Mandarin", "Japanese", "Korean", "Turkish", "Polish", "Swedish"
    ];

    [ObservableProperty]
    private string _selectedTheme = "System";

    // Defaults mirror PlayerViewModel's DefaultLoadTimeout/DefaultStallThreshold/
    // DefaultPauseReconnectThreshold - kept in sync manually since there's no shared constants class
    // between the two VMs, only the persisted setting keys they both agree on.

    // How long the initial connection attempt gets before "Channel unavailable (timed out)".
    [ObservableProperty]
    private int _loadTimeoutSeconds = 15;

    // How long playback can go with no new frame actually displayed before it's treated as frozen.
    [ObservableProperty]
    private int _stallThresholdSeconds = 5;

    // How long a pause has to last before resuming reconnects from scratch instead of just
    // unpausing, to get ahead of IPTV providers that drop idle connections.
    [ObservableProperty]
    private int _pauseReconnectThresholdSeconds = 45;

    // Loose substring match against whatever the stream/container calls the track (e.g. "English"),
    // applied once per playback - see PlayerViewModel.FindPreferredTrack. Blank means "use whatever
    // the stream defaults to" for audio, and "off" for subtitles specifically.
    [ObservableProperty]
    private string _preferredAudioLanguage = "";

    [ObservableProperty]
    private string _preferredSubtitleLanguage = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public SettingsViewModel(
        IProfileRepository profileRepository,
        PlaylistImportService importService,
        EpgImportService epgImportService,
        SeriesImportService seriesImportService,
        MovieImportService movieImportService,
        AccountInfoService accountInfoService,
        ISettingsStore settingsStore,
        ILogger<SettingsViewModel> logger)
    {
        _profileRepository = profileRepository;
        _importService = importService;
        _epgImportService = epgImportService;
        _seriesImportService = seriesImportService;
        _movieImportService = movieImportService;
        _accountInfoService = accountInfoService;
        _settingsStore = settingsStore;
        _logger = logger;

        _ = LoadProfilesAsync();
        _ = LoadThemeAsync();
        _ = LoadPlaybackTimingSettingsAsync();
        _ = LoadTrackPreferencesAsync();
    }

    private async Task LoadThemeAsync()
    {
        var saved = await _settingsStore.GetAsync("Theme");
        if (saved is null)
        {
            return;
        }

        _isApplyingSavedTheme = true;
        SelectedTheme = saved;
        _isApplyingSavedTheme = false;
    }

    partial void OnSelectedThemeChanged(string value)
    {
        Application.Current!.RequestedThemeVariant = value switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (!_isApplyingSavedTheme)
        {
            _ = _settingsStore.SetAsync("Theme", value);
        }
    }

    private async Task LoadPlaybackTimingSettingsAsync()
    {
        _isApplyingSavedPlaybackTiming = true;
        LoadTimeoutSeconds = await GetSavedIntAsync("PlaybackLoadTimeoutSeconds", LoadTimeoutSeconds);
        StallThresholdSeconds = await GetSavedIntAsync("PlaybackStallThresholdSeconds", StallThresholdSeconds);
        PauseReconnectThresholdSeconds = await GetSavedIntAsync("PlaybackPauseReconnectThresholdSeconds", PauseReconnectThresholdSeconds);
        _isApplyingSavedPlaybackTiming = false;
    }

    private async Task<int> GetSavedIntAsync(string key, int fallback)
    {
        var saved = await _settingsStore.GetAsync(key);
        return saved is not null && int.TryParse(saved, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;
    }

    partial void OnLoadTimeoutSecondsChanged(int value) => SavePlaybackTimingSetting("PlaybackLoadTimeoutSeconds", value);
    partial void OnStallThresholdSecondsChanged(int value) => SavePlaybackTimingSetting("PlaybackStallThresholdSeconds", value);
    partial void OnPauseReconnectThresholdSecondsChanged(int value) => SavePlaybackTimingSetting("PlaybackPauseReconnectThresholdSeconds", value);

    private void SavePlaybackTimingSetting(string key, int value)
    {
        if (_isApplyingSavedPlaybackTiming || value <= 0)
        {
            return;
        }

        _ = _settingsStore.SetAsync(key, value.ToString(CultureInfo.InvariantCulture));
        WeakReferenceMessenger.Default.Send(new PlaybackTimingSettingsChangedMessage());
    }

    private async Task LoadTrackPreferencesAsync()
    {
        _isApplyingSavedTrackPreferences = true;
        PreferredAudioLanguage = await _settingsStore.GetAsync("PreferredAudioLanguage") ?? "";
        PreferredSubtitleLanguage = await _settingsStore.GetAsync("PreferredSubtitleLanguage") ?? "";
        _isApplyingSavedTrackPreferences = false;
    }

    partial void OnPreferredAudioLanguageChanged(string value) => SaveTrackPreference("PreferredAudioLanguage", value);
    partial void OnPreferredSubtitleLanguageChanged(string value) => SaveTrackPreference("PreferredSubtitleLanguage", value);

    private void SaveTrackPreference(string key, string value)
    {
        if (_isApplyingSavedTrackPreferences)
        {
            return;
        }

        _ = _settingsStore.SetAsync(key, value);
        WeakReferenceMessenger.Default.Send(new PlaybackTrackPreferencesChangedMessage());
    }

    private async Task LoadProfilesAsync()
    {
        var profiles = await _profileRepository.GetAllAsync();
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }
    }

    public async Task AddProfileAsync(ProfileSource profile)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"Importing '{profile.Name}'...";
        try
        {
            profile.SortOrder = Profiles.Count;
            await _profileRepository.AddAsync(profile);

            var result = await _importService.ImportAsync(profile);
            StatusMessage = $"Imported {result.Channels.Count} channels in {result.Categories.Count} categories.";

            if (profile.EpgSourceType != EpgSourceType.None)
            {
                StatusMessage += " Importing EPG...";
                await _epgImportService.ImportAsync(profile);
                StatusMessage = $"Imported {result.Channels.Count} channels, {result.Categories.Count} categories, and EPG data.";
            }

            var seriesResult = await _seriesImportService.ImportAsync(profile);
            if (seriesResult is not null)
            {
                StatusMessage += $" Imported {seriesResult.SeriesList.Count} series.";
            }

            var movieResult = await _movieImportService.ImportAsync(profile);
            if (movieResult is not null)
            {
                StatusMessage += $" Imported {movieResult.Movies.Count} movies.";
            }

            await RefreshAccountInfoAsync(profile);
            await LoadProfilesAsync();
            WeakReferenceMessenger.Default.Send(new ChannelsUpdatedMessage());
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
            _logger.LogError(ex, "Failed to import profile {ProfileName}", profile.Name);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Edited connection details are saved unconditionally first so they're never lost even if the
    // account-info ping below fails (e.g. the new server is briefly unreachable) - the user can
    // still hit Refresh later once it's back.
    public async Task EditProfileAsync(ProfileSource profile)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"Updating '{profile.Name}'...";
        try
        {
            await _profileRepository.UpdateAsync(profile);
            await RefreshAccountInfoAsync(profile);

            StatusMessage = $"Updated '{profile.Name}'. Refresh to re-import with the new settings.";
            await LoadProfilesAsync();
            WeakReferenceMessenger.Default.Send(new ChannelsUpdatedMessage());
        }
        catch (Exception ex)
        {
            StatusMessage = $"Update failed: {ex.Message}";
            _logger.LogError(ex, "Failed to update profile {ProfileName}", profile.Name);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // No-op for profile types with no account info to report (e.g. M3U) - see AccountInfoService.
    private async Task RefreshAccountInfoAsync(ProfileSource profile)
    {
        var accountInfo = await _accountInfoService.GetAccountInfoAsync(profile);
        if (accountInfo is null)
        {
            return;
        }

        profile.XtreamStatus = accountInfo.Status;
        profile.XtreamExpiryUtc = accountInfo.ExpiryUtc;
        profile.XtreamMaxConnections = accountInfo.MaxConnections;
        await _profileRepository.UpdateAsync(profile);
    }

    [RelayCommand]
    private async Task RefreshProfileAsync(ProfileSource? profile)
    {
        if (profile is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"Refreshing '{profile.Name}'...";
        try
        {
            var result = await _importService.ImportAsync(profile);
            StatusMessage = result.NotModified
                ? "Already up to date - no changes on the server."
                : $"Refreshed: {result.Channels.Count} channels in {result.Categories.Count} categories.";

            if (profile.EpgSourceType != EpgSourceType.None)
            {
                await _epgImportService.ImportAsync(profile);
                if (!result.NotModified)
                {
                    StatusMessage += " EPG refreshed.";
                }
            }

            var seriesResult = await _seriesImportService.ImportAsync(profile);
            if (seriesResult is not null && !result.NotModified)
            {
                StatusMessage += $" {seriesResult.SeriesList.Count} series refreshed.";
            }

            var movieResult = await _movieImportService.ImportAsync(profile);
            if (movieResult is not null && !result.NotModified)
            {
                StatusMessage += $" {movieResult.Movies.Count} movies refreshed.";
            }

            await RefreshAccountInfoAsync(profile);
            WeakReferenceMessenger.Default.Send(new ChannelsUpdatedMessage());
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh failed: {ex.Message}";
            _logger.LogError(ex, "Failed to refresh profile {ProfileName}", profile.Name);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(ProfileSource? profile)
    {
        if (profile is null || IsBusy)
        {
            return;
        }

        await _profileRepository.DeleteAsync(profile.Id);
        await LoadProfilesAsync();
        WeakReferenceMessenger.Default.Send(new ChannelsUpdatedMessage());
    }
}
