using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.Sources.Xtream;

namespace BitMagic.BennyBox.ViewModels;

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
        LocalMoviesPath = profile.LocalMoviesPath ?? string.Empty;
        LocalSeriesPath = profile.LocalSeriesPath ?? string.Empty;
        LocalClipsPath = profile.LocalClipsPath ?? string.Empty;
        SftpHost = profile.SftpHost ?? string.Empty;
        SftpPort = profile.SftpPort?.ToString() ?? "22";
        SftpUsername = profile.SftpUsername ?? string.Empty;
        SftpPassword = CredentialProtector.Unprotect(profile.SftpPasswordEncrypted) ?? string.Empty;
        SftpMoviesRemotePath = profile.SftpMoviesRemotePath ?? string.Empty;
        SftpSeriesRemotePath = profile.SftpSeriesRemotePath ?? string.Empty;
        SftpClipsRemotePath = profile.SftpClipsRemotePath ?? string.Empty;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsM3uSelected))]
    [NotifyPropertyChangedFor(nameof(IsXtreamSelected))]
    [NotifyPropertyChangedFor(nameof(IsLocalFolderSelected))]
    [NotifyPropertyChangedFor(nameof(IsSftpSelected))]
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

    public bool IsLocalFolderSelected
    {
        get => SelectedSourceType == ProfileSourceType.LocalFolder;
        set
        {
            if (value)
            {
                SelectedSourceType = ProfileSourceType.LocalFolder;
            }
        }
    }

    public bool IsSftpSelected
    {
        get => SelectedSourceType == ProfileSourceType.Sftp;
        set
        {
            if (value)
            {
                SelectedSourceType = ProfileSourceType.Sftp;
            }
        }
    }

    // Either or both may be set - a folder tree/SFTP site with both a movies root and a shows root
    // is common, so this isn't a single either/or choice the way it was before.
    [ObservableProperty]
    private string _localMoviesPath = string.Empty;

    [ObservableProperty]
    private string _localSeriesPath = string.Empty;

    [ObservableProperty]
    private string _localClipsPath = string.Empty;

    [ObservableProperty]
    private string _sftpHost = string.Empty;

    [ObservableProperty]
    private string _sftpPort = "22";

    [ObservableProperty]
    private string _sftpUsername = string.Empty;

    [ObservableProperty]
    private string _sftpPassword = string.Empty;

    [ObservableProperty]
    private string _sftpMoviesRemotePath = string.Empty;

    [ObservableProperty]
    private string _sftpSeriesRemotePath = string.Empty;

    [ObservableProperty]
    private string _sftpClipsRemotePath = string.Empty;

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

        if (SelectedSourceType == ProfileSourceType.LocalFolder)
        {
            if (string.IsNullOrWhiteSpace(LocalMoviesPath) && string.IsNullOrWhiteSpace(LocalSeriesPath) && string.IsNullOrWhiteSpace(LocalClipsPath))
            {
                ErrorMessage = "Set a Movies folder, a TV Shows folder, a Clips folder, or some combination.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(LocalMoviesPath) && !Directory.Exists(LocalMoviesPath))
            {
                ErrorMessage = "The Movies folder path doesn't exist.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(LocalSeriesPath) && !Directory.Exists(LocalSeriesPath))
            {
                ErrorMessage = "The TV Shows folder path doesn't exist.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(LocalClipsPath) && !Directory.Exists(LocalClipsPath))
            {
                ErrorMessage = "The Clips folder path doesn't exist.";
                return false;
            }

            profile = new ProfileSource
            {
                Name = Name.Trim(),
                SourceType = ProfileSourceType.LocalFolder,
                LocalMoviesPath = string.IsNullOrWhiteSpace(LocalMoviesPath) ? null : LocalMoviesPath.Trim(),
                LocalSeriesPath = string.IsNullOrWhiteSpace(LocalSeriesPath) ? null : LocalSeriesPath.Trim(),
                LocalClipsPath = string.IsNullOrWhiteSpace(LocalClipsPath) ? null : LocalClipsPath.Trim()
            };

            ApplyEditingIdentity(profile);
            return true;
        }

        if (SelectedSourceType == ProfileSourceType.Sftp)
        {
            if (string.IsNullOrWhiteSpace(SftpHost) || string.IsNullOrWhiteSpace(SftpUsername) ||
                string.IsNullOrWhiteSpace(SftpPassword))
            {
                ErrorMessage = "Host, username, and password are required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(SftpMoviesRemotePath) && string.IsNullOrWhiteSpace(SftpSeriesRemotePath) && string.IsNullOrWhiteSpace(SftpClipsRemotePath))
            {
                ErrorMessage = "Set a Movies remote path, a TV Shows remote path, a Clips remote path, or some combination.";
                return false;
            }

            if (!int.TryParse(SftpPort, out var port) || port is <= 0 or > 65535)
            {
                ErrorMessage = "Port must be a number between 1 and 65535.";
                return false;
            }

            profile = new ProfileSource
            {
                Name = Name.Trim(),
                SourceType = ProfileSourceType.Sftp,
                SftpHost = SftpHost.Trim(),
                SftpPort = port,
                SftpUsername = SftpUsername.Trim(),
                SftpPasswordEncrypted = CredentialProtector.Protect(SftpPassword),
                SftpMoviesRemotePath = string.IsNullOrWhiteSpace(SftpMoviesRemotePath) ? null : SftpMoviesRemotePath.Trim(),
                SftpSeriesRemotePath = string.IsNullOrWhiteSpace(SftpSeriesRemotePath) ? null : SftpSeriesRemotePath.Trim(),
                SftpClipsRemotePath = string.IsNullOrWhiteSpace(SftpClipsRemotePath) ? null : SftpClipsRemotePath.Trim()
            };

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
