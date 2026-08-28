using DesktopManager.Core;

namespace DesktopManager.Core.Tests;

public sealed class WindowLifetimeControllerTests
{
    [Fact]
    public void HandleClose_HidesByDefault_AndExitsAfterExplicitRequest()
    {
        var lifetime = new WindowLifetimeController();

        Assert.Equal(WindowCloseAction.HideToTray, lifetime.HandleClose());

        lifetime.RequestExit();

        Assert.Equal(WindowCloseAction.ExitApplication, lifetime.HandleClose());
    }
}
