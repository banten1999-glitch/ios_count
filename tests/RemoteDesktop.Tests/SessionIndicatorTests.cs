using FluentAssertions;
using RemoteDesktop.Core.Session;
using Xunit;

namespace RemoteDesktop.Tests;

public class SessionIndicatorTests
{
    private sealed class FakeIndicatorView : ISessionIndicatorView
    {
        public int ShowCalls;
        public int HideCalls;
        public IReadOnlyList<SessionIndicatorInfo> LastShown = Array.Empty<SessionIndicatorInfo>();
        public void Show(IReadOnlyList<SessionIndicatorInfo> active) { ShowCalls++; LastShown = active; }
        public void Hide() { HideCalls++; }
    }

    private static SessionIndicatorInfo Info(string id) =>
        new(id, id + "-name", DateTimeOffset.UnixEpoch);

    [Fact]
    public void Indicator_shows_while_a_session_is_active_and_hides_only_when_all_end()
    {
        var view = new FakeIndicatorView();
        var indicator = new MandatorySessionIndicator(view);

        indicator.IsVisible.Should().BeFalse();

        indicator.OnSessionStarted(Info("a"));
        indicator.IsVisible.Should().BeTrue();
        indicator.ActiveCount.Should().Be(1);

        indicator.OnSessionStarted(Info("b"));
        indicator.ActiveCount.Should().Be(2);
        view.LastShown.Should().HaveCount(2);

        indicator.OnSessionEnded("a");
        indicator.IsVisible.Should().BeTrue("one session is still active");
        view.HideCalls.Should().Be(0);

        indicator.OnSessionEnded("b");
        indicator.IsVisible.Should().BeFalse();
        view.HideCalls.Should().Be(1);
    }

    [Fact]
    public void Ending_an_unknown_session_does_not_hide_an_active_indicator()
    {
        var view = new FakeIndicatorView();
        var indicator = new MandatorySessionIndicator(view);
        indicator.OnSessionStarted(Info("a"));

        indicator.OnSessionEnded("nonexistent");

        indicator.IsVisible.Should().BeTrue();
        view.HideCalls.Should().Be(0);
    }
}
