using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    // SanguineRoseEffect's radius and FoeSpawner's placement envelope, in normalized world metres.
    private const float RoseEnemyRadius = 12f;
    private const float SummonMinimumDistance = 4f;
    private const float SummonMaximumDistance = 20f;
    private const float SummonFloorDistance = 4f;
    private const float SummonSeparation = 1.25f;
    private const float SummonClearanceRadius = .65f;
    private const int SummonPlacementAttempts = 8;

    private DaggerfallInventoryUseResult UseSanguineRose(KitUniqueInventoryItem source)
    {
        if (State.PlayerControl.Position is not { } player)
            return new(false, "Sanguine Rose cannot summon without a player position.");
        if (!QueryEnemies(DaggerfallActorIdentity.PlayerEntityId, player, Vector3.UnitZ, RoseEnemyRadius, -1d,
                definition => definition.Team != "player-ally").Any(pair => pair.Distance <= RoseEnemyRadius))
            return new(false, "No monsters nearby.");
        DaggerfallActorDefinition daedroth = _definitions.Actors.Values.Single(actor => actor.MobileId == 27);
        if (!_sites.Projection.Inputs.MobileSprites.ContainsKey(daedroth.MobileId!.Value))
            return new(false, "This site cannot display the Sanguine Rose summon.");
        if (!TrySummonPose(source, player, out ActorPose pose))
            return new(false, "This site has no clear ground for the Sanguine Rose summon.");
        // The emitted actor belongs to the site, independently of its source item's later lifetime.
        _ = _roster.Spawn(daedroth.Id.Value, pose, playerAllied: true);
        DaggerfallItemConditionResult charged = _itemCondition.Damage(source, 100);
        return new(true, charged.Outcome == DaggerfallItemConditionOutcome.Broken
            ? "Sanguine Rose summoned an allied Daedroth and broke."
            : "Sanguine Rose summoned an allied Daedroth.");
    }

    private PursuitTarget? SelectAllyTarget(long observerId)
    {
        if (!State.Actors.TryGet(observerId, out var observer) || observer.IsDefeated) return null;
        Vector3 forward = new(MathF.Sin(observer.HeadingYawRadians), 0f, -MathF.Cos(observer.HeadingYawRadians));
        foreach (PerceptionPair pair in QueryEnemies(observerId, observer.Position, forward,
                     Math.Max(_tuning.EnemyBehavior.DetectionDistance, DaggerfallPerceptionQueryDefaults.SightRadius),
                     DaggerfallPerceptionQueryDefaults.MinimumFacingCosine,
                     definition => definition.Team != "player-ally")
                     .Where(pair => pair.Kind == PerceptionPairKind.Visible)
                     .OrderBy(pair => pair.Distance).ThenBy(pair => pair.Target))
        {
            long id = checked((long)pair.Target);
            // EnemySenses excludes pacified enemies and protected quest actors from ally attacks.
            if (_enemyBehavior.IsPacified(id) || State.Quests.ProtectsActor(id)) continue;
            return new PursuitTarget(id, State.Actors.Get(id).Position);
        }
        return null;
    }

    private IEnumerable<PerceptionPair> QueryEnemies(long observerId, WorldPoint origin, Vector3 forward,
        double radius, double facing, Func<DaggerfallActorDefinition, bool> eligible)
    {
        PerceptionTarget[] targets = State.Actors.All.Where(actor => actor.DurableId != observerId && !actor.IsDefeated
                && _roster.Definitions.TryGetValue(actor.DurableId, out var definition)
                && definition.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass && eligible(definition))
            .OrderBy(actor => actor.DurableId)
            .Select(actor => new PerceptionTarget(checked((ulong)actor.DurableId), actor.Position.ToVector())).ToArray();
        PerceptionQueryRequest request = new(_spatial.Session,
            new[] { new PerceptionObserver(checked((ulong)observerId), origin.ToVector(), forward, radius, facing, 1d) },
            targets, ReadOnlyMemory<SpatialEntityCollider>.Empty, 0, 0, 64);
        PerceptionReadoutResult receipt;
        do
        {
            receipt = _engine.Perception.QueryVisibility(request);
            foreach (PerceptionPair pair in receipt.Pairs.ToArray()) yield return pair;
            if (receipt.HasNextPairCursor)
                request = request with { PairCursor = receipt.NextPairCursor, ExpectedProjectionIdentity = receipt.ProjectionIdentity };
        } while (receipt.HasNextPairCursor);
    }

    private bool TrySummonPose(KitUniqueInventoryItem source, WorldPoint player, out ActorPose pose, string randomScope = "daggerfall.sanguine-rose.v1")
    {
        CharacterStepEnvironment environment = _sites.Projection.CharacterEnvironment(State.PlayerControl.Motion);
        // These are call-local spawn-clearance envelopes, not another retained collision world.
        SpatialEntityCollider[] actors = State.Actors.All.Where(actor => !actor.IsDefeated).Select(actor =>
        {
            Vector3 center = actor.Position.ToVector() + Vector3.UnitY * SummonSeparation;
            return new SpatialEntityCollider(actor.Actor.Entity.Value, center - new Vector3(SummonClearanceRadius),
                center + new Vector3(SummonClearanceRadius), 0, 0, true, false, false);
        }).Append(_spatial.ProjectCharacterCollider(State.PlayerControl, State.Actors.Player.Actor.Entity.Value)).ToArray();
        SpatialEntityCollider[] rayActors = actors.Where(actor => actor.Entity != State.Actors.Player.Actor.Entity.Value).ToArray();
        ulong identity = State.Inventory.GetDurableItemId(new(source.EntityId)).Value;
        int condition = State.ItemInstances.RequireUnique(identity).CurrentCondition;
        for (int attempt = 0; attempt < SummonPlacementAttempts; attempt++)
        {
            string key = $"source:{identity}:condition:{condition}:attempt:{attempt}";
            // Like FoeSpawner, start outside the forward view. A refused placement does not consume the source.
            float angle = State.PlayerControl.YawRadians + (float)_random.DrawKeyed(new(0, randomScope, key + ":angle", 90, 270)).Value * MathF.PI / 180f;
            Vector3 direction = new(MathF.Sin(angle), 0f, -MathF.Cos(angle));
            float distance = (float)_random.DrawKeyed(new(0, randomScope, key + ":distance", (int)SummonMinimumDistance, (int)SummonMaximumDistance)).Value;
            SpatialHit wall = _spatial.CastRay(player.ToVector(), direction, SummonMaximumDistance, rayActors, environment);
            if (wall.StartSolid) continue;
            if (wall.Present)
            {
                float cosine = Vector3.Dot(-direction, wall.Normal);
                if (cosine <= 0f) continue;
                distance = MathF.Min(distance, (float)wall.Distance - SummonSeparation / cosine);
                if (distance < SummonMinimumDistance) continue;
            }
            Vector3 candidate = player.ToVector() + direction * distance;
            SpatialHit floor = _spatial.CastRay(candidate, -Vector3.UnitY, SummonFloorDistance, actors, environment);
            if (!floor.Present || floor.StartSolid || floor.Normal.Y <= 0f) continue;
            SpatialHit clearance = _spatial.OverlapCapsule(floor.Point + Vector3.UnitY * SummonSeparation,
                0d, SummonClearanceRadius, actors, environment);
            if (clearance.Present) continue;
            pose = new(WorldPoint.From(floor.Point), MathF.Atan2(player.X - floor.Point.X, -(player.Z - floor.Point.Z)));
            return true;
        }
        pose = default;
        return false;
    }
}
