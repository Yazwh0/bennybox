using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Threading;

namespace BitMagic.BennyBox.Android.Views;

// One row of a grid: up to Columns items of the same kind side by side.
public sealed record GridRow(IReadOnlyList<object> Items);

// Lays a page's flat Rows list (headings and items) out as a grid that can still virtualise: headings
// pass through on their own, and the items between them are grouped into GridRows. Pages that mix
// kinds (Search, Favourites: channel rows and posters) give each kind its own key and column count,
// and a row never mixes kinds. Rebuilds when the source changes (coalesced, since lists refill an
// item at a time) or when a column count changes with the width.
public sealed class GridRows
{
    // The kind of grid cell an item is, or null for something full width (a heading).
    private readonly Func<object, string?> _kindOf;
    private readonly Dictionary<string, int> _columns = [];
    private ObservableCollection<object>? _source;
    private bool _rebuildQueued;

    public const string Item = "item";

    public GridRows(Func<object, string?> kindOf) => _kindOf = kindOf;

    // The common case: one kind of item, everything else full width.
    public GridRows(Func<object, bool> isItem) : this(o => isItem(o) ? Item : null)
    {
    }

    public ObservableCollection<object> Rows { get; } = [];

    public int Columns
    {
        get => ColumnsFor(Item);
        set => SetColumns(Item, value);
    }

    public void SetColumns(string kind, int columns)
    {
        if (columns > 0 && ColumnsFor(kind) != columns)
        {
            _columns[kind] = columns;
            Rebuild();
        }
    }

    private int ColumnsFor(string kind) => _columns.TryGetValue(kind, out var columns) ? columns : 1;

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
        string? pendingKind = null;
        foreach (var item in _source)
        {
            var kind = _kindOf(item);
            if (kind is null)
            {
                Flush();
                Rows.Add(item);
                continue;
            }

            if (kind != pendingKind)
            {
                Flush();
                pendingKind = kind;
            }

            pending.Add(item);
            if (pending.Count >= ColumnsFor(kind))
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
