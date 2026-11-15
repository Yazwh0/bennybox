using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Core.Tests;

public class CategoryNamesTests
{
    [Theory]
    [InlineData("Animation;General;Kids", "Animation")]
    [InlineData("General;News", "News")]
    [InlineData("General", "General")]
    [InlineData(" UK | Sport ", "UK | Sport")]
    [InlineData("News, Sports", "News, Sports")]
    [InlineData("", "Uncategorized")]
    [InlineData(null, "Uncategorized")]
    [InlineData(" ; ", "Uncategorized")]
    public void PicksOneTidyCategory(string? groupTitle, string expected) =>
        Assert.Equal(expected, CategoryNames.Tidy(groupTitle));
}
