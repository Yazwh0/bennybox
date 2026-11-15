using Avalonia;
using Avalonia.Controls;

namespace BitMagic.BennyBox.Android.Views;

// The search box and filter chips at the top of the browse pages (Live TV, Shows, Films, Videos). On a
// phone the chips go underneath the search box; from Material's 600dp breakpoint (tablets, phones
// turned sideways) there's room for them beside it, on the same line. The page's filter Grid has the
// search box in column 0, the chips' StackPanel ("FilterChips") and the refresh button in column 2.
// The chips bind their height to the search box's, so they line up in either layout.
public static class FilterLayout
{
    private const double WideFrom = 600;
    private const double MaxPhoneWidth = 720;
    private const double MaxWideWidth = 1000;

    public static void Apply(Grid panel, Control chips, double width)
    {
        var wide = width >= WideFrom;
        Grid.SetRow(chips, wide ? 0 : 1);
        Grid.SetColumn(chips, wide ? 1 : 0);
        Grid.SetColumnSpan(chips, wide ? 1 : 3);
        chips.Margin = wide ? new Thickness(8, 0, 0, 0) : new Thickness(0, 4, 0, 0);
        chips.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        panel.Width = Math.Min(wide ? MaxWideWidth : MaxPhoneWidth, Math.Max(0, width));
    }
}
