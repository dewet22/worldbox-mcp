using WorldBoxBridge.Session;

namespace WorldBoxBridge.Commands.Action;

/// <summary>
/// The permission each Action-category command demands, in one place.
/// </summary>
/// <remarks>
/// This is a design decision rather than an implementation detail, and it drifted once:
/// <c>invoke_power</c> accepted <c>ActionFaction</c> while its sibling <c>paint_tile</c>
/// required <c>ActionGlobal</c>, so a FactionPlayer in a PvP session could drop a volcano
/// anywhere on the map. Nothing caught it because the two gates lived in two files and
/// neither could be linked into the test project. This class has no Unity dependency, so
/// the tests link it and lock the values.
/// </remarks>
public static class ActionPermissions
{
    /// <summary>
    /// Terraforming is map-wide: WorldBox has no per-kingdom semantics for tile types, so a
    /// FactionPlayer must not be able to reshape an opponent's territory.
    /// </summary>
    public const Permission PaintTile = Permission.ActionGlobal;

    /// <summary>
    /// God powers are the god-mode toolbar: meteors, nukes, plagues, world-wide toggles.
    /// None of them are scoped to a kingdom, so they carry the same gate as
    /// <see cref="PaintTile"/>. Faction-legitimate creature placement is not lost by this,
    /// it stays reachable through <c>spawn</c>, which covers every actor asset.
    /// </summary>
    public const Permission InvokePower = Permission.ActionGlobal;

    /// <summary>
    /// Placing an actor is the one action a FactionPlayer performs on its own behalf, so
    /// either scope satisfies it. Checked with
    /// <see cref="RequestContext.RequireAnyOf"/>, not <c>Require</c>: the value is a mask of
    /// alternatives, not a set of requirements.
    /// </summary>
    public const Permission Spawn = Permission.ActionFaction | Permission.ActionGlobal;
}
