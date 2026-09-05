using FluentAssertions;
using WorldBoxBridge.Commands.Action;
using Xunit;

namespace WorldBoxBridge.Tests;

public class PulsePathTests
{
    [Fact]
    public void First_pulse_is_at_the_start_point()
    {
        var p = PulsePath.At(index: 0, pulses: 10, x: 5, y: 7, x2: 50, y2: 70);
        p.X.Should().Be(5);
        p.Y.Should().Be(7);
    }

    [Fact]
    public void Last_pulse_is_exactly_at_the_end_point()
    {
        var p = PulsePath.At(index: 9, pulses: 10, x: 5, y: 7, x2: 50, y2: 70);
        p.X.Should().Be(50);
        p.Y.Should().Be(70);
    }

    [Fact]
    public void Middle_pulse_lands_halfway()
    {
        var p = PulsePath.At(index: 1, pulses: 3, x: 0, y: 0, x2: 10, y2: 20);
        p.X.Should().Be(5);
        p.Y.Should().Be(10);
    }

    [Fact]
    public void Single_pulse_stays_at_the_start()
    {
        var p = PulsePath.At(index: 0, pulses: 1, x: 3, y: 4, x2: 90, y2: 90);
        p.X.Should().Be(3);
        p.Y.Should().Be(4);
    }

    [Fact]
    public void Degenerate_drag_with_equal_endpoints_stays_put()
    {
        var p = PulsePath.At(index: 7, pulses: 20, x: 12, y: 34, x2: 12, y2: 34);
        p.X.Should().Be(12);
        p.Y.Should().Be(34);
    }

    [Fact]
    public void Interpolation_never_leaves_the_bounding_box()
    {
        // Rounded lerp between two in-bounds endpoints must stay inside their bounding box,
        // which is what lets the command bounds-check only the endpoints up front.
        for (var i = 0; i < 50; i++)
        {
            var p = PulsePath.At(i, pulses: 50, x: 200, y: 10, x2: 3, y2: 255);
            p.X.Should().BeInRange(3, 200);
            p.Y.Should().BeInRange(10, 255);
        }
    }

    [Fact]
    public void Reverse_drags_interpolate_downwards()
    {
        var p = PulsePath.At(index: 2, pulses: 5, x: 100, y: 100, x2: 0, y2: 0);
        p.X.Should().Be(50);
        p.Y.Should().Be(50);
    }
}
