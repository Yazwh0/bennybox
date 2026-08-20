using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.Messages;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BitMagic.BennyBox.ViewModels;

// Lists every Download, split into "In Progress" (Queued/Downloading) and "Completed" (Completed/
// Failed/Canceled) sections - same flattened-header-row pattern every other page uses (see
// CategoryHeaderRow). Stays live via DownloadManager.DownloadChanged rather than polling or requiring
// a manual refresh - see that event's own doc comment for why it fires from a background thread
// (hence the Dispatcher.UIThread.Post below, the same convention used everywhere else in this app for
// background-thread-originated updates).
public partial class DownloadsViewModel : ViewModelBase
{
    private readonly IDownloadRepository _downloadRepository;
    private readonly DownloadManager _downloadManager;
    private readonly ILogger<DownloadsViewModel> _logger;

    private Dictionary<Guid, DownloadItemViewModel> _itemsById = [];

    public string Title => "Downloads";

    public ObservableCollection<object> Rows { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasNoDownloads;

    public DownloadsViewModel(IDownloadRepository downloadRepository, DownloadManager downloadManager, ILogger<DownloadsViewModel> logger)
    {
        _downloadRepository = downloadRepository;
        _downloadManager = downloadManager;
        _logger = logger;

        _downloadManager.DownloadChanged += OnDownloadChanged;

        _ = LoadAsync();
    }

    private void OnDownloadChanged(object? sender, Download download) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_itemsById.TryGetValue(download.Id, out var existing))
            {
                existing.Update(download);
            }
            else
            {
                _itemsById[download.Id] = new DownloadItemViewModel(download);
            }
            RebuildRows();
        });

    // Jumps to wherever this completed download's playable copy actually lives (Movies/Series/Clips,
    // whichever applies) via the same Open*Message pattern Favorites/Search already use to open an
    // item from elsewhere in the app - reuses DownloadManager's own title-based match against the
    // Downloads profile rather than this ViewModel guessing at it directly.
    [RelayCommand]
    private async Task OpenAsync(DownloadItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var resolved = await _downloadManager.ResolveLibraryLocationAsync(item.Download);
        if (resolved is not { } location)
        {
            return;
        }

        switch (location.Item)
        {
            case Movie movie when item.Download.ContentType == WatchProgressContentType.Clip:
                WeakReferenceMessenger.Default.Send(new OpenClipMessage(movie, location.SourceName));
                break;
            case Movie movie:
                WeakReferenceMessenger.Default.Send(new OpenMovieMessage(movie, location.SourceName));
                break;
            case Series series:
                WeakReferenceMessenger.Default.Send(new OpenSeriesMessage(series, location.SourceName));
                break;
        }
    }

    [RelayCommand]
    private void Cancel(DownloadItemViewModel? item)
    {
        if (item is not null)
        {
            _downloadManager.CancelDownload(item.Download.Id);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(DownloadItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await _downloadManager.DeleteDownloadAsync(item.Download);
        _itemsById.Remove(item.Download.Id);
        RebuildRows();
    }

    // Resumes from wherever the failed/canceled attempt's partial file left off - see
    // DownloadManager.RetryDownloadAsync. The row transitions in place (Failed/Canceled -> Queued ->
    // Downloading) via the usual DownloadChanged event, same as any other status change.
    [RelayCommand]
    private async Task RetryAsync(DownloadItemViewModel? item)
    {
        if (item is not null)
        {
            await _downloadManager.RetryDownloadAsync(item.Download.Id);
        }
    }

    private void RebuildRows()
    {
        var all = _itemsById.Values.ToList();
        var inProgress = all.Where(i => i.IsInProgress).OrderBy(i => i.Download.StartedUtc).ToList();
        var done = all.Where(i => !i.IsInProgress).OrderByDescending(i => i.Download.CompletedUtc ?? i.Download.StartedUtc).ToList();

        Rows.Clear();
        if (inProgress.Count > 0)
        {
            Rows.Add(new CategoryHeaderRow("In Progress"));
            foreach (var item in inProgress)
            {
                Rows.Add(item);
            }
        }
        if (done.Count > 0)
        {
            Rows.Add(new CategoryHeaderRow("Completed"));
            foreach (var item in done)
            {
                Rows.Add(item);
            }
        }

        HasNoDownloads = all.Count == 0;
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var downloads = await _downloadRepository.GetAllAsync();
            _itemsById = downloads.ToDictionary(d => d.Id, d => new DownloadItemViewModel(d));
            RebuildRows();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load downloads");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
