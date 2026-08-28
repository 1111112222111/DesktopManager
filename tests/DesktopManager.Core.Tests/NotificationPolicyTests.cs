using DesktopManager.Core;

namespace DesktopManager.Core.Tests;

public sealed class NotificationPolicyTests
{
    [Theory]
    [InlineData(21, 59, NotificationDecision.Show)]
    [InlineData(22, 0, NotificationDecision.SuppressQuietHours)]
    [InlineData(0, 0, NotificationDecision.SuppressQuietHours)]
    [InlineData(7, 59, NotificationDecision.SuppressQuietHours)]
    [InlineData(8, 0, NotificationDecision.Show)]
    public void Evaluate_HandlesQuietHoursAcrossMidnight(
        int hour,
        int minute,
        NotificationDecision expected)
    {
        var preferences = new NotificationPreferences(
            IsEnabled: true,
            QuietHoursEnabled: true,
            QuietHoursStart: new TimeOnly(22, 0),
            QuietHoursEnd: new TimeOnly(8, 0));

        var decision = NotificationPolicy.Evaluate(
            preferences,
            new TimeOnly(hour, minute));

        Assert.Equal(expected, decision);
    }

    [Theory]
    [InlineData(11, 59, NotificationDecision.Show)]
    [InlineData(12, 0, NotificationDecision.SuppressQuietHours)]
    [InlineData(13, 29, NotificationDecision.SuppressQuietHours)]
    [InlineData(13, 30, NotificationDecision.Show)]
    public void Evaluate_HandlesQuietHoursWithinOneDay(
        int hour,
        int minute,
        NotificationDecision expected)
    {
        var preferences = new NotificationPreferences(
            IsEnabled: true,
            QuietHoursEnabled: true,
            QuietHoursStart: new TimeOnly(12, 0),
            QuietHoursEnd: new TimeOnly(13, 30));

        var decision = NotificationPolicy.Evaluate(
            preferences,
            new TimeOnly(hour, minute));

        Assert.Equal(expected, decision);
    }

    [Fact]
    public void Evaluate_SuppressesWhenNotificationsAreDisabled()
    {
        var preferences = NotificationPreferences.Default with { IsEnabled = false };

        var decision = NotificationPolicy.Evaluate(preferences, new TimeOnly(12, 0));

        Assert.Equal(NotificationDecision.SuppressDisabled, decision);
    }
}
