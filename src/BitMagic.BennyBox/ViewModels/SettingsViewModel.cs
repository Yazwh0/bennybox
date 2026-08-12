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
    private readonly ClipImportService _clipImportService;
    private readonly AccountInfoService _accountInfoService;
    private readonly ISettingsStore _settingsStore;
    private readonly IMetadataEnrichmentService _metadataEnrichmentService;
    private readonly DownloadManager _downloadManager;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _isApplyingSavedTheme;
    private bool _isApplyingSavedWindowOpacity;
    private bool _isApplyingSavedPlaybackTiming;
    private bool _isApplyingSavedTrackPreferences;
    private bool _isApplyingSavedRemoteKeyMode;
    private bool _isApplyingSavedTmdbApiKey;

    public string Title => "Settings";

    public ObservableCollection<ProfileSource> Profiles { get; } = [];

    public string[] ThemeOptions { get; } = ["System", "Light", "Dark"];

    // ComboBox source for the Preferred audio/subtitle pickers on SettingsView - "" is the leading
    // entry deliberately (renders as a blank row) so "no preference" is selectable, matching
    // PreferredAudioLanguage/PreferredSubtitleLanguage's blank-means-default/off behavior. Matching
    // against the actual stream is a loose substring match (see PlayerViewModel.FindPreferredTrack),
    // so this list doesn't need to be exhaustive - it's just the common cases up front.
    public string[] CommonLanguages { get; } =
    [
        "", "English", "Spanish", "French", "German", "Italian", "Portuguese", "Dutch", "Russian",
        "Arabic", "Hindi", "Mandarin", "Japanese", "Korean", "Turkish", "Polish", "Swedish"
    ];

    [ObservableProperty]
    private string _selectedTheme = "System";

    // Drives MainWindow's Background via WindowBackgroundOpacityToBrushConverter - 100 is fully
    // opaque (no see-through at all), lower values let more of the OS's AcrylicBlur show through.
    // Default (55) matches the "Smoked Glass" look's original hardcoded SmokedBgTranslucentBrush
    // Opacity before this was made adjustable.
    [ObservableProperty]
    private int _windowBackgroundOpacityPercent = 55;

    // Defaults mirror PlayerViewModel's DefaultSkipInterval/DefaultLoadTimeout/DefaultStallThreshold/
    // DefaultPauseReconnectThreshold - kept in sync manually since there's no shared constants class
    // between the two VMs, only the persisted setting keys they both agree on.

    // How far the skip forward/back transport buttons jump.
    [ObservableProperty]
    private int _skipIntervalSeconds = 30;

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

    // Mirrors RemoteControlServer's RemoteKeyFixedSettingKey - kept as a plain bool here (rather than
    // reading the server's own state) since the server only exists while the remote is actually
    // running, but this setting needs to be readable/settable from Settings regardless.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemoteKeySessionBound))]
    private bool _isRemoteKeyFixed;

    public bool IsRemoteKeySessionBound
    {
        get => !IsRemoteKeyFixed;
        set => IsRemoteKeyFixed = !value;
    }

    // Read by TmdbMetadataEnrichmentService (see BitMagic.BennyBox.Sources.Tmdb) via the same
    // ISettingsStore key ("TmdbApiKey") - fills in Plot/Genre/ReleaseDate/poster for LocalFolder/Sftp
    // movies and shows with no local NFO/poster. Overrides the build's bundled key (see
    // HasEmbeddedTmdbKey) when set; on a build with no bundled key, blank here means enrichment is
    // skipped entirely instead.
    [ObservableProperty]
    private string _tmdbApiKey = "";

    // Whether THIS build has a bundled TMDb key at all (see EmbeddedTmdbApiKey) - independent of
    // whatever's typed into TmdbApiKey above. Drives the Settings page's placeholder/description text
    // so it doesn't claim "leave blank to skip" on a build where a blank field still works.
    public bool HasEmbeddedTmdbKey => _metadataEnrichmentService.HasEmbeddedFallback;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    // Where downloaded movies/episodes/clips land (see DownloadManager) - shown read-only here with a
    // "Change location..." picker, not editable as free text (same rationale as the Movies/TV Shows
    // folder pickers on AddProfileView - a typo'd path is worse than not letting you type one).
    [ObservableProperty]
    private string _downloadsFolderPath = "";

    [ObservableProperty]
    private string _downloadsDiskUsageLabel = "Nothing downloaded yet";

    public SettingsViewModel(
        IProfileRepository profileRepository,
        PlaylistImportService importService,
        EpgImportService epgImportService,
        SeriesImportService seriesImportService,
        MovieImportService movieImportService,
        ClipImportService clipImportService,
        AccountInfoService accountInfoService,
        ISettingsStore settingsStore,
        IMetadataEnrichmentService metadataEnrichmentService,
        DownloadManager downloadManager,
        ILogger<SettingsViewModel> logger)
    {
        _profileRepository = profileRepository;
        _importService = importService;
        _epgImportService = epgImportService;
        _seriesImportService = seriesImportService;
        _movieImportService = movieImportService;
        _clipImportService = clipImportService;
        _accountInfoService = accountInfoService;
        _settingsStore = settingsStore;
        _metadataEnrichmentService = metadataEnrichmentService;
        _downloadManager = downloadManager;
        _logger = logger;

        _ = LoadProfilesAsync();
        _ = LoadThemeAsync();
        _ = LoadWindowOpacityAsync();
        _ = LoadPlaybackTimingSettingsAsync();
        _ = LoadTrackPreferencesAsync();
        _ = LoadRemoteKeyModeAsync();
        _ = LoadTmdbApiKeyAsync();
        _ = LoadDownloadsSectionAsync();
    }

    private async Task LoadDownloadsSectionAsync()
    {
        DownloadsFolderPath = await _downloadManager.GetDownloadsRootAsync();
        await RefreshDownloadsDiskUsageAsync();
    }

    private async Task RefreshDownloadsDiskUsageAsync()
    {
        var downloadsProfile = await _downloadManager.GetDownloadsProfileIfExistsAsync();
        if (downloadsProfile is null)
        {
            DownloadsDiskUsageLabel = "Nothing downloaded yet";
            return;
        }

        var totalBytes = await Task.Run(() =>
        {
            long total = 0;
            foreach (var root in new[] { downloadsProfile.LocalMoviesPath, downloadsProfile.LocalSeriesPath, downloadsProfile.LocalClipsPath })
            {
                if (root is null || !Directory.Exists(root))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch (IOException)
                    {
                        // Vanished/locked between enumeration and stat - skip it, same rationale as
                        // LocalFileSystem.EnumerateFilesAsync.
                    }
                }
            }
            return total;
        });

        DownloadsDiskUsageLabel = $"{FormatBytes(totalBytes)} used";
    }

    // Called from SettingsView's code-behind after the user picks a new folder (the picker itself
    // needs a TopLevel/StorageProvider, which only the View has - see AddProfileView.axaml.cs for the
    // same split).
    public async Task SetDownloadsLocationAsync(string path)
    {
        await _downloadManager.SetDownloadsRootAsync(path);
        DownloadsFolderPath = path;
        StatusMessage = "New downloads will be saved here from now on - files already downloaded stay where they are.";
    }

    [RelayCommand]
    private async Task ClearAllDownloadsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _downloadManager.ClearAllDownloadsAsync();
            await RefreshDownloadsDiskUsageAsync();
            WeakReferenceMessenger.Default.Send(new ChannelsUpdatedMessage());
            StatusMessage = "Downloads cleared.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't clear downloads: {ex.Message}";
            _logger.LogError(ex, "Failed to clear all downloads");
        }
        finally
        {
            IsBusy = false;
        }
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

    private async Task LoadWindowOpacityAsync()
    {
        _isApplyingSavedWindowOpacity = true;
        WindowBackgroundOpacityPercent = await GetSavedIntAsync("WindowBackgroundOpacityPercent", WindowBackgroundOpacityPercent);
        _isApplyingSavedWindowOpacity = false;
    }

    partial void OnWindowBackgroundOpacityPercentChanged(int value)
    {
        if (_isApplyingSavedWindowOpacity)
        {
            return;
        }

        _ = _settingsStore.SetAsync("WindowBackgroundOpacityPercent", value.ToString(CultureInfo.InvariantCulture));
    }

    private async Task LoadPlaybackTimingSettingsAsync()
    {
        _isApplyingSavedPlaybackTiming = true;
        SkipIntervalSeconds = await GetSavedIntAsync("PlaybackSkipIntervalSeconds", SkipIntervalSeconds);
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

    partial void OnSkipIntervalSecondsChanged(int value) => SavePlaybackTimingSetting("PlaybackSkipIntervalSeconds", value);
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

    private async Task LoadRemoteKeyModeAsync()
    {
        _isApplyingSavedRemoteKeyMode = true;
        IsRemoteKeyFixed = await _settingsStore.GetAsync("RemoteKeyFixed") == "true";
        _isApplyingSavedRemoteKeyMode = false;
    }

    // No message to send here, unlike playback timing/track preferences - RemoteControlServer reads
    // this setting fresh every time it starts or regenerates a code rather than caching it, so
    // there's no running instance's state to keep in sync.
    partial void OnIsRemoteKeyFixedChanged(bool value)
    {
        if (_isApplyingSavedRemoteKeyMode)
        {
            return;
        }

        _ = _settingsStore.SetAsync("RemoteKeyFixed", value ? "true" : "false");
    }

    private async Task LoadTmdbApiKeyAsync()
    {
        _isApplyingSavedTmdbApiKey = true;
        TmdbApiKey = await _settingsStore.GetAsync("TmdbApiKey") ?? "";
        _isApplyingSavedTmdbApiKey = false;
    }

    partial void OnTmdbApiKeyChanged(string value)
    {
        if (_isApplyingSavedTmdbApiKey)
        {
            return;
        }

        _ = _settingsStore.SetAsync("TmdbApiKey", value.Trim());
    }

    private async Task LoadProfilesAsync()
    {
        // The Downloads profile is a real LocalFolder ProfileSource (see DownloadManager) but isn't
        // meant to be managed like a user-added one - no Edit/Refresh/Delete buttons for it here, it
        // gets its own section instead (folder location, disk usage, clear all).
        var downloadsProfile = await _downloadManager.GetDownloadsProfileIfExistsAsync();
        var profiles = await _profileRepository.GetAllAsync();
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            if (downloadsProfile is not null && profile.Id == downloadsProfile.Id)
            {
                continue;
            }

            Profiles.Add(profile);
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }
        return $"{value:0.#} {units[unitIndex]}";
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
            StatusMessage = $"Added '{profile.Name}': {await ImportProfileContentAsync(profile)}.";
        }
        catch (Exception ex)
        {
            // The profile itself may already be in the database (AddAsync ran before whatever threw) -
            // still reload the list below so it shows up and can be edited/retried, even though import
            // failed. Without this, a save-then-throw here used to leave the profile in the DB but
            // invisible in the UI, with no way to reach it again.
            StatusMessage = $"Failed to add '{profile.Name}': {ex.Message}";
            _logger.LogError(ex, "Failed to add profile {ProfileName}", profile.Name);
        }
        finally
        {
            await LoadProfilesAsync();
            WeakReferenceMessenger.Default.Send(new ChannelsUpdatedMessage());
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
        }
        catch (Exception ex)
        {
            StatusMessage = $"Update failed: {ex.Message}";
            _logger.LogError(ex, "Failed to update profile {ProfileName}", profile.Name);
        }
        finally
        {
            await LoadProfilesAsync();
            WeakReferenceMessenger.Default.Send(new ChannelsUpdatedMessage());
            IsBusy = false;
        }
    }

    // Runs every import step (channels/EPG, series, movies, account info) independently so a failure
    // in one - e.g. an SFTP permission error while listing a movies path - doesn't stop the others from
    // running or prevent the caller's profile-list reload. Used by both AddProfileAsync (first import)
    // and RefreshProfileAsync (re-import).
    private async Task<string> ImportProfileContentAsync(ProfileSource profile)
    {
        var messages = new List<string>();

        try
        {
            var result = await _importService.ImportAsync(profile);
            messages.Add(result.NotModified
                ? "channels up to date"
                : $"{result.Channels.Count} channels in {result.Categories.Count} categories");

            if (profile.EpgSourceType != EpgSourceType.None)
            {
                await _epgImportService.ImportAsync(profile);
                if (!result.NotModified)
                {
                    messages.Add("EPG updated");
                }
            }
        }
        catch (Exception ex)
        {
            messages.Add($"channels/EPG failed: {ex.Message}");
            _logger.LogError(ex, "Failed to import channels/EPG for profile {ProfileName}", profile.Name);
        }

        try
        {
            var seriesResult = await _seriesImportService.ImportAsync(profile);
            if (seriesResult is not null)
            {
                messages.Add($"{seriesResult.SeriesList.Count} series");
            }
        }
        catch (Exception ex)
        {
            messages.Add($"series failed: {ex.Message}");
            _logger.LogError(ex, "Failed to import series for profile {ProfileName}", profile.Name);
        }

        try
        {
            var movieResult = await _movieImportService.ImportAsync(profile);
            if (movieResult is not null)
            {
                messages.Add($"{movieResult.Movies.Count} movies");
            }
        }
        catch (Exception ex)
        {
            messages.Add($"movies failed: {ex.Message}");
            _logger.LogError(ex, "Failed to import movies for profile {ProfileName}", profile.Name);
        }

        try
        {
            var clipResult = await _clipImportService.ImportAsync(profile);
            if (clipResult is not null)
            {
                messages.Add($"{clipResult.Clips.Count} clips");
            }
        }
        catch (Exception ex)
        {
            messages.Add($"clips failed: {ex.Message}");
            _logger.LogError(ex, "Failed to import clips for profile {ProfileName}", profile.Name);
        }

        try
        {
            await RefreshAccountInfoAsync(profile);
        }
        catch (Exception ex)
        {
            messages.Add($"account info failed: {ex.Message}");
            _logger.LogError(ex, "Failed to refresh account info for profile {ProfileName}", profile.Name);
        }

        return string.Join(", ", messages);
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
            StatusMessage = $"Refreshed '{profile.Name}': {await ImportProfileContentAsync(profile)}.";
        }
        finally
        {
            await LoadProfilesAsync();
            WeakReferenceMessenger.Default.Send(new ChannelsUpdatedMessage());
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
