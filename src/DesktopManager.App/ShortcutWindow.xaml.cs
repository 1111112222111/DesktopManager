using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DesktopManager.Core;

namespace DesktopManager.App;

public partial class ShortcutWindow : AdaptiveDesktopWindow
{
    private readonly ObservableCollection<ShortcutRow> _rows = [];
    private readonly ShellIconProvider _icons = new();
    private readonly ICollectionView _groupedRowsView;
    private ShortcutWindowDefinition _definition;
    public event Action<ShortcutWindowDefinition>? DefinitionChanged;
    public event Action<Guid>? RemoveRequested;
    public Guid DefinitionId => _definition.Id;
    internal override string LayoutKey => $"shortcut:{_definition.Id:N}";

    public ShortcutWindow(ShortcutWindowDefinition definition)
    {
        _definition = definition;
        InitializeComponent();
        _groupedRowsView = CollectionViewSource.GetDefaultView(_rows);
        _groupedRowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ShortcutRow.GroupName)));
        TargetsList.ItemsSource = _groupedRowsView;
        ApplyDefinition();
    }

    private void ApplyDefinition()
    {
        TitleText.Text = _definition.Name; var layout = _definition.EffectiveLayout;
        InitializeAdaptiveLayout(layout, 300, 190);
        _rows.Clear(); foreach (var target in _definition.EffectiveTargets) _rows.Add(ToRow(target));
        UpdateCount();
    }

    private ShortcutRow ToRow(ShortcutTarget target) => new(target,
        target.Kind is ShortcutTargetKind.Web ? null : _icons.GetIcon(target.Target),
        target.Kind is ShortcutTargetKind.Web ? "\uE71B" : string.Empty,
        target.Kind switch { ShortcutTargetKind.Web => "网址", ShortcutTargetKind.Folder => "文件夹", ShortcutTargetKind.Application => "程序", _ => "文件" });

    private void UpdateCount() { CountText.Text = $"{_rows.Count} 项"; EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed; }
    private void SaveTargets()
    {
        _definition = _definition with { Targets = _rows.Select(row => row.Target).ToArray() };
        UpdateCount(); DefinitionChanged?.Invoke(_definition);
    }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || e.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Button>(source) is not null) return;
        BeginAdaptiveDrag();
        e.Handled = true;
    }
    private void ContentPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left || e.OriginalSource is not DependencyObject source
            || FindAncestor<ListBoxItem>(source) is not null || FindAncestor<System.Windows.Controls.Button>(source) is not null
            || FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null) return;
        TargetsList.UnselectAll();
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }
    private void TargetsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || FindAncestor<ListBoxItem>(source) is not { } item) return;
        if (!item.IsSelected) TargetsList.UnselectAll();
        item.IsSelected = true;
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = CreateTargetDialog();
        if (dialog.ShowDialog() is true && dialog.Result is { } result)
        {
            _rows.Add(ToRow(result));
            SaveTargets();
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (TargetsList.SelectedItem is not ShortcutRow selected) return;
        var dialog = CreateTargetDialog(selected.Target);
        if (dialog.ShowDialog() is not true || dialog.Result is not { } result) return;

        var index = _rows.IndexOf(selected);
        if (index >= 0) _rows[index] = ToRow(result);
        SaveTargets();
    }

    private ShortcutTargetDialog CreateTargetDialog(ShortcutTarget? existing = null)
    {
        var groups = _rows.Select(row => row.GroupName).Distinct(StringComparer.CurrentCultureIgnoreCase);
        var dialog = new ShortcutTargetDialog(groups, existing);
        var mainWindow = System.Windows.Application.Current.MainWindow;
        if (mainWindow?.IsVisible is true)
        {
            dialog.Owner = mainWindow;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        return dialog;
    }
    private void RemoveWindow_Click(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(_definition.Id);
    private void Window_DragOver(object sender, System.Windows.DragEventArgs e) { e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Link : System.Windows.DragDropEffects.None; e.Handled = true; }
    private void Window_Drop(object sender, System.Windows.DragEventArgs e) { if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths) return; foreach (var path in paths.Where(Path.Exists)) { var target = new ShortcutTarget(Guid.NewGuid(), string.Empty, path, ShortcutTarget.DetectKind(path)).Normalize(); _rows.Add(ToRow(target)); } SaveTargets(); }
    private void OpenRow_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is ShortcutRow row) Open([row]); }
    private void OpenItem_Click(object sender, RoutedEventArgs e) { if (TargetsList.SelectedItem is ShortcutRow row) Open([row]); }
    private void OpenSelected_Click(object sender, RoutedEventArgs e) => Open(TargetsList.SelectedItems.Cast<ShortcutRow>());
    private void OpenGroup_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CollectionViewGroup group)
            Open(group.Items.OfType<ShortcutRow>());
        e.Handled = true;
    }
    private void OpenAll_Click(object sender, RoutedEventArgs e) => Open(_rows);
    private static void Open(IEnumerable<ShortcutRow> rows)
    {
        var failures = new List<string>();
        foreach (var row in rows)
        {
            var target = row.Target;
            var valid = target.Kind is ShortcutTargetKind.Web
                ? Uri.TryCreate(target.Target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                : Path.Exists(target.Target);
            if (!valid) { failures.Add(row.Name); continue; }
            try { Process.Start(new ProcessStartInfo(target.Target) { UseShellExecute = true }); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException) { failures.Add(row.Name); }
        }
        if (failures.Count > 0) System.Windows.MessageBox.Show($"以下目标无法打开：{string.Join("、", failures)}", "快速应用", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void Delete_Click(object sender, RoutedEventArgs e) { foreach (var row in TargetsList.SelectedItems.Cast<ShortcutRow>().ToArray()) _rows.Remove(row); SaveTargets(); }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter) { OpenSelected_Click(sender, e); e.Handled = true; } }
    public ShortcutWindowDefinition CaptureDefinition() => _definition with { Layout = CaptureAdaptiveLayout() };
    public sealed record ShortcutRow(ShortcutTarget Target, ImageSource? Icon, string Glyph, string KindText)
    {
        public string Name => Target.Name;
        public string GroupName => Target.EffectiveGroup;
    }
}
