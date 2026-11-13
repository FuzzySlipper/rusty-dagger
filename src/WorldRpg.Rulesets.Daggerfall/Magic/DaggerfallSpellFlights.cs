using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Spells in flight: the dungeon missiles a release sends travelling, advanced one segment for each admitted step until
/// they meet something or outlive their lifespan, and what each looks like to presentation.
/// </summary>
internal sealed class DaggerfallSpellFlights(DaggerfallCasting casting, DaggerfallSpellRelease release)
{
    /// <summary>
    /// Advances each admitted dungeon missile in one segment per elapsed admitted simulation step
    /// and submits every newly traversed segment to the Engine spatial owner. A bundle released
    /// during this batch carries its actual release step, so only later slices can age it. The
    /// action target is an aim fact at launch; it is never consulted again to re-aim a live missile.
    /// </summary>
    internal void Advance(ProductUpdateFacts facts, ulong generation, double fixedDeltaSeconds)
    {
        if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds <= 0d)
            throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));

        float displacement = checked((float)(DaggerfallDungeonSpellPolicy.MissileMovementSpeedMetresPerSecond * fixedDeltaSeconds));
        ulong lastStep = checked(facts.SimulationStep + facts.AdmittedStepCount - 1);
        foreach (DaggerfallLiveSpell bundle in casting.PendingFlightOperations)
        {
            if (bundle.ReleaseOrigin is not Vector3 from
                || !DaggerfallDungeonSpellPolicy.TryNormalizeDirection(bundle.ReleaseDirection ?? default, out Vector3 direction))
            {
                // An admitted release with no usable launch direction is a terminal miss. The
                // source target is not a fallback caster or a proof of impact.
                _ = casting.Deliver(bundle, []);
                continue;
            }

            // Non-ranged action target modes have no travelling segment. They still terminate
            // through the common impact owner on this admitted update, as they did before the
            // per-step movement path was introduced.
            if (bundle.Target is not (DaggerfallSpellTarget.SingleTargetAtRange or DaggerfallSpellTarget.AreaAtRange))
            {
                _ = release.DeliverSpellImpact(bundle, from, from);
                continue;
            }

            ulong stepsToAdvance = facts.AdmittedStepCount;
            if (bundle.ReleaseGeneration == generation && bundle.ReleaseSimulationStep is ulong releaseStep)
            {
                // A release is admitted during its simulation step and consumes that step's
                // movement slice, but none of the slices before it. A release in the last inner
                // step therefore advances once; a release before this batch advances every slice.
                stepsToAdvance = releaseStep > lastStep ? 0UL
                    : releaseStep == lastStep ? 1UL
                    : releaseStep >= facts.SimulationStep ? checked(lastStep - releaseStep + 1UL)
                    : facts.AdmittedStepCount;
            }

            for (ulong step = 0; step < stepsToAdvance && !bundle.Delivered; step++)
            {
                Vector3 next = from + direction * displacement;
                bundle.DungeonFlightElapsedSeconds += fixedDeltaSeconds;
                DaggerfallCastResult impact = release.DeliverSpellImpact(bundle, from, next);
                if (impact.Outcome != DaggerfallCastOutcome.Released || bundle.Delivered) break;
                from = next;
                bundle.ReleaseOrigin = from;
                if (bundle.DungeonFlightElapsedSeconds > DaggerfallDungeonSpellPolicy.MissileLifespanSeconds)
                    _ = casting.Deliver(bundle, []);
            }
        }
    }

    /// <summary>The spells in flight now, as presentation draws them.</summary>
    internal IReadOnlyList<DaggerfallDungeonSpellFlightView> Views()
    {
        return [.. casting.PendingRangedFlights.Select(bundle =>
        {
            Vector3 origin = bundle.ReleaseOrigin
                ?? (bundle.ActionSource is { } source
                    ? source.Origin + Vector3.UnitY * DaggerfallDungeonSpellPolicy.MissileOriginHeightMetres
                    : Vector3.Zero);
            return DaggerfallDungeonSpellPolicy.TryNormalizeDirection(bundle.ReleaseDirection ?? default, out Vector3 direction)
                ? new DaggerfallDungeonSpellFlightView(bundle.Sequence, WorldPoint.From(origin), direction, bundle.Spell.Element)
                : (DaggerfallDungeonSpellFlightView?)null;
        }).Where(view => view is not null).Select(view => view!.Value)];
    }
}
