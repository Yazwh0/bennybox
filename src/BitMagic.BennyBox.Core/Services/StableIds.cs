namespace BitMagic.BennyBox.Core.Services;

// Re-importing a source replaces its channels / shows / films / videos wholesale, and each new item
// would get a brand-new internal Id - but favourites (and anything else keyed on that Id) point at the
// old one, so every refresh silently emptied Favourites. This carries the Id over from the item already
// stored with the same source id (the provider's own id for it), so a refresh keeps an item's identity.
//
// Source ids aren't always unique (a playlist can list the same tvg-id twice), so matching is in order:
// the first new item with a given source id takes the first old Id with it, the second the second, and
// so on. Items that are genuinely new keep the fresh Id they were created with.
public static class StableIds
{
    public static void Carry<T>(IEnumerable<T> existing, IEnumerable<T> incoming,
        Func<T, string> sourceId, Func<T, Guid> getId, Action<T, Guid> setId)
    {
        var oldIds = new Dictionary<string, Queue<Guid>>(StringComparer.Ordinal);
        foreach (var item in existing)
        {
            var key = sourceId(item);
            if (!oldIds.TryGetValue(key, out var queue))
            {
                queue = new Queue<Guid>();
                oldIds[key] = queue;
            }

            queue.Enqueue(getId(item));
        }

        foreach (var item in incoming)
        {
            if (oldIds.TryGetValue(sourceId(item), out var queue) && queue.TryDequeue(out var id))
            {
                setId(item, id);
            }
        }
    }
}
