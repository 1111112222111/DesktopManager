using System.Windows;
using System.Windows.Media;
using DesktopManager.Core;
using Forms = System.Windows.Forms;
using MediaBrush = System.Windows.Media.Brush;

namespace DesktopManager.App;

public partial class ShortcutTargetDialog : Window
{
    private readonly Guid _targetId;
    public ShortcutTarget? Result { get; private set; }

    public ShortcutTargetDialog(IEnumerable<string>? groupNames = null, ShortcutTarget? existing = null)
    {
        _targetId = existing?.Id ?? Guid.NewGuid();
        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        GroupComboBox.ItemsSource = (groupNames ?? [])
            .Append(ShortcutTarget.DefaultGroupName)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Select(group => group.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => group, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (existing is not null)
        {
            Title = "编辑快速应用";
            SaveButton.Content = "保存修改";
            NameText.Text = existing.Name;
            GroupComboBox.Text = existing.EffectiveGroup;
            TargetText.Text = existing.Target;
        }
        else
        {
            GroupComboBox.Text = ShortcutTarget.DefaultGroupName;
        }

        Loaded += (_, _) =>
        {
            TargetText.Focus();
            TargetText.CaretIndex = TargetText.Text.Length;
            UpdateValidation();
        };
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            CheckFileExists = true,
            Title = "选择文件或程序"
        };
        if (dialog.ShowDialog(this) is true)
        {
            TargetText.Text = dialog.FileName;
            TargetText.CaretIndex = TargetText.Text.Length;
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择快速应用文件夹",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() is Forms.DialogResult.OK)
        {
            TargetText.Text = dialog.SelectedPath;
            TargetText.CaretIndex = TargetText.Text.Length;
        }
    }

    private void Input_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateValidation();

    private void UpdateValidation()
    {
        if (SaveButton is null || ValidationText is null || TargetTypeText is null) return;

        if (string.IsNullOrWhiteSpace(TargetText.Text))
        {
            SaveButton.IsEnabled = false;
            ValidationText.Foreground = FindBrush("MutedBrush");
            ValidationText.Text = "输入地址后会在这里确认可用性。";
            TargetTypeText.Text = string.Empty;
            return;
        }

        var valid = ShortcutTarget.TryCreate(
            _targetId,
            NameText.Text,
            TargetText.Text,
            GroupComboBox.Text,
            out var target,
            out var error);
        SaveButton.IsEnabled = valid;
        ValidationText.Foreground = FindBrush(valid ? "SageBrush" : "DangerBrush");
        ValidationText.Text = valid ? "地址有效，可以确认保存。" : error;
        TargetTypeText.Text = valid && target is not null ? KindText(target.Kind) : string.Empty;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!ShortcutTarget.TryCreate(
                _targetId,
                NameText.Text,
                TargetText.Text,
                GroupComboBox.Text,
                out var target,
                out var error)
            || target is null)
        {
            SaveButton.IsEnabled = false;
            ValidationText.Foreground = FindBrush("DangerBrush");
            ValidationText.Text = error;
            TargetText.Focus();
            return;
        }

        Result = target;
        DialogResult = true;
    }

    private MediaBrush FindBrush(string key) => (MediaBrush)FindResource(key);

    private static string KindText(ShortcutTargetKind kind) => kind switch
    {
        ShortcutTargetKind.Web => "网址",
        ShortcutTargetKind.Folder => "文件夹",
        ShortcutTargetKind.Application => "程序",
        _ => "文件"
    };
}
