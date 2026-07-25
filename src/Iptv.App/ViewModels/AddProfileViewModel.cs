using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iptv.Core.Models;
using Iptv.Core.Services;
using Iptv.Sources.Xtream;

namespace Iptv.App.ViewModels;

[SupportedOSPlatform("windows")]
public partial class AddProfileViewModel : ViewModelBase
{
    private readonly XtreamClient _xtreamClient;

    public AddProfileViewModel(XtreamClient xtreamClient)
    {
        _xtreamClient = xtreamClient;
    }

    // Set by LoadForEdit when this dialog is editing an existing profile rather than creating a new
    // one - TryBuildProfile carries its Id/SortOrder over so the edited profile replaces it in place
    // instead of appearing as a duplicate at the end of the list.
    private ProfileSource? _editingProfile;

    public bool IsEditMode => _editingProfile is not null;

    public void LoadForEdit(ProfileSource profile)
    {
        _editingProfile = profile;

        Name = profile.Name;
        SelectedSourceType = profile.SourceType;
        M3uUrl = profile.M3uUrl ?? string.Empty;
        EpgUrl = profile.EpgSourceType == EpgSourceType.XmltvUrl ? profile.EpgUrl ?? string.Empty : string.Empty;
        XtreamServerUrl = profile.XtreamServerUrl ?? string.Empty;
        XtreamUsername = profile.XtreamUsername ?? string.Empty;
        XtreamPassword = CredentialProtector.Unprotect(profile.XtreamPasswordEncrypted) ?? string.Empty;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsM3uSelected))]
    [NotifyPropertyChangedFor(nameof(IsXtreamSelected))]
    private ProfileSourceType _selectedSourceType = ProfileSourceType.M3u;

    public bool IsM3uSelected
    {
        get => SelectedSourceType == ProfileSourceType.M3u;
        set
        {
            if (value)
            {
                SelectedSourceType = ProfileSourceType.M3u;
            }
        }
    }

    public bool IsXtreamSelected
    {
        get => SelectedSourceType == ProfileSourceType.XtreamCodes;
        set
        {
            if (value)
            {
                SelectedSourceType = ProfileSourceType.XtreamCodes;
            }
        }
    }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _m3uUrl = string.Empty;

    [ObservableProperty]
    private string _epgUrl = string.Empty;

    [ObservableProperty]
    private string _xtreamServerUrl = string.Empty;

    [ObservableProperty]
    private string _xtreamUsername = string.Empty;

    [ObservableProperty]
    private string _xtreamPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    [ObservableProperty]
    private bool _isTestingConnection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTestResult))]
    private string? _testResultMessage;

    [ObservableProperty]
    private bool _testSucceeded;

    public bool HasTestResult => !string.IsNullOrEmpty(TestResultMessage);

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsTestingConnection = true;
        TestResultMessage = null;
        TestSucceeded = false;
        try
        {
            var result = await _xtreamClient.AuthenticateAsync(XtreamServerUrl.TrimEnd('/'), XtreamUsername, XtreamPassword);
            TestSucceeded = true;
            TestResultMessage = $"Connected. Status: {result.UserInfo.Status}, expires {FormatExpiry(result.UserInfo.ExpDate)}.";
        }
        catch (Exception ex)
        {
            TestResultMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    private static string FormatExpiry(string? expDateUnixSeconds)
    {
        if (long.TryParse(expDateUnixSeconds, out var seconds) && seconds > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("yyyy-MM-dd");
        }
        return "unknown";
    }

    public bool TryBuildProfile(out ProfileSource? profile)
    {
        profile = null;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Name is required.";
            return false;
        }

        if (SelectedSourceType == ProfileSourceType.M3u)
        {
            if (string.IsNullOrWhiteSpace(M3uUrl) || !Uri.TryCreate(M3uUrl, UriKind.Absolute, out _))
            {
                ErrorMessage = "A valid M3U URL is required.";
                return false;
            }

            profile = new ProfileSource
            {
                Name = Name.Trim(),
                SourceType = ProfileSourceType.M3u,
                M3uUrl = M3uUrl.Trim()
            };

            if (!string.IsNullOrWhiteSpace(EpgUrl) && Uri.TryCreate(EpgUrl, UriKind.Absolute, out _))
            {
                profile.EpgSourceType = EpgSourceType.XmltvUrl;
                profile.EpgUrl = EpgUrl.Trim();
            }

            ApplyEditingIdentity(profile);
            return true;
        }

        if (string.IsNullOrWhiteSpace(XtreamServerUrl) || !Uri.TryCreate(XtreamServerUrl, UriKind.Absolute, out _) ||
            string.IsNullOrWhiteSpace(XtreamUsername) || string.IsNullOrWhiteSpace(XtreamPassword))
        {
            ErrorMessage = "Server URL, username, and password are required.";
            return false;
        }

        profile = new ProfileSource
        {
            Name = Name.Trim(),
            SourceType = ProfileSourceType.XtreamCodes,
            XtreamServerUrl = XtreamServerUrl.Trim().TrimEnd('/'),
            XtreamUsername = XtreamUsername.Trim(),
            XtreamPasswordEncrypted = CredentialProtector.Protect(XtreamPassword),
            EpgSourceType = EpgSourceType.XtreamEmbedded
        };

        ApplyEditingIdentity(profile);
        return true;
    }

    private void ApplyEditingIdentity(ProfileSource profile)
    {
        if (_editingProfile is null)
        {
            return;
        }

        profile.Id = _editingProfile.Id;
        profile.SortOrder = _editingProfile.SortOrder;
    }
}
