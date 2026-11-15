using BitMagic.BennyBox.Android.Views;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox.Views;

// See DownloadsView.axaml and MixedGridView.
public partial class DownloadsView : MixedGridView
{
    public DownloadsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is DownloadsViewModel viewModel)
            {
                AttachRows(DownloadRows, viewModel.Rows);
            }
        };
    }

    // Only a finished download can be opened.
    protected override void Activate(object item)
    {
        if (item is DownloadItemViewModel { IsCompleted: true } download && DataContext is DownloadsViewModel viewModel)
        {
            viewModel.OpenCommand.Execute(download);
        }
    }
}
