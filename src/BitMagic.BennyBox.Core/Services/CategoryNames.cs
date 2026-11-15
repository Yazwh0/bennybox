namespace BitMagic.BennyBox.Core.Services;

// Public playlists (iptv-org and friends) list several categories in one group-title, separated by
// semicolons ("Animation;General;Kids"). A channel sits in one category here, and showing the raw text
// made a mess of near-identical combinations, so a channel goes under the first one listed, passing over
// "General" when there's something more specific. Used when importing a playlist, and again when the
// channel list is built so playlists imported before this still come out tidy.
public static class CategoryNames
{
    public static string Tidy(string? groupTitle)
    {
        var parts = (groupTitle ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return "Uncategorized";
        }

        return parts.FirstOrDefault(p => !p.Equals("General", StringComparison.OrdinalIgnoreCase)) ?? parts[0];
    }
}
