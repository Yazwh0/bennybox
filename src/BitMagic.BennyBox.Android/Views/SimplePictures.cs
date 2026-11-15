using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace BitMagic.BennyBox.Android.Views;

// Simple picture mode's fixed set of pictures. Each place has one shape and one colour that never
// change (TV is always a blue TV set, Films a red clapperboard...), wherever it appears: home tiles,
// headings, the title bar, category tiles and the small badge on each picture. Someone who finds words
// hard can learn "blue TV = channels" and find things by picture alone.
public enum SimplePicture
{
    Tv,
    Films,
    Shows,
    Videos,
    Favourite,
    NotFavourite,
    KeepWatching,
    Home,
    Settings,
    Back,
    Play,
    Pause,
    Quieter,
    Louder,
    Big,
    Small,
}

public static class SimplePictures
{
    public sealed record Info(Geometry Shape, Color Background, Color Foreground);

    private static readonly Color White = Colors.White;
    private static readonly Color Slate = Color.Parse("#37474F");
    private static readonly Color Star = Color.Parse("#FFCA28");

    // Shapes are on a 24x24 grid (Material icons, filled where there's a choice), except the TV, which
    // is drawn here with an aerial so it doesn't read as a plain screen.
    private static readonly Dictionary<SimplePicture, Info> All = new()
    {
        [SimplePicture.Tv] = new(Geometry.Parse(
            "M7.4,1.6 L12,5.6 L16.6,1.6 L17.4,2.4 L13.4,7 H10.6 L6.6,2.4 Z " +
            "M3,7 H21 A2,2 0 0 1 23,9 V19 A2,2 0 0 1 21,21 H3 A2,2 0 0 1 1,19 V9 A2,2 0 0 1 3,7 Z M3.5,9.5 V18.5 H20.5 V9.5 Z"),
            Color.Parse("#1E88E5"), White),
        [SimplePicture.Films] = new(Geometry.Parse(
            "M18 4l2 4h-3l-2-4h-2l2 4h-3l-2-4H8l2 4H7L5 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V4h-4z"),
            Color.Parse("#E53935"), White),
        [SimplePicture.Shows] = new(Geometry.Parse(
            "M4 6H2v14c0 1.1.9 2 2 2h14v-2H4V6zm16-4H8c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-8 12.5v-9l6 4.5-6 4.5z"),
            Color.Parse("#43A047"), White),
        [SimplePicture.Videos] = new(Geometry.Parse(
            "M17 10.5V7c0-.55-.45-1-1-1H4c-.55 0-1 .45-1 1v10c0 .55.45 1 1 1h12c.55 0 1-.45 1-1v-3.5l4 4v-11l-4 4z"),
            Color.Parse("#FB8C00"), White),
        [SimplePicture.Favourite] = new(Geometry.Parse(
            "M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z"),
            Slate, Star),
        [SimplePicture.NotFavourite] = new(Geometry.Parse(
            "M22 9.24l-7.19-.62L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27 18.18 21l-1.63-7.03L22 9.24zM12 15.4l-3.76 2.27 1-4.28-3.32-2.88 4.38-.38L12 6.1l1.71 4.04 4.38.38-3.32 2.88 1 4.28L12 15.4z"),
            Slate, Star),
        [SimplePicture.KeepWatching] = new(Geometry.Parse(
            "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 14.5v-9l6 4.5-6 4.5z"),
            Color.Parse("#8E24AA"), White),
        [SimplePicture.Home] = new(Geometry.Parse("M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z"),
            Color.Parse("#546E7A"), White),
        [SimplePicture.Settings] = new(Geometry.Parse(
            "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"),
            Color.Parse("#546E7A"), White),
        [SimplePicture.Back] = new(Geometry.Parse("M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20v-2z"),
            Color.Parse("#546E7A"), White),
        [SimplePicture.Play] = new(Geometry.Parse("M8 5v14l11-7z"), Color.Parse("#43A047"), White),
        [SimplePicture.Pause] = new(Geometry.Parse("M6 19h4V5H6v14zm8-14v14h4V5h-4z"), Color.Parse("#546E7A"), White),
        [SimplePicture.Quieter] = new(Geometry.Parse(
            "M18.5 12c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM5 9v6h4l5 5V4L9 9H5z"),
            Color.Parse("#546E7A"), White),
        [SimplePicture.Louder] = new(Geometry.Parse(
            "M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z"),
            Color.Parse("#546E7A"), White),
        [SimplePicture.Big] = new(Geometry.Parse(
            "M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z"),
            Color.Parse("#546E7A"), White),
        [SimplePicture.Small] = new(Geometry.Parse(
            "M5 16h3v3h2v-5H5v2zm3-8H5v2h5V5H8v3zm6 11h2v-3h3v-2h-5v5zm2-11V5h-2v5h5V8h-3z"),
            Color.Parse("#546E7A"), White),
    };

    public static Info Get(SimplePicture picture) => All[picture];
}

// Draws one SimplePicture. Backed (the default) is the picture's white shape on its own coloured
// rounded square; unbacked is just the shape in the inherited text colour, for buttons that already
// have their own background (the player bar, Back).
public sealed class SimplePictureIcon : Control
{
    public static readonly StyledProperty<SimplePicture> PictureProperty =
        AvaloniaProperty.Register<SimplePictureIcon, SimplePicture>(nameof(Picture));

    public static readonly StyledProperty<bool> BackedProperty =
        AvaloniaProperty.Register<SimplePictureIcon, bool>(nameof(Backed), true);

    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<SimplePictureIcon, double>(nameof(CornerRadius), 10);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<SimplePictureIcon>();

    static SimplePictureIcon()
    {
        AffectsRender<SimplePictureIcon>(PictureProperty, BackedProperty, CornerRadiusProperty, ForegroundProperty);
    }

    public SimplePicture Picture
    {
        get => GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    public bool Backed
    {
        get => GetValue(BackedProperty);
        set => SetValue(BackedProperty, value);
    }

    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 24 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 24 : availableSize.Height);

    public override void Render(DrawingContext context)
    {
        var info = SimplePictures.Get(Picture);
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        IBrush fill;
        double padding;
        if (Backed)
        {
            context.DrawRectangle(new SolidColorBrush(info.Background), null, bounds, CornerRadius, CornerRadius);
            fill = new SolidColorBrush(info.Foreground);
            padding = Math.Min(bounds.Width, bounds.Height) * 0.2;
        }
        else
        {
            fill = Foreground ?? Brushes.White;
            padding = 0;
        }

        var size = Math.Min(bounds.Width, bounds.Height) - padding * 2;
        var scale = size / 24;
        var x = (bounds.Width - size) / 2;
        var y = (bounds.Height - size) / 2;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y)))
        {
            context.DrawGeometry(fill, null, info.Shape);
        }
    }
}
