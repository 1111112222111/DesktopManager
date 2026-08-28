using System.Text.RegularExpressions;

namespace DesktopManager.Infrastructure;

public static class DiagnosticPrivacy
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var roots = new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), "%TEMP%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%")
        }
        .Where(item => !string.IsNullOrWhiteSpace(item.Item1))
        .OrderByDescending(item => item.Item1.Length);
        var redacted = value;
        foreach (var (root, replacement) in roots)
        {
            redacted = redacted.Replace(root, replacement, StringComparison.OrdinalIgnoreCase);
        }
        return Regex.Replace(
            redacted,
            @"(?i)(?<![A-Z0-9_])(?:[A-Z]:\\|\\\\)[^\s\""'<>|]+",
            "[PATH]",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }
}
