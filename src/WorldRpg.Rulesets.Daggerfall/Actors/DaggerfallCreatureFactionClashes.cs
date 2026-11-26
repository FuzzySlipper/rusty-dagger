using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Resolved pair of opposing creature factions and published actor definitions for a staged clash.</summary>
internal sealed record DaggerfallCreatureClashPlan(
    string FactionA,
    string FactionB,
    DaggerfallCreatureFactionRelation Relation,
    DaggerfallActorDefinition DefinitionA,
    DaggerfallActorDefinition DefinitionB,
    string? DrivingConditionTokenId = null);

/// <summary>Result of staging a live two-sided creature-faction clash in the active scene.</summary>
internal sealed record DaggerfallCreatureClashResult(
    bool Staged,
    string Message,
    string? FactionA = null,
    string? FactionB = null,
    DaggerfallCreatureFactionRelation Relation = DaggerfallCreatureFactionRelation.Neutral,
    IReadOnlyList<long>? SideAActorIds = null,
    IReadOnlyList<long>? SideBActorIds = null,
    string? DrivingConditionTokenId = null);

/// <summary>
/// Stages live two-sided creature-faction clashes (downstream of regional board-game conditions such as
/// <see cref="DaggerfallWorldConditionKind.MonsterSurge"/> or explicit faction pairs). Clashing actors spawn
/// facing each other with <c>LairTruce = false</c> and <c>FactionInfighting = true</c> so they may fight freely.
/// </summary>
internal sealed class DaggerfallCreatureFactionClashes(
    DaggerfallDefinitions definitions,
    DaggerfallActorPlacement placement,
    Func<int, bool> hasMobileSprite,
    Func<string, ActorPose, bool, bool, long> spawn)
{
    internal const int DefaultCountPerSide = 2;
    internal const int MaxCountPerSide = 6;
    internal const float DefaultDistanceMetres = 12f;

    /// <summary>
    /// Plans a clash pair either from explicit faction ids or downstream of active regional
    /// <see cref="DaggerfallWorldConditionToken"/>s (preferring two opposing active conditions, otherwise
    /// pairing the surging faction against one of its authored natural enemies).
    /// </summary>
    internal (DaggerfallCreatureClashPlan? Plan, string? Refusal) TryPlanClash(
        IReadOnlyList<DaggerfallWorldConditionToken> activeConditions,
        string? explicitFactionA = null,
        string? explicitFactionB = null)
    {
        ArgumentNullException.ThrowIfNull(activeConditions);
        DaggerfallCreatureFactionSet factions = definitions.CreatureFactions;

        string? factionA = NormalizeFactionArg(explicitFactionA);
        string? factionB = NormalizeFactionArg(explicitFactionB);
        string? drivingTokenId = null;

        if (factionA is null && factionB is null)
        {
            if (!TryPickFromConditions(factions, activeConditions, out factionA, out factionB, out drivingTokenId))
                return (null, "No active regional condition (e.g. monster-surge or border-tension) can drive a creature clash.");
        }
        else if (factionA is not null && factionB is null)
        {
            if (!factions.TryGet(factionA, out DaggerfallCreatureFactionDefinition? defA) || defA is null)
                return (null, $"Unknown creature faction '{factionA}'.");
            factionB = defA.Enemies.FirstOrDefault(enemy => PickDefinition(enemy) is not null);
            if (factionB is null)
                return (null, $"Creature faction '{factionA}' has no authored natural enemy with a published actor here.");
        }
        else if (factionA is null && factionB is not null)
        {
            if (!factions.TryGet(factionB, out DaggerfallCreatureFactionDefinition? defB) || defB is null)
                return (null, $"Unknown creature faction '{factionB}'.");
            factionA = defB.Enemies.FirstOrDefault(enemy => PickDefinition(enemy) is not null);
            if (factionA is null)
                return (null, $"Creature faction '{factionB}' has no authored natural enemy with a published actor here.");
        }

        if (!factions.TryGet(factionA, out _))
            return (null, $"Unknown creature faction '{factionA}'.");
        if (!factions.TryGet(factionB, out _))
            return (null, $"Unknown creature faction '{factionB}'.");

        DaggerfallCreatureFactionRelation relation = factions.Relation(factionA, factionB);
        if (relation == DaggerfallCreatureFactionRelation.Ally)
            return (null, $"Cannot stage a clash between allied factions '{factionA}' and '{factionB}'.");

        DaggerfallActorDefinition? def1 = PickDefinition(factionA!);
        if (def1 is null)
            return (null, $"No published combatant actor is available for creature faction '{factionA}' in this scene.");
        DaggerfallActorDefinition? def2 = PickDefinition(factionB!);
        if (def2 is null)
            return (null, $"No published combatant actor is available for creature faction '{factionB}' in this scene.");

        return (new DaggerfallCreatureClashPlan(factionA!, factionB!, relation, def1, def2, drivingTokenId), null);
    }

    /// <summary>
    /// Stages a live clash near <paramref name="origin"/> facing each other on clear floor (or at explicit opposing
    /// fallback poses when floor placement is unavailable), with <c>LairTruce = false</c> and <c>FactionInfighting = true</c>.
    /// </summary>
    internal DaggerfallCreatureClashResult StageClash(
        WorldPoint origin,
        float playerYawRadians,
        IReadOnlyList<DaggerfallWorldConditionToken> activeConditions,
        string? explicitFactionA = null,
        string? explicitFactionB = null,
        int countPerSide = DefaultCountPerSide,
        float distanceMetres = DefaultDistanceMetres,
        bool requireClearFloor = true)
    {
        if (countPerSide is < 1 or > MaxCountPerSide)
            return new(false, $"Clash count per side must be between 1 and {MaxCountPerSide}.");
        if (!float.IsFinite(distanceMetres) || distanceMetres < 4f || distanceMetres > 40f)
            return new(false, "Clash distance must be between 4 and 40 metres.");

        (DaggerfallCreatureClashPlan? plan, string? refusal) = TryPlanClash(activeConditions, explicitFactionA, explicitFactionB);
        if (plan is null)
            return new(false, refusal ?? "Could not plan creature-faction clash.");

        Vector3 forward = ActorHeading.Forward(playerYawRadians);
        Vector3 right = Vector3.Normalize(new Vector3(-forward.Z, 0f, forward.X));
        Vector3 arenaCenter = origin.ToVector() + forward * distanceMetres;
        Vector3 sideACenter = arenaCenter - right * 2.5f;
        Vector3 sideBCenter = arenaCenter + right * 2.5f;

        List<long> sideAIds = [];
        List<long> sideBIds = [];
        float facingAToB = ActorHeading.Yaw(sideBCenter - sideACenter);
        float facingBToA = ActorHeading.Yaw(sideACenter - sideBCenter);

        for (int i = 0; i < countPerSide; i++)
        {
            float rowOffset = (i - (countPerSide - 1) * 0.5f) * 1.8f;
            Vector3 desiredA = sideACenter + forward * rowOffset;
            Vector3 desiredB = sideBCenter + forward * rowOffset;

            ActorPose poseA;
            if (placement.TrySpawnPose($"clash:{plan.FactionA}:{i}", "daggerfall.creature-clash", WorldPoint.From(desiredA), 1.5f, 4.5f, out ActorPose foundA, SpawnDirection.Any))
                poseA = new(foundA.Position, ActorHeading.Yaw(sideBCenter - foundA.Position.ToVector()));
            else if (!requireClearFloor)
                poseA = new(WorldPoint.From(desiredA), facingAToB);
            else
                continue;

            sideAIds.Add(spawn(plan.DefinitionA.Id.Value, poseA, false, true));

            ActorPose poseB;
            if (placement.TrySpawnPose($"clash:{plan.FactionB}:{i}", "daggerfall.creature-clash", WorldPoint.From(desiredB), 1.5f, 4.5f, out ActorPose foundB, SpawnDirection.Any))
                poseB = new(foundB.Position, ActorHeading.Yaw(sideACenter - foundB.Position.ToVector()));
            else if (!requireClearFloor)
                poseB = new(WorldPoint.From(desiredB), facingBToA);
            else
                continue;

            sideBIds.Add(spawn(plan.DefinitionB.Id.Value, poseB, false, true));
        }

        if (sideAIds.Count == 0 || sideBIds.Count == 0)
            return new(false, $"Could not find clear floor to stage '{plan.FactionA}' vs '{plan.FactionB}' clash.");

        return new(
            Staged: true,
            Message: $"Staged {sideAIds.Count} {plan.FactionA} vs {sideBIds.Count} {plan.FactionB} ({plan.Relation}).",
            FactionA: plan.FactionA,
            FactionB: plan.FactionB,
            Relation: plan.Relation,
            SideAActorIds: sideAIds,
            SideBActorIds: sideBIds,
            DrivingConditionTokenId: plan.DrivingConditionTokenId);
    }

    private bool TryPickFromConditions(
        DaggerfallCreatureFactionSet factions,
        IReadOnlyList<DaggerfallWorldConditionToken> activeConditions,
        out string? factionA,
        out string? factionB,
        out string? drivingTokenId)
    {
        factionA = null;
        factionB = null;
        drivingTokenId = null;

        List<(DaggerfallWorldConditionToken Token, IReadOnlyList<string> Factions)> driven = [];
        foreach (DaggerfallWorldConditionToken token in activeConditions)
        {
            IReadOnlyList<string> ids = DaggerfallCreatureFactionPolicy.FactionsForCondition(token, factions);
            if (ids.Count > 0)
                driven.Add((token, ids));
        }
        if (driven.Count == 0)
            return false;

        // First prefer two distinct opposing active conditions in the same region (e.g. MonsterSurge(orcs) vs BorderTension or MonsterSurge(undead)).
        for (int i = 0; i < driven.Count; i++)
        {
            for (int j = i + 1; j < driven.Count; j++)
            {
                foreach (string a in driven[i].Factions)
                {
                    if (PickDefinition(a) is null) continue;
                    foreach (string b in driven[j].Factions)
                    {
                        if (factions.AreNaturalEnemies(a, b) && PickDefinition(b) is not null)
                        {
                            factionA = a;
                            factionB = b;
                            drivingTokenId = driven[i].Token.TokenId;
                            return true;
                        }
                    }
                }
            }
        }

        // Otherwise pair the primary surging/active condition faction with its first authored natural enemy that has a published actor.
        foreach ((DaggerfallWorldConditionToken token, IReadOnlyList<string> ids) in driven)
        {
            foreach (string a in ids)
            {
                if (PickDefinition(a) is null) continue;
                IReadOnlyList<string> rivals = DaggerfallCreatureFactionPolicy.RivalFactionsForCondition(factions, token);
                foreach (string b in rivals)
                {
                    if (factions.AreNaturalEnemies(a, b) && PickDefinition(b) is not null)
                    {
                        factionA = a;
                        factionB = b;
                        drivingTokenId = token.TokenId;
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private DaggerfallActorDefinition? PickDefinition(string factionId) =>
        definitions.Actors.Values
            .Where(a => a.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass
                && string.Equals(a.Team, factionId, StringComparison.Ordinal)
                && a.MobileId is int mid
                && definitions.Mobiles.Mobiles.TryGetValue(mid, out DaggerfallMobileDefinition? mobile)
                && mobile.IsPublished
                && hasMobileSprite(mid))
            .OrderBy(a => a.Level ?? 1)
            .ThenBy(a => a.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();

    private static string? NormalizeFactionArg(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string trimmed = raw.Trim().ToLowerInvariant();
        return trimmed is "auto" or "none" or "-" ? null : trimmed;
    }
}
