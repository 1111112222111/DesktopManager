using DesktopManager.Core;

namespace DesktopManager.Core.Tests;

public sealed class DesktopItemDispositionPolicyTests
{
    [Fact]
    public void WithDisposition_ReplacesExistingChoice_AndInboxRemovesPreference()
    {
        const string path = @"C:\Desktop\常用链接.lnk";

        var kept = DesktopItemDispositionPolicy.Empty.WithDisposition(
            path,
            DesktopItemDisposition.Keep);
        var ignored = kept.WithDisposition(path, DesktopItemDisposition.Ignore);
        var restored = ignored.WithDisposition(path, DesktopItemDisposition.Inbox);

        Assert.Equal(DesktopItemDisposition.Keep, kept.GetDisposition(path));
        Assert.Equal(DesktopItemDisposition.Ignore, ignored.GetDisposition(path));
        Assert.Single(ignored.Preferences);
        Assert.Equal(DesktopItemDisposition.Inbox, restored.GetDisposition(path));
        Assert.Empty(restored.Preferences);
    }
}
