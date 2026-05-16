using System;
using System.Linq;
using FluentAssertions;
using WorldBoxBridge.Session;
using Xunit;

namespace WorldBoxBridge.Tests;

public class MessageBusTests
{
    private static MessageBus TwoAgents() => new(new[] { "athena", "ares" });

    [Fact]
    public void Constructor_rejects_zero_agents()
    {
        FluentActions.Invoking(() => new MessageBus(Array.Empty<string>()))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*nobody could send or receive*");
    }

    [Fact]
    public void Constructor_rejects_zero_inbox_size()
    {
        FluentActions.Invoking(() => new MessageBus(new[] { "a" }, maxInboxSize: 0))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Send_assigns_increasing_seq_numbers()
    {
        var bus = TwoAgents();
        var s1 = bus.Send("athena", "ares", "diplomacy", "hello");
        var s2 = bus.Send("athena", "ares", "diplomacy", "again");
        s1.Should().Be(1);
        s2.Should().Be(2);
    }

    [Fact]
    public void Recv_returns_messages_addressed_to_caller_only()
    {
        var bus = new MessageBus(new[] { "a", "b", "c" });
        bus.Send("a", "b", null, "to b");
        bus.Send("a", "c", null, "to c");

        bus.Recv("b").Should().HaveCount(1).And.Subject.First().Content.Should().Be("to b");
        bus.Recv("c").Should().HaveCount(1).And.Subject.First().Content.Should().Be("to c");
        bus.Recv("a").Should().BeEmpty();
    }

    [Fact]
    public void Send_with_star_broadcasts_to_all_except_sender()
    {
        var bus = new MessageBus(new[] { "a", "b", "c" });
        bus.Send("a", "*", "alert", "war!");

        bus.Recv("a").Should().BeEmpty();
        bus.Recv("b").Should().HaveCount(1);
        bus.Recv("c").Should().HaveCount(1);
        bus.Recv("b").First().Content.Should().Be("war!");
    }

    [Fact]
    public void Send_to_unknown_recipient_throws_arg_exception()
    {
        var bus = TwoAgents();
        FluentActions.Invoking(() => bus.Send("athena", "ghost", null, "x"))
            .Should().Throw<ArgumentException>()
            .WithMessage("*not registered*");
    }

    [Fact]
    public void Send_from_unknown_sender_throws()
    {
        var bus = TwoAgents();
        FluentActions.Invoking(() => bus.Send("ghost", "athena", null, "x"))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Recv_supports_since_seq_cursor()
    {
        var bus = TwoAgents();
        bus.Send("athena", "ares", null, "1st");
        bus.Send("athena", "ares", null, "2nd");
        bus.Send("athena", "ares", null, "3rd");

        var first = bus.Recv("ares", sinceSeq: 0, max: 100);
        first.Should().HaveCount(3);

        var afterFirst = bus.Recv("ares", sinceSeq: first[0].Seq, max: 100);
        afterFirst.Should().HaveCount(2);
        afterFirst[0].Content.Should().Be("2nd");

        var afterAll = bus.Recv("ares", sinceSeq: first[^1].Seq, max: 100);
        afterAll.Should().BeEmpty();
    }

    [Fact]
    public void Recv_caps_at_max()
    {
        var bus = TwoAgents();
        for (var i = 0; i < 20; i++) bus.Send("athena", "ares", null, $"m{i}");

        var page = bus.Recv("ares", sinceSeq: 0, max: 5);
        page.Should().HaveCount(5);
        page[0].Content.Should().Be("m0");
        page[4].Content.Should().Be("m4");
    }

    [Fact]
    public void Bounded_inbox_drops_oldest_messages()
    {
        // Inbox capped at 3 → after 5 sends, only seqs 3..5 remain (oldest 2 dropped).
        var bus = new MessageBus(new[] { "a", "b" }, maxInboxSize: 3);
        for (var i = 1; i <= 5; i++) bus.Send("a", "b", null, $"m{i}");

        var all = bus.Recv("b", sinceSeq: 0, max: 100);
        all.Should().HaveCount(3);
        all.Select(m => m.Content).Should().Equal("m3", "m4", "m5");
    }

    [Fact]
    public void Delivered_count_reflects_post_fanout_seq()
    {
        // 3 agents → broadcast from "a" fans out to "b" and "c" → seq increments twice.
        var bus = new MessageBus(new[] { "a", "b", "c" });
        bus.Send("a", "*", null, "hi");
        bus.DeliveredCount.Should().Be(2);
    }

    [Fact]
    public void Recv_for_unknown_agent_throws()
    {
        var bus = TwoAgents();
        FluentActions.Invoking(() => bus.Recv("ghost"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*not registered*");
    }
}
