namespace DesktopManager.Core;

public enum ShortcutTargetKind
{
    Web,
    File,
    Folder,
    Application
}

public sealed record ShortcutTarget(Guid Id, string Name, string Target, ShortcutTargetKind Kind, string Group = "")
{
    public const string DefaultGroupName = "未分组";
    public string EffectiveGroup => string.IsNullOrWhiteSpace(Group) ? DefaultGroupName : Group.Trim();

    public ShortcutTarget Normalize()
    {
        var target = (Target ?? string.Empty).Trim();
        var name = string.IsNullOrWhiteSpace(Name) ? InferName(target) : Name.Trim();
        return this with { Name = name, Target = target, Group = (Group ?? string.Empty).Trim() };
    }

    public static bool TryCreate(
        Guid id,
        string? name,
        string? targetInput,
        string? group,
        out ShortcutTarget? target,
        out string error)
    {
        target = null;
        var normalizedTarget = (targetInput ?? string.Empty).Trim().Trim('"').Trim();
        if (normalizedTarget.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            normalizedTarget = "https://" + normalizedTarget;
        }

        var isWeb = Uri.TryCreate(normalizedTarget, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && !string.IsNullOrWhiteSpace(uri.Host);
        if (!isWeb)
        {
            normalizedTarget = Environment.ExpandEnvironmentVariables(normalizedTarget);
            if (!Path.Exists(normalizedTarget))
            {
                error = string.IsNullOrWhiteSpace(normalizedTarget)
                    ? "请输入网址，或选择一个现存文件、文件夹或程序。"
                    : "该地址不可用。请输入 http/https 网址，或选择一个现存路径。";
                return false;
            }
        }

        target = new ShortcutTarget(id, name ?? string.Empty, normalizedTarget, DetectKind(normalizedTarget), group ?? string.Empty).Normalize();
        error = string.Empty;
        return true;
    }

    public static ShortcutTargetKind DetectKind(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            return ShortcutTargetKind.Web;
        }
        if (Directory.Exists(target))
        {
            return ShortcutTargetKind.Folder;
        }
        return string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase)
            ? ShortcutTargetKind.Application
            : ShortcutTargetKind.File;
    }

    private static string InferName(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }
        var trimmed = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed) is { Length: > 0 } fileName ? fileName : target;
    }
}

public sealed record DesktopWidgetLayout(
    double Left = 40,
    double Top = 40,
    double Width = 360,
    double Height = 300,
    bool IsVisible = true);

public sealed record ShortcutWindowDefinition(
    Guid Id,
    string Name,
    ShortcutTarget[]? Targets = null,
    DesktopWidgetLayout? Layout = null,
    bool IsEnabled = true)
{
    public IReadOnlyList<ShortcutTarget> EffectiveTargets => Targets ?? [];
    public DesktopWidgetLayout EffectiveLayout => Layout ?? new();
}

public sealed record CalendarWindowDefinition(
    bool IsEnabled = false,
    DesktopWidgetLayout? Layout = null)
{
    public DesktopWidgetLayout EffectiveLayout => Layout ?? new(430, 40, 420, 390, true);
}

public sealed record TodoItem(
    Guid Id,
    string Title,
    bool IsCompleted = false,
    DateOnly? DueDate = null,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset? CompletedAt = null)
{
    public TodoItem Normalize(DateTimeOffset? now = null)
    {
        var normalizedCreatedAt = CreatedAt == default ? now ?? DateTimeOffset.UtcNow : CreatedAt;
        return this with
        {
            Title = (Title ?? string.Empty).Trim(),
            CreatedAt = normalizedCreatedAt,
            CompletedAt = IsCompleted ? CompletedAt ?? now ?? DateTimeOffset.UtcNow : null
        };
    }

    public TodoItem WithCompletion(bool completed, DateTimeOffset? now = null) =>
        (this with { IsCompleted = completed, CompletedAt = completed ? now ?? DateTimeOffset.UtcNow : null }).Normalize(now);

    public bool IsOverdue(DateOnly today) => !IsCompleted && DueDate is { } dueDate && dueDate < today;

    public static bool TryCreate(
        Guid id,
        string? title,
        DateOnly? dueDate,
        DateTimeOffset now,
        out TodoItem? item,
        out string error)
    {
        var normalizedTitle = (title ?? string.Empty).Trim();
        if (normalizedTitle.Length == 0)
        {
            item = null;
            error = "请输入待办事项。";
            return false;
        }

        item = new TodoItem(id, normalizedTitle, DueDate: dueDate, CreatedAt: now).Normalize(now);
        error = string.Empty;
        return true;
    }
}

public sealed record TodoWindowDefinition(
    bool IsEnabled = false,
    TodoItem[]? Items = null,
    DesktopWidgetLayout? Layout = null)
{
    public IReadOnlyList<TodoItem> EffectiveItems => Items ?? [];
    public DesktopWidgetLayout EffectiveLayout => Layout ?? new(856, 40, 390, 430, true);

    public TodoWindowDefinition Normalize(DateTimeOffset? now = null)
    {
        var ids = new HashSet<Guid>();
        var normalizedItems = EffectiveItems
            .Select(item => item.Normalize(now))
            .Where(item => item.Title.Length > 0 && ids.Add(item.Id))
            .ToArray();
        return this with { Items = normalizedItems };
    }
}

public static class TodoItemQuery
{
    public static IReadOnlyList<TodoItem> Apply(IEnumerable<TodoItem> source, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source
            .OrderBy(item => item.IsCompleted)
            .ThenBy(item => item.IsCompleted ? 1 : item.IsOverdue(today) ? 0 : item.DueDate is null ? 2 : 1)
            .ThenBy(item => item.IsCompleted ? DateOnly.MaxValue : item.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(item => item.IsCompleted ? item.CompletedAt ?? item.CreatedAt : DateTimeOffset.MinValue)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}

public sealed record DesktopWidgetsPreferences(
    ShortcutWindowDefinition[]? ShortcutWindows = null,
    CalendarWindowDefinition? Calendar = null,
    TodoWindowDefinition? Todo = null)
{
    public IReadOnlyList<ShortcutWindowDefinition> EffectiveShortcutWindows => ShortcutWindows ?? [];
    public ShortcutWindowDefinition? EffectiveShortcutWindow => EffectiveShortcutWindows.FirstOrDefault();
    public CalendarWindowDefinition EffectiveCalendar => Calendar ?? new();
    public TodoWindowDefinition EffectiveTodo => Todo ?? new();

    public DesktopWidgetsPreferences Normalize()
    {
        var shortcut = EffectiveShortcutWindow;
        return this with
        {
            ShortcutWindows = shortcut is null ? [] : [shortcut],
            Todo = EffectiveTodo.Normalize()
        };
    }
}

public enum HolidayDayKind
{
    Holiday,
    AdjustedWorkday
}

public sealed record HolidayDay(DateOnly Date, string Name, HolidayDayKind Kind);

public enum CalendarDayKind
{
    Regular,
    Weekend,
    Holiday,
    AdjustedWorkday
}

public sealed record CalendarDayClassification(CalendarDayKind Kind, string Name);

public static class ChineseHolidayCatalog
{
    public static IReadOnlyList<HolidayDay> ForYear(int year) => year == 2026 ? Holidays2026 : [];

    public static CalendarDayClassification Classify(DateOnly date)
    {
        var official = ForYear(date.Year).FirstOrDefault(day => day.Date == date);
        if (official is not null)
        {
            return new CalendarDayClassification(
                official.Kind is HolidayDayKind.Holiday ? CalendarDayKind.Holiday : CalendarDayKind.AdjustedWorkday,
                official.Name);
        }
        return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? new CalendarDayClassification(CalendarDayKind.Weekend, "周末")
            : new CalendarDayClassification(CalendarDayKind.Regular, string.Empty);
    }

    private static readonly HolidayDay[] Holidays2026 =
    [
        .. Range(2026, 1, 1, 3, "元旦"), new(new(2026, 1, 4), "调休", HolidayDayKind.AdjustedWorkday),
        new(new(2026, 2, 14), "调休", HolidayDayKind.AdjustedWorkday), .. Range(2026, 2, 15, 23, "春节"), new(new(2026, 2, 28), "调休", HolidayDayKind.AdjustedWorkday),
        .. Range(2026, 4, 4, 6, "清明节"),
        .. Range(2026, 5, 1, 5, "劳动节"), new(new(2026, 5, 9), "调休", HolidayDayKind.AdjustedWorkday),
        .. Range(2026, 6, 19, 21, "端午节"),
        new(new(2026, 9, 20), "调休", HolidayDayKind.AdjustedWorkday), .. Range(2026, 9, 25, 27, "中秋节"),
        .. Range(2026, 10, 1, 7, "国庆节"), new(new(2026, 10, 10), "调休", HolidayDayKind.AdjustedWorkday)
    ];

    private static HolidayDay[] Range(int year, int month, int firstDay, int lastDay, string name) =>
        Enumerable.Range(firstDay, lastDay - firstDay + 1)
            .Select(day => new HolidayDay(new DateOnly(year, month, day), name, HolidayDayKind.Holiday))
            .ToArray();
}
