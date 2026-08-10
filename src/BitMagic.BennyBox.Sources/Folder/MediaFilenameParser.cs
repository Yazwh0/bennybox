using System.Text.RegularExpressions;

namespace BitMagic.BennyBox.Sources.Folder;

// Filename/foldername parsing - deliberately the LAST resort, not the primary metadata source (see
// the plan discussion: NFO sidecars and embedded tags come first; this only produces a Title/Year or
// Show/Season/Episode identifier so an item can appear in the library at all when nothing richer is
// available). Patterns cover the common conventions (Kodi/Jellyfin-style "Title (Year)" and
// "SxxEyy"), not every scene-release naming scheme.
public static partial class MediaFilenameParser
{
    [GeneratedRegex(@"(?<year>19\d{2}|20\d{2})")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"[Ss](?<season>\d{1,2})[Ee](?<episode>\d{1,3})")]
    private static partial Regex EpisodeRegex();

    // Some libraries spell "Season NN Episode NN" out in full rather than using the compact "SxxEyy"
    // scene tag - e.g. "Shooting Stars Season 01 Episode 04.avi".
    [GeneratedRegex(@"season\s*(?<season>\d{1,2})\s*episode\s*(?<episode>\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex SpelledOutEpisodeRegex();

    [GeneratedRegex(@"season\s*(?<season>\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonFolderRegex();

    [GeneratedRegex(@"[Ss](?<season>\d{1,2})")]
    private static partial Regex SeasonOnlyRegex();

    // Matches a folder name that is ONLY a season indicator - e.g. "Season 01" - with no show title
    // embedded in it at all (some seedbox libraries organize a show as bare "Season NN" folders,
    // relying entirely on the episode filenames inside to carry the show name).
    [GeneratedRegex(@"^\s*season\s*\d{1,2}\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BareSeasonLabelRegex();

    public static bool IsBareSeasonLabel(string title) => BareSeasonLabelRegex().IsMatch(title);

    public record MovieNameGuess(string Title, int? Year);

    public static MovieNameGuess ParseMovieName(string nameWithoutExtension)
    {
        var normalized = Normalize(nameWithoutExtension);
        var yearMatch = YearRegex().Match(normalized);
        if (!yearMatch.Success)
        {
            return new MovieNameGuess(normalized.Trim(), null);
        }

        var title = normalized[..yearMatch.Index].Trim(' ', '-', '(', '[', '.');
        return new MovieNameGuess(string.IsNullOrWhiteSpace(title) ? normalized.Trim() : title, int.Parse(yearMatch.Groups["year"].Value));
    }

    public record EpisodeNameGuess(int? Season, int? Episode, string Title);

    // seasonFolderHint is the enclosing "Season NN" folder name (if any) - used when the season isn't
    // in the filename itself, which some libraries omit (relying purely on folder structure instead).
    public static EpisodeNameGuess ParseEpisodeName(string nameWithoutExtension, string? seasonFolderHint)
    {
        var normalized = Normalize(nameWithoutExtension);
        var match = EpisodeRegex().Match(normalized);
        var spelledOutMatch = match.Success ? null : SpelledOutEpisodeRegex().Match(normalized);
        var tag = match.Success ? match : spelledOutMatch is { Success: true } ? spelledOutMatch : null;

        int? season = tag is not null ? int.Parse(tag.Groups["season"].Value) : ParseSeasonFolder(seasonFolderHint);
        int? episode = tag is not null ? int.Parse(tag.Groups["episode"].Value) : null;

        var title = tag is not null
            ? normalized[(tag.Index + tag.Length)..].Trim(' ', '-', '.')
            : normalized.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            title = season is not null && episode is not null
                ? $"Season {season} Episode {episode}"
                : normalized.Trim();
        }

        return new EpisodeNameGuess(season, episode, title);
    }

    public static int? ParseSeasonFolder(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        var match = SeasonFolderRegex().Match(folderName);
        return match.Success ? int.Parse(match.Groups["season"].Value) : null;
    }

    public record ShowFolderGuess(string Title, int? Season);

    // Scene/seedbox TV libraries commonly ship one top-level folder PER SEASON rather than one per
    // show - e.g. "Fresh.Meat.S04.1080p.AMZN.WEB-DL.DD+2.0.x264-Cinefeel" is season 4 of "Fresh Meat",
    // not a show called that whole string. Extracts the clean show title (everything before the
    // season tag) and the season number, so multiple such folders can be grouped back into one show
    // instead of each becoming its own separate series - see FolderMediaScanner.ScanSeriesAsync.
    // Falls back to treating the whole (normalized) folder name as the title with no season when it
    // doesn't carry a recognizable season tag at all - the original "one folder per show" convention.
    public static ShowFolderGuess ParseShowFolderName(string folderName)
    {
        var normalized = Normalize(folderName);

        foreach (var match in new[]
        {
            EpisodeRegex().Match(normalized),
            SpelledOutEpisodeRegex().Match(normalized),
            SeasonOnlyRegex().Match(normalized),
            SeasonFolderRegex().Match(normalized)
        })
        {
            if (!match.Success)
            {
                continue;
            }

            var title = normalized[..match.Index].Trim(' ', '-', '(', '[', '.');
            return new ShowFolderGuess(string.IsNullOrWhiteSpace(title) ? normalized.Trim() : title, int.Parse(match.Groups["season"].Value));
        }

        return new ShowFolderGuess(normalized.Trim(), null);
    }

    // Scene-release names lean on dots/underscores as word separators ("Movie.Title.2020.1080p") -
    // turning those into spaces first makes both regexes above and the fallback "whole name is the
    // title" path read far closer to how a human would title the file.
    private static string Normalize(string name) => name.Replace('.', ' ').Replace('_', ' ');
}
