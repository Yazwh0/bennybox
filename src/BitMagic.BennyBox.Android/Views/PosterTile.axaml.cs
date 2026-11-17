using Avalonia;
using Avalonia.Controls;

namespace BitMagic.BennyBox.Android.Views;

// See PosterTile.axaml.
public partial class PosterTile : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<PosterTile, string?>(nameof(Title));
    public static readonly StyledProperty<string?> SubtitleProperty = AvaloniaProperty.Register<PosterTile, string?>(nameof(Subtitle));
    public static readonly StyledProperty<string?> ImageUrlProperty = AvaloniaProperty.Register<PosterTile, string?>(nameof(ImageUrl));
    public static readonly StyledProperty<double> PosterHeightProperty = AvaloniaProperty.Register<PosterTile, double>(nameof(PosterHeight), 165);
    public static readonly StyledProperty<bool> IsFavouriteProperty = AvaloniaProperty.Register<PosterTile, bool>(nameof(IsFavourite));
    public static readonly StyledProperty<bool> DidNotWorkProperty = AvaloniaProperty.Register<PosterTile, bool>(nameof(DidNotWork));
    public static readonly StyledProperty<bool> CannotPlayHereProperty = AvaloniaProperty.Register<PosterTile, bool>(nameof(CannotPlayHere));
    public static readonly StyledProperty<double?> ProgressProperty = AvaloniaProperty.Register<PosterTile, double?>(nameof(Progress));

    public PosterTile() => InitializeComponent();

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string? ImageUrl { get => GetValue(ImageUrlProperty); set => SetValue(ImageUrlProperty, value); }
    public double PosterHeight { get => GetValue(PosterHeightProperty); set => SetValue(PosterHeightProperty, value); }
    public bool IsFavourite { get => GetValue(IsFavouriteProperty); set => SetValue(IsFavouriteProperty, value); }
    public bool DidNotWork { get => GetValue(DidNotWorkProperty); set => SetValue(DidNotWorkProperty, value); }
    public bool CannotPlayHere { get => GetValue(CannotPlayHereProperty); set => SetValue(CannotPlayHereProperty, value); }

    // 0-1 for a bar along the bottom of the poster (part-watched, downloading); null for none.
    public double? Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
}
