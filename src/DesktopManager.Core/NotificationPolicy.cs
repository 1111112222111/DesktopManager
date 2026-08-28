namespace DesktopManager.Core;

public enum NotificationDecision
{
    Show,
    SuppressDisabled,
    SuppressQuietHours
}

public sealed record NotificationPreferences(
    bool IsEnabled,
    bool QuietHoursEnabled,
    TimeOnly QuietHoursStart,
    TimeOnly QuietHoursEnd)
{
    public static NotificationPreferences Default { get; } = new(
        IsEnabled: true,
        QuietHoursEnabled: true,
        QuietHoursStart: new TimeOnly(22, 0),
        QuietHoursEnd: new TimeOnly(8, 0));
}

public static class NotificationPolicy
{
    public static NotificationDecision Evaluate(
        NotificationPreferences preferences,
        TimeOnly currentTime)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.IsEnabled)
        {
            return NotificationDecision.SuppressDisabled;
        }

        var isQuietTime = preferences.QuietHoursEnabled
            && preferences.QuietHoursStart != preferences.QuietHoursEnd
            && (preferences.QuietHoursStart < preferences.QuietHoursEnd
                ? currentTime >= preferences.QuietHoursStart
                    && currentTime < preferences.QuietHoursEnd
                : currentTime >= preferences.QuietHoursStart
                    || currentTime < preferences.QuietHoursEnd);
        return isQuietTime
            ? NotificationDecision.SuppressQuietHours
            : NotificationDecision.Show;
    }
}
