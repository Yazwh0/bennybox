using Avalonia.Controls;
using BitMagic.BennyBox.Android.Views;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox.Views;

// See SearchView.axaml and MixedGridView.
public partial class SearchView : MixedGridView
{
    public SearchView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SearchViewModel viewModel)
            {
                AttachRows(ResultRows, viewModel.Rows);
            }
        };
    }

    protected override Control? FilterArea => FilterPanel;

    protected override void Activate(object item)
    {
        if (DataContext is not SearchViewModel viewModel)
        {
            return;
        }

        switch (item)
        {
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
