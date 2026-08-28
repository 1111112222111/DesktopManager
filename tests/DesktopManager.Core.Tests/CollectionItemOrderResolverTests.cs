using DesktopManager.Core;

namespace DesktopManager.Core.Tests;

public sealed class CollectionItemOrderResolverTests
{
    [Fact]
    public void Apply_PutsSavedItemsFirstAndKeepsNewItemsInDefaultOrder()
    {
        string[] items = ["A.lnk", "B.lnk", "C.lnk", "D.lnk"];

        var result = CollectionItemOrderResolver.Apply(items, ["C.lnk", "A.lnk"], item => item);

        Assert.Equal(["C.lnk", "A.lnk", "B.lnk", "D.lnk"], result);
    }

    [Fact]
    public void Apply_IgnoresMissingAndDuplicateSavedNames()
    {
        string[] items = ["A.lnk", "B.lnk"];

        var result = CollectionItemOrderResolver.Apply(items, ["b.LNK", "missing", "B.lnk"], item => item);

        Assert.Equal(["B.lnk", "A.lnk"], result);
    }
}
