using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Where actors can be placed and whom they see as foes: clear spawn poses around the player and the enemies a
/// perception query finds, used by summons, law, quests and allies.
/// </summary>
internal sealed partial class DaggerfallSession
{
    // FoeSpawner's placement envelope, in normalized world metres.
    private const float SummonFloorDistance = 4f;
    private const float SummonSeparation = 1.25f;
    private const float SummonClearanceRadius = .65f;
    private const int SummonPlacementAttempts = 8;

    private PursuitTarget? SelectAllyTarget(long observerId)
    {
        if (!State.Actors.TryGet(observerId, out var observer) || observer.IsDefeated) return null;
        Vector3 forward = ActorHeading.Forward(observer.HeadingYawRadians);
        foreach (PerceptionPair pair in QueryEnemies(observerId, observer.Position, forward,
                     Math.Max(_tuning.EnemyBehavior.DetectionDistance, DaggerfallPerceptionQueryDefaults.SightRadius),
                     DaggerfallPerceptionQueryDefaults.MinimumFacingCosine,
                     definition => definition.Team != "player-ally")
                     .Where(pair => pair.Kind == PerceptionPairKind.Visible)
                     .OrderBy(pair => pair.Distance).ThenBy(pair => pair.Target))
        {
            long id = checked((long)pair.Target);
            // EnemySenses excludes pacified enemies and protected quest actors from ally attacks.
            if (_enemyBehavior.IsPacified(id) || !State.Quests.AllowsFoeInfighting(id)) continue;
            return new PursuitTarget(id, State.Actors.Get(id).Position);
        }
        return null;
    }

    private IEnumerable<PerceptionPair> QueryEnemies(long observerId, WorldPoint origin, Vector3 forward,
        double radius, double facing, Func<DaggerfallActorDefinition, bool> eligible)
    {
        PerceptionTarget[] targets = State.Actors.All.Where(actor => actor.DurableId != observerId && !actor.IsDefeated
                && State.Npcs.IsGameplayActive(actor.DurableId)
                && _roster.Definitions.TryGetValue(actor.DurableId, out var definition)
                && definition.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass && eligible(definition with { Team = EffectiveFoeTeam(actor.DurableId) }))
            .OrderBy(actor => actor.DurableId)
            .Select(actor => new PerceptionTarget(checked((ulong)actor.DurableId), actor.Position.ToVector())).ToArray();
        PerceptionQueryRequest request = new(_spatial.Session,
            new[] { new PerceptionObserver(checked((ulong)observerId), origin.ToVector(), forward, radius, facing, 1d) },
            targets, ReadOnlyMemory<SpatialEntityCollider>.Empty, 0, 64);
        PerceptionReadoutResult receipt;
        do
        {
            receipt = _engine.Perception.QueryVisibility(request);
            foreach (PerceptionPair pair in receipt.Pairs.ToArray()) yield return pair;
            if (receipt.HasNextPairCursor)
                request = request with { PairCursor = receipt.NextPairCursor };
        } while (receipt.HasNextPairCursor);
    }

    /// <summary>Where a placement ray starts relative to the player's facing.</summary>
    private enum SpawnDirection
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

    private bool TrySpawnPose(string operation, string randomScope, WorldPoint player, float minimumDistance, float maximumDistance, out ActorPose pose,
        SpawnDirection direction = SpawnDirection.Behind)
    {
        CharacterStepEnvironment environment = _exterior.CharacterEnvironment(State.PlayerControl.Motion);
        // These are call-local spawn-clearance envelopes, not another retained collision world.
        SpatialEntityCollider[] actors = State.Actors.All.Where(actor => !actor.IsDefeated && State.Npcs.IsGameplayActive(actor.DurableId)).Select(actor =>
        {
            Vector3 center = actor.Position.ToVector() + Vector3.UnitY * SummonSeparation;
            return new SpatialEntityCollider(actor.Actor.Entity.Value, center - new Vector3(SummonClearanceRadius),
                center + new Vector3(SummonClearanceRadius), 0, 0, true, false, false);
        }).Append(_spatial.ProjectCharacterCollider(State.PlayerControl, State.Actors.Player.Actor.Entity.Value)).ToArray();
        SpatialEntityCollider[] rayActors = actors.Where(actor => actor.Entity != State.Actors.Player.Actor.Entity.Value).ToArray();
        for (int attempt = 0; attempt < SummonPlacementAttempts; attempt++)
        {
            string key = $"{operation}:attempt:{attempt}";
            // Like FoeSpawner, start outside the forward view unless the caller places in any direction.
            // A refused placement does not consume the source.
            float offsetDegrees = direction switch
            {
                SpawnDirection.BesideView => ((float)_tuning.Camera.FieldOfViewYDegrees
                    + (float)_random.DrawKeyed(new(0, randomScope, key + ":angle", 0, 4)).Value)
                    * (_random.DrawKeyed(new(0, randomScope, key + ":side", 0, 1)).Value == 0 ? -1f : 1f),
                SpawnDirection.Any => _random.DrawKeyed(new(0, randomScope, key + ":angle", 0, 359)).Value,
                _ => _random.DrawKeyed(new(0, randomScope, key + ":angle", 90, 270)).Value,
            };
            Vector3 rayDirection = ActorHeading.Forward(State.PlayerControl.YawRadians + offsetDegrees * MathF.PI / 180f);
            float distance = (float)_random.DrawKeyed(new(0, randomScope, key + ":distance", (int)minimumDistance, (int)maximumDistance)).Value;
            SpatialHit wall = _spatial.CastRay(player.ToVector(), rayDirection, maximumDistance, rayActors, environment);
            if (wall.StartSolid) continue;
            if (wall.Present)
            {
                float cosine = Vector3.Dot(-rayDirection, wall.Normal);
                if (cosine <= 0f) continue;
                distance = MathF.Min(distance, (float)wall.Distance - SummonSeparation / cosine);
                if (distance < minimumDistance) continue;
            }
            Vector3 candidate = player.ToVector() + rayDirection * distance;
            SpatialHit floor = _spatial.CastRay(candidate, -Vector3.UnitY, SummonFloorDistance, actors, environment);
            if (!floor.Present || floor.StartSolid || floor.Normal.Y <= 0f) continue;
            SpatialHit clearance = _spatial.OverlapCapsule(floor.Point + Vector3.UnitY * SummonSeparation,
                0d, SummonClearanceRadius, actors, environment);
            if (clearance.Present) continue;
            // Actor poses are feet. Admit the entire controller capsule at its projected center,
            // not only the donor's small separation sphere, before handing this pose to the roster.
            CharacterShapeConfig shape = _spatial.CurrentController.Shape;
            Vector3 center = floor.Point + Vector3.UnitY * (shape.StandingHeight * .5f + shape.ContactSkin + shape.ClearancePadding);
            SpatialHit body = _spatial.OverlapCapsule(center, shape.StandingHeight * .5f - shape.Radius,
                shape.Radius + shape.ClearancePadding, actors, environment);
            if (body.Present) continue;
            pose = new(WorldPoint.From(floor.Point), MathF.Atan2(player.X - floor.Point.X, -(player.Z - floor.Point.Z)));
            return true;
        }
        pose = default;
        return false;
    }

    /// <summary>Where the player and every gameplay-active actor stand now.</summary>
    private IReadOnlyDictionary<long, WorldPoint> CurrentPositions()
    {
        Dictionary<long, WorldPoint> positions = [];
        if (State.PlayerControl.Position is WorldPoint player) positions.Add(DaggerfallActorIdentity.PlayerEntityId, player);
        foreach (ActorState actor in State.Actors.All.Where(actor => State.Npcs.IsGameplayActive(actor.DurableId)))
            positions[actor.DurableId] = actor.Position;
        return positions;
    }
}
