using CommunityToolkit.Mvvm.ComponentModel;
using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.ViewModels;

// Wraps a Download row - not itself persisted as an ObservableObject's fields (Download is a plain
// Core model), so Update() just swaps the reference and fires a single "everything may have changed"
// notification rather than mirroring every field into its own [ObservableProperty].
public partial class DownloadItemViewModel : ObservableObject
{
    public Download Download { get; private set; }

    public string Title => Download.Title;
    public string? CoverUrl => Download.CoverUrl;
    public bool IsInProgress => Download.Status is DownloadStatus.Queued or DownloadStatus.Downloading;
    public bool IsFailedOrCanceled => Download.Status is DownloadStatus.Failed or DownloadStatus.Canceled;
    public bool IsCompleted => Download.Status == DownloadStatus.Completed;

    public double ProgressFraction =>
        Download.TotalBytes is { } total && total > 0
            ? Math.Clamp((double)Download.BytesDownloaded / total, 0, 1)
            : 0;

    public string StatusLabel => Download.Status switch
    {
        DownloadStatus.Queued => "Queued",
        DownloadStatus.Downloading => Download.TotalBytes is { } total && total > 0
            ? $"{FormatBytes(Download.BytesDownloaded)} / {FormatBytes(total)} ({ProgressFraction:P0})"
            : $"{FormatBytes(Download.BytesDownloaded)} downloaded",
        DownloadStatus.Completed => "Downloaded",
        DownloadStatus.Failed => Download.ErrorMessage is { } msg ? $"Failed: {msg}" : "Failed",
        DownloadStatus.Canceled => "Canceled",
        _ => ""
    };

    public DownloadItemViewModel(Download download)
    {
        Download = download;
    }

    public void Update(Download download)
    {
        Download = download;
        OnPropertyChanged((string?)null);
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
