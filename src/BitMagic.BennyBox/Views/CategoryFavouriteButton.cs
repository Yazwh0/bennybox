using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BitMagic.BennyBox.Messages;
using BitMagic.BennyBox.UI.Services;
using CommunityToolkit.Mvvm.Messaging;

namespace BitMagic.BennyBox.Views;

// The star beside a page's category drop-down: makes the chosen category a favourite (or not). Favourite
// categories are listed first and starred in the drop-down (CategoryFavouriteConverters), sit on the
// phone's simple mode home screen, and travel in a setup code - so they can be picked here and sent to a
// phone. Disabled while "All ..." is chosen, which isn't a category.
public sealed class CategoryFavouriteButton : Button
{
    public static readonly StyledProperty<string?> CategoryProperty =
        AvaloniaProperty.Register<CategoryFavouriteButton, string?>(nameof(Category));

    public static readonly StyledProperty<CategoryPlace> PlaceProperty =
        AvaloniaProperty.Register<CategoryFavouriteButton, CategoryPlace>(nameof(Place));

    // Styled as an ordinary Button, so the glass-* classes apply.
    protected override Type StyleKeyOverride => typeof(Button);

    public CategoryFavouriteButton()
    {
        WeakReferenceMessenger.Default.Register<FavoritesUpdatedMessage>(this, (_, _) => UpdateLook());
        _ = LoadAsync();
    }

    public string? Category
    {
        get => GetValue(CategoryProperty);
        set => SetValue(CategoryProperty, value);
    }

    public CategoryPlace Place
    {
        get => GetValue(PlaceProperty);
        set => SetValue(PlaceProperty, value);
    }

    private bool IsRealCategory => Category is { Length: > 0 } name && !name.StartsWith("All ", StringComparison.Ordinal);

    private async Task LoadAsync()
    {
        if (CategoryFavourites.Current is { } favourites)
        {
            await favourites.EnsureLoadedAsync();
        }

        UpdateLook();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CategoryProperty || change.Property == PlaceProperty)
        {
            UpdateLook();
        }
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (IsRealCategory && CategoryFavourites.Current is { } favourites)
        {
            favourites.Toggle(Place, Category!);
        }
    }

    private void UpdateLook()
    {
        var isFavourite = IsRealCategory && CategoryFavourites.Current?.IsFavourite(Place, Category!) == true;
        Content = isFavourite ? "★" : "☆";
        IsEnabled = IsRealCategory;
        ToolTip.SetTip(this, isFavourite ? "Remove this category from favourites" : "Make this category a favourite");
    }
}
