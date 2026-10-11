using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.Sources.Jellyfin;
using BitMagic.BennyBox.Sources.Xtream;

namespace BitMagic.BennyBox.ViewModels;

public partial class AddProfileViewModel : ViewModelBase
{
    private readonly XtreamClient _xtreamClient;
    private readonly JellyfinClient _jellyfinClient;
    private readonly ICredentialProtector _credentialProtector;

    public AddProfileViewModel(XtreamClient xtreamClient, JellyfinClient jellyfinClient, ICredentialProtector credentialProtector)
    {
        _xtreamClient = xtreamClient;
        _jellyfinClient = jellyfinClient;
        _credentialProtector = credentialProtector;
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
        XtreamPassword = _credentialProtector.Unprotect(profile.XtreamPasswordEncrypted) ?? string.Empty;
        LocalMoviesPath = profile.LocalMoviesPath ?? string.Empty;
        LocalSeriesPath = profile.LocalSeriesPath ?? string.Empty;
        LocalClipsPath = profile.LocalClipsPath ?? string.Empty;
        SftpHost = profile.SftpHost ?? string.Empty;
        SftpPort = profile.SftpPort?.ToString() ?? "22";
        SftpUsername = profile.SftpUsername ?? string.Empty;
        SftpPassword = _credentialProtector.Unprotect(profile.SftpPasswordEncrypted) ?? string.Empty;
        SftpMoviesRemotePath = profile.SftpMoviesRemotePath ?? string.Empty;
        SftpSeriesRemotePath = profile.SftpSeriesRemotePath ?? string.Empty;
        SftpClipsRemotePath = profile.SftpClipsRemotePath ?? string.Empty;
        JellyfinServerUrl = profile.JellyfinServerUrl ?? string.Empty;
        JellyfinUsername = profile.JellyfinUsername ?? string.Empty;
        JellyfinPassword = _credentialProtector.Unprotect(profile.JellyfinPasswordEncrypted) ?? string.Empty;
        JellyfinApiKey = _credentialProtector.Unprotect(profile.JellyfinApiKeyEncrypted) ?? string.Empty;
        JellyfinLibraries.Clear();
        foreach (var mapping in JellyfinLibraryMapping.Parse(profile.JellyfinLibraryMappings))
        {
            JellyfinLibraries.Add(new JellyfinLibraryRowViewModel(mapping.Id, mapping.Name, mapping.Target));
        }
    }

    // Filled from the server by Test Connection (keeping any choice already made for a library), or from
    // the saved mappings when editing. Empty means "default every library by its Jellyfin type".
    public System.Collections.ObjectModel.ObservableCollection<JellyfinLibraryRowViewModel> JellyfinLibraries { get; } = [];

    // Only for a brand new profile: the Id it will be saved with, decided up front so Test Connection
    // can log in to Jellyfin under the same device id the saved profile will use (see
    // JellyfinClient.DeviceIdFor), and that login's token can be kept rather than thrown away.
    private readonly Guid _newProfileId = Guid.NewGuid();

    private Guid ProfileIdForSession => _editingProfile?.Id ?? _newProfileId;

    // The last successful Jellyfin Test Connection, and the credentials it was made with - reused by
    // TryBuildProfile only if those credentials haven't been edited since.
    private (string ServerUrl, string Username, string Password, JellyfinAuthResult Result)? _jellyfinTestLogin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsM3uSelected))]
    [NotifyPropertyChangedFor(nameof(IsXtreamSelected))]
    [NotifyPropertyChangedFor(nameof(IsLocalFolderSelected))]
    [NotifyPropertyChangedFor(nameof(IsSftpSelected))]
    [NotifyPropertyChangedFor(nameof(IsJellyfinSelected))]
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

    public bool IsJellyfinSelected
    {
        get => SelectedSourceType == ProfileSourceType.Jellyfin;
        set
        {
            if (value)
            {
                SelectedSourceType = ProfileSourceType.Jellyfin;
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
    private string _jellyfinServerUrl = string.Empty;

    [ObservableProperty]
    private string _jellyfinUsername = string.Empty;

    [ObservableProperty]
    private string _jellyfinPassword = string.Empty;

    // Used instead of the password when set - see ProfileSource.JellyfinApiKeyEncrypted.
    [ObservableProperty]
    private string _jellyfinApiKey = string.Empty;

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
            if (SelectedSourceType == ProfileSourceType.Jellyfin)
            {
                await TestJellyfinConnectionAsync();
                return;
            }

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

    private async Task TestJellyfinConnectionAsync()
    {
        _jellyfinTestLogin = null;
        if (!TryNormaliseJellyfinServerUrl(out var serverUrl))
        {
            TestResultMessage = "Connection failed: enter a valid server URL.";
            return;
        }

        var username = JellyfinUsername.Trim();
        var deviceId = JellyfinClient.DeviceIdFor(ProfileIdForSession);
        string connectedAs;
        string userId;
        string token;
        if (!string.IsNullOrWhiteSpace(JellyfinApiKey))
        {
            token = JellyfinApiKey.Trim();
            var user = await _jellyfinClient.ResolveUserAsync(serverUrl, token, username, deviceId);
            connectedAs = user.Name ?? username;
            userId = user.Id;
        }
        else
        {
            if (username.Length == 0)
            {
                TestResultMessage = "Connection failed: enter a username and password, or an API key.";
                return;
            }

            var password = JellyfinPassword;
            var result = await _jellyfinClient.AuthenticateAsync(serverUrl, username, password, deviceId);
            _jellyfinTestLogin = (serverUrl, username, password, result);
            connectedAs = result.UserName ?? username;
            userId = result.UserId;
            token = result.AccessToken;
        }

        // Rebuild the list from what the server has now, keeping any choice already made (in this dialog,
        // or saved on the profile) for libraries that still exist.
        var libraries = await _jellyfinClient.GetLibrariesAsync(serverUrl, userId, token, deviceId);
        var existing = JellyfinLibraries.ToDictionary(l => l.Id, l => l.SelectedOption.Target);
        JellyfinLibraries.Clear();
        foreach (var library in libraries)
        {
            var target = existing.TryGetValue(library.Id, out var chosen) ? chosen : JellyfinLibraryMapping.DefaultTarget(library.CollectionType);
            JellyfinLibraries.Add(new JellyfinLibraryRowViewModel(library.Id, library.Name ?? "Library", target));
        }

        var info = await _jellyfinClient.GetPublicSystemInfoAsync(serverUrl);
        TestSucceeded = true;
        TestResultMessage = info?.ServerName is { Length: > 0 } serverName
            ? $"Connected to {serverName} (Jellyfin {info.Version}) as {connectedAs}."
            : $"Connected as {connectedAs}.";
    }

    partial void OnSelectedSourceTypeChanged(ProfileSourceType value)
    {
        // Xtream and Jellyfin share the one Test Connection result - don't show one's result under the other.
        TestResultMessage = null;
        TestSucceeded = false;
    }

    // Accepts "jellyfin.local:8096" as well as a full URL - Jellyfin's own clients do, and a bare
    // host:port is how the server's dashboard usually presents its address.
    private bool TryNormaliseJellyfinServerUrl(out string serverUrl)
    {
        serverUrl = JellyfinServerUrl.Trim().TrimEnd('/');
        if (serverUrl.Length > 0 && !serverUrl.Contains("://", StringComparison.Ordinal))
        {
            serverUrl = $"http://{serverUrl}";
        }

        return Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
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
                SftpPasswordEncrypted = _credentialProtector.Protect(SftpPassword),
                SftpMoviesRemotePath = string.IsNullOrWhiteSpace(SftpMoviesRemotePath) ? null : SftpMoviesRemotePath.Trim(),
                SftpSeriesRemotePath = string.IsNullOrWhiteSpace(SftpSeriesRemotePath) ? null : SftpSeriesRemotePath.Trim(),
                SftpClipsRemotePath = string.IsNullOrWhiteSpace(SftpClipsRemotePath) ? null : SftpClipsRemotePath.Trim()
            };

            ApplyEditingIdentity(profile);
            return true;
        }

        if (SelectedSourceType == ProfileSourceType.Jellyfin)
        {
            if (!TryNormaliseJellyfinServerUrl(out var jellyfinServerUrl))
            {
                ErrorMessage = "A valid server URL is required.";
                return false;
            }

            var apiKey = JellyfinApiKey.Trim();
            if (apiKey.Length == 0 && string.IsNullOrWhiteSpace(JellyfinUsername))
            {
                ErrorMessage = "Enter a username and password, or an API key.";
                return false;
            }

            var username = JellyfinUsername.Trim();
            profile = new ProfileSource
            {
                Name = Name.Trim(),
                SourceType = ProfileSourceType.Jellyfin,
                JellyfinServerUrl = jellyfinServerUrl,
                JellyfinUsername = username.Length == 0 ? null : username,
                JellyfinPasswordEncrypted = _credentialProtector.Protect(JellyfinPassword),
                JellyfinApiKeyEncrypted = _credentialProtector.Protect(apiKey),
                JellyfinLibraryMappings = JellyfinLibraryMapping.Serialize(JellyfinLibraries.Select(l => l.ToMapping()).ToList()),
                EpgSourceType = EpgSourceType.JellyfinEmbedded
            };

            if (apiKey.Length > 0)
            {
                // The user id is looked up from the key on first import (JellyfinClient.GetSessionAsync) -
                // only worth carrying over if nothing that decides which user it is has changed.
                if (_editingProfile is { } editingWithKey && editingWithKey.JellyfinServerUrl == jellyfinServerUrl &&
                    editingWithKey.JellyfinUsername == profile.JellyfinUsername && _credentialProtector.Unprotect(editingWithKey.JellyfinApiKeyEncrypted) == apiKey)
                {
                    profile.JellyfinUserId = editingWithKey.JellyfinUserId;
                }
            }
            // Carry a login over rather than forcing a new one on the first import - a new login
            // revokes the old token, breaking stream URLs already stored for an edited profile. Left
            // unset (log in on first import) if the credentials changed since that login was made.
            else if (_jellyfinTestLogin is { } test && test.ServerUrl == jellyfinServerUrl && test.Username == username && test.Password == JellyfinPassword)
            {
                profile.JellyfinUserId = test.Result.UserId;
                profile.JellyfinAccessTokenEncrypted = _credentialProtector.Protect(test.Result.AccessToken);
            }
            else if (_editingProfile is { } editing && editing.JellyfinServerUrl == jellyfinServerUrl && editing.JellyfinUsername == username &&
                     _credentialProtector.Unprotect(editing.JellyfinPasswordEncrypted) == JellyfinPassword)
            {
                profile.JellyfinUserId = editing.JellyfinUserId;
                profile.JellyfinAccessTokenEncrypted = editing.JellyfinAccessTokenEncrypted;
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
            XtreamPasswordEncrypted = _credentialProtector.Protect(XtreamPassword),
            EpgSourceType = EpgSourceType.XtreamEmbedded
        };

        ApplyEditingIdentity(profile);
        return true;
    }

    private void ApplyEditingIdentity(ProfileSource profile)
    {
        if (_editingProfile is null)
        {
            profile.Id = _newProfileId;
            return;
        }

        profile.Id = _editingProfile.Id;
        profile.SortOrder = _editingProfile.SortOrder;
        profile.IsEnabled = _editingProfile.IsEnabled;
    }
}
