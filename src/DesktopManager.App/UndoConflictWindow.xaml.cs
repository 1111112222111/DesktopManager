using System.IO;
using System.Windows;

namespace DesktopManager.App;

public partial class UndoConflictWindow : Window
{
    private readonly string _allowedSourceRoot;

    public DesktopManager.Core.UndoConflictResolution Resolution { get; private set; }
    public string? AlternateRestorePath { get; private set; }

    public UndoConflictWindow(string conflictPath, string allowedSourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conflictPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedSourceRoot);
        _allowedSourceRoot = Path.GetFullPath(allowedSourceRoot);
        InitializeComponent();
        ConflictPathText.Text = $"原恢复位置：{conflictPath}";
        AllowedRootText.Text = $"允许范围：{_allowedSourceRoot}";
        UpdateAvailability();
    }

    private void Option_Changed(object sender, RoutedEventArgs e) => UpdateAvailability();

    private void AlternatePathText_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        UpdateAvailability();

    private void UpdateAvailability()
    {
        if (AlternatePathText is null || ConfirmButton is null)
        {
            return;
        }
        var alternate = AlternateOption.IsChecked is true;
        AlternatePathText.IsEnabled = alternate;
        ConfirmButton.IsEnabled = !alternate || IsValidAlternatePath(AlternatePathText.Text);
    }

    private bool IsValidAlternatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return false;
        }
        var rootPrefix = _allowedSourceRoot
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Resolution = SafeRenameOption.IsChecked is true
            ? DesktopManager.Core.UndoConflictResolution.SafeRename
            : SkipOption.IsChecked is true
                ? DesktopManager.Core.UndoConflictResolution.Skip
                : DesktopManager.Core.UndoConflictResolution.AlternatePath;
        AlternateRestorePath = Resolution is DesktopManager.Core.UndoConflictResolution.AlternatePath
            ? Path.GetFullPath(AlternatePathText.Text.Trim())
            : null;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
