using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Ai;
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
/// <param name="harmPassesThrough">Whether a caster's harmful touch or area spell passes through a target actor.</param>
internal sealed class DaggerfallEnemySpellRelease(DaggerfallCasting casting, DaggerfallSpellTargets targets,
    Func<DaggerfallTouchCast, DaggerfallTouchReach> touch, ActorContact contact, SpatialMovementSystem spatial,
    Func<CharacterStepEnvironment> environment, Func<long, long, bool>? harmPassesThrough = null)
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
        // A missile leaves the caster's chest, an arm's length along its aim, as its lane was swept.
        if (attempt.Target is DaggerfallSpellTarget.SingleTargetAtRange or DaggerfallSpellTarget.AreaAtRange)
            origin += Vector3.UnitY * DaggerfallContactBody.ChestHeight + attempt.Direction * DaggerfallDungeonSpellPolicy.MissileArmLengthMetres;
        // The behavior admitted its target in touch reach; touch delivery decides whom the touch reaches.
        DaggerfallTouchReach reach = attempt.Target == DaggerfallSpellTarget.ByTouch
            ? touch(new(attempt.ActorId, origin + Vector3.UnitY * (DaggerfallContactBody.Envelope.Height * DaggerfallContactBody.EyeFraction), attempt.Direction, attempt.TargetId))
            : default;
        DaggerfallCastResult release = casting.Release(attempt.ActorId, attempt.Target != DaggerfallSpellTarget.ByTouch || reach.Releases, origin, attempt.Direction);
        if (release.Bundle is not { } bundle) return release;

        Func<long, bool>? passes = harmPassesThrough is not null && bundle.Spell.Effects.Any(DaggerfallMagicHarm.Harms)
            ? id => harmPassesThrough(attempt.ActorId, id) : null;
        return bundle.Target switch
        {
            DaggerfallSpellTarget.CasterOnly => casting.Deliver(bundle, [attempt.ActorId]),
            DaggerfallSpellTarget.ByTouch => casting.Deliver(bundle, [.. reach.Targets.Where(id => passes?.Invoke(id) != true)]),
            DaggerfallSpellTarget.AreaAroundCaster => casting.Deliver(bundle,
                targets.Area(attempt.ActorId, origin, excludeCaster: true, passes: passes)),
            _ => release,
        };
    }

    /// <summary>
    /// Applies the donor's ranged admission boundary through the one Engine spatial owner: whether a missile from
    /// <paramref name="casterId"/> standing at <paramref name="origin"/> would reach <paramref name="target"/>. The
    /// projectile starts at the authored arm distance from the caster's chest, checks its radius at that origin, then
    /// sweeps to the target's chest; both stand on their feet, so the lane runs clear of the floor between them. Only
    /// the target aimed at is an allowed hit; static cover and any other actor, the player included, reject the action
    /// before readiness or magicka deduction.
    /// </summary>
    internal bool LaneClear(long casterId, WorldPoint origin, PursuitTarget target)
    {
        Vector3 chest = Vector3.UnitY * DaggerfallContactBody.ChestHeight;
        Vector3 from = origin.ToVector() + chest;
        Vector3 aim = target.Position.ToVector() + chest;
        Vector3 delta = aim - from;
        if (!DaggerfallSpellTargets.ValidDirection(delta)) return false;
        float distance = delta.Length();
        float armDistance = DaggerfallDungeonSpellPolicy.MissileArmLengthMetres;
        if (!float.IsFinite(distance) || distance <= armDistance) return false;
        Vector3 direction = Vector3.Normalize(delta);
        Vector3 launch = from + direction * armDistance;
        ReadOnlyMemory<SpatialEntityCollider> colliders = contact.Colliders(casterId);
        CharacterStepEnvironment world = environment();
        SpatialHit atLaunch = spatial.OverlapCapsule(launch, 0d, .45d, colliders, world);
        if (Blocks(atLaunch, target.DurableId)) return false;
        SpatialHit alongPath = spatial.CastCapsule(launch, 0d, .45d,
            direction * (distance - armDistance), colliders, world);
        return !Blocks(alongPath, target.DurableId);
    }

    private bool Blocks(SpatialHit hit, long target) => hit.Present
        && !(hit.Kind == SpatialHitKind.Entity && contact.ActorFor(hit) == target);
}
