using System.Text.Json;
using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.Messages;
using BitMagic.BennyBox.ViewModels;
using CommunityToolkit.Mvvm.Messaging;

namespace BitMagic.BennyBox.UI.Services;

// Remembers which streams didn't work the last time they were played, so lists can flag them and put
// them at the bottom instead of leaving you to find out by tapping each one. Sources (Xtream in
// particular) give no health information of their own, and testing streams up front would use up the
// account's connection limit, so this only learns from real playback: PlayerViewModel records a
// failure when a stream errors or times out before showing a picture, and clears it as soon as one
// plays.
//
// Only failures are stored (a list of stream URLs in settings), so it stays small. Current is there
// for the list item view models, which are created all over the place and only need to ask.
public sealed class StreamHealth
{
    private const string SettingKey = "FailedStreams";
    private const string CannotPlaySettingKey = "UnplayableStreams";
    private const int MaxRemembered = 5000;

    private readonly ISettingsStore _settingsStore;

    // Replaced, never changed in place, so a background sort can read them while a playback result is
    // being recorded on the UI thread. A stream is in at most one of the two.
    private HashSet<string> _failed = [];

    // Streams this device can't decode (4K/UHD on a phone, say - see IPlayerEngine.DeviceCannotPlay).
    // Kept apart from _failed because the stream itself is fine and the device is the problem, which
    // won't change by trying again later; stored on this device only, like everything in settings.
    private HashSet<string> _cannotPlay = [];
    private Task? _loading;

    public StreamHealth(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        Current = this;
    }

    public static StreamHealth? Current { get; private set; }

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    public bool DidNotWork(string? streamUrl) => streamUrl is not null && _failed.Contains(streamUrl);

    public bool CannotPlayHere(string? streamUrl) => streamUrl is not null && _cannotPlay.Contains(streamUrl);

    // Either mark goes to the end of a list.
    public bool IsUnplayable(string? streamUrl) => DidNotWork(streamUrl) || CannotPlayHere(streamUrl);

    // A picture showed: clears both marks. Didn't: marks it as not working (and not a device problem).
    public void Record(string streamUrl, bool worked) => Update(streamUrl, failed: !worked, cannotPlay: false);

    public void RecordCannotPlay(string streamUrl) => Update(streamUrl, failed: false, cannotPlay: true);

    private void Update(string streamUrl, bool failed, bool cannotPlay)
    {
        var failedChanged = Change(ref _failed, streamUrl, failed);
        var cannotPlayChanged = Change(ref _cannotPlay, streamUrl, cannotPlay);
        if (failedChanged)
        {
            _ = _settingsStore.SetAsync(SettingKey, JsonSerializer.Serialize(_failed));
        }

        if (cannotPlayChanged)
        {
            _ = _settingsStore.SetAsync(CannotPlaySettingKey, JsonSerializer.Serialize(_cannotPlay));
        }

        if (failedChanged || cannotPlayChanged)
        {
            WeakReferenceMessenger.Default.Send(new StreamHealthChangedMessage(streamUrl));
        }
    }

    private static bool Change(ref HashSet<string> set, string streamUrl, bool include)
    {
        if (include == set.Contains(streamUrl) || (include && set.Count >= MaxRemembered))
        {
            return false;
        }

        var updated = new HashSet<string>(set);
        if (include)
        {
            updated.Add(streamUrl);
        }
        else
        {
            updated.Remove(streamUrl);
        }

        set = updated;
        return true;
    }

    // For a page's StreamHealthChangedMessage handler: re-reads DidNotWork on whichever rows play that
    // stream, so their warning appears or disappears straight away.
    public static void Refresh(IEnumerable<object> rows, string streamUrl)
    {
        foreach (var row in rows)
        {
            switch (row)
            {
                case ChannelListItemViewModel channel when channel.Channel.StreamUrl == streamUrl:
                    channel.RefreshHealth();
                    break;
                case MovieListItemViewModel movie when movie.Movie.StreamUrl == streamUrl:
                    movie.RefreshHealth();
                    break;
                case ClipListItemViewModel clip when clip.Clip.StreamUrl == streamUrl:
                    clip.RefreshHealth();
                    break;
                case EpisodeListItemViewModel episode when episode.Episode.StreamUrl == streamUrl:
                    episode.RefreshHealth();
                    break;
            }
        }
    }

    private async Task LoadAsync()
    {
        _failed = await LoadSetAsync(SettingKey);
        _cannotPlay = await LoadSetAsync(CannotPlaySettingKey);
    }

    private async Task<HashSet<string>> LoadSetAsync(string key)
    {
        try
        {
            var json = await _settingsStore.GetAsync(key);
            if (!string.IsNullOrEmpty(json) && JsonSerializer.Deserialize<List<string>>(json) is { } urls)
            {
                return [.. urls];
            }
        }
        catch (JsonException)
        {
            // A damaged setting just means starting again with nothing remembered.
        }

        return [];
    }
}
