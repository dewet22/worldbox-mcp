using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using WorldBoxBridge.Threading;
using Xunit;

namespace WorldBoxBridge.Tests;

public class ConcurrencyGateTests
{
    /// <summary>Long enough that a slow CI runner never fails a test that should succeed.</summary>
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(5);

    /// <summary>Short enough that a test which must time out does not stall the suite.</summary>
    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(50);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Capacity_below_one_clamps_to_one(int requested)
    {
        // A gate of zero admits nobody, which would wedge the bridge rather than bound it.
        var gate = new ConcurrencyGate(requested);
        gate.Capacity.Should().Be(1);
        gate.TryEnter().Should().BeTrue();
    }

    [Fact]
    public void Entries_are_allowed_up_to_capacity_and_no_further()
    {
        var gate = new ConcurrencyGate(3);
        gate.TryEnter().Should().BeTrue();
        gate.TryEnter().Should().BeTrue();
        gate.TryEnter().Should().BeTrue();
        gate.TryEnter().Should().BeFalse();
    }

    [Fact]
    public void Available_tracks_the_slots_that_are_out()
    {
        var gate = new ConcurrencyGate(2);
        gate.Available.Should().Be(2);
        gate.TryEnter();
        gate.Available.Should().Be(1);
        gate.TryEnter();
        gate.Available.Should().Be(0);
        gate.Exit();
        gate.Available.Should().Be(1);
    }

    [Fact]
    public void Exit_frees_a_slot_for_the_next_caller()
    {
        var gate = new ConcurrencyGate(1);
        gate.TryEnter().Should().BeTrue();
        gate.TryEnter().Should().BeFalse();
        gate.Exit();
        gate.TryEnter().Should().BeTrue();
    }

    [Fact]
    public async Task Waiting_on_a_full_gate_gives_up_after_the_timeout()
    {
        var gate = new ConcurrencyGate(1);
        gate.TryEnter().Should().BeTrue();

        var admitted = await gate.TryEnterAsync(Brief, CancellationToken.None);

        admitted.Should().BeFalse();
    }

    [Fact]
    public async Task A_waiter_is_admitted_when_a_slot_frees_up()
    {
        var gate = new ConcurrencyGate(1);
        gate.TryEnter().Should().BeTrue();

        // Start the wait, then hand the slot back. Either order is safe: SemaphoreSlim returns
        // immediately when a slot is already free, so the assertion does not race the release.
        var waiting = gate.TryEnterAsync(Generous, CancellationToken.None);
        gate.Exit();

        (await waiting).Should().BeTrue();
    }

    [Fact]
    public async Task Cancelling_a_waiter_throws_rather_than_reporting_a_refusal()
    {
        // The bridge tells the two apart: a refusal is 503 BUSY, a cancellation is shutdown.
        var gate = new ConcurrencyGate(1);
        gate.TryEnter().Should().BeTrue();
        using var cts = new CancellationTokenSource();

        var waiting = gate.TryEnterAsync(Generous, cts.Token);
        cts.Cancel();

        await FluentActions
            .Awaiting(() => waiting)
            .Should()
            .ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Capacity_holds_under_contention()
    {
        const int capacity = 4;
        const int callers = 64;
        var gate = new ConcurrencyGate(capacity);
        var inside = 0;
        var peak = 0;
        var refused = 0;

        var runs = new Task[callers];
        for (var i = 0; i < callers; i++)
        {
            runs[i] = Task.Run(async () =>
            {
                if (!await gate.TryEnterAsync(Generous, CancellationToken.None))
                {
                    Interlocked.Increment(ref refused);
                    return;
                }
                try
                {
                    var now = Interlocked.Increment(ref inside);
                    var seen = Volatile.Read(ref peak);
                    while (now > seen)
                    {
                        var previous = Interlocked.CompareExchange(ref peak, now, seen);
                        if (previous == seen)
                        {
                            break;
                        }
                        seen = previous;
                    }
                    await Task.Yield();
                }
                finally
                {
                    Interlocked.Decrement(ref inside);
                    gate.Exit();
                }
            });
        }
        await Task.WhenAll(runs);

        peak.Should().BeLessThanOrEqualTo(capacity);
        refused.Should().Be(0, "short work under a generous timeout should all get through");
        gate.Available.Should().Be(capacity);
    }
}
