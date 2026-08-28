namespace DesktopManager.Infrastructure;

public static class WindowsDesktopLocation
{
    public static string GetCurrentUserDesktop()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopPath))
        {
            throw new DirectoryNotFoundException("Windows 未返回当前用户的桌面位置。");
        }

        return Path.GetFullPath(desktopPath);
    }

    public static string GetPublicDesktop()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopPath))
        {
            throw new DirectoryNotFoundException("Windows 未返回公共桌面位置。");
        }
        return Path.GetFullPath(desktopPath);
    }
}
