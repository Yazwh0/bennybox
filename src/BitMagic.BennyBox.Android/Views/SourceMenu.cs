using Avalonia.Controls;

namespace BitMagic.BennyBox.Android.Views;

// The Source chip's menu on the browse pages: a short list (usually two or three sources), so a
// plain menu under the chip rather than a sheet.
public static class SourceMenu
{
    public static void Show(Control anchor, IEnumerable<string> sources, Action<string> select)
    {
        var menu = new MenuFlyout();
        foreach (var source in sources)
        {
            var item = new MenuItem { Header = source };
            item.Click += (_, _) => select(source);
            menu.Items.Add(item);
        }

        menu.ShowAt(anchor);
    }
}
