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
    private const int MaxRemembered = 5000;

    private readonly ISettingsStore _settingsStore;

    // Replaced, never changed in place, so a background sort can read it while a playback result is
    // being recorded on the UI thread.
    private HashSet<string> _failed = [];
    private Task? _loading;

    public StreamHealth(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        Current = this;
    }

    public static StreamHealth? Current { get; private set; }

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    public bool DidNotWork(string? streamUrl) => streamUrl is not null && _failed.Contains(streamUrl);

    public void Record(string streamUrl, bool worked)
    {
        if (worked != _failed.Contains(streamUrl))
        {
            return;
        }

        var updated = new HashSet<string>(_failed);
        if (worked)
        {
            updated.Remove(streamUrl);
        }
        else if (updated.Count < MaxRemembered)
        {
            updated.Add(streamUrl);
        }
        else
        {
            return;
        }

        _failed = updated;
        _ = _settingsStore.SetAsync(SettingKey, JsonSerializer.Serialize(updated));
        WeakReferenceMessenger.Default.Send(new StreamHealthChangedMessage(streamUrl));
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
        try
        {
            var json = await _settingsStore.GetAsync(SettingKey);
            if (!string.IsNullOrEmpty(json) && JsonSerializer.Deserialize<List<string>>(json) is { } urls)
            {
                _failed = [.. urls];
            }
        }
        catch (JsonException)
        {
            // A damaged setting just means starting again with nothing remembered.
        }
    }
}
