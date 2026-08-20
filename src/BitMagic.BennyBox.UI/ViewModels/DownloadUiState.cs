namespace BitMagic.BennyBox.ViewModels;

// Per-row download affordance state - distinct from Core's DownloadStatus (which only exists once a
// Download row has actually been created). NotDownloaded also covers "no Downloads profile yet" and
// "a previous download of this failed/was canceled" - either way, the button offers to (re)download.
public enum DownloadUiState
{
    NotDownloaded,
    Queued,
    Downloading,
    Completed
}
