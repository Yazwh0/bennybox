using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.ViewModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BitMagic.BennyBox.RemoteControl;

// Real, ships-in-Release phone remote control - distinct from DebugRemoteControlServer (which is
// #if DEBUG-only, localhost-only, and gives unrestricted access for development). This binds to the
// LAN so a phone on the same network can reach it, which is exactly why it needs to be gated by a
// session token and exposes only a small, purpose-built surface, not arbitrary ViewModel access.
//
// Uses Kestrel (via the Microsoft.AspNetCore.App framework reference), not HttpListener like the
// debug bridge - HttpListener's underlying http.sys driver refuses to bind any non-loopback address
// (including a specific LAN IP, not just wildcards) without either running elevated or a one-time
// `netsh http add urlacl` reservation. Kestrel binds via plain managed sockets, so it works for a
// normal, non-administrator user - the only realistic option for a consumer feature.
//
// Split across RemoteControlServer.{LiveTv,Guide,Series,Movies,Clips,Favorites}.cs by content area -
// this file is just the core (auth, lifecycle, routing table) plus the original playback-transport
// routes. Every content-area route deliberately reads repositories directly rather than driving
// LiveTvViewModel/GuideViewModel/SeriesViewModel/MoviesViewModel/ClipsViewModel/FavoritesViewModel -
// those are effectively singletons bound straight to the desktop screen, so searching from the phone
// would otherwise visibly hijack whatever's shown on the TV/desktop at that moment. Downloads has no
// remote equivalent - it's cancel/retry/delete management, not "browse and play" (playing a
// downloaded item already happens transparently through Movies/Series/Clips, see
// DownloadPlaybackResolver), so there was nothing here for it to mirror.
public sealed partial class RemoteControlServer : IAsyncDisposable
{
    private const int PreferredPort = 47819;

    // The desktop virtualizes lists of 15k+ rows in a native control; a phone browser rendering that
    // many DOM nodes would not be pleasant, so any browsing list route caps its results and reports
    // Truncated=true rather than shipping everything.
    private const int MaxListResults = 200;

    // Session-bound (default) means a fresh token every time the server starts, same as before this
    // setting existed - old QR codes/bookmarked URLs stop working the moment the app restarts, which
    // is the safer default for something that grants LAN access to playback control. Fixed persists
    // one token across restarts, for someone who'd rather bookmark the remote URL once and not
    // re-scan a QR code every launch, at the cost of that URL staying valid indefinitely (until
    // manually regenerated).
    private const string RemoteKeyFixedSettingKey = "RemoteKeyFixed";
    private const string RemoteKeyFixedTokenSettingKey = "RemoteKeyFixedToken";

    private readonly PlayerViewModel _player;
    private readonly IProfileRepository _profileRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IEpgRepository _epgRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly IMovieRepository _movieRepository;
    private readonly IClipRepository _clipRepository;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IWatchedItemRepository _watchedItemRepository;
    private readonly IWatchProgressRepository _watchProgressRepository;
    private readonly IReminderRepository _reminderRepository;
    private readonly SeriesImportService _seriesImportService;
    private readonly MovieImportService _movieImportService;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<RemoteControlServer> _logger;
    private WebApplication? _app;

    public int? Port { get; private set; }
    public Guid Token { get; private set; }
    public bool IsRunning => _app is not null;

    public RemoteControlServer(
        PlayerViewModel player,
        IProfileRepository profileRepository,
        IChannelRepository channelRepository,
        IEpgRepository epgRepository,
        ISeriesRepository seriesRepository,
        IMovieRepository movieRepository,
        IClipRepository clipRepository,
        IFavoriteRepository favoriteRepository,
        IWatchedItemRepository watchedItemRepository,
        IWatchProgressRepository watchProgressRepository,
        IReminderRepository reminderRepository,
        SeriesImportService seriesImportService,
        MovieImportService movieImportService,
        ISettingsStore settingsStore,
        ILogger<RemoteControlServer> logger)
    {
        _player = player;
        _profileRepository = profileRepository;
        _channelRepository = channelRepository;
        _epgRepository = epgRepository;
        _seriesRepository = seriesRepository;
        _movieRepository = movieRepository;
        _clipRepository = clipRepository;
        _favoriteRepository = favoriteRepository;
        _watchedItemRepository = watchedItemRepository;
        _watchProgressRepository = watchProgressRepository;
        _reminderRepository = reminderRepository;
        _seriesImportService = seriesImportService;
        _movieImportService = movieImportService;
        _settingsStore = settingsStore;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        Token = await IsFixedKeyModeAsync() ? await LoadOrCreateFixedTokenAsync() : Guid.NewGuid();

        if (!await TryStartOnPortAsync(PreferredPort))
        {
            await TryStartOnPortAsync(0); // 0 = let the OS pick any free port
        }
    }

    private async Task<bool> IsFixedKeyModeAsync() =>
        await _settingsStore.GetAsync(RemoteKeyFixedSettingKey) == "true";

    private async Task<Guid> LoadOrCreateFixedTokenAsync()
    {
        var saved = await _settingsStore.GetAsync(RemoteKeyFixedTokenSettingKey);
        if (saved is not null && Guid.TryParse(saved, out var parsed))
        {
            return parsed;
        }

        // First time fixed mode has actually been used - mint one and persist it so it's the same
        // token every subsequent start, not just this one.
        var generated = Guid.NewGuid();
        await _settingsStore.SetAsync(RemoteKeyFixedTokenSettingKey, generated.ToString());
        return generated;
    }

    private async Task<bool> TryStartOnPortAsync(int port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(o => o.ListenAnyIP(port));

        var app = builder.Build();
        MapRoutes(app);

        try
        {
            await app.StartAsync();
        }
        catch (Exception ex) when (port != 0)
        {
            _logger.LogWarning(ex, "Remote control server failed to bind port {Port}, falling back to an OS-assigned port", port);
            await app.DisposeAsync();
            return false;
        }

        _app = app;
        Port = new Uri(app.Urls.First()).Port;
        _logger.LogWarning("Remote control server listening on port {Port}", Port);
        return true;
    }

    private void MapRoutes(WebApplication app)
    {
        app.MapGet("/", HandleIndex);
        app.MapGet("/api/status", HandleStatus);
        app.MapPost("/api/command", HandleCommand);
        app.MapPost("/api/volume", HandleVolume);

        MapLiveTvRoutes(app);
        MapGuideRoutes(app);
        MapSeriesRoutes(app);
        MapMoviesRoutes(app);
        MapClipsRoutes(app);
        MapFavoritesRoutes(app);
        MapSearchRoutes(app);
    }

    public async Task RegenerateAsync()
    {
        var newToken = Guid.NewGuid();
        if (await IsFixedKeyModeAsync())
        {
            await _settingsStore.SetAsync(RemoteKeyFixedTokenSettingKey, newToken.ToString());
        }

        Token = newToken;
    }

    public async Task StopAsync()
    {
        if (_app is null)
        {
            return;
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
        Port = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private bool IsAuthorized(HttpRequest request)
    {
        var supplied = request.Query["token"].ToString();
        if (string.IsNullOrEmpty(supplied))
        {
            supplied = request.Headers["X-Remote-Token"].ToString();
        }

        return Guid.TryParse(supplied, out var parsed) && parsed == Token;
    }

    private IResult HandleIndex(HttpRequest request) =>
        IsAuthorized(request) ? Results.Content(RemoteControlPage.Build(Token), "text/html") : Results.StatusCode(401);

    private async Task<IResult> HandleStatus(HttpRequest request)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var status = await RunOnUiThreadAsync(() => new StatusResponse(
            _player.NowPlayingChannelName,
            _player.IsPlaying,
            _player.IsPaused,
            _player.CanPause,
            _player.IsSeekable,
            _player.PositionLabel,
            _player.DurationLabel,
            _player.Volume,
            _player.IsMuted));

        return Results.Json(status);
    }

    private async Task<IResult> HandleCommand(HttpRequest request, CommandRequest? body)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        if (body?.Command is null)
        {
            return Results.BadRequest();
        }

        await RunOnUiThreadAsync(() =>
        {
            switch (body.Command)
            {
                case "playpause":
                    if (_player.TogglePauseCommand.CanExecute(null))
                    {
                        _player.TogglePauseCommand.Execute(null);
                    }
                    break;
                case "stop":
                    _player.StopCommand.Execute(null);
                    break;
                case "skipforward":
                    if (_player.SkipForwardCommand.CanExecute(null))
                    {
                        _player.SkipForwardCommand.Execute(null);
                    }
                    break;
                case "skipback":
                    if (_player.SkipBackwardCommand.CanExecute(null))
                    {
                        _player.SkipBackwardCommand.Execute(null);
                    }
                    break;
                case "mute":
                    _player.ToggleMuteCommand.Execute(null);
                    break;
            }
        });

        return Results.Ok();
    }

    private async Task<IResult> HandleVolume(HttpRequest request, VolumeRequest? body)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        if (body is null)
        {
            return Results.BadRequest();
        }

        var clamped = Math.Clamp(body.Value, 0, 100);
        await RunOnUiThreadAsync(() => _player.Volume = clamped);
        return Results.Ok();
    }

    private static async Task<T> RunOnUiThreadAsync<T>(Func<T> action) => await Dispatcher.UIThread.InvokeAsync(action);
    private static async Task RunOnUiThreadAsync(Action action) => await Dispatcher.UIThread.InvokeAsync(action);

    private static (List<T> Items, bool Truncated) FilterAndCap<T>(IEnumerable<T> items, Func<T, bool> matches)
    {
        var matched = items.Where(matches).ToList();
        return matched.Count > MaxListResults
            ? (matched.Take(MaxListResults).ToList(), true)
            : (matched, false);
    }

    private sealed record CommandRequest(string? Command);
    private sealed record VolumeRequest(int Value);
    private sealed record IdRequest(Guid Id);
    private sealed record StatusResponse(
        string? NowPlaying, bool IsPlaying, bool IsPaused, bool CanPause, bool IsSeekable,
        string PositionLabel, string DurationLabel, int Volume, bool IsMuted);
}
