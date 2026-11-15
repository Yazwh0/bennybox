using System.Text.Json;
using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace BitMagic.BennyBox.UI.Services;

// Where a category lives: the same name can be a TV, film, show and video category at once.
public enum CategoryPlace
{
    Tv,
    Films,
    Shows,
    Videos,
}

public sealed record FavouriteCategory(CategoryPlace Place, string Name);

// Categories marked as favourites ("UK | Sport" on TV, say), so they can sit on simple mode's home
// screen as one tap to everything in them, and come first in the normal mode category picker. Kept by
// name in settings - categories have no stable id of their own across refreshes, and the name is what
// a person picked - which also lets them travel in a setup code (SharedSettings).
public sealed class CategoryFavourites(ISettingsStore settingsStore)
{
    public const string SettingKey = "FavouriteCategories";

    private List<FavouriteCategory> _items = [];
    private Task? _loading;

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    public IReadOnlyList<FavouriteCategory> Items => _items;

    public bool IsFavourite(CategoryPlace place, string name) =>
        _items.Any(f => f.Place == place && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // Adds or removes it, returning whether it's now a favourite.
    public bool Toggle(CategoryPlace place, string name)
    {
        var added = !IsFavourite(place, name);
        _items = added
            ? [.. _items, new FavouriteCategory(place, name)]
            : _items.Where(f => !(f.Place == place && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();
        Save();
        return added;
    }

    // The list as stored, for a setup code, and replacing it from one.
    public string Serialise() => JsonSerializer.Serialize(_items);

    public void Replace(string json)
    {
        _items = Parse(json);
        Save();
    }

    private void Save()
    {
        _ = settingsStore.SetAsync(SettingKey, Serialise());
        // Favourites and simple mode's home screen reload on this, as for any other favourite.
        WeakReferenceMessenger.Default.Send(new FavoritesUpdatedMessage());
    }

    private async Task LoadAsync() => _items = Parse(await settingsStore.GetAsync(SettingKey));

    private static List<FavouriteCategory> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<FavouriteCategory>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
