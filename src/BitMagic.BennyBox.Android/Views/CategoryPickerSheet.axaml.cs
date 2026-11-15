using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BitMagic.BennyBox.Android.Views;

public sealed record CategoryChoice(string Name, SimplePicture Picture);

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

    private void Refresh() =>
        List.ItemsSource = (Categories?.OfType<string>() ?? []).Select(name => new CategoryChoice(name,
            name.StartsWith("All ", StringComparison.Ordinal) ? SimplePicture.Home
            : SimplePictures.ForCategory(name) ?? FallbackPicture)).ToList();

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
