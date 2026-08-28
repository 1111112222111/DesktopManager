using DesktopManager.Core;

namespace DesktopManager.Core.Tests;

public sealed class CollectionItemTypeAheadTests
{
    [Fact]
    public void FindNextIndex_MatchesIgnoringCaseAndCyclesFromSelection()
    {
        string[] names = ["Adobe.lnk", "Browser.lnk", "antivirus.lnk"];

        Assert.Equal(2, CollectionItemTypeAhead.FindNextIndex(names, 0, "a"));
        Assert.Equal(0, CollectionItemTypeAhead.FindNextIndex(names, 2, "A"));
    }

    [Fact]
    public void FindNextIndex_WhenNothingMatches_ReturnsMinusOne()
    {
        Assert.Equal(-1, CollectionItemTypeAhead.FindNextIndex(["文档", "图片"], -1, "x"));
    }
}
