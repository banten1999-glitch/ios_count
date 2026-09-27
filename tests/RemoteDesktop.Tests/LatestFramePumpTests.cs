using System.Collections.Concurrent;
using RemoteDesktop.Core.Media;
using Xunit;

namespace RemoteDesktop.Tests;

public class LatestFramePumpTests
{
    private sealed record Frame(int Number);

    [Fact]
    public async Task SlowConsumerReceivesNewestFrameWithoutHistoryBacklog()
    {
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new ConcurrentQueue<int>();
        await using var pump = new LatestFramePump<Frame>(60, frame =>
        {
            seen.Enqueue(frame.Number);
            if (frame.Number == 1)
            {
                entered.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            }
            if (frame.Number == 100) latest.TrySetResult();
        });
        try
        {
            Assert.True(pump.Post(new Frame(1)));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (int i = 2; i <= 100; i++) Assert.True(pump.Post(new Frame(i)));
        }
        finally { release.Set(); }
        await latest.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 1, 100 }, seen.ToArray());
    }

    [Fact]
    public async Task LastFrameIsDeliveredEvenWhenNoMoreScreenChangesArrive()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var last = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pump = new LatestFramePump<Frame>(10, frame =>
        {
            if (frame.Number == 1) first.TrySetResult();
            if (frame.Number == 2) last.TrySetResult();
        });
        pump.Post(new Frame(1));
        await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        pump.Post(new Frame(2));
        await last.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StoppingIdleWorkerCompletesAndRejectsNewFrames()
    {
        var pump = new LatestFramePump<Frame>(30, _ => { });
        await pump.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pump.Post(new Frame(1)));
    }
}
