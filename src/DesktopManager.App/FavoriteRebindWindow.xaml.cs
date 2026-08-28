using System.Windows;
using System.Windows.Controls;

namespace DesktopManager.App;

public sealed record FavoriteRebindCandidate(
    string Path,
    string Name,
    string Kind,
    string Scope);

public partial class FavoriteRebindWindow : Window
{
    public string? SelectedPath { get; private set; }

    public FavoriteRebindWindow(
        string oldPath,
        IReadOnlyList<FavoriteRebindCandidate> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldPath);
        ArgumentNullException.ThrowIfNull(candidates);
        InitializeComponent();
        OldPathText.Text = $"失效成员：{oldPath}";
        CandidatesList.ItemsSource = candidates;
    }

    private void CandidatesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ConfirmButton.IsEnabled = CandidatesList.SelectedItem is FavoriteRebindCandidate;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (CandidatesList.SelectedItem is not FavoriteRebindCandidate candidate)
        {
            return;
        }
        SelectedPath = candidate.Path;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
