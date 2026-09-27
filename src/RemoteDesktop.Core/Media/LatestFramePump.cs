using System.Diagnostics;
using System.Threading.Channels;

namespace RemoteDesktop.Core.Media;

/// <summary>One frame being processed and at most one newer frame waiting. Never queues history.</summary>
public sealed class LatestFramePump<T> : IAsyncDisposable where T : class
{
    private readonly Channel<T> _frames = Channel.CreateBounded<T>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        AllowSynchronousContinuations = false
    });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private double _intervalMs;

    public LatestFramePump(int framesPerSecond, Action<T> consume)
    {
        SetFrameRate(framesPerSecond);
        _worker = Task.Run(async () =>
        {
            var clock = Stopwatch.StartNew();
            double nextFrameMs = 0;
            try
            {
                while (await _frames.Reader.WaitToReadAsync(_stop.Token))
                {
                    var remaining = nextFrameMs - clock.Elapsed.TotalMilliseconds;
                    if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), _stop.Token);
                    _stop.Token.ThrowIfCancellationRequested();
                    if (!_frames.Reader.TryRead(out var latest)) continue;
                    var started = clock.Elapsed.TotalMilliseconds;
                    consume(latest);
                    nextFrameMs = started + Volatile.Read(ref _intervalMs);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        });
    }

    public bool Post(T frame) => _frames.Writer.TryWrite(frame);

    public void SetFrameRate(int framesPerSecond)
    {
        if (framesPerSecond < 1 || framesPerSecond > 120)
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        Volatile.Write(ref _intervalMs, 1000.0 / framesPerSecond);
    }

    public async ValueTask DisposeAsync()
    {
        _frames.Writer.TryComplete();
        _stop.Cancel();
        await _worker.ConfigureAwait(false);
        _stop.Dispose();
    }
}
