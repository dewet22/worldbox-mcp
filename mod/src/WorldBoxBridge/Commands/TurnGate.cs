using System;
using System.Collections.Generic;

namespace WorldBoxBridge.Commands;

/// <summary>
/// Decides which commands a <c>turn_based</c> session reserves for the current-turn agent.
/// </summary>
/// <remarks>
/// Pure logic with no Unity, BepInEx or Newtonsoft dependency, so it is linked into the test
/// project. <c>HttpBridge</c> itself cannot be, which is why this decision lives here rather
/// than as a private helper on it.
/// </remarks>
public static class TurnGate
{
    /// <summary>
    /// Commands that stay open to every agent even though their category is gated.
    /// <para>
    /// Closing a window is a shared unblock, not a move. An open window freezes the simulation
    /// for the whole session, and dismissing it restores the normal state instead of advancing
    /// anyone's position. It cannot destroy another agent's work either: no command opens a
    /// window, so the only ones that appear come from the game itself or from a human at the
    /// keyboard. Role gating still applies on top of this, <c>dismiss_window</c> requires
    /// <c>Permission.AdvanceTime</c>, which Observer and Narrator do not have.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> AlwaysAllowed = new(StringComparer.Ordinal)
    {
        "dismiss_window",
    };

    /// <summary>
    /// True when the command is reserved for the current-turn agent. <c>Action</c> modifies the
    /// world and <c>Control</c> changes the simulation flow, so both are gated. <c>Meta</c>,
    /// <c>Discovery</c>, <c>Read</c> and <c>Bus</c> stay open so spectators and read tools keep
    /// working whoever holds the turn.
    /// </summary>
    /// <param name="name">
    /// The command's registered name. Matched with the same ordinal comparison
    /// <c>CommandRegistry</c> uses to resolve it, so an unregistered spelling stays gated.
    /// </param>
    public static bool IsTurnGated(string name, CommandCategory category) =>
        (category is CommandCategory.Action or CommandCategory.Control)
        && !AlwaysAllowed.Contains(name);
}
