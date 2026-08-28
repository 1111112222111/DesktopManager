using System.IO;

namespace DesktopManager.App;

internal static class AppDataLocation
{
    private const string OverrideVariable = "DESKTOP_MANAGER_DATA_ROOT";

    public static bool IsOverridden =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OverrideVariable));

    public static string Root
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(OverrideVariable);
            return !string.IsNullOrWhiteSpace(overridden) && Path.IsPathRooted(overridden)
                ? Path.GetFullPath(overridden)
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DesktopManager");
        }
    }
}
