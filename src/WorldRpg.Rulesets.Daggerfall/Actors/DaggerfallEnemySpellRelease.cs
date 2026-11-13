using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// An enemy's chosen spell, released through the same readiness, cost, flight and effect owners the player's and a
/// dungeon action's use, and the donor's ranged admission boundary that decides whether it may be loosed at all.
/// </summary>
/// <param name="touch">Whom a touch at its cast frame reaches.</param>
/// <param name="environment">The world environment contact queries run against now.</param>
internal sealed class DaggerfallEnemySpellRelease(DaggerfallCasting casting, DaggerfallSpellTargets targets,
    Func<DaggerfallTouchCast, DaggerfallTouchReach> touch, ActorContact contact, SpatialMovementSystem spatial,
    Func<CharacterStepEnvironment> environment)
{
    /// <summary>
    /// Releases one AI-selected spell through the same readiness, cost, flight and effect owners
    /// used by player and dungeon action callers. Visibility was already admitted by the behavior
    /// perception query; ranged releases then await the shared swept Engine flight.
    /// </summary>
    internal DaggerfallCastResult Execute(DaggerfallEnemySpellAttempt attempt)
    {
        DaggerfallCastResult ready = casting.Ready(attempt.ActorId, attempt.SpellKey);
        if (ready.Outcome != DaggerfallCastOutcome.Ready) return ready;
        Vector3 origin = attempt.Origin.ToVector();
        if (attempt.Target is DaggerfallSpellTarget.SingleTargetAtRange or DaggerfallSpellTarget.AreaAtRange)
            origin += attempt.Direction * DaggerfallDungeonSpellPolicy.MissileArmLengthMetres;
        // The behavior admitted the player in touch reach; touch delivery decides whom the touch reaches.
        DaggerfallTouchReach reach = attempt.Target == DaggerfallSpellTarget.ByTouch
            ? touch(new(attempt.ActorId, origin + Vector3.UnitY * (DaggerfallContactBody.Envelope.Height * DaggerfallContactBody.EyeFraction), attempt.Direction, DaggerfallActorIdentity.PlayerEntityId))
            : default;
        DaggerfallCastResult release = casting.Release(attempt.ActorId, attempt.Target != DaggerfallSpellTarget.ByTouch || reach.Releases, origin, attempt.Direction);
        if (release.Bundle is not { } bundle) return release;

        return bundle.Target switch
        {
            DaggerfallSpellTarget.CasterOnly => casting.Deliver(bundle, [attempt.ActorId]),
            DaggerfallSpellTarget.ByTouch => casting.Deliver(bundle, reach.Targets),
            DaggerfallSpellTarget.AreaAroundCaster => casting.Deliver(bundle,
                targets.Area(attempt.ActorId, origin, excludeCaster: true)),
            _ => release,
        };
    }

    /// <summary>
    /// Applies the donor's ranged admission boundary through the one Engine spatial owner. The
    /// projectile starts at the authored arm distance, checks its radius at that origin, then
    /// sweeps to the admitted aim. Only the intended player entity is an allowed hit; static cover
    /// and another actor reject the action before readiness or magicka deduction.
    /// </summary>
    internal bool RangedPathClear(DaggerfallEnemySpellAttempt attempt)
    {
        if (attempt.Target is not (DaggerfallSpellTarget.SingleTargetAtRange or DaggerfallSpellTarget.AreaAtRange))
            return true;
        Vector3 origin = attempt.Origin.ToVector();
        Vector3 aim = attempt.Aim.ToVector();
        Vector3 delta = aim - origin;
        if (!DaggerfallSpellTargets.ValidDirection(delta)) return false;
        float distance = delta.Length();
        float armDistance = DaggerfallDungeonSpellPolicy.MissileArmLengthMetres;
        if (!float.IsFinite(distance) || distance <= armDistance) return false;
        Vector3 direction = Vector3.Normalize(delta);
        Vector3 launch = origin + direction * armDistance;
        ReadOnlyMemory<SpatialEntityCollider> colliders = contact.Colliders(attempt.ActorId);
        CharacterStepEnvironment world = environment();
        SpatialHit atLaunch = spatial.OverlapCapsule(launch, 0d, .45d, colliders, world);
        if (Blocks(atLaunch)) return false;
        SpatialHit alongPath = spatial.CastCapsule(launch, 0d, .45d,
            direction * (distance - armDistance), colliders, world);
        return !Blocks(alongPath);
    }

    private bool Blocks(SpatialHit hit) => hit.Present
        && !(hit.Kind == SpatialHitKind.Entity && contact.ActorFor(hit) == DaggerfallActorIdentity.PlayerEntityId);
}
