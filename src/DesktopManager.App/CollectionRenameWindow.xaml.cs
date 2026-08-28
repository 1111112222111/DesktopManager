using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace DesktopManager.App;

public partial class CollectionRenameWindow : Window
{
    public string NewName => NameText.Text.Trim();

    public CollectionRenameWindow(string currentName)
    {
        InitializeComponent();
        NameText.Text = currentName;
        Loaded += (_, _) =>
        {
            NameText.Focus();
            var extensionLength = System.IO.Path.GetExtension(currentName).Length;
            NameText.Select(0, Math.Max(0, currentName.Length - extensionLength));
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            MessageBox.Show(this, "请输入新名称。", "重命名", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
