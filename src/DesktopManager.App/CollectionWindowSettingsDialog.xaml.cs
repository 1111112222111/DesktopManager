using System.Windows;
using DesktopManager.Core;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;

namespace DesktopManager.App;

public partial class CollectionWindowSettingsDialog : Window
{
    private bool _initialized;
    private string _startColor;
    private string _endColor;
    private readonly double _globalOpacity;

    public CollectionWindowAppearance? ResultAppearance { get; private set; }
    public bool UseGlobalAppearance { get; private set; }
    public bool ApplyToAllWindows { get; private set; }

    public CollectionWindowSettingsDialog(CollectionWindowAppearance appearance, bool hasOverride)
    {
        InitializeComponent();
        var normalized = appearance.Normalize();
        _startColor = normalized.SurfaceColor;
        _endColor = normalized.GradientEndColor;
        _globalOpacity = normalized.SurfaceOpacity;
        SolidChoice.IsChecked = normalized.FillMode is CollectionWindowFillMode.Solid;
        GradientChoice.IsChecked = normalized.FillMode is CollectionWindowFillMode.Gradient;
        RestoreGlobalButton.IsEnabled = hasOverride;
        _initialized = true;
        UpdatePreview();
    }

    private void FillChoice_Checked(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            UpdatePreview();
        }
    }

    private void ChooseStartColor_Click(object sender, RoutedEventArgs e)
    {
        if (TryChooseColor(_startColor, out var selected))
        {
            _startColor = selected;
            ColorStatusText.Text = "已更新表面颜色。";
            UpdatePreview();
        }
    }

    private void ChooseEndColor_Click(object sender, RoutedEventArgs e)
    {
        if (TryChooseColor(_endColor, out var selected))
        {
            _endColor = selected;
            ColorStatusText.Text = "已更新渐变结束颜色。";
            UpdatePreview();
        }
    }

    private void ReadDesktopColors_Click(object sender, RoutedEventArgs e)
    {
        if (!DesktopWallpaperPaletteProvider.TryCreateSuggestion(out var suggestion, out var error)
            || suggestion is null)
        {
            ColorStatusText.Text = error;
            return;
        }
        _startColor = SolidChoice.IsChecked is true ? suggestion.SolidColor : suggestion.GradientStartColor;
        _endColor = suggestion.GradientEndColor;
        ColorStatusText.Text = $"已根据 {suggestion.SourceDescription} 生成适配颜色。";
        UpdatePreview();
    }

    private void RestoreGlobal_Click(object sender, RoutedEventArgs e)
    {
        UseGlobalAppearance = true;
        DialogResult = true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => Complete(applyToAll: false);
    private void ApplyAll_Click(object sender, RoutedEventArgs e) => Complete(applyToAll: true);

    private void Complete(bool applyToAll)
    {
        ResultAppearance = BuildAppearance();
        UseGlobalAppearance = false;
        ApplyToAllWindows = applyToAll;
        DialogResult = true;
    }

    private CollectionWindowAppearance BuildAppearance() => new CollectionWindowAppearance(
        _globalOpacity,
        _startColor,
        false,
        GradientChoice.IsChecked is true ? CollectionWindowFillMode.Gradient : CollectionWindowFillMode.Solid,
        _endColor).Normalize();

    private void UpdatePreview()
    {
        var appearance = BuildAppearance();
        var start = (Media.Color)Media.ColorConverter.ConvertFromString(appearance.SurfaceColor);
        var end = (Media.Color)Media.ColorConverter.ConvertFromString(appearance.GradientEndColor);
        StartColorPreview.Background = new Media.SolidColorBrush(start);
        EndColorPreview.Background = new Media.SolidColorBrush(end);
        StartColorText.Text = appearance.SurfaceColor;
        EndColorText.Text = appearance.GradientEndColor;
        StartColorLabel.Text = appearance.FillMode is CollectionWindowFillMode.Gradient ? "起始颜色" : "表面颜色";
        EndColorButton.Visibility = appearance.FillMode is CollectionWindowFillMode.Gradient ? Visibility.Visible : Visibility.Collapsed;
        GlobalOpacityText.Text = $"全局透明度 {appearance.SurfaceOpacity:P0}";
        CollectionWindowMaterialRenderer.Apply(PreviewSurface, appearance);
    }

    private static bool TryChooseColor(string currentColor, out string selectedColor)
    {
        var current = (Media.Color)Media.ColorConverter.ConvertFromString(currentColor);
        using var dialog = new Forms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B)
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            selectedColor = currentColor;
            return false;
        }
        selectedColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        return true;
    }
}
