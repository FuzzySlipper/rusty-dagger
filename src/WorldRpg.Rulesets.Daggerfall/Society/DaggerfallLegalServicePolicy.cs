using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Guilds;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Legal standing gate for town, temple, bank, and guild services. Characters who are wanted
/// (<c>RegionalReputation &lt; -10</c>) or banished in a region are refused by lawful providers,
/// while criminal guilds (Thieves Guild and Dark Brotherhood), fences/shady inns, and personal
/// allies with high disposition remain open so outlaw play is a viable path rather than a softlock.
/// </summary>
internal sealed class DaggerfallLegalServicePolicy(
    DaggerfallSocialState social,
    DaggerfallCrimeState crime,
    DaggerfallActorLedger actorLedger,
    DaggerfallDefinitions definitions,
    Func<long, FactionComponent?> actorFactions,
    Func<DaggerfallInteriorBuilding?> currentInteriorBuilding,
    Func<DaggerfallInteriorBuilding, int> buildingQuality)
{
    internal const int WantedReputationThreshold = -10;
    internal const int PawnShopBuildingType = 12;
    internal const int TavernBuildingType = 15;
    internal const int ShadyTavernMaxQuality = 8;
    internal const int PersonalAllyDispositionThreshold = 20;

    /// <summary>Whether the player is legally wanted or banished in <paramref name="region"/>.</summary>
    internal bool IsWantedOrBanished(int region) =>
        crime.BanishedRegions.Contains(region)
        || social.RegionalReputation(region) < WantedReputationThreshold;

    /// <summary>Whether <paramref name="npc"/> refuses service because the player is wanted or banished in its region.</summary>
    internal bool IsProviderDenied(DaggerfallNpc npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (!IsWantedOrBanished(npc.Site.Region))
            return false;
        return !IsOutlawExempt(npc, currentInteriorBuilding());
    }

    /// <summary>Whether room rental at <paramref name="building"/> in <paramref name="region"/> is denied to a wanted/banished player.</summary>
    internal bool IsRoomRentalDenied(int region, DaggerfallInteriorBuilding? building, DaggerfallNpc? tavernKeeper = null)
    {
        if (!IsWantedOrBanished(region))
            return false;
        if (tavernKeeper is not null && IsOutlawExempt(tavernKeeper, building))
            return false;
        return !IsShadyOrCriminalBuilding(building);
    }

    /// <summary>
    /// Whether <paramref name="npc"/> still serves a wanted or banished player: Thieves Guild or Dark Brotherhood
    /// affiliations, fences and shady inns, or personal allies whose disposition delta meets
    /// <see cref="PersonalAllyDispositionThreshold"/>.
    /// </summary>
    internal bool IsOutlawExempt(DaggerfallNpc npc, DaggerfallInteriorBuilding? building)
    {
        ArgumentNullException.ThrowIfNull(npc);

        // 1. Personal allies with high disposition in ActorLedger keep serving the player.
        if (actorLedger.TryGet(npc.DurableId, out ActorLedgerEntry? entry)
            && entry?.Social is { DispositionDelta: >= PersonalAllyDispositionThreshold })
            return true;

        // 2. Thieves Guild (42) and Dark Brotherhood (108) providers (including sub-factions).
        FactionComponent? fc = actorFactions(npc.DurableId);
        if (IsCriminalGuildFaction(fc?.PrimaryFactionId)
            || IsCriminalGuildFaction(npc.Appearance.FactionId)
            || IsCriminalGuildFaction(building?.FactionId))
            return true;

        // 3. Explicit fence/underworld role or service.
        if (string.Equals(npc.Role, "fence", StringComparison.OrdinalIgnoreCase)
            || string.Equals(npc.Role, "thief", StringComparison.OrdinalIgnoreCase)
            || string.Equals(npc.Role, "assassin", StringComparison.OrdinalIgnoreCase)
            || npc.Services.Contains("fence", StringComparer.OrdinalIgnoreCase))
            return true;

        // 4. Fences (pawn shops) and low-quality shady inns.
        return IsShadyOrCriminalBuilding(building);
    }

    internal bool IsShadyOrCriminalBuilding(DaggerfallInteriorBuilding? building)
    {
        if (building is null) return false;
        if (IsCriminalGuildFaction(building.FactionId)) return true;
        if (building.BuildingType == PawnShopBuildingType) return true;
        if (building.BuildingType == TavernBuildingType && buildingQuality(building) <= ShadyTavernMaxQuality) return true;
        return false;
    }

    private bool IsCriminalGuildFaction(int? factionId)
    {
        if (factionId is not int current || current <= 0) return false;
        for (int depth = 0; depth < 8 && current > 0; depth++)
        {
            if (current is DaggerfallConcreteGuildCatalog.ThievesFactionId
                or DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId)
                return true;
            if (!definitions.Factions.Factions.TryGetValue(current, out DaggerfallFactionDefinition? def))
                break;
            if (def.Parent == current) break;
            current = def.Parent;
        }
        return false;
    }
}
