using System.Windows;
using DesktopManager.Core;

namespace DesktopManager.App;

public partial class TodoItemDialog : Window
{
    private readonly TodoItem _existing;
    public TodoItem? Result { get; private set; }

    public TodoItemDialog(TodoItem existing)
    {
        _existing = existing;
        InitializeComponent();
        TitleText.Text = existing.Title;
        DueDatePicker.SelectedDate = existing.DueDate?.ToDateTime(TimeOnly.MinValue);
        Loaded += (_, _) =>
        {
            TitleText.Focus();
            TitleText.SelectAll();
            UpdateValidation();
        };
    }

    private void Input_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateValidation();

    private void UpdateValidation()
    {
        if (SaveButton is null || ValidationText is null) return;
        var valid = !string.IsNullOrWhiteSpace(TitleText.Text);
        SaveButton.IsEnabled = valid;
        ValidationText.Foreground = (System.Windows.Media.Brush)FindResource(valid ? "MutedBrush" : "DangerBrush");
        ValidationText.Text = valid ? "修改会自动同步到桌面窗口。" : "请输入待办事项。";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleText.Text.Trim();
        if (title.Length == 0)
        {
            UpdateValidation();
            TitleText.Focus();
            return;
        }

        Result = _existing with
        {
            Title = title,
            DueDate = DueDatePicker.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null
        };
        DialogResult = true;
    }
}
