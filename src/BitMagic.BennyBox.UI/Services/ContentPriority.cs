using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.UI.Services;

// The current "Show first" prefixes (see PriorityOrder), shared by every page that sorts categories or
// channels. Loaded from settings on first use, and updated by SettingsViewModel when the setting
// changes (which then reloads the pages via ChannelsUpdatedMessage).
public sealed class ContentPriority(ISettingsStore settingsStore)
{
    private IReadOnlyList<string> _prefixes = [];
    private Task? _loading;

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    public void Set(string? setting) => _prefixes = PriorityOrder.Parse(setting);

    // A ranking function over a snapshot of the prefixes, safe to use from a background sort.
    public Func<string?, int> Ranker()
    {
        var prefixes = _prefixes;
        return name => PriorityOrder.Rank(prefixes, name);
    }

    private async Task LoadAsync() => Set(await settingsStore.GetAsync(PriorityOrder.SettingKey));
}
