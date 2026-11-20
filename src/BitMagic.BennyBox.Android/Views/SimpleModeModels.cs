using System;
using System.Collections.Generic;
using BitMagic.BennyBox.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BitMagic.BennyBox.Android.Views;

// What simple picture mode's screens are built from (see SimpleHomeView). Every screen is a list of
// headings and rows of same-sized tiles, so the layout never changes shape between screens.

// The places you can browse from the simple home screen.
public enum SimpleDestination
{
    Tv,
    Films,
    Shows,
    Videos,
}

// One tile: a picture (ImageUrl, with fallbacks) or, for places and categories, a big SimplePicture.
// Payload is what a tap acts on - a SimpleDestination, a SimpleCategory, or one of the normal app's item
// view models (channel, film, show, clip, "keep watching" entry). Items also carry Badge, their type's
// picture in the corner, and a star when they're a favourite.
public sealed partial class SimpleTile : ObservableObject
{
    public required string Title { get; init; }
    public required object Payload { get; init; }
    public string? ImageUrl { get; init; }
    public string? FallbackImageUrl { get; init; }
    public string? TmdbFallbackTitle { get; init; }
    public SimplePicture? Icon { get; init; }
    public SimplePicture? Badge { get; init; }
    public double? Progress { get; init; }

    // Shown big on the tile when there's no picture (an episode's number); otherwise its initials.
    public string? BigText { get; init; }
    public string Placeholder => BigText ?? SimpleModeConverters.InitialsOf(Title);

    // A programme on a channel's page: the time sits over the corner of its picture (and is the big
    // text instead when there's no picture).
    public bool ShowsTimeOverPicture { get; init; }

    // What press-and-hold reads aloud, when it should say more than the label ("Episode 3, ...").
    public string? SpokenText { get; init; }
    public string Speech => CannotPlayHere ? $"{SpokenText ?? Title}. This won't play on this device." : SpokenText ?? Title;

    // An episode that's been watched: a green tick on the tile, and the picture faded.
    public bool IsWatched { get; init; }

    // A category with no keyword picture: the first few pictures inside it, shown as a 2x2 grid.
    public IReadOnlyList<SimpleImage>? Collage { get; init; }

    [ObservableProperty]
    private bool _isFavourite;

    // Didn't play last time (StreamHealth): the picture is greyed out with a red "no" sign on it, and
    // it goes to the end of the grid. It can still be tapped - it may work again.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PictureOpacity))]
    private bool _didNotWork;

    // This device can't play it (4K/UHD on a phone, say - StreamHealth.CannotPlayHere): greyed out like
    // DidNotWork, but with a crossed-out phone instead, since trying again won't help.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PictureOpacity))]
    private bool _cannotPlayHere;

    public bool IsUnplayable => DidNotWork || CannotPlayHere;

    // A programme that hasn't been on yet (on a channel's page): faded, and a tap reads it out rather than playing.
    public bool IsLater { get; init; }

    public double PictureOpacity => DidNotWork || CannotPlayHere ? 0.35 : IsWatched || IsLater ? 0.5 : 1;

    // The stream this tile plays, for matching a StreamHealthChangedMessage.
    public string? StreamUrl => Payload switch
    {
        ChannelListItemViewModel channel => channel.Channel.StreamUrl,
        MovieListItemViewModel movie => movie.Movie.StreamUrl,
        ClipListItemViewModel clip => clip.Clip.StreamUrl,
        _ => null,
    };

    public bool HasIcon => Icon is not null;
    public bool HasCollage => Collage is not null;
    public bool HasPicture => Icon is null && Collage is null;
    public bool HasBadge => Badge is not null;
    public bool HasProgress => Progress is not null;

    // Channels, films, shows, videos and categories can be made favourites (not episodes or places).
    public bool CanFavourite => Payload is ChannelListItemViewModel or MovieListItemViewModel
        or SeriesListItemViewModel or ClipListItemViewModel or SimpleCategory;

    // The big picture on the hold panel for something without a picture of its own (a category).
    public SimplePicture? PanelPicture => Icon ?? (HasCollage ? Badge : null);
    public bool HasPanelPicture => PanelPicture is not null;

    // The hold panel's first button: a category opens rather than plays.
    public string PrimaryLabel => Payload is SimpleCategory ? "Open" : "Play";
}

// The "Normal mode" tile on the home screen: held (not tapped) for a couple of seconds, with Progress
// filling as it's held, so it's easy to find but hard to hit by accident.
public sealed partial class SimpleExitTile : ObservableObject
{
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isHolding;
}

public sealed record SimpleImage(string? Url, string? FallbackUrl, string? TmdbFallbackTitle);

public sealed record SimpleCategory(SimpleDestination Destination, string Name);

public sealed record SimpleHeader(string Title, SimplePicture Icon);

// The big "Play next" button at the top of a show's screen, a whole row wide: which episode it will
// play, so it's clear without reading the list below.
public sealed record SimplePlayNextTile(EpisodeListItemViewModel Episode, string Label, string Detail, string EpisodeTitle);

// The big "Watch now" button at the top of a channel's page, a whole row wide: the channel live, with
// what's on and how far through it is when the guide knows.
public sealed record SimpleWatchNowTile(ChannelListItemViewModel Channel, string? NowTitle, double? Progress, string? NowImageUrl = null)
{
    // What's on now's picture: the guide's own, or TMDB by title (see ChannelLogoImage).
    public string? NowSearchTitle => NowTitle is null ? null : BitMagic.BennyBox.UI.Services.ProgrammeTitles.Clean(NowTitle);
    public bool HasNowTitle => NowTitle is not null;
    public bool HasProgress => Progress is not null;
}

// The big "Carry on watching" button at the top of a Keep watching item's page, a whole row wide.
public sealed record SimpleResumeTile(WatchProgressItemViewModel Item)
{
    public string? Left => Item.Progress.DurationSeconds > 0 && Item.Progress.PositionSeconds < Item.Progress.DurationSeconds
        ? (Item.Progress.DurationSeconds - Item.Progress.PositionSeconds) switch
        {
            < 90 => "Nearly finished",
            var seconds when seconds < 3600 => $"{Math.Round(seconds / 60)} minutes left",
            var seconds => $"{(int)(seconds / 3600)} hr {Math.Round(seconds % 3600 / 60)} min left",
        }
        : null;

    public bool HasLeft => Left is not null;
}

// The Remove tile on a Keep watching item's page: takes it off Keep watching.
public sealed record SimpleRemoveProgress(WatchProgressItemViewModel Item);

// Tiles are laid out in rows (2 per row on a phone, 4 on a tablet) rather than in a wrap panel so the
// list can virtualise - a TV category can hold hundreds of channels.
public sealed record SimpleTileRow(IReadOnlyList<object> Tiles);
