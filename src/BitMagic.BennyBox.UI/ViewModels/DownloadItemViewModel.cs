using CommunityToolkit.Mvvm.ComponentModel;
using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.ViewModels;

// Wraps a Download row. The displayed values are stored rather than computed on demand: DownloadManager
// mutates its Download from a background thread, so Update() needs the previous values to compare with,
// and only the bindings whose value actually changed get notified.
public partial class DownloadItemViewModel : ObservableObject
{
    public Download Download { get; private set; }

    public string Title => Download.Title;
    public string? CoverUrl => Download.CoverUrl;

    [ObservableProperty]
    private bool _isInProgress;

    [ObservableProperty]
    private bool _isFailedOrCanceled;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    private string _statusLabel = "";

    public DownloadItemViewModel(Download download)
    {
        Download = download;
        Update(download);
    }

    public void Update(Download download)
    {
        Download = download;

        var status = download.Status;
        var bytes = download.BytesDownloaded;
        var totalBytes = download.TotalBytes;
        var fraction = totalBytes is { } known && known > 0
            ? Math.Clamp((double)bytes / known, 0, 1)
            : 0;

        IsInProgress = status is DownloadStatus.Queued or DownloadStatus.Downloading;
        IsFailedOrCanceled = status is DownloadStatus.Failed or DownloadStatus.Canceled;
        IsCompleted = status == DownloadStatus.Completed;
        ProgressFraction = fraction;
        StatusLabel = status switch
        {
            DownloadStatus.Queued => "Queued",
            DownloadStatus.Downloading => totalBytes is { } total && total > 0
                ? $"{FormatBytes(bytes)} / {FormatBytes(total)} ({fraction:P0})"
                : $"{FormatBytes(bytes)} downloaded",
            DownloadStatus.Completed => "Downloaded",
            DownloadStatus.Failed => download.ErrorMessage is { } msg ? $"Failed: {msg}" : "Failed",
            DownloadStatus.Canceled => "Canceled",
            _ => ""
        };
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }
        return $"{value:0.#} {units[unitIndex]}";
    }
}
