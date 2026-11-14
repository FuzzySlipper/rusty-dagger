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
}

/// <summary>
/// Where actors can be placed and whom they see as foes: clear spawn poses around the player, the enemies a
/// perception query finds, an ally's target and where everyone stands, used by summons, law, quests, encounters,
/// spells and allies.
/// </summary>
/// <param name="isGameplayActive">Whether an actor answers the world now (the NPC registry's presence).</param>
/// <param name="definitions">Every live actor's definition by durable identity (the roster's).</param>
/// <param name="foeTeam">The team an actor fights for now, quest overrides included.</param>
/// <param name="isPacified">Whether an enemy is pacified.</param>
/// <param name="allowsFoeInfighting">Whether an actor may be attacked by the player's allies (quests protect some).</param>
/// <param name="environment">The world environment the player's placement queries run against now.</param>
/// <param name="allyDetectionDistance">How far an ally looks for a foe, at least the donor's sight radius.</param>
/// <param name="fieldOfViewYDegrees">The camera's vertical field of view, the edge a beside-view spawn starts past.</param>
internal sealed class DaggerfallActorPlacement(ActorsState actors, PlayerControlState player, Func<long, bool> isGameplayActive,
    IReadOnlyDictionary<long, DaggerfallActorDefinition> definitions, Func<long, string?> foeTeam, Func<long, bool> isPacified,
    Func<long, bool> allowsFoeInfighting, SpatialMovementSystem spatial, IPerceptionService perception, IRandomService random,
    Func<CharacterStepEnvironment> environment, double allyDetectionDistance, float fieldOfViewYDegrees)
{
    // FoeSpawner's placement envelope, in normalized world metres.
    private const float SummonFloorDistance = 4f;
    internal const float SummonSeparation = 1.25f;
    private const float SummonClearanceRadius = .65f;
    private const int SummonPlacementAttempts = 8;

    /// <summary>The nearest visible foe of the player's side an ally can attack, if any.</summary>
    internal PursuitTarget? SelectAllyTarget(long observerId)
    {
        if (!actors.TryGet(observerId, out var observer) || observer.IsDefeated) return null;
        Vector3 forward = ActorHeading.Forward(observer.HeadingYawRadians);
        foreach (PerceptionPair pair in QueryEnemies(observerId, observer.Position, forward,
                     Math.Max(allyDetectionDistance, DaggerfallPerceptionQueryDefaults.SightRadius),
                     DaggerfallPerceptionQueryDefaults.MinimumFacingCosine,
                     definition => definition.Team != "player-ally")
                     .Where(pair => pair.Kind == PerceptionPairKind.Visible)
                     .OrderBy(pair => pair.Distance).ThenBy(pair => pair.Target))
        {
            long id = checked((long)pair.Target);
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
        PerceptionTarget[] targets = actors.All.Where(actor => actor.DurableId != observerId && !actor.IsDefeated
                && isGameplayActive(actor.DurableId)
                && definitions.TryGetValue(actor.DurableId, out var definition)
                && definition.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass && eligible(definition with { Team = foeTeam(actor.DurableId) }))
            .OrderBy(actor => actor.DurableId)
            .Select(actor => new PerceptionTarget(checked((ulong)actor.DurableId), actor.Position.ToVector())).ToArray();
        PerceptionQueryRequest request = new(spatial.Session,
            new[] { new PerceptionObserver(checked((ulong)observerId), origin.ToVector(), forward, radius, facing, 1d) },
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
        // These are call-local spawn-clearance envelopes, not another retained collision world.
        SpatialEntityCollider[] colliders = actors.All.Where(actor => !actor.IsDefeated && isGameplayActive(actor.DurableId)).Select(actor =>
        {
            Vector3 center = actor.Position.ToVector() + Vector3.UnitY * SummonSeparation;
            return new SpatialEntityCollider(actor.Actor.Entity.Value, center - new Vector3(SummonClearanceRadius),
                center + new Vector3(SummonClearanceRadius), 0, 0, true, false, false);
        }).Append(spatial.ProjectCharacterCollider(player, actors.Player.Actor.Entity.Value)).ToArray();
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
            SpatialHit floor = spatial.CastRay(candidate, -Vector3.UnitY, SummonFloorDistance, colliders, stepEnvironment);
            if (!floor.Present || floor.StartSolid || floor.Normal.Y <= 0f) continue;
            SpatialHit clearance = spatial.OverlapCapsule(floor.Point + Vector3.UnitY * SummonSeparation,
                0d, SummonClearanceRadius, colliders, stepEnvironment);
            if (clearance.Present) continue;
            // Actor poses are feet. Admit the entire controller capsule at its projected center,
            // not only the donor's small separation sphere, before handing this pose to the roster.
            CharacterShapeConfig shape = spatial.CurrentController.Shape;
            Vector3 center = floor.Point + Vector3.UnitY * (shape.StandingHeight * .5f + shape.ContactSkin + shape.ClearancePadding);
            SpatialHit body = spatial.OverlapCapsule(center, shape.StandingHeight * .5f - shape.Radius,
                shape.Radius + shape.ClearancePadding, colliders, stepEnvironment);
            if (body.Present) continue;
            pose = new(WorldPoint.From(floor.Point), MathF.Atan2(origin.X - floor.Point.X, -(origin.Z - floor.Point.Z)));
            return true;
        }
        pose = default;
        return false;
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
