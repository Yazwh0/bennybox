using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Iptv.App.Messages;
using Iptv.Core.Models;
using Iptv.Core.Services;
using Microsoft.Extensions.Logging;

namespace Iptv.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IProfileRepository _profileRepository;
    private readonly PlaylistImportService _importService;
    private readonly EpgImportService _epgImportService;
    private readonly SeriesImportService _seriesImportService;
    private readonly MovieImportService _movieImportService;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _isApplyingSavedTheme;

    public string Title => "Settings";

    public ObservableCollection<ProfileSource> Profiles { get; } = [];

    public string[] ThemeOptions { get; } = ["System", "Light", "Dark"];

    [ObservableProperty]
    private string _selectedTheme = "System";

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
        ISettingsStore settingsStore,
        ILogger<SettingsViewModel> logger)
    {
        _profileRepository = profileRepository;
        _importService = importService;
        _epgImportService = epgImportService;
        _seriesImportService = seriesImportService;
        _movieImportService = movieImportService;
        _settingsStore = settingsStore;
        _logger = logger;

        _ = LoadProfilesAsync();
        _ = LoadThemeAsync();
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
