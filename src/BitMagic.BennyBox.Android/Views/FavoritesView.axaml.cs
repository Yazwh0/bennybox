using BitMagic.BennyBox.Android.Views;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox.Views;

// See FavoritesView.axaml and MixedGridView.
public partial class FavoritesView : MixedGridView
{
    public FavoritesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is FavoritesViewModel viewModel)
            {
                AttachRows(FavouriteRows, viewModel.Rows);
            }
        };
    }

    protected override void Activate(object item)
    {
        if (DataContext is not FavoritesViewModel viewModel)
        {
            return;
        }

        switch (item)
        {
            case WatchProgressItemViewModel progress:
                viewModel.PlayWatchProgressCommand.Execute(progress);
                break;
            case SeriesListItemViewModel series:
                viewModel.SelectSeriesCommand.Execute(series);
                break;
            case MovieListItemViewModel movie:
                viewModel.SelectMovieCommand.Execute(movie);
                break;
            case ClipListItemViewModel clip:
                viewModel.SelectClipCommand.Execute(clip);
                break;
        }
    }
}
