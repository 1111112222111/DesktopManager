using DesktopManager.Core;

namespace DesktopManager.Core.Tests;

public sealed class DesktopWidgetsTests
{
    [Theory]
    [InlineData("https://example.com", ShortcutTargetKind.Web)]
    [InlineData("C:\\Tools\\demo.exe", ShortcutTargetKind.Application)]
    [InlineData("C:\\Docs\\notes.txt", ShortcutTargetKind.File)]
    public void DetectKind_UsesTargetShape(string target, ShortcutTargetKind expected) =>
        Assert.Equal(expected, ShortcutTarget.DetectKind(target));

    [Fact]
    public void Normalize_WebTargetWithoutName_UsesHost()
    {
        var target = new ShortcutTarget(Guid.NewGuid(), " ", " https://example.com/docs ", ShortcutTargetKind.Web).Normalize();
        Assert.Equal("example.com", target.Name);
        Assert.Equal("https://example.com/docs", target.Target);
    }

    [Fact]
    public void Normalize_BlankGroup_UsesUncategorizedEffectiveGroup()
    {
        var target = new ShortcutTarget(Guid.NewGuid(), "文档", "C:\\Docs\\notes.txt", ShortcutTargetKind.File, "  ").Normalize();

        Assert.Equal(string.Empty, target.Group);
        Assert.Equal("未分组", target.EffectiveGroup);
    }

    [Fact]
    public void TryCreate_NormalizesWwwUrlAndKeepsGroup()
    {
        var created = ShortcutTarget.TryCreate(
            Guid.NewGuid(), "官网", " www.example.com/docs ", " 工作 ", out var target, out var error);

        Assert.True(created, error);
        Assert.NotNull(target);
        Assert.Equal("https://www.example.com/docs", target.Target);
        Assert.Equal(ShortcutTargetKind.Web, target.Kind);
        Assert.Equal("工作", target.Group);
    }

    [Fact]
    public void TryCreate_AcceptsQuotedExistingFilePath()
    {
        var path = Path.GetTempFileName();
        try
        {
            var created = ShortcutTarget.TryCreate(
                Guid.NewGuid(), string.Empty, $"\"{path}\"", string.Empty, out var target, out var error);

            Assert.True(created, error);
            Assert.NotNull(target);
            Assert.Equal(path, target.Target);
            Assert.Equal(ShortcutTargetKind.File, target.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Holidays2026_ContainsOfficialHolidayAndAdjustedWorkdayMarkers()
    {
        var days = ChineseHolidayCatalog.ForYear(2026);
        Assert.Contains(days, day => day.Date == new DateOnly(2026, 2, 17) && day.Name == "春节" && day.Kind == HolidayDayKind.Holiday);
        Assert.Contains(days, day => day.Date == new DateOnly(2026, 2, 28) && day.Kind == HolidayDayKind.AdjustedWorkday);
        Assert.Contains(days, day => day.Date == new DateOnly(2026, 10, 7) && day.Name == "国庆节");
    }

    [Fact]
    public void UnknownHolidayYear_DoesNotInventAdjustedWorkdays() =>
        Assert.Empty(ChineseHolidayCatalog.ForYear(2027));

    [Fact]
    public void PreferencesNormalize_KeepsOnlyTheFirstUniqueShortcutWindow()
    {
        var first = new ShortcutWindowDefinition(Guid.NewGuid(), "保留", IsEnabled: false);
        var duplicate = new ShortcutWindowDefinition(Guid.NewGuid(), "旧重复窗口");

        var normalized = new DesktopWidgetsPreferences([first, duplicate]).Normalize();

        Assert.Single(normalized.EffectiveShortcutWindows);
        Assert.Equal(first.Id, normalized.EffectiveShortcutWindow!.Id);
        Assert.False(normalized.EffectiveShortcutWindow.IsEnabled);
    }

    [Fact]
    public void PreferencesNormalize_PreservesEnabledWidgetContentGroupsAndLayouts()
    {
        var target = new ShortcutTarget(Guid.NewGuid(), "文档", "C:\\Docs\\notes.txt", ShortcutTargetKind.File, "工作");
        var shortcut = new ShortcutWindowDefinition(
            Guid.NewGuid(),
            "快速应用",
            [target],
            new DesktopWidgetLayout(120, 180, 520, 360, true),
            IsEnabled: true);
        var calendar = new CalendarWindowDefinition(
            IsEnabled: true,
            new DesktopWidgetLayout(650, 180, 460, 410, true));

        var normalized = new DesktopWidgetsPreferences([shortcut], calendar).Normalize();

        Assert.True(normalized.EffectiveShortcutWindow!.IsEnabled);
        Assert.Equal("工作", Assert.Single(normalized.EffectiveShortcutWindow.EffectiveTargets).Group);
        Assert.Equal(520, normalized.EffectiveShortcutWindow.EffectiveLayout.Width);
        Assert.True(normalized.EffectiveCalendar.IsEnabled);
        Assert.Equal(460, normalized.EffectiveCalendar.EffectiveLayout.Width);
    }

    [Fact]
    public void TodoTryCreate_TrimsTitleAndRejectsBlankTitle()
    {
        var now = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.FromHours(8));

        Assert.False(TodoItem.TryCreate(Guid.NewGuid(), "  ", null, now, out _, out var error));
        Assert.Equal("请输入待办事项。", error);

        Assert.True(TodoItem.TryCreate(Guid.NewGuid(), "  提交周报  ", new DateOnly(2026, 8, 28), now, out var item, out _));
        Assert.Equal("提交周报", item!.Title);
        Assert.Equal(new DateOnly(2026, 8, 28), item.DueDate);
        Assert.Equal(now, item.CreatedAt);
    }

    [Fact]
    public void TodoCompletion_RecordsAndClearsCompletionTime()
    {
        var created = new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);
        var completed = created.AddHours(2);
        var item = new TodoItem(Guid.NewGuid(), "整理资料", CreatedAt: created);

        var done = item.WithCompletion(true, completed);
        Assert.True(done.IsCompleted);
        Assert.Equal(completed, done.CompletedAt);

        var restored = done.WithCompletion(false, completed.AddHours(1));
        Assert.False(restored.IsCompleted);
        Assert.Null(restored.CompletedAt);
    }

    [Fact]
    public void TodoQuery_OrdersOverdueThenDueDateThenNameAndCompletedLast()
    {
        var today = new DateOnly(2026, 8, 27);
        var items = new[]
        {
            new TodoItem(Guid.NewGuid(), "无日期"),
            new TodoItem(Guid.NewGuid(), "明天 B", DueDate: today.AddDays(1)),
            new TodoItem(Guid.NewGuid(), "明天 A", DueDate: today.AddDays(1)),
            new TodoItem(Guid.NewGuid(), "逾期", DueDate: today.AddDays(-1)),
            new TodoItem(Guid.NewGuid(), "已完成", IsCompleted: true, CompletedAt: DateTimeOffset.UtcNow)
        };

        var ordered = TodoItemQuery.Apply(items, today);

        Assert.Equal(new[] { "逾期", "明天 A", "明天 B", "无日期", "已完成" }, ordered.Select(item => item.Title));
    }

    [Fact]
    public void TodoDefinitionNormalize_RemovesBlankAndDuplicateItemsWhilePreservingState()
    {
        var id = Guid.NewGuid();
        var definition = new TodoWindowDefinition(
            IsEnabled: true,
            Items:
            [
                new TodoItem(id, "  保留  "),
                new TodoItem(id, "重复"),
                new TodoItem(Guid.NewGuid(), "  ")
            ],
            Layout: new DesktopWidgetLayout(30, 50, 410, 440, true));

        var normalized = definition.Normalize(new DateTimeOffset(2026, 8, 27, 9, 0, 0, TimeSpan.Zero));

        Assert.True(normalized.IsEnabled);
        Assert.Equal("保留", Assert.Single(normalized.EffectiveItems).Title);
        Assert.Equal(410, normalized.EffectiveLayout.Width);
    }

    [Fact]
    public void CalendarClassification_MarksOrdinaryWeekendAsRestDay()
    {
        var day = ChineseHolidayCatalog.Classify(new DateOnly(2026, 8, 29));
        Assert.Equal(CalendarDayKind.Weekend, day.Kind);
        Assert.Equal("周末", day.Name);
    }

    [Fact]
    public void CalendarClassification_OfficialAdjustedWorkdayOverridesWeekend()
    {
        var day = ChineseHolidayCatalog.Classify(new DateOnly(2026, 1, 4));
        Assert.Equal(CalendarDayKind.AdjustedWorkday, day.Kind);
        Assert.Equal("调休", day.Name);
    }
}
