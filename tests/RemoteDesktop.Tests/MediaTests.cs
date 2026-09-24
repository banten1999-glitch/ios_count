using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using RemoteDesktop.Core.Media;
using Xunit;

namespace RemoteDesktop.Tests;

public class MediaTests
{
    [Fact]
    public void DisplayInfo_maps_normalized_corners_and_clamps()
    {
        var d = new DisplayInfo(0, @"\\.\DISPLAY1", X: 100, Y: 200, Width: 1920, Height: 1080,
            ScaleFactor: 1.0, IsPrimary: true);

        d.ToPixel(0, 0).Should().Be((100, 200));
        d.ToPixel(1, 1).Should().Be((100 + 1919, 200 + 1079));
        d.ToPixel(-5, 5).Should().Be((100, 200 + 1079), "out-of-range values clamp to [0,1]");
    }

    [Fact]
    public void DisplayLayout_falls_back_to_primary_for_unknown_index()
    {
        var layout = new DisplayLayout(new[]
        {
            new DisplayInfo(0, "A", 0, 0, 1920, 1080, 1.0, false),
            new DisplayInfo(1, "B", 1920, 0, 2560, 1440, 1.5, true),
        });

        layout.Primary.Index.Should().Be(1);
        layout.Select(99).Index.Should().Be(1, "unknown index falls back to primary");
        layout.Select(0).Index.Should().Be(0);
    }

    [Fact]
    public void QualityLadder_rejects_unordered_rungs()
    {
        var bad = () => new QualityLadder(new[]
        {
            new QualityRung("hi", 1.0, 30, 6000),
            new QualityRung("lo", 1.0, 15, 1500),
        });
        bad.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Adaptive_controller_downshifts_immediately_when_bandwidth_drops()
    {
        var clock = new FakeTimeProvider();
        var ctrl = new AdaptiveQualityController(QualityLadder.Default(), startIndex: 2, clock: clock);
        ctrl.Current.Name.Should().Be("High"); // 6000 kbps

        ctrl.Update(2000); // well below 6000*0.9
        ctrl.Current.Name.Should().Be("Balanced"); // 3000 kbps
    }

    [Fact]
    public void Adaptive_controller_upshifts_only_after_cooldown_and_with_headroom()
    {
        var clock = new FakeTimeProvider();
        var ctrl = new AdaptiveQualityController(QualityLadder.Default(), startIndex: 0,
            upCooldown: TimeSpan.FromSeconds(10), clock: clock);
        ctrl.Current.Name.Should().Be("Low"); // 1200 kbps, next Balanced=3000

        // Enough bandwidth for Balanced (3000*1.3=3900) but cooldown not elapsed yet.
        ctrl.Update(5000).Name.Should().Be("Low");

        clock.Advance(TimeSpan.FromSeconds(11));
        ctrl.Update(5000).Name.Should().Be("Balanced");
    }

    [Fact]
    public void Adaptive_controller_does_not_flap_on_marginal_estimates()
    {
        var clock = new FakeTimeProvider();
        var ctrl = new AdaptiveQualityController(QualityLadder.Default(), startIndex: 1, clock: clock);
        ctrl.Current.Name.Should().Be("Balanced"); // 3000

        // Just above current need but below next rung's headroom, past cooldown: stays put.
        clock.Advance(TimeSpan.FromSeconds(11));
        ctrl.Update(3200).Name.Should().Be("Balanced");
    }

    [Fact]
    public void FramePacer_admits_first_then_drops_while_in_flight()
    {
        var pacer = new FramePacer(targetFrameRate: 30); // ~33ms interval

        pacer.TryAdmit(0).Should().BeTrue();
        pacer.TryAdmit(100).Should().BeFalse("a frame is still in flight");

        pacer.CompleteSend();
        pacer.TryAdmit(101).Should().BeTrue("in-flight cleared and interval elapsed");
    }

    [Fact]
    public void FramePacer_enforces_minimum_interval()
    {
        var pacer = new FramePacer(targetFrameRate: 10); // 100ms interval
        pacer.TryAdmit(0).Should().BeTrue();
        pacer.CompleteSend();

        pacer.TryAdmit(50).Should().BeFalse("only 50ms since last admit, < 100ms");
        pacer.TryAdmit(100).Should().BeTrue();
    }
}
