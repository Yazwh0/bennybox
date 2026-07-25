using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Iptv.App.ViewModels;

namespace Iptv.App.Controls;

// Renders one channel's programme blocks for the current window as raw draw calls rather than
// a control per programme - with hundreds of channels visible via virtualization, a literal
// per-programme visual tree would be a catastrophic control count. See GuideView for the
// frozen-header/frozen-column scroll sync this depends on.
public class EpgRowControl : Control
{
    public const double RowHeight = 48;

    public static readonly StyledProperty<GuideRowViewModel?> RowProperty =
        AvaloniaProperty.Register<EpgRowControl, GuideRowViewModel?>(nameof(Row));

    public GuideRowViewModel? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    private static readonly IBrush ProgrammeFill = new SolidColorBrush(Color.FromRgb(45, 47, 56));
    private static readonly IBrush ProgrammeFillAlt = new SolidColorBrush(Color.FromRgb(55, 57, 68));
    private static readonly IBrush LiveProgrammeFill = new SolidColorBrush(Color.FromRgb(58, 47, 92));
    private static readonly IPen BlockBorderPen = new Pen(new SolidColorBrush(Color.FromRgb(25, 26, 31)), 1);
    private static readonly IBrush TextBrush = Brushes.White;
    private static readonly IPen NowLinePen = new Pen(Brushes.OrangeRed, 2);

    static EpgRowControl()
    {
        AffectsRender<EpgRowControl>(RowProperty);
        AffectsMeasure<EpgRowControl>(RowProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var row = Row;
        if (row is null)
        {
            return new Size(0, RowHeight);
        }

        var totalMinutes = (row.WindowEnd - row.WindowStart).TotalMinutes;
        return new Size(Math.Max(0, totalMinutes * row.PixelsPerMinute), RowHeight);
    }

    public override void Render(DrawingContext context)
    {
        var row = Row;
        if (row is null)
        {
            return;
        }

        var height = Bounds.Height;
        var width = Bounds.Width;

        var index = 0;
        foreach (var programme in row.Programmes)
        {
            var startX = Math.Max(0, MinutesToX(row, programme.StartUtc));
            var endX = Math.Min(width, MinutesToX(row, programme.EndUtc));
            if (endX <= startX)
            {
                index++;
                continue;
            }

            var isLive = programme.StartUtc <= row.NowUtc && row.NowUtc < programme.EndUtc;
            var rect = new Rect(startX, 2, endX - startX, height - 4);
            context.FillRectangle(isLive ? LiveProgrammeFill : index % 2 == 0 ? ProgrammeFill : ProgrammeFillAlt, rect);
            context.DrawRectangle(BlockBorderPen, rect);

            if (rect.Width > 20)
            {
                var text = new FormattedText(
                    programme.Title,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    12,
                    TextBrush)
                {
                    MaxTextWidth = Math.Max(0, rect.Width - 8),
                    MaxTextHeight = height - 4,
                    Trimming = TextTrimming.CharacterEllipsis
                };
                context.DrawText(text, new Point(rect.X + 4, rect.Y + 4));
            }

            index++;
        }

        var nowX = MinutesToX(row, row.NowUtc);
        if (nowX >= 0 && nowX <= width)
        {
            context.DrawLine(NowLinePen, new Point(nowX, 0), new Point(nowX, height));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var row = Row;
        if (row is null)
        {
            return;
        }

        var x = e.GetPosition(this).X;
        var clickedTime = row.WindowStart.AddMinutes(x / row.PixelsPerMinute);
        var programme = row.Programmes.FirstOrDefault(p => p.StartUtc <= clickedTime && clickedTime < p.EndUtc);

        if (programme is not null && programme.StartUtc <= row.NowUtc && row.NowUtc < programme.EndUtc)
        {
            row.TuneRequested?.Invoke(row.Channel);
        }
    }

    private static double MinutesToX(GuideRowViewModel row, DateTime timeUtc) =>
        (timeUtc - row.WindowStart).TotalMinutes * row.PixelsPerMinute;
}
