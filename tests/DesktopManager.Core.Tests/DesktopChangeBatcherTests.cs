using DesktopManager.Core;

namespace DesktopManager.Core.Tests;

public sealed class DesktopChangeBatcherTests
{
    [Fact]
    public async Task Signal_WhenChangesArriveInBurst_EmitsOneCombinedBatch()
    {
        var received = new TaskCompletionSource<IReadOnlyList<DesktopChange>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCount = 0;
        using var batcher = new DesktopChangeBatcher(
            TimeSpan.FromMilliseconds(80),
            changes =>
            {
                Interlocked.Increment(ref callbackCount);
                received.TrySetResult(changes);
            });

        for (var index = 0; index < 20; index++)
        {
            batcher.Signal(new DesktopChange(
                DesktopChangeKind.Changed,
                $@"C:\Desktop\item-{index}.txt"));
        }

        var batch = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Task.Delay(200);

        Assert.Equal(20, batch.Count);
        Assert.Equal(1, Volatile.Read(ref callbackCount));
    }
}
