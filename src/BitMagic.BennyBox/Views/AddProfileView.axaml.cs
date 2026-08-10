using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox.Views;

public partial class AddProfileView : UserControl
{
    public AddProfileView() => InitializeComponent();

    private async void OnBrowseMoviesFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddProfileViewModel viewModel &&
            await PickFolderAsync("Select Movies Folder") is { } path)
        {
            viewModel.LocalMoviesPath = path;
        }
    }

    private async void OnBrowseSeriesFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddProfileViewModel viewModel &&
            await PickFolderAsync("Select TV Shows Folder") is { } path)
        {
            viewModel.LocalSeriesPath = path;
        }
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return null;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath : null;
    }
}
