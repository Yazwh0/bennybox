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

    // Category pictures, picked from words in the provider's category names (SimplePictures.ForCategory).
    Sport,
    News,
    Kids,
    Family,
    Music,
    Comedy,
    Horror,
    Romance,
    SciFi,
    Nature,
    Action,
    Crime,
    Food,

    // Laid over a picture whose stream didn't play last time (StreamHealth).
    NotWorking,

    // On an episode that's been watched.
    Watched,

    // A channel's page: earlier programmes to watch again (catch-up), and ones still to come.
    WatchAgain,
    Later,

    // The player's message when this phone or tablet can't play a stream (4K, say): a phone with a line through it.
    NotOnThisDevice,

    // Take something off a list (Keep watching): a bin.
    Remove,
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

        [SimplePicture.Sport] = new(Geometry.Parse(
            "M19 5h-2V3H7v2H5c-1.1 0-2 .9-2 2v1c0 2.55 1.92 4.63 4.39 4.94.63 1.5 1.98 2.63 3.61 2.96V19H7v2h10v-2h-4v-3.1c1.63-.33 2.98-1.46 3.61-2.96C19.08 12.63 21 10.55 21 8V7c0-1.1-.9-2-2-2zM5 8V7h2v3.82C5.84 10.4 5 9.3 5 8zm14 0c0 1.3-.84 2.4-2 2.82V7h2v1z"),
            Color.Parse("#00897B"), White),
        [SimplePicture.News] = new(Geometry.Parse(
            "M3,4 H21 V20 H3 Z M5,6 V9 H19 V6 Z M5,11 V12.5 H11 V11 Z M5,14 V15.5 H11 V14 Z M5,17 V18 H11 V17 Z M13,11 V18 H19 V11 Z"),
            Color.Parse("#3949AB"), White),
        [SimplePicture.Kids] = new(Geometry.Parse(
            "M12,1.5 A6,7 0 1 1 11.99,1.5 Z M10.8,15.6 H13.2 L12.7,17 H11.3 Z M11.6,17 H12.4 V22.5 H11.6 Z"),
            Color.Parse("#EC407A"), White),
        [SimplePicture.Family] = new(Geometry.Parse(
            "M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z"),
            Color.Parse("#039BE5"), White),
        [SimplePicture.Music] = new(Geometry.Parse(
            "M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z"),
            Color.Parse("#8E24AA"), White),
        [SimplePicture.Comedy] = new(Geometry.Parse(
            "M12,2 A10,10 0 1 1 11.99,2 Z M8.5,7.5 A1.5,1.5 0 1 0 8.51,7.5 Z M15.5,7.5 A1.5,1.5 0 1 0 15.51,7.5 Z M7,13 H17 A5,5 0 0 1 7,13 Z"),
            Color.Parse("#F9A825"), White),
        [SimplePicture.Horror] = new(Geometry.Parse(
            "M12,2 A7,7 0 0 0 5,9 V21 L7.33,19 L9.67,21 L12,19 L14.33,21 L16.67,19 L19,21 V9 A7,7 0 0 0 12,2 Z M9.5,8 A1.5,2 0 1 0 9.51,8 Z M14.5,8 A1.5,2 0 1 0 14.51,8 Z"),
            Color.Parse("#424242"), White),
        [SimplePicture.Romance] = new(Geometry.Parse(
            "M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z"),
            Color.Parse("#D81B60"), White),
        [SimplePicture.SciFi] = new(Geometry.Parse(
            "M12,1 C15,4 16,8 16,12 L18,15 V19 L15.5,17 H8.5 L6,19 V15 L8,12 C8,8 9,4 12,1 Z M12,7.2 A1.8,1.8 0 1 0 12.01,7.2 Z M10,18 H14 L12,22.5 Z"),
            Color.Parse("#5E35B1"), White),
        [SimplePicture.Nature] = new(Geometry.Parse(
            "M12,2 L18,10 H15 L20,17 H13 V22 H11 V17 H4 L9,10 H6 Z"),
            Color.Parse("#558B2F"), White),
        [SimplePicture.Action] = new(Geometry.Parse(
            "M11 21h-1l1-7H7.5c-.58 0-.57-.32-.38-.66.19-.34.05-.08.07-.12C8.48 10.94 10.42 7.54 13 3h1l-1 7h3.5c.49 0 .56.33.47.51l-.07.15C12.96 17.55 11 21 11 21z"),
            Color.Parse("#F4511E"), White),
        [SimplePicture.Crime] = new(Geometry.Parse(
            "M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z"),
            Color.Parse("#6D4C41"), White),
        [SimplePicture.Food] = new(Geometry.Parse(
            "M11 9H9V2H7v7H5V2H3v7c0 2.12 1.66 3.84 3.75 3.97V22h2.5v-9.03C11.34 12.84 13 11.12 13 9V2h-2v7zm5-3v8h2.5v8H21V2c-2.76 0-5 2.24-5 4z"),
            Color.Parse("#FF7043"), White),
        [SimplePicture.NotWorking] = new(Geometry.Parse(
            "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zM4 12c0-4.42 3.58-8 8-8 1.85 0 3.55.63 4.9 1.69L5.69 16.9C4.63 15.55 4 13.85 4 12zm8 8c-1.85 0-3.55-.63-4.9-1.69L18.31 7.1C19.37 8.45 20 10.15 20 12c0 4.42-3.58 8-8 8z"),
            Color.Parse("#C62828"), White),
        [SimplePicture.Watched] = new(Geometry.Parse("M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"),
            Color.Parse("#2E7D32"), White),
        [SimplePicture.WatchAgain] = new(Geometry.Parse(
            "M12 5V1L7 6l5 5V7c3.31 0 6 2.69 6 6s-2.69 6-6 6-6-2.69-6-6H4c0 4.42 3.58 8 8 8s8-3.58 8-8-3.58-8-8-8z"),
            Color.Parse("#00838F"), White),
        [SimplePicture.Later] = new(Geometry.Parse(
            "M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67z"),
            Color.Parse("#78909C"), White),
        [SimplePicture.NotOnThisDevice] = new(Geometry.Parse(
            "M17 5v8.61l2 2V3c0-1.1-.9-2-2-2H7c-.71 0-1.33.37-1.68.93L8.39 5H17zM1.49 3.76L5 7.27V21c0 1.1.9 2 2 2h10c1.1 0 1.99-.89 2-1.98l1.51 1.51 1.41-1.41L2.9 2.35 1.49 3.76zM7 9.27L15.73 18H7V9.27z"),
            Color.Parse("#C62828"), White),
        [SimplePicture.Remove] = new(Geometry.Parse("M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"),
            Color.Parse("#616161"), White),
    };

    public static Info Get(SimplePicture picture) => All[picture];

    // Words that give a category a fixed picture. A category matches when any word in its name starts
    // with one of these ("Sports", "UK | Football" -> Sport); the first matching row wins, so more
    // specific rows go first. Anything unmatched gets a collage of its first few pictures instead.
    private static readonly (SimplePicture Picture, string[] Words)[] CategoryWords =
    [
        (SimplePicture.Kids, ["kid", "child", "cartoon", "animat", "junior", "toddler", "baby", "disney", "nick"]),
        (SimplePicture.News, ["news", "weather"]),
        (SimplePicture.Sport, ["sport", "football", "soccer", "f1", "formula", "golf", "tennis", "cricket", "rugby",
            "boxing", "ufc", "wwe", "wrestl", "racing", "motorsport", "nfl", "nba", "nhl", "mlb", "premier", "league", "olympic", "cup"]),
        (SimplePicture.Music, ["music", "radio", "concert", "mtv"]),
        (SimplePicture.Comedy, ["comedy", "comedies", "sitcom", "funny", "stand"]),
        (SimplePicture.Horror, ["horror", "halloween", "scary", "zombie"]),
        (SimplePicture.Romance, ["romance", "romantic", "love"]),
        (SimplePicture.SciFi, ["sci", "fantasy", "space", "superhero"]),
        (SimplePicture.Nature, ["documentar", "doc", "nature", "wildlife", "animal", "discovery", "history", "travel", "science"]),
        (SimplePicture.Crime, ["crime", "thriller", "mystery", "detective", "police", "suspense"]),
        (SimplePicture.Action, ["action", "adventure", "war", "western", "martial"]),
        (SimplePicture.Family, ["family", "families"]),
        (SimplePicture.Food, ["food", "cook", "kitchen", "chef"]),
    ];

    public static SimplePicture? ForCategory(string name)
    {
        var words = name.ToLowerInvariant()
            .Split((char[])[' ', '|', '-', '/', '&', ',', ':', '(', ')', '[', ']', '.', '_', '+'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var (picture, keys) in CategoryWords)
        {
            if (words.Any(word => keys.Any(word.StartsWith)))
            {
                return picture;
            }
        }

        return null;
    }
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
