using System.Windows;
using System.Windows.Controls;

namespace DesktopManager.App;

public partial class PlanTargetWindow : Window
{
    public string? RelativeDestination { get; private set; }

    public PlanTargetWindow(string itemName, string currentRelativeDestination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
        InitializeComponent();
        ItemText.Text = $"项目：{itemName}";
        RelativeDestinationText.Text = currentRelativeDestination;
        RelativeDestinationText.SelectAll();
        RelativeDestinationText.Focus();
        UpdateConfirmAvailability();
    }

    private void RelativeDestinationText_Changed(object sender, TextChangedEventArgs e) =>
        UpdateConfirmAvailability();

    private void UpdateConfirmAvailability()
    {
        if (ConfirmButton is not null)
        {
            ConfirmButton.IsEnabled = !string.IsNullOrWhiteSpace(RelativeDestinationText.Text);
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        RelativeDestination = RelativeDestinationText.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
