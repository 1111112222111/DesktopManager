namespace DesktopManager.Core;

public enum WindowCloseAction
{
    HideToTray,
    ExitApplication
}

public sealed class WindowLifetimeController
{
    private bool _exitRequested;

    public WindowCloseAction HandleClose() => _exitRequested
        ? WindowCloseAction.ExitApplication
        : WindowCloseAction.HideToTray;

    public void RequestExit() => _exitRequested = true;
}
