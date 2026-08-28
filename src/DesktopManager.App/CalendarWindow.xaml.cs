using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopManager.Core;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace DesktopManager.App;

public partial class CalendarWindow : AdaptiveDesktopWindow
{
    private DateOnly _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private CalendarWindowDefinition _definition;
    public event Action? CloseRequested;
    internal override string LayoutKey => "calendar";

    public CalendarWindow(CalendarWindowDefinition definition)
    {
        _definition = definition; InitializeComponent(); var layout = definition.EffectiveLayout;
        InitializeAdaptiveLayout(layout, 350, 330);
        RenderMonth();
    }
    private void RenderMonth()
    {
        MonthText.Text = $"{_month.Year}年 {_month.Month}月";
        var holidays = ChineseHolidayCatalog.ForYear(_month.Year);
        var firstOffset = ((int)_month.DayOfWeek + 6) % 7;
        var days = DateTime.DaysInMonth(_month.Year, _month.Month);
        var cells = new List<CalendarDayRow>();
        for (var index = 0; index < 42; index++)
        {
            var day = index - firstOffset + 1;
            if (day < 1 || day > days) { cells.Add(CalendarDayRow.Empty); continue; }
            var date = new DateOnly(_month.Year, _month.Month, day);
            var classification = ChineseHolidayCatalog.Classify(date);
            var today = date == DateOnly.FromDateTime(DateTime.Today);
            cells.Add(new(day.ToString(), classification.Kind switch
                {
                    CalendarDayKind.Holiday or CalendarDayKind.Weekend => "休",
                    CalendarDayKind.AdjustedWorkday => "班",
                    _ => string.Empty
                }, classification.Name,
                classification.Kind switch
                {
                    CalendarDayKind.Holiday => FindBrush("ChampagneBrush"),
                    CalendarDayKind.AdjustedWorkday => FindBrush("SageBrush"),
                    _ => FindBrush("GlassMutedBrush")
                },
                FindBrush("GlassTextBrush"), today ? FindBrush("GlassLineBrush") : MediaBrushes.Transparent, today ? new Thickness(1) : new Thickness(0),
                classification.Kind switch
                {
                    CalendarDayKind.Holiday => FindBrush("GlassHolidayBrush"),
                    CalendarDayKind.AdjustedWorkday => FindBrush("GlassAdjustedWorkdayBrush"),
                    CalendarDayKind.Weekend => FindBrush("GlassWeekendBrush"),
                    _ => MediaBrushes.Transparent
                }));
        }
        DaysItems.ItemsSource = cells;
        DataStatusText.Text = holidays.Count == 0 ? "该年份暂无官方调休数据" : $"{_month.Year} 年法定节假日与调休数据";
    }
    private MediaBrush FindBrush(string key) => (MediaBrush)FindResource(key);
    private void Previous_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(-1); RenderMonth(); }
    private void Next_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(1); RenderMonth(); }
    private void Today_Click(object sender, RoutedEventArgs e) { _month = new(DateTime.Today.Year, DateTime.Today.Month, 1); RenderMonth(); }
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || e.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Button>(source) is not null) return;
        BeginAdaptiveDrag();
        e.Handled = true;
    }
    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }
    public CalendarWindowDefinition CaptureDefinition() => _definition with { IsEnabled = true, Layout = CaptureAdaptiveLayout() };
    private sealed record CalendarDayRow(string Day, string Marker, string HolidayName, MediaBrush MarkerBrush, MediaBrush Foreground, MediaBrush BorderBrush, Thickness BorderThickness, MediaBrush Background)
    { public static CalendarDayRow Empty { get; } = new("", "", "", MediaBrushes.Transparent, MediaBrushes.Transparent, MediaBrushes.Transparent, new(0), MediaBrushes.Transparent); }
}
