using System.Windows;
using System.Windows.Controls;
using DesktopManager.Core;

namespace DesktopManager.App;

public partial class ConflictResolutionWindow : Window
{
    public Guid? SelectedRuleId { get; private set; }

    public ConflictResolutionWindow(RuleConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        InitializeComponent();
        SourcePathText.Text = conflict.SourcePath;
        ChoicesList.ItemsSource = conflict.Choices;
    }

    private void ChoicesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ConfirmButton.IsEnabled = ChoicesList.SelectedItem is RuleConflictChoice;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (ChoicesList.SelectedItem is not RuleConflictChoice choice)
        {
            return;
        }

        SelectedRuleId = choice.RuleId;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
