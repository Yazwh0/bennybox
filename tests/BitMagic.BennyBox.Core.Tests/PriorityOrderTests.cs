using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Core.Tests;

public class PriorityOrderTests
{
    [Fact]
    public void Parse_TrimsSplitsAndDropsDuplicates()
    {
        Assert.Equal(["UK", "US"], PriorityOrder.Parse(" UK , US,, uk "));
        Assert.Empty(PriorityOrder.Parse(null));
    }

    [Fact]
    public void Ordering_PutsPrefixesFirstInListedOrder()
    {
        var prefixes = PriorityOrder.Parse("UK, US");
        string[] names = ["Albania | News", "US | Sport", "uk: BBC One", "Zed TV", "UK | Films", "USA Today"];

        var ordered = names.OrderBy(n => PriorityOrder.Rank(prefixes, n)).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal(["UK | Films", "uk: BBC One", "US | Sport", "USA Today", "Albania | News", "Zed TV"], ordered);
    }

    [Fact]
    public void Rank_WithNoPrefixesKeepsEverythingEqual()
    {
        Assert.Equal(0, PriorityOrder.Rank([], "UK | Sport"));
        Assert.Equal(0, PriorityOrder.Rank([], null));
    }
}
