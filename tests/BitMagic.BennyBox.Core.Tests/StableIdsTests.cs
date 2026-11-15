using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Core.Tests;

public class StableIdsTests
{
    private sealed class Item(string sourceId)
    {
        public string SourceId { get; } = sourceId;
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    private static void Carry(IEnumerable<Item> existing, IEnumerable<Item> incoming) =>
        StableIds.Carry(existing, incoming, i => i.SourceId, i => i.Id, (i, id) => i.Id = id);

    [Fact]
    public void SameSourceId_KeepsTheOldId()
    {
        var old = new Item("123");
        var refreshed = new Item("123");

        Carry([old], [refreshed]);

        Assert.Equal(old.Id, refreshed.Id);
    }

    [Fact]
    public void NewItems_KeepTheirFreshId()
    {
        var brandNew = new Item("999");
        var freshId = brandNew.Id;

        Carry([new Item("123")], [brandNew]);

        Assert.Equal(freshId, brandNew.Id);
    }

    [Fact]
    public void DuplicateSourceIds_AreMatchedInOrder()
    {
        var oldA = new Item("bbc1");
        var oldB = new Item("bbc1");
        var newA = new Item("bbc1");
        var newB = new Item("bbc1");
        var newC = new Item("bbc1");
        var newCId = newC.Id;

        Carry([oldA, oldB], [newA, newB, newC]);

        Assert.Equal(oldA.Id, newA.Id);
        Assert.Equal(oldB.Id, newB.Id);
        Assert.Equal(newCId, newC.Id);
    }
}
