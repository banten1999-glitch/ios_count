using FluentAssertions;
using RemoteDesktop.Core.Input;
using RemoteDesktop.Core.Session;
using Xunit;

namespace RemoteDesktop.Tests;

internal sealed class RecordingInputSink : IInputSink
{
    public readonly List<string> Actions = new();
    public void MoveMouse(double x, double y, int monitorIndex) => Actions.Add($"move:{x:0.###},{y:0.###},{monitorIndex}");
    public void MouseButton(MouseButton button, bool isDown) => Actions.Add($"btn:{button}:{(isDown ? "down" : "up")}");
    public void MouseWheel(int dx, int dy) => Actions.Add($"wheel:{dx},{dy}");
    public void Key(int vk, bool isDown) => Actions.Add($"key:{vk}:{(isDown ? "down" : "up")}");
}

public class InputAndSessionTests
{
    [Fact]
    public void Executor_applies_events_and_drops_stale_sequences()
    {
        var sink = new RecordingInputSink();
        var exec = new RemoteInputExecutor(sink);

        exec.Apply(new MouseMoveEvent { Sequence = 1, X = 0.5, Y = 0.5 }).Should().BeTrue();
        exec.Apply(new MouseMoveEvent { Sequence = 3, X = 0.1, Y = 0.2 }).Should().BeTrue();
        exec.Apply(new MouseMoveEvent { Sequence = 2, X = 0.9, Y = 0.9 }).Should().BeFalse("seq 2 is stale");

        sink.Actions.Should().HaveCount(2);
    }

    [Fact]
    public void ReleaseAll_releases_every_held_key_and_button()
    {
        var sink = new RecordingInputSink();
        var exec = new RemoteInputExecutor(sink);

        exec.Apply(new KeyEvent { Sequence = 1, VirtualKey = 65, IsDown = true });   // 'A' down
        exec.Apply(new MouseButtonEvent { Sequence = 2, Button = MouseButton.Left, IsDown = true });
        exec.Apply(new KeyEvent { Sequence = 3, VirtualKey = 17, IsDown = true });    // Ctrl down

        sink.Actions.Clear();
        exec.ReleaseAll();

        sink.Actions.Should().Contain("key:65:up");
        sink.Actions.Should().Contain("key:17:up");
        sink.Actions.Should().Contain("btn:Left:up");
        exec.Tracker.PressedKeys.Should().BeEmpty();
        exec.Tracker.PressedButtons.Should().BeEmpty();
    }

    [Fact]
    public void Released_key_is_not_released_again()
    {
        var sink = new RecordingInputSink();
        var exec = new RemoteInputExecutor(sink);

        exec.Apply(new KeyEvent { Sequence = 1, VirtualKey = 65, IsDown = true });
        exec.Apply(new KeyEvent { Sequence = 2, VirtualKey = 65, IsDown = false });
        sink.Actions.Clear();

        exec.ReleaseAll();
        sink.Actions.Should().BeEmpty("the key was already released, nothing is held");
    }

    [Fact]
    public void ReleaseAll_continues_even_if_the_sink_throws()
    {
        var faulting = new ThrowingSink();
        var exec = new RemoteInputExecutor(faulting);
        exec.Apply(new KeyEvent { Sequence = 1, VirtualKey = 65, IsDown = true });
        exec.Apply(new KeyEvent { Sequence = 2, VirtualKey = 66, IsDown = true });

        var release = () => exec.ReleaseAll();
        release.Should().NotThrow();
        faulting.ReleaseAttempts.Should().Be(2, "both held keys must be attempted despite faults");
    }

    private sealed class ThrowingSink : IInputSink
    {
        public int ReleaseAttempts;
        public void MoveMouse(double x, double y, int monitorIndex) { }
        public void MouseButton(MouseButton button, bool isDown) { }
        public void MouseWheel(int dx, int dy) { }
        public void Key(int vk, bool isDown) { if (!isDown) { ReleaseAttempts++; throw new InvalidOperationException(); } }
    }

    [Fact]
    public void Codec_roundtrips_each_event_type()
    {
        InputEvent[] events =
        {
            new MouseMoveEvent { Sequence = 1, X = 0.25, Y = 0.75, MonitorIndex = 1 },
            new MouseButtonEvent { Sequence = 2, Button = MouseButton.Right, IsDown = true },
            new MouseWheelEvent { Sequence = 3, DeltaX = 0, DeltaY = 120 },
            new KeyEvent { Sequence = 4, VirtualKey = 0x1B, IsDown = false },
        };

        foreach (var e in events)
        {
            var decoded = InputEventCodec.TryDecode(InputEventCodec.Encode(e));
            decoded.Should().BeEquivalentTo(e);
        }
    }

    [Fact]
    public void Codec_returns_null_for_malformed_json()
    {
        InputEventCodec.TryDecode("{not valid").Should().BeNull();
    }

    [Fact]
    public void Host_session_releases_input_on_stop_and_ignores_later_messages()
    {
        var sink = new RecordingInputSink();
        using var session = new HostControlSession(sink);
        SessionState? observed = null;
        session.StateChanged += s => observed = s;

        session.HandleDataChannelMessage(InputEventCodec.Encode(
            new KeyEvent { Sequence = 1, VirtualKey = 65, IsDown = true })).Should().BeTrue();

        session.Stop();

        sink.Actions.Should().Contain("key:65:up");
        observed.Should().Be(SessionState.Disconnected);

        // Messages after stop are ignored.
        session.HandleDataChannelMessage(InputEventCodec.Encode(
            new KeyEvent { Sequence = 2, VirtualKey = 66, IsDown = true })).Should().BeFalse();
    }

    [Fact]
    public void Host_session_stop_is_idempotent()
    {
        var sink = new RecordingInputSink();
        var session = new HostControlSession(sink);
        int stateEvents = 0;
        session.StateChanged += _ => stateEvents++;

        session.Stop();
        session.Stop();

        stateEvents.Should().Be(1);
    }

    [Fact]
    public void Reconnect_policy_bounds_attempts()
    {
        var policy = new ReconnectPolicy(maxAttempts: 3, baseDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromSeconds(10), rng: new Random(1));

        policy.NextDelay(1).Should().NotBeNull();
        policy.NextDelay(3).Should().NotBeNull();
        policy.NextDelay(4).Should().BeNull("attempts beyond the max mean give up and show Disconnected");
        policy.NextDelay(0).Should().BeNull();
    }

    [Fact]
    public void Reconnect_delay_is_capped()
    {
        var policy = new ReconnectPolicy(maxAttempts: 20, baseDelay: TimeSpan.FromSeconds(1),
            maxDelay: TimeSpan.FromSeconds(5), rng: new Random(2));

        // With +20% jitter, an attempt far past the cap stays within maxDelay*1.2.
        policy.NextDelay(20)!.Value.Should().BeLessThan(TimeSpan.FromSeconds(6));
    }
}
