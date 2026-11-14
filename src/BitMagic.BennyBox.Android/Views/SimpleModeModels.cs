using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;

namespace BitMagic.BennyBox.Android.Views;

// What simple picture mode's screens are built from (see SimpleHomeView). Every screen is a list of
// headings and rows of same-sized tiles, so the layout never changes shape between screens.

// The top-level places on the simple home screen.
public enum SimpleDestination
{
    Favourites,
    Tv,
    Films,
    Shows,
    Videos,
}

// One tile: a picture (ImageUrl, with fallbacks) or, for places and categories, a big icon. Payload is
// what a tap acts on - a SimpleDestination, a SimpleCategory, or one of the normal app's item view
// models (channel, film, show, clip, "keep watching" entry).
public sealed class SimpleTile
{
    public required string Title { get; init; }
    public required object Payload { get; init; }
    public string? ImageUrl { get; init; }
    public string? FallbackImageUrl { get; init; }
    public string? TmdbFallbackTitle { get; init; }
    public FASymbol? Icon { get; init; }
    public double? Progress { get; init; }

    public bool HasIcon => Icon is not null;
    public bool HasPicture => Icon is null;
    public bool HasProgress => Progress is not null;
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

public sealed record SimpleCategory(SimpleDestination Destination, string Name);

public sealed record SimpleHeader(string Title, FASymbol Icon);

// Tiles are laid out in rows (2 per row on a phone, 4 on a tablet) rather than in a wrap panel so the
// list can virtualise - a TV category can hold hundreds of channels.
public sealed record SimpleTileRow(IReadOnlyList<object> Tiles);
