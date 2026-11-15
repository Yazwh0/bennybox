using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using BitMagic.BennyBox.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BitMagic.BennyBox.Android.Views;

public sealed record CategoryChoice(string Name, SimplePicture Picture, bool IsFavourite, bool CanFavourite);

// See CategoryPickerSheet.axaml.
public partial class CategoryPickerSheet : UserControl
{
    public static readonly StyledProperty<IEnumerable?> CategoriesProperty =
        AvaloniaProperty.Register<CategoryPickerSheet, IEnumerable?>(nameof(Categories));

    public static readonly StyledProperty<string?> FilterTextProperty =
        AvaloniaProperty.Register<CategoryPickerSheet, string?>(nameof(FilterText), defaultBindingMode: BindingMode.TwoWay);

    // The picture for a category with no keyword picture of its own: the page's place (Films, Shows...).
    public static readonly StyledProperty<SimplePicture> FallbackPictureProperty =
        AvaloniaProperty.Register<CategoryPickerSheet, SimplePicture>(nameof(FallbackPicture), SimplePicture.Shows);

    public IEnumerable? Categories
    {
        get => GetValue(CategoriesProperty);
        set => SetValue(CategoriesProperty, value);
    }

    public string? FilterText
    {
        get => GetValue(FilterTextProperty);
        set => SetValue(FilterTextProperty, value);
    }

    public SimplePicture FallbackPicture
    {
        get => GetValue(FallbackPictureProperty);
        set => SetValue(FallbackPictureProperty, value);
    }

    public event EventHandler<string>? Picked;

    public CategoryPickerSheet() => InitializeComponent();

    public void Open()
    {
        _ = App.Services!.GetRequiredService<CategoryFavourites>().EnsureLoadedAsync().ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh));
        FilterText = "";
        List.SelectedItem = null;
        IsVisible = true;
    }

    public void Close()
    {
        IsVisible = false;
        FilterText = "";
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CategoriesProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldList)
            {
                oldList.CollectionChanged -= OnCategoriesChanged;
            }

            if (change.NewValue is INotifyCollectionChanged newList)
            {
                newList.CollectionChanged += OnCategoriesChanged;
            }

            Refresh();
        }
        else if (change.Property == FallbackPictureProperty)
        {
            Refresh();
        }
    }

    private void OnCategoriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    // "All ..." first, then favourite categories, then the rest in the page's own order.
    private void Refresh()
    {
        var favourites = App.Services!.GetRequiredService<CategoryFavourites>();
        var place = Place;
        List.ItemsSource = (Categories?.OfType<string>() ?? [])
            .Select(name =>
            {
                var isAll = name.StartsWith("All ", StringComparison.Ordinal);
                return new CategoryChoice(name,
                    isAll ? SimplePicture.Home : SimplePictures.ForCategory(name) ?? FallbackPicture,
                    !isAll && favourites.IsFavourite(place, name), !isAll);
            })
            .OrderBy(c => c.CanFavourite ? (c.IsFavourite ? 1 : 2) : 0)
            .ToList();
    }

    // Which place this page's categories belong to, from the picture it uses for them.
    private CategoryPlace Place => FallbackPicture switch
    {
        SimplePicture.Tv => CategoryPlace.Tv,
        SimplePicture.Films => CategoryPlace.Films,
        SimplePicture.Videos => CategoryPlace.Videos,
        _ => CategoryPlace.Shows,
    };

    // The star on a row: add or remove that category as a favourite, without picking it.
    private void OnFavouriteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: CategoryChoice choice })
        {
            App.Services!.GetRequiredService<CategoryFavourites>().Toggle(Place, choice.Name);
            Refresh();
        }

        e.Handled = true;
    }

    private void OnPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is CategoryChoice choice)
        {
            Close();
            Picked?.Invoke(this, choice.Name);
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, Scrim))
        {
            Close();
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
