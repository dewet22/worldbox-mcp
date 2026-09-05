using FluentAssertions;
using WorldBoxBridge.Commands.Control;
using WorldBoxBridge.Commands.Read;
using WorldBoxBridge.Reflection;
using Xunit;

namespace WorldBoxBridge.Tests;

/// <summary>
/// The branch logic behind <c>dismiss_window</c> and <c>get_ui_state</c>. Both commands need
/// <c>JObject</c> and cannot be linked here, so the decisions live in
/// <see cref="WindowDismissal"/> and <see cref="UiStateReport"/> and are driven through
/// <see cref="IGameUiAccess"/> by <see cref="FakeUi"/>.
/// </summary>
public class GameUiAccessSeamTests
{
    /// <summary>
    /// Nullable on purpose: null is what the real implementation returns when a symbol is
    /// missing from the WorldBox build, and it is not the same as false.
    /// </summary>
    private sealed class FakeUi : IGameUiAccess
    {
        public bool? WindowActive { get; set; }
        public string? WindowId { get; set; }
        public bool HideSucceeds { get; set; } = true;
        public bool? Paused { get; set; }
        public int HideCalls { get; private set; }
        public int WindowIdReadsBeforeHide { get; private set; }

        public bool? IsWindowActive() => WindowActive;

        public string? CurrentWindowId()
        {
            if (HideCalls == 0)
            {
                WindowIdReadsBeforeHide++;
            }
            return WindowId;
        }

        public bool HideAllWindows()
        {
            HideCalls++;
            return HideSucceeds;
        }

        public bool? ConfigPaused => Paused;
    }

    // ─── dismiss_window ───────────────────────────────────────────────────

    [Fact]
    public void Nothing_open_reports_no_dismissal_and_leaves_the_game_alone()
    {
        var ui = new FakeUi { WindowActive = false, WindowId = "welcome" };
        var result = WindowDismissal.Run(ui);
        result.Dismissed.Should().BeFalse();
        result.Window.Should().BeNull();
        result.Unsupported.Should().BeFalse();
        ui.HideCalls.Should().Be(0);
    }

    [Fact]
    public void A_build_that_cannot_report_window_state_counts_as_nothing_open()
    {
        var ui = new FakeUi { WindowActive = null };
        WindowDismissal.Run(ui).Dismissed.Should().BeFalse();
        ui.HideCalls.Should().Be(0);
    }

    [Fact]
    public void An_open_window_is_closed_and_named()
    {
        var ui = new FakeUi { WindowActive = true, WindowId = "welcome" };
        var result = WindowDismissal.Run(ui);
        result.Dismissed.Should().BeTrue();
        result.Window.Should().Be("welcome");
        result.Unsupported.Should().BeFalse();
        ui.HideCalls.Should().Be(1);
    }

    [Fact]
    public void The_window_id_is_read_before_the_dismissal_not_after()
    {
        // Reading it afterwards would always report null: hideAllEvent leaves no current
        // window, so the caller would lose the one thing the response is for.
        var ui = new FakeUi { WindowActive = true, WindowId = "settings" };
        WindowDismissal.Run(ui);
        ui.WindowIdReadsBeforeHide.Should().Be(1);
    }

    [Fact]
    public void A_build_without_hideAllEvent_reports_unsupported_rather_than_success()
    {
        var ui = new FakeUi
        {
            WindowActive = true,
            WindowId = "welcome",
            HideSucceeds = false,
        };
        var result = WindowDismissal.Run(ui);
        result.Unsupported.Should().BeTrue();
        result.Dismissed.Should().BeFalse();
    }

    // ─── get_ui_state ─────────────────────────────────────────────────────

    [Fact]
    public void Report_passes_the_live_values_through()
    {
        var ui = new FakeUi
        {
            WindowActive = true,
            WindowId = "kingdom",
            Paused = true,
        };
        var report = UiStateReport.From(ui, effectivePaused: true, worldLoading: false);
        report.WindowActive.Should().BeTrue();
        report.CurrentWindow.Should().Be("kingdom");
        report.ConfigPaused.Should().BeTrue();
        report.EffectivePaused.Should().BeTrue();
        report.WorldLoading.Should().BeFalse();
    }

    [Fact]
    public void Every_missing_symbol_reads_as_not_blocked_rather_than_failing_the_read()
    {
        var ui = new FakeUi { WindowActive = null, Paused = null };
        var report = UiStateReport.From(ui, effectivePaused: null, worldLoading: null);
        report.WindowActive.Should().BeFalse();
        report.CurrentWindow.Should().BeNull();
        report.ConfigPaused.Should().BeFalse();
        report.EffectivePaused.Should().BeFalse();
        report.WorldLoading.Should().BeFalse();
    }
}
