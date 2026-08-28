using DesktopManager.Infrastructure;

namespace DesktopManager.IntegrationTests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task TryAcquire_WhenAnotherInstanceOwnsIdentity_SignalsOwnerAndRejectsSecond()
    {
        var applicationId = $"DesktopManager.Tests.{Guid.NewGuid():N}";
        var activationReceived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var first = new SingleInstanceCoordinator(applicationId);
        using var second = new SingleInstanceCoordinator(applicationId);

        var firstAcquired = first.TryAcquire(() => activationReceived.TrySetResult());
        var secondAcquired = second.TryAcquire(() => { });

        Assert.True(firstAcquired);
        Assert.False(secondAcquired);
        await activationReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
