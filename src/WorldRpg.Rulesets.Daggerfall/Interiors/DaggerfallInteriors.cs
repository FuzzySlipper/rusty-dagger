using System.Numerics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Places entered through a door, interiors and dungeons, as an arrival and a departure reach them: each profile's
/// action graph and triggers, admitted the first time the player is there and active while they are; a dungeon's visit
/// on its map, and the portal the player left it by; the buildings the player has been inside. The crossing
/// (<see cref="DaggerfallSiteCrossing"/>) calls it at fixed points of every site change; it names no session.
/// </summary>
internal sealed class DaggerfallInteriors(
    DaggerfallDungeonActionTriggerRuntime actionTriggers,
    Dictionary<DaggerfallWorldProfileKey, DaggerfallDungeonActionGraph> actionGraphs,
    Dictionary<DaggerfallWorldProfileKey, DaggerfallDungeonDiscovery> discoveries,
    Func<DaggerfallWorldProfileKey, DaggerfallSiteProfile, DaggerfallDungeonActionGraph> newActionGraph,
    DaggerfallSiteContext site)
{
    // How near the player must stand to a dungeon's portal marker to have left by it, in metres squared.
    private const float UsedPortalReach = 9f;

    /// <summary>The action-trigger runtime spanning every admitted profile.</summary>
    internal DaggerfallDungeonActionTriggerRuntime ActionTriggers { get; } = actionTriggers ?? throw new ArgumentNullException(nameof(actionTriggers));

    /// <summary>
    /// The profiles a session starts with action graphs for: the active one, and on a restore every
    /// profile the save carries action state for, in the stable order action graph save identity uses.
    /// Any other profile gains its graph when the session first enters it.
    /// </summary>
    internal static IReadOnlyList<(DaggerfallWorldProfileKey Key, DaggerfallSiteProfile Inputs)> ActionProfiles(
        DaggerfallSiteProfile active,
        DaggerfallSiteProfiles? profiles,
        IEnumerable<string>? savedProfileIds = null)
    {
        ArgumentNullException.ThrowIfNull(active);
        Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteProfile> admitted = new() { [active.ProfileKey] = active };
        foreach (string id in savedProfileIds ?? [])
        {
            if (StringComparer.Ordinal.Equals(id, active.ProfileKey.LogicalId)) continue;
            DaggerfallSiteProfile saved = (profiles ?? throw new ArgumentException($"Saved action state names profile '{id}' without an admitted site catalog.", nameof(profiles)))
                .RequireLogicalProfile(id);
            admitted.TryAdd(saved.ProfileKey, saved);
        }
        if (admitted.Keys.Select(key => key.LogicalId).Distinct(StringComparer.Ordinal).Count() != admitted.Count)
            throw new ArgumentException("Admitted world profiles must use distinct logical ids for action graph save identity.", nameof(profiles));
        return admitted.OrderBy(entry => entry.Key.Site.Region)
            .ThenBy(entry => entry.Key.Site.Index)
            .ThenBy(entry => entry.Key.LogicalId, StringComparer.Ordinal)
            .Select(entry => (entry.Key, entry.Value))
            .ToArray();
    }

    /// <summary>Admits a profile's action triggers, and its action graph the first time.</summary>
    internal void AdmitActions(DaggerfallWorldProfileKey key, DaggerfallSiteProfile inputs)
    {
        key.Validate();
        ArgumentNullException.ThrowIfNull(inputs);
        ActionTriggers.AdmitProfile(key, inputs);
        if (actionGraphs.ContainsKey(key)) return;
        if (inputs.ProfileKey != key)
            throw new InvalidOperationException($"World profile '{key.LogicalId}' action graph was paired with '{inputs.ProfileKey.LogicalId}'.");
        actionGraphs.Add(key, newActionGraph(key, inputs));
    }

    /// <summary>
    /// The player arrived at <paramref name="destination"/>: a dungeon's visit begins on its map, its actions are admitted
    /// and its triggers become the active ones, and a building entered is one the player has been inside.
    /// </summary>
    internal void Enter(DaggerfallWorldProfileKey destination, DaggerfallSiteProfile target)
    {
        if (target.DungeonMap is { } destinationMap)
        {
            if (!discoveries.TryGetValue(destination, out DaggerfallDungeonDiscovery? discovery))
                discoveries.Add(destination, discovery = new DaggerfallDungeonDiscovery(destination, destinationMap));
            discovery.BeginVisit();
        }
        AdmitActions(destination, target);
        ActionTriggers.ActivateProfile(destination);
        if (DaggerfallWorldProfileIds.TryParse(destination.LogicalId, out _, out var visitedBuilding) && visitedBuilding is { } building)
            site.DiscoverBuilding(destination.Site, building);
    }

    /// <summary>A failed site change leaves the player where they were: that profile's triggers are the active ones again.</summary>
    internal void Reactivate(DaggerfallWorldProfileKey source) => ActionTriggers.ActivateProfile(source);

    /// <summary>
    /// The player left <paramref name="source"/> for <paramref name="destination"/> from <paramref name="position"/>: on a
    /// dungeon's map, the portal to that destination they stood at is revealed.
    /// </summary>
    internal void Left(DaggerfallWorldProfileKey source, DaggerfallSiteProfile sourceInputs, DaggerfallWorldProfileKey destination,
        WorldPoint position)
    {
        if (!discoveries.TryGetValue(source, out DaggerfallDungeonDiscovery? sourceDiscovery) || sourceInputs.DungeonMap is not { } sourceMap)
            return;
        DaggerfallSiteMarker? usedPortal = sourceMap.Markers
            .Where(marker => marker.Kind == DaggerfallSiteMarkerKind.Portal
                && marker.DestinationLogicalProfile == destination.LogicalId)
            .OrderBy(marker => Vector3.DistanceSquared(marker.Position.ToVector(), position.ToVector()))
            .FirstOrDefault();
        if (usedPortal is not null && Vector3.DistanceSquared(usedPortal.Position.ToVector(), position.ToVector()) <= UsedPortalReach)
            sourceDiscovery.RevealMarker(usedPortal.Id);
    }
}
