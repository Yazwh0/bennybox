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

    // A category with no keyword picture: the first few pictures inside it, shown as a 2x2 grid.
    public IReadOnlyList<SimpleImage>? Collage { get; init; }

    [ObservableProperty]
    private bool _isFavourite;

    public bool HasIcon => Icon is not null;
    public bool HasCollage => Collage is not null;
    public bool HasPicture => Icon is null && Collage is null;
    public bool HasBadge => Badge is not null;
    public bool HasProgress => Progress is not null;

    // Only real items (channel, film, show, video) can be made favourites.
    public bool CanFavourite => Payload is ChannelListItemViewModel or MovieListItemViewModel
        or SeriesListItemViewModel or ClipListItemViewModel;
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

// Tiles are laid out in rows (2 per row on a phone, 4 on a tablet) rather than in a wrap panel so the
// list can virtualise - a TV category can hold hundreds of channels.
public sealed record SimpleTileRow(IReadOnlyList<object> Tiles);
