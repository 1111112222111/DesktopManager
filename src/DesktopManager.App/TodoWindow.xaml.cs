using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DesktopManager.Core;
using MediaBrush = System.Windows.Media.Brush;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DesktopManager.App;

public partial class TodoWindow : AdaptiveDesktopWindow
{
    private readonly ObservableCollection<TodoRow> _rows = [];
    private readonly ObservableCollection<DateChoiceRow> _dateChoices = [];
    private readonly List<TodoItem> _items = [];
    private readonly ICollectionView _groupedRowsView;
    private TodoWindowDefinition _definition;
    private DateOnly? _selectedDate;

    public event Action<TodoWindowDefinition>? DefinitionChanged;
    public event Action? CloseRequested;
    internal override string LayoutKey => "todo";

    public TodoWindow(TodoWindowDefinition definition)
    {
        _definition = definition.Normalize();
        InitializeComponent();
        _groupedRowsView = CollectionViewSource.GetDefaultView(_rows);
        _groupedRowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TodoRow.SectionName)));
        TasksList.ItemsSource = _groupedRowsView;
        DateChoices.ItemsSource = _dateChoices;
        _items.AddRange(_definition.EffectiveItems);
        _selectedDate = DateOnly.FromDateTime(DateTime.Today);
        InitializeAdaptiveLayout(_definition.EffectiveLayout, 330, 280);
        RefreshDateChoices();
        RefreshRows();
    }

    private void RefreshDateChoices()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        _dateChoices.Clear();
        _dateChoices.Add(new DateChoiceRow(
            null,
            "无日期",
            "—",
            "查看无日期事项，并将新事项设为无日期",
            _selectedDate is null,
            _items.Any(item => item.DueDate is null)));
        for (var offset = 0; offset < 7; offset++)
        {
            var date = today.AddDays(offset);
            var upperText = offset switch
            {
                0 => "今天",
                1 => "明天",
                _ => date.ToDateTime(TimeOnly.MinValue).ToString("ddd", CultureInfo.CurrentCulture)
            };
            _dateChoices.Add(new DateChoiceRow(
                date,
                upperText,
                date.ToString("M/d", CultureInfo.CurrentCulture),
                $"查看 {date:yyyy年M月d日} 的事项，并将新事项设为该日期",
                _selectedDate == date,
                _items.Any(item => item.DueDate == date)));
        }
    }

    private void DateChoice_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DateChoiceRow choice) return;
        _selectedDate = choice.Date;
        RefreshDateChoices();
        RefreshRows();
        QuickAddText.Focus();
        e.Handled = true;
    }

    private void RefreshRows(Guid? selectId = null)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var visibleItems = _items.Where(item => item.DueDate == _selectedDate).ToArray();
        _rows.Clear();
        foreach (var item in TodoItemQuery.Apply(visibleItems, today))
        {
            _rows.Add(new TodoRow(item));
        }

        var pendingCount = visibleItems.Count(item => !item.IsCompleted);
        CountText.Text = pendingCount == 0 ? "全部完成" : $"{pendingCount} 项未完成";
        ClearCompletedButton.IsEnabled = visibleItems.Any(item => item.IsCompleted);
        EmptyText.Text = BuildEmptyText(today);
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TasksList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (selectId is { } id) TasksList.SelectedItem = _rows.FirstOrDefault(row => row.Item.Id == id);
    }

    private string BuildEmptyText(DateOnly today)
    {
        var dateText = _selectedDate switch
        {
            null => "无日期",
            { } date when date == today => "今天",
            { } date => date.ToString("M月d日", CultureInfo.CurrentCulture)
        };
        return $"{dateText}暂无事项\n在上方快速新增";
    }

    private void PublishItems(Guid? selectId = null)
    {
        _definition = (_definition with { Items = _items.ToArray() }).Normalize();
        _items.Clear();
        _items.AddRange(_definition.EffectiveItems);
        RefreshDateChoices();
        RefreshRows(selectId);
        DefinitionChanged?.Invoke(_definition);
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddQuickItem();

    private void AddQuickItem()
    {
        if (!TodoItem.TryCreate(Guid.NewGuid(), QuickAddText.Text, _selectedDate, DateTimeOffset.Now, out var item, out var error) || item is null)
        {
            QuickAddText.BorderBrush = (MediaBrush)FindResource("GlassDangerTextBrush");
            QuickAddText.ToolTip = error;
            QuickAddText.Focus();
            return;
        }

        _items.Add(item);
        QuickAddText.Clear();
        QuickAddText.ToolTip = null;
        QuickAddText.BorderBrush = (MediaBrush)FindResource("GlassLineBrush");
        PublishItems(item.Id);
        QuickAddText.Focus();
    }

    private void QuickAddText_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key is not Key.Enter) return;
        AddQuickItem();
        e.Handled = true;
    }

    private void QuickAddText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (QuickAddPlaceholder is null) return;
        QuickAddPlaceholder.Visibility = string.IsNullOrEmpty(QuickAddText.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(QuickAddText.Text))
        {
            QuickAddText.BorderBrush = (MediaBrush)FindResource("GlassLineBrush");
            QuickAddText.ToolTip = null;
        }
    }

    private void Completion_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TodoRow row) return;
        SetCompleted(row.Item.Id, !row.Item.IsCompleted);
        e.Handled = true;
    }

    private void ToggleSelected_Click(object sender, RoutedEventArgs e)
    {
        if (TasksList.SelectedItem is TodoRow row) SetCompleted(row.Item.Id, !row.Item.IsCompleted);
    }

    private void SetCompleted(Guid id, bool completed)
    {
        var index = _items.FindIndex(item => item.Id == id);
        if (index < 0) return;
        _items[index] = _items[index].WithCompletion(completed, DateTimeOffset.Now);
        PublishItems(id);
    }

    private void Edit_Click(object sender, RoutedEventArgs e) => EditSelected();
    private void TasksList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelected();

    private void EditSelected()
    {
        if (TasksList.SelectedItem is not TodoRow row) return;
        var dialog = new TodoItemDialog(row.Item);
        if (System.Windows.Application.Current.MainWindow?.IsVisible is true)
        {
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        if (dialog.ShowDialog() is not true || dialog.Result is not { } result) return;
        var index = _items.FindIndex(item => item.Id == row.Item.Id);
        if (index < 0) return;
        _items[index] = result.Normalize();
        PublishItems(result.Id);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TasksList.SelectedItem is not TodoRow row) return;
        _items.RemoveAll(item => item.Id == row.Item.Id);
        PublishItems();
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        _items.RemoveAll(item => item.IsCompleted && item.DueDate == _selectedDate);
        PublishItems();
    }

    private void ContentPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left || e.OriginalSource is not DependencyObject source
            || FindAncestor<ListBoxItem>(source) is not null || FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null
            || FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null) return;
        TasksList.UnselectAll();
        e.Handled = true;
    }

    private void TasksList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || FindAncestor<ListBoxItem>(source) is not { } item) return;
        TasksList.UnselectAll();
        item.IsSelected = true;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || e.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null) return;
        TasksList.UnselectAll();
        BeginAdaptiveDrag();
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        if (e.Key is Key.Delete) Delete_Click(sender, e);
        else if (e.Key is Key.F2) EditSelected();
        else if (e.Key is Key.Space && TasksList.SelectedItem is TodoRow row) SetCompleted(row.Item.Id, !row.Item.IsCompleted);
        else if (e.Key is Key.Escape) TasksList.UnselectAll();
        else return;
        e.Handled = true;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }

    public TodoWindowDefinition CaptureDefinition() => _definition with { IsEnabled = true, Items = _items.ToArray(), Layout = CaptureAdaptiveLayout() };

    public sealed record DateChoiceRow(
        DateOnly? Date,
        string UpperText,
        string LowerText,
        string AutomationName,
        bool IsSelected,
        bool HasItems);

    public sealed record TodoRow(TodoItem Item)
    {
        public string Title => Item.Title;
        public bool IsCompleted => Item.IsCompleted;
        public string SectionName => Item.IsCompleted ? "已完成" : "未完成";
        public string ToggleAutomationName => Item.IsCompleted ? $"恢复 {Title}" : $"完成 {Title}";
        public string FullDescription => $"{Title}；{SectionName}";
    }
}
