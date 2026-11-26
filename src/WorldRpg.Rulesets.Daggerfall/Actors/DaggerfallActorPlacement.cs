using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Where a placement ray starts relative to the player's facing.</summary>
internal enum SpawnDirection
{
    /// <summary>Anywhere in the half behind the player.</summary>
    Behind,
    /// <summary>
    /// The donor's line-of-sight spawn: just past the edge of the view, at the camera's field of view
    /// plus up to four degrees, to the left or right.
    /// </summary>
    BesideView,
    /// <summary>Any direction, as the donor places a foe that need not stay out of sight.</summary>
    Any,
    /// <summary>In front of the player, up to <see cref="DaggerfallActorPlacement.AheadDegrees"/> either side of the facing.</summary>
    Ahead,
}

/// <summary>
/// Where actors can be placed and whom they see as foes: clear spawn poses around the player, the enemies a
/// perception query finds, the team rule that picks whom an enemy or ally pursues, and where everyone stands, used by
/// summons, law, quests, encounters, spells, allies and enemy behaviour.
/// </summary>
/// <param name="isGameplayActive">Whether an actor answers the world now (the NPC registry's presence).</param>
/// <param name="definitions">Every live actor's definition by durable identity (the roster's).</param>
/// <param name="foeTeam">The team an actor fights for now, quest overrides included.</param>
/// <param name="isPacified">Whether an enemy is pacified.</param>
/// <param name="allowsFoeInfighting">Whether an actor may fight other actors and be attacked by the player's allies (quests protect some).</param>
/// <param name="restrained">Whether a quest holds an actor back from acting.</param>
/// <param name="joinsInfighting">Whether a quest opted an actor into fights between actors (the classic foe-infighting operation).</param>
/// <param name="infightingActive">Whether any active quest has opted foes into infighting.</param>
/// <param name="environment">The world environment the player's placement queries run against now.</param>
/// <param name="detectionDistance">How far an enemy or ally looks for a foe, at least the donor's sight radius.</param>
/// <param name="fieldOfViewYDegrees">The camera's vertical field of view, the edge a beside-view spawn starts past.</param>
internal sealed class DaggerfallActorPlacement(ActorsState actors, PlayerControlState player, Func<long, bool> isGameplayActive,
    IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions, Func<long, string?> foeTeam, Func<long, bool> isPacified,
    Func<long, bool> allowsFoeInfighting, Func<long, bool> restrained, Func<long, bool> joinsInfighting, Func<bool> infightingActive,
    SpatialMovementSystem spatial, IPerceptionService perception, IRandomService random,
    Func<CharacterStepEnvironment> environment, double detectionDistance, float fieldOfViewYDegrees, IActorSight? sight = null,
    DaggerfallCreatureFactionSet? creatureFactions = null,
    Func<long, bool>? isUnderLairTruce = null,
    Action<long>? liftLairTruce = null,
    Func<long, bool>? joinsFactionInfighting = null,
    Func<bool>? hasFactionInfightingPotential = null,
    Func<string?, string?, bool>? boardConditionDrivesClash = null,
    Func<long, bool>? isProvoked = null)
{
    private readonly IActorSight _sight = sight ?? PoseSight.Instance;
    private readonly DaggerfallCreatureFactionSet _creatureFactions = creatureFactions ?? DaggerfallCreatureFactionSet.ClassicDefault;
    private readonly Func<long, bool> _isUnderLairTruce = isUnderLairTruce ?? (_ => false);
    private readonly Action<long> _liftLairTruce = liftLairTruce ?? (_ => { });
    private readonly Func<long, bool> _joinsFactionInfighting = joinsFactionInfighting ?? (_ => false);
    private readonly Func<bool> _hasFactionInfightingPotential = hasFactionInfightingPotential ?? (() => false);
    private readonly Func<string?, string?, bool> _boardConditionDrivesClash = boardConditionDrivesClash ?? ((_, _) => false);
    private readonly Func<long, bool> _isProvoked = isProvoked ?? (_ => false);

    // FoeSpawner's placement envelope, in normalized world metres.
    private const float SummonFloorDistance = 4f;
    internal const float SummonSeparation = 1.25f;
    private const float SummonClearanceRadius = .65f;
    private const int SummonPlacementAttempts = 8;
    /// <summary>How far either side of the facing an <see cref="SpawnDirection.Ahead"/> pose may be.</summary>
    internal const int AheadDegrees = 30;

    /// <summary>
    /// Whom an enemy or ally pursues, the team rule: none while a quest holds it back or it is defeated; for an ally of
    /// the player, the nearest visible foe it may attack, where it may fight at all; for anyone else the player, unless
    /// a quest's infighting or creature-faction enmity (under <c>LairTruce</c> for unalerted static dungeon placements,
    /// and free for overland bands, staged clashes, and board-game surges) lets it take a nearer visible rival.
    /// </summary>
    internal PursuitTarget? SelectTarget(long observerId)
    {
        if (!actors.TryGet(observerId, out var observer) || observer.IsDefeated || restrained(observerId)) return null;
        string? team = EffectiveTeam(observerId);
        if (team == "player-ally")
            return allowsFoeInfighting(observerId) ? SelectAllyTarget(observerId) : null;
        PursuitTarget? selected = player.Position is { } position ? new(DaggerfallActorIdentity.PlayerEntityId, position) : null;
        if (!allowsFoeInfighting(observerId)) return selected;

        bool questInfighting = infightingActive();
        bool observerPacified = isPacified(observerId);
        bool observerUnderTruce = _isUnderLairTruce(observerId);
        bool observerClash = !observerUnderTruce && _joinsFactionInfighting(observerId);
        bool creatureInfightingPossible = !observerUnderTruce && !observerPacified
            && (observerClash || _hasFactionInfightingPotential());
        if (!questInfighting && !creatureInfightingPossible) return selected;

        double best = player.Position is { } standing ? Vector3.Distance(observer.Position.ToVector(), standing.ToVector()) : double.PositiveInfinity;
        Vector3 forward = ActorHeading.Forward(observer.HeadingYawRadians);
        double facingCosine = _isProvoked(observerId) ? -1d : DaggerfallPerceptionQueryDefaults.MinimumFacingCosine;
        foreach (PerceptionPair pair in QueryEnemies(observerId, observer.Position, forward,
                     Math.Max(detectionDistance, DaggerfallPerceptionQueryDefaults.SightRadius),
                     facingCosine, _ => true)
                     .Where(pair => pair.Kind == PerceptionPairKind.Visible).OrderBy(pair => pair.Distance).ThenBy(pair => pair.Target))
        {
            long id = checked((long)pair.Target);
            if (!allowsFoeInfighting(id) || pair.Distance >= best) continue;
            string? targetTeam = EffectiveTeam(id);

            // Classic quest foe-infighting opt-in (existing contract, unchanged).
            bool questEligible = questInfighting
                && (joinsInfighting(observerId) || joinsInfighting(id))
                && _creatureFactions.AreTeamsHostile(team, targetTeam, observerPacified);

            // Creature-faction relations under LairTruce: natural enemies may fight once neither side is held
            // under static-dungeon LairTruce, when driven by overland/staged clash membership or a regional board-game condition.
            // Luring an already-alerted enemy into close sight (<= LairTruceLureRadius) of a dormant natural enemy lifts its truce.
            bool factionEligible = false;
            if (!questEligible && creatureInfightingPossible && _creatureFactions.AreNaturalEnemies(team, targetTeam))
            {
                bool clashDriven = observerClash || _joinsFactionInfighting(id) || _boardConditionDrivesClash(team, targetTeam);
                if (clashDriven)
                {
                    bool targetUnderTruce = _isUnderLairTruce(id);
                    if (targetUnderTruce && pair.Distance <= Modules.Behavior.DaggerfallEnemyPerceptionMemory.LairTruceLureRadius)
                    {
                        _liftLairTruce(id);
                        targetUnderTruce = false;
                    }
                    factionEligible = !targetUnderTruce;
                }
            }

            if (!questEligible && !factionEligible) continue;
            selected = new(id, actors.Get(id).Position); best = pair.Distance;
        }
        return selected;
    }

    private string? EffectiveTeam(long actorId) =>
        foeTeam(actorId)
        ?? (actors.TryGet(actorId, out ActorState? actor) && actor.Actor.TryGet<FactionComponent>(out FactionComponent? faction)
            ? faction.EffectiveTeam
            : definitions.TryGetValue(actorId, out DaggerfallActorDefinition? definition) ? definition.Team : null);

    /// <summary>The nearest visible foe of the player's side an ally can attack, if any.</summary>
    internal PursuitTarget? SelectAllyTarget(long observerId)
    {
        if (!actors.TryGet(observerId, out var observer) || observer.IsDefeated) return null;
        Vector3 forward = ActorHeading.Forward(observer.HeadingYawRadians);
        foreach (PerceptionPair pair in QueryEnemies(observerId, observer.Position, forward,
                     Math.Max(detectionDistance, DaggerfallPerceptionQueryDefaults.SightRadius),
                     DaggerfallPerceptionQueryDefaults.MinimumFacingCosine,
                     _ => true)
                     .Where(pair => pair.Kind == PerceptionPairKind.Visible)
                     .OrderBy(pair => pair.Distance).ThenBy(pair => pair.Target))
        {
            long id = checked((long)pair.Target);
            if (EffectiveTeam(id) == "player-ally") continue;
            // EnemySenses excludes pacified enemies and protected quest actors from ally attacks.
            if (isPacified(id) || !allowsFoeInfighting(id)) continue;
            return new PursuitTarget(id, actors.Get(id).Position);
        }
        return null;
    }

    /// <summary>
    /// The perception pairs between one observer and every gameplay-active, undefeated monster or enemy class that
    /// <paramref name="eligible"/> admits with its current team, page by page.
    /// </summary>
    internal IEnumerable<PerceptionPair> QueryEnemies(long observerId, WorldPoint origin, Vector3 forward,
        double radius, double facing, Func<DaggerfallActorDefinition, bool> eligible)
    {
        // One query sights every foe along lines lifted evenly off the floor, by the watcher's own sight height
        // (IActorSight.Line of the watcher with itself), so each distance and facing stays the poses'.
        (Vector3 eye, _) = _sight.Line(observerId, origin, observerId, origin);
        Vector3 lift = eye - origin.ToVector();
        PerceptionTarget[] targets = actors.All.Where(actor => actor.DurableId != observerId && !actor.IsDefeated
                && isGameplayActive(actor.DurableId)
                && definitions.TryGetValue(actor.DurableId, out var definition)
                && (actor.Actor.TryGet<ActorHarmPolicy>(out ActorHarmPolicy? harm)
                    ? harm.IsCombatant
                    : definition.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass)
                && eligible(definition with { Team = EffectiveTeam(actor.DurableId) }))
            .OrderBy(actor => actor.DurableId)
            .Select(actor => new PerceptionTarget(checked((ulong)actor.DurableId), actor.Position.ToVector() + lift)).ToArray();
        PerceptionQueryRequest request = new(spatial.Session,
            new[] { new PerceptionObserver(checked((ulong)observerId), eye, forward, radius, facing, 1d) },
            targets, ReadOnlyMemory<SpatialEntityCollider>.Empty, 0, 64);
        PerceptionReadoutResult receipt;
        do
        {
            receipt = perception.QueryVisibility(request);
            foreach (PerceptionPair pair in receipt.Pairs.ToArray()) yield return pair;
            if (receipt.HasNextPairCursor)
                request = request with { PairCursor = receipt.NextPairCursor };
        } while (receipt.HasNextPairCursor);
    }

    /// <summary>
    /// A clear pose around <paramref name="origin"/> on a floor, facing it, with room for a whole controller capsule,
    /// drawn with keyed draws under <paramref name="operation"/> in <paramref name="randomScope"/>; false after
    /// eight refused attempts.
    /// </summary>
    internal bool TrySpawnPose(string operation, string randomScope, WorldPoint origin, float minimumDistance, float maximumDistance, out ActorPose pose,
        SpawnDirection direction = SpawnDirection.Behind)
    {
        CharacterStepEnvironment stepEnvironment = environment();
        SpatialEntityCollider[] colliders = Colliders();
        SpatialEntityCollider[] rayActors = colliders.Where(actor => actor.Entity != actors.Player.Actor.Entity.Value).ToArray();
        for (int attempt = 0; attempt < SummonPlacementAttempts; attempt++)
        {
            string key = $"{operation}:attempt:{attempt}";
            // Like FoeSpawner, start outside the forward view unless the caller places in any direction.
            // A refused placement does not consume the source.
            float offsetDegrees = direction switch
            {
                SpawnDirection.BesideView => (fieldOfViewYDegrees
                    + (float)random.DrawKeyed(new(0, randomScope, key + ":angle", 0, 4)).Value)
                    * (random.DrawKeyed(new(0, randomScope, key + ":side", 0, 1)).Value == 0 ? -1f : 1f),
                SpawnDirection.Any => random.DrawKeyed(new(0, randomScope, key + ":angle", 0, 359)).Value,
                SpawnDirection.Ahead => random.DrawKeyed(new(0, randomScope, key + ":angle", 0, 2 * AheadDegrees)).Value - AheadDegrees,
                _ => random.DrawKeyed(new(0, randomScope, key + ":angle", 90, 270)).Value,
            };
            Vector3 rayDirection = ActorHeading.Forward(player.YawRadians + offsetDegrees * MathF.PI / 180f);
            float distance = (float)random.DrawKeyed(new(0, randomScope, key + ":distance", (int)minimumDistance, (int)maximumDistance)).Value;
            SpatialHit wall = spatial.CastRay(origin.ToVector(), rayDirection, maximumDistance, rayActors, stepEnvironment);
            if (wall.StartSolid) continue;
            if (wall.Present)
            {
                float cosine = Vector3.Dot(-rayDirection, wall.Normal);
                if (cosine <= 0f) continue;
                distance = MathF.Min(distance, (float)wall.Distance - SummonSeparation / cosine);
                if (distance < minimumDistance) continue;
            }
            Vector3 candidate = origin.ToVector() + rayDirection * distance;
            if (ClearFloor(candidate, colliders, stepEnvironment) is not { } feet) continue;
            pose = new(feet, MathF.Atan2(origin.X - feet.X, -(origin.Z - feet.Z)));
            return true;
        }
        pose = default;
        return false;
    }

    /// <summary>
    /// Where an actor can stand at <paramref name="at"/>: the floor under it (or up to a separation above it) with room
    /// for a whole controller capsule, clear of every actor but <paramref name="moving"/> (those about to be moved);
    /// null when there is none.
    /// </summary>
    internal WorldPoint? StandingAt(Vector3 at, IReadOnlySet<long> moving) =>
        ClearFloor(at + Vector3.UnitY * SummonSeparation, Colliders(moving), environment());

    // These are call-local spawn-clearance envelopes, not another retained collision world.
    private SpatialEntityCollider[] Colliders(IReadOnlySet<long>? moving = null) => [.. actors.All
        .Where(actor => !actor.IsDefeated && isGameplayActive(actor.DurableId) && moving?.Contains(actor.DurableId) != true).Select(actor =>
        {
            Vector3 center = actor.Position.ToVector() + Vector3.UnitY * SummonSeparation;
            return new SpatialEntityCollider(actor.Actor.Entity.Value, center - new Vector3(SummonClearanceRadius),
                center + new Vector3(SummonClearanceRadius), 0, 0, true, false, false);
        }).Append(spatial.ProjectCharacterCollider(player, actors.Player.Actor.Entity.Value))];

    private WorldPoint? ClearFloor(Vector3 above, SpatialEntityCollider[] colliders, CharacterStepEnvironment stepEnvironment)
    {
        // The floor is the world's: a look down that started inside another actor's clearance box would otherwise
        // take that box's underside for floor and stand the new actor inside it.
        SpatialHit floor = spatial.CastRay(above, -Vector3.UnitY, SummonFloorDistance, ReadOnlyMemory<SpatialEntityCollider>.Empty, stepEnvironment);
        if (!floor.Present || floor.StartSolid || floor.Normal.Y <= 0f) return null;
        SpatialHit clearance = spatial.OverlapCapsule(floor.Point + Vector3.UnitY * SummonSeparation,
            0d, SummonClearanceRadius, colliders, stepEnvironment);
        if (clearance.Present) return null;
        // Actor poses are feet. Admit the entire controller capsule at its projected center,
        // not only the donor's small separation sphere, before handing this pose to the roster.
        CharacterShapeConfig shape = spatial.CurrentController.Shape;
        Vector3 center = floor.Point + Vector3.UnitY * (shape.StandingHeight * .5f + shape.ContactSkin + shape.ClearancePadding);
        SpatialHit body = spatial.OverlapCapsule(center, shape.StandingHeight * .5f - shape.Radius,
            shape.Radius + shape.ClearancePadding, colliders, stepEnvironment);
        return body.Present ? null : WorldPoint.From(floor.Point);
    }

    /// <summary>Where the player and every gameplay-active actor stand now.</summary>
    internal IReadOnlyDictionary<long, WorldPoint> CurrentPositions()
    {
        Dictionary<long, WorldPoint> positions = [];
        if (player.Position is WorldPoint standing) positions.Add(DaggerfallActorIdentity.PlayerEntityId, standing);
        foreach (ActorState actor in actors.All.Where(actor => isGameplayActive(actor.DurableId)))
            positions[actor.DurableId] = actor.Position;
        return positions;
    }
}
