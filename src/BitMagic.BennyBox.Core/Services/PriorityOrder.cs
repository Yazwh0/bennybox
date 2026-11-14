namespace BitMagic.BennyBox.Core.Services;

// The "Show first" setting: names (categories, channels) starting with one of these prefixes sort to
// the top, in the order the prefixes are listed - e.g. "UK, US" puts "UK | Sport" and "UK: BBC One"
// before "US | News", and both before everything else. Matching ignores case.
public static class PriorityOrder
{
    public const string SettingKey = "PriorityPrefixes";

    public static IReadOnlyList<string> Parse(string? setting) =>
        (setting ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // 0 for the first prefix, 1 for the second..., prefixes.Count for names matching none, so a plain
    // OrderBy(Rank).ThenBy(name) gives the wanted order.
    public static int Rank(IReadOnlyList<string> prefixes, string? name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            var trimmed = name.TrimStart();
            for (var i = 0; i < prefixes.Count; i++)
            {
                if (trimmed.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return prefixes.Count;
    }
}
