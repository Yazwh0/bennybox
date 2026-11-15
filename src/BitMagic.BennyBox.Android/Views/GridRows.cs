using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Threading;

namespace BitMagic.BennyBox.Android.Views;

// One row of a poster grid: up to Columns items side by side.
public sealed record GridRow(IReadOnlyList<object> Items);

// Lays a page's flat Rows list (category headings and items) out as a grid that can still virtualise:
// headings pass through on their own, and the items between them are grouped into GridRows of
// Columns each. Rebuilds when the source changes (coalesced, since lists refill an item at a time) or
// when the column count changes with the width.
public sealed class GridRows
{
    private readonly Func<object, bool> _isItem;
    private ObservableCollection<object>? _source;
    private int _columns = 3;
    private bool _rebuildQueued;

    public GridRows(Func<object, bool> isItem) => _isItem = isItem;

    public ObservableCollection<object> Rows { get; } = [];

    public int Columns
    {
        get => _columns;
        set
        {
            if (value != _columns && value > 0)
            {
                _columns = value;
                Rebuild();
            }
        }
    }

    public void Attach(ObservableCollection<object> source)
    {
        if (_source is not null)
        {
            _source.CollectionChanged -= OnSourceChanged;
        }

        _source = source;
        _source.CollectionChanged += OnSourceChanged;
        Rebuild();
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildQueued)
        {
            return;
        }

        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            Rebuild();
        }, DispatcherPriority.Background);
    }

    private void Rebuild()
    {
        Rows.Clear();
        if (_source is null)
        {
            return;
        }

        var pending = new List<object>();
        foreach (var item in _source)
        {
            if (!_isItem(item))
            {
                Flush();
                Rows.Add(item);
                continue;
            }

            pending.Add(item);
            if (pending.Count == _columns)
            {
                Flush();
            }
        }

        Flush();

        void Flush()
        {
            if (pending.Count > 0)
            {
                Rows.Add(new GridRow(pending));
                pending = [];
            }
        }
    }
}
