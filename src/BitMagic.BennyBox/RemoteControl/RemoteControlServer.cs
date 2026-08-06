using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using BitMagic.BennyBox.ViewModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BitMagic.BennyBox.RemoteControl;

// Real, ships-in-Release phone remote control - distinct from DebugRemoteControlServer (which is
// #if DEBUG-only, localhost-only, and gives unrestricted access for development). This binds to the
// LAN so a phone on the same network can reach it, which is exactly why it needs to be gated by a
// session token and exposes only a small, purpose-built surface (playback control), not arbitrary
// ViewModel access.
//
// Uses Kestrel (via the Microsoft.AspNetCore.App framework reference), not HttpListener like the
// debug bridge - HttpListener's underlying http.sys driver refuses to bind any non-loopback address
// (including a specific LAN IP, not just wildcards) without either running elevated or a one-time
// `netsh http add urlacl` reservation. Kestrel binds via plain managed sockets, so it works for a
// normal, non-administrator user - the only realistic option for a consumer feature.
public sealed class RemoteControlServer : IAsyncDisposable
{
    private const int PreferredPort = 47819;

    private readonly PlayerViewModel _player;
    private readonly ILogger _logger;
    private WebApplication? _app;

    public int? Port { get; private set; }
    public Guid Token { get; private set; }
    public bool IsRunning => _app is not null;

    public RemoteControlServer(PlayerViewModel player, ILogger<RemoteControlServer> logger)
    {
        _player = player;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        Token = Guid.NewGuid();

        if (!await TryStartOnPortAsync(PreferredPort))
        {
            await TryStartOnPortAsync(0); // 0 = let the OS pick any free port
        }
    }

    private async Task<bool> TryStartOnPortAsync(int port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(o => o.ListenAnyIP(port));

        var app = builder.Build();
        app.MapGet("/", HandleIndex);
        app.MapGet("/api/status", HandleStatus);
        app.MapPost("/api/command", HandleCommand);
        app.MapPost("/api/volume", HandleVolume);

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

    public void Regenerate() => Token = Guid.NewGuid();

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

    private async Task<IResult> HandleCommand(HttpRequest request)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var body = await request.ReadFromJsonAsync<CommandRequest>();
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

    private async Task<IResult> HandleVolume(HttpRequest request)
    {
        if (!IsAuthorized(request))
        {
            return Results.StatusCode(401);
        }

        var body = await request.ReadFromJsonAsync<VolumeRequest>();
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

    private sealed record CommandRequest(string? Command);
    private sealed record VolumeRequest(int Value);
    private sealed record StatusResponse(
        string? NowPlaying, bool IsPlaying, bool IsPaused, bool CanPause, bool IsSeekable,
        string PositionLabel, string DurationLabel, int Volume, bool IsMuted);
}
