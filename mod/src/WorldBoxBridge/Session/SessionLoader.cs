using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldBoxBridge.Session;

/// <summary>
/// Loads a multi-agent <see cref="Session"/> from <c>agents.json</c>. If the file is
/// missing or unreadable, falls back to legacy single-token mode using the bridge's
/// shared <see cref="BridgeConfig.Token"/>.
/// </summary>
/// <remarks>
/// JSON format (see <c>docs/multi-agent.md</c>):
/// <code>
/// {
///   "scenario": "pvp",          // "pvp" | "coop" | "hierarchical" | "sandbox"
///   "partial_intel": true,
///   "turn_based": false,
///   "agents": [
///     {
///       "id": "athena",
///       "token": "&lt;random secret&gt;",
///       "role": "faction_player",
///       "kingdom_claim": "auto:0" // optional: "auto:&lt;ordinal&gt;" or "id:&lt;kingdom_id&gt;"
///     },
///     { "id": "ares", "token": "...", "role": "faction_player", "kingdom_claim": "auto:1" }
///   ]
/// }
/// </code>
/// JSON was picked over TOML because Newtonsoft.Json is already loaded by every command
/// in the bridge — pulling in a TOML parser would add a dep that BepInEx has to resolve.
/// </remarks>
internal static class SessionLoader
{
    public static Session Load(string agentsJsonPath, string legacyToken, ManualLogSource log)
    {
        if (!File.Exists(agentsJsonPath))
        {
            log.LogInfo($"[session] no '{agentsJsonPath}' — using legacy single-token mode.");
            return Session.Legacy(legacyToken);
        }

        try
        {
            var raw = File.ReadAllText(agentsJsonPath);
            var obj = JObject.Parse(raw);
            var scenario = (obj.Value<string?>("scenario") ?? "sandbox").Trim().ToLowerInvariant();
            var partialIntel = obj.Value<bool?>("partial_intel") ?? DefaultPartialIntel(scenario);
            var turnBased = obj.Value<bool?>("turn_based") ?? false;

            var agentsToken = obj["agents"] as JArray;
            if (agentsToken == null || agentsToken.Count == 0)
            {
                throw new InvalidDataException("'agents' must be a non-empty array.");
            }

            var agents = new List<Agent>(agentsToken.Count);
            foreach (var t in agentsToken)
            {
                if (t is not JObject ao)
                {
                    throw new InvalidDataException("each entry of 'agents' must be a JSON object.");
                }
                agents.Add(ParseAgent(ao));
            }

            var registry = new AgentRegistry(agents);

            // Build the optional TurnOrder. Explicit "turn_order": ["id1", "id2"] wins; otherwise
            // we fall back to agents in declaration order. Required only when turn_based=true.
            TurnOrder? turnOrder = null;
            if (turnBased)
            {
                IEnumerable<string> rotation;
                if (obj["turn_order"] is JArray turnArray && turnArray.Count > 0)
                {
                    rotation = turnArray
                        .Values<string>()
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Cast<string>()
                        .ToList();
                    foreach (var id in rotation)
                    {
                        if (registry.GetById(id) is null)
                        {
                            throw new InvalidDataException(
                                $"turn_order references unknown agent '{id}'."
                            );
                        }
                    }
                }
                else
                {
                    rotation = agents.Select(a => a.Id);
                }
                turnOrder = new TurnOrder(rotation);
            }

            log.LogInfo(
                $"[session] loaded '{agentsJsonPath}': scenario={scenario}, agents={registry.Count}, "
                    + $"partial_intel={partialIntel}, turn_based={turnBased}"
                    + (
                        turnOrder is not null
                            ? $", turn_order=[{string.Join(",", turnOrder.AgentIds)}]"
                            : ""
                    )
            );
            return new Session(registry, scenario, partialIntel, turnBased, turnOrder);
        }
        catch (Exception ex)
        {
            log.LogError(
                $"[session] FAILED to load '{agentsJsonPath}': {ex.Message}. "
                    + "Falling back to legacy single-token mode. Fix the file and restart the game."
            );
            return Session.Legacy(legacyToken);
        }
    }

    private static Agent ParseAgent(JObject ao)
    {
        var id = ao.Value<string?>("id")?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            throw new InvalidDataException("agent 'id' is required.");
        }
        var token = ao.Value<string?>("token")?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidDataException($"agent '{id}' missing 'token'.");
        }
        var roleStr = (ao.Value<string?>("role") ?? "god").Trim().ToLowerInvariant();
        var role = roleStr switch
        {
            "god" => AgentRole.God,
            "faction_player" or "faction" or "player" => AgentRole.FactionPlayer,
            "observer" => AgentRole.Observer,
            "narrator" => AgentRole.Narrator,
            _ => throw new InvalidDataException(
                $"agent '{id}' has unknown role '{roleStr}'. Use god | faction_player | observer | narrator."
            ),
        };
        var perms = PermissionDefaults.For(role);

        long? kingdomClaim = null;
        var claim = ao.Value<string?>("kingdom_claim")?.Trim();
        if (!string.IsNullOrEmpty(claim))
        {
            // "auto:N" → resolved at world-load time (a later phase picks the Nth alive kingdom).
            // "id:N"   → hard kingdom id, recorded immediately.
            // We store the numeric resolution only for "id:N"; "auto:N" is parked as null here
            // and resolved by ResolveAutoKingdomClaims() when the world is ready.
            if (claim!.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
            {
                if (!long.TryParse(claim.Substring(3), out var kid))
                {
                    throw new InvalidDataException(
                        $"agent '{id}' kingdom_claim '{claim}' — expected 'id:<int>'."
                    );
                }
                kingdomClaim = kid;
            }
            else if (!claim.StartsWith("auto:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"agent '{id}' kingdom_claim '{claim}' — must be 'auto:<ordinal>' or 'id:<int>'."
                );
            }
        }
        // Optional objectives per agent — informational metadata, the evaluator lives client-side.
        var objectives = new List<Objective>();
        if (ao["objectives"] is JArray oArr)
        {
            foreach (var ot in oArr)
            {
                if (ot is not JObject obj)
                    continue;
                var objId = obj.Value<string?>("id") ?? $"obj{objectives.Count}";
                var label = obj.Value<string?>("label") ?? objId;
                var kind = obj.Value<string?>("kind") ?? "freeform";
                var target = obj.Value<string?>("target");
                objectives.Add(new Objective(objId, label, kind, target));
            }
        }
        var objectiveSet = objectives.Count > 0 ? new ObjectiveSet(objectives) : null;
        return new Agent(id!, token!, role, kingdomClaim, perms, objectiveSet);
    }

    private static bool DefaultPartialIntel(string scenario) =>
        scenario switch
        {
            "pvp" => true,
            "coop" or "hierarchical" or "sandbox" => false,
            _ => false,
        };
}
