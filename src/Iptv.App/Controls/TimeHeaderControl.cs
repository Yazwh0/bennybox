using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Iptv.App.Controls;

public class TimeHeaderControl : Control
{
    public const double HeaderHeight = 32;

    public static readonly StyledProperty<DateTime> WindowStartProperty =
        AvaloniaProperty.Register<TimeHeaderControl, DateTime>(nameof(WindowStart));

    public static readonly StyledProperty<DateTime> WindowEndProperty =
        AvaloniaProperty.Register<TimeHeaderControl, DateTime>(nameof(WindowEnd));

    public static readonly StyledProperty<double> PixelsPerMinuteProperty =
        AvaloniaProperty.Register<TimeHeaderControl, double>(nameof(PixelsPerMinute), 4);

    public DateTime WindowStart
    {
        get => GetValue(WindowStartProperty);
        set => SetValue(WindowStartProperty, value);
    }

    public DateTime WindowEnd
    {
        get => GetValue(WindowEndProperty);
        set => SetValue(WindowEndProperty, value);
    }

    public double PixelsPerMinute
    {
        get => GetValue(PixelsPerMinuteProperty);
        set => SetValue(PixelsPerMinuteProperty, value);
    }

    private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(25, 26, 31));
    private static readonly IPen TickPen = new Pen(new SolidColorBrush(Color.FromRgb(70, 72, 80)), 1);
    private static readonly IBrush TextBrush = Brushes.White;

    static TimeHeaderControl()
    {
        AffectsRender<TimeHeaderControl>(WindowStartProperty, WindowEndProperty, PixelsPerMinuteProperty);
        AffectsMeasure<TimeHeaderControl>(WindowStartProperty, WindowEndProperty, PixelsPerMinuteProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var totalMinutes = (WindowEnd - WindowStart).TotalMinutes;
        return new Size(Math.Max(0, totalMinutes * PixelsPerMinute), HeaderHeight);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        context.FillRectangle(Background, new Rect(0, 0, width, height));

        if (WindowEnd <= WindowStart || PixelsPerMinute <= 0)
        {
            return;
        }

        var localStart = WindowStart.ToLocalTime();
        var localEnd = WindowEnd.ToLocalTime();
        var firstTick = new DateTime(localStart.Year, localStart.Month, localStart.Day, localStart.Hour, localStart.Minute < 30 ? 0 : 30, 0);
        if (firstTick < localStart)
        {
            firstTick = firstTick.AddMinutes(30);
        }

        for (var t = firstTick; t < localEnd; t = t.AddMinutes(30))
        {
            var minutesFromStart = (t.ToUniversalTime() - WindowStart).TotalMinutes;
            var x = minutesFromStart * PixelsPerMinute;
            context.DrawLine(TickPen, new Point(x, height - 8), new Point(x, height));

            var text = new FormattedText(
                t.ToString("h:mm tt", CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                11,
                TextBrush);
            context.DrawText(text, new Point(x + 4, 6));
        }
    }
}
