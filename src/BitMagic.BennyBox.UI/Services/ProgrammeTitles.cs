using System.Text.RegularExpressions;

namespace BitMagic.BennyBox.UI.Services;

// Guide titles carry extras that get in the way of reading them aloud or looking them up on TMDB:
// the little superscript "New" marker (ᴺᵉʷ), a "New:" prefix, and bracketed notes like "(S2 E3)" or
// "[HD]". Both want just the programme's name.
public static partial class ProgrammeTitles
{
    public static string Clean(string title)
    {
        var cleaned = Superscripts().Replace(title, "");
        cleaned = NewPrefix().Replace(cleaned, "");
        cleaned = BracketedNotes().Replace(cleaned, "");
        cleaned = cleaned.Trim();
        return cleaned.Length > 0 ? cleaned : title.Trim();
    }

    [GeneratedRegex("[ʰ-˿ᴬ-ᵿ]+")]
    private static partial Regex Superscripts();

    [GeneratedRegex(@"^\s*New\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex NewPrefix();

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex BracketedNotes();
}
