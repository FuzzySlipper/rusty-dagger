using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The player's cast and a released spell's delivery: readying a known spell, recalling the last one cast from a
/// spellbook, releasing the ready spell from the eye along a direction, and delivering it by touch, around its caster
/// or where its flight meets something. A spell the player releases with harm in it goes through allies and, when the
/// attack spares them, civilians.
/// </summary>
/// <param name="eyeHeight">The player's eye above the feet.</param>
/// <param name="positions">Where the player and every gameplay-active actor stand now.</param>
/// <param name="touch">Whom a touch at its cast frame reaches.</param>
/// <param name="environment">The world environment contact queries run against now.</param>
/// <param name="passesThrough">Whether the player's harm goes through an actor, given whether it harms civilians.</param>
/// <param name="playImpact">Where a flight's impact sounds: the bundle's sequence, its element and the point.</param>
internal sealed class DaggerfallSpellRelease(DaggerfallCasting casting, DaggerfallSpellTargets targets, DaggerfallDefinitions definitions,
    ActorsState actors, DaggerfallCharacterState character, MechanicsInventoryCoordinator inventory, PresentationState presentation,
    float eyeHeight, Func<IReadOnlyDictionary<long, WorldPoint>> positions, Func<DaggerfallTouchCast, DaggerfallTouchReach> touch,
    ActorContact contact, Func<CharacterStepEnvironment> environment, Func<long, bool, bool> passesThrough,
    Action<long, int, Vector3> playImpact)
{
    private const int SpellbookTemplateIndex = 132;

    private string? _lastPlayerSpell;

    internal DaggerfallCastResult ReadyPlayerSpell(string key)
    {
        if (!character.KnownSpells.Contains(key))
        return casting.Refuse(actors.Player.DurableId, DaggerfallCastOutcome.UnknownSpell);
        character.RequireKnownSpell(key);
        return casting.Ready(actors.Player.DurableId, key);
    }

    /// <summary>Readies the spell a cast will release: the one ready, or else the last one cast, which a spellbook recalls.</summary>
    /// <param name="report">How a cast outcome is told to the player.</param>
    internal bool Ready(Action<DaggerfallCastOutcome> report)
    {
        long player = actors.Player.DurableId;
        if (casting.ReadyFor(player) is null)
        {
            if (_lastPlayerSpell is null) { presentation.SetOutcome("You have not cast a spell yet."); return false; }
            if (!inventory.Read().UniqueItems.Any(item => definitions.SourceTemplateFor(definitions.RequireItem(new(item.Definition.Value)))?.Index == SpellbookTemplateIndex))
            {
                presentation.SetOutcome("You need a spellbook to recast a spell.");
                return false;
            }
            if (ReadyPlayerSpell(_lastPlayerSpell).Outcome is var readied && readied != DaggerfallCastOutcome.Ready) { report(readied); return false; }
        }
        if (casting.ReleaseRefusal(player) is not { } refusal) return true;
        report(refusal);
        return false;
    }

    /// <summary>The cast's windup is over: the ready spell leaves the left hand along the view.</summary>
    /// <param name="report">How a cast outcome is told to the player.</param>
    internal void Release(Vector3 direction, bool harmsCivilians, Action<DaggerfallCastOutcome> report)
    {
        long player = actors.Player.DurableId;
        DaggerfallReadySpell? ready = casting.ReadyFor(player);
        report(ReleaseReadySpell(player, direction, harmsCivilians).Outcome);
        // The spell the player set active stays ready for the next cast; what an item readied was good for one.
        if (ready is { Source: DaggerfallCastSource.Spell } chosen && casting.ReadyFor(player) is null && character.KnownSpells.Contains(chosen.SpellKey))
            casting.RestoreReadySpell(chosen with { Cost = casting.AvailableSpellCost(player, chosen.SpellKey) ?? chosen.Cost });
    }

    /// <summary>Releases the ready source at its live position. Ranged bundles await Engine collision delivery.</summary>
    internal DaggerfallCastResult ReleaseReadySpell(long casterId, Vector3 direction, bool harmsCivilians = false)
    {
        if (casting.ReadyFor(casterId) is not { } ready) return new(DaggerfallCastOutcome.Unready);
        var target = DaggerfallMagicCostPolicy.TargetForRangeType(definitions.Magic.Spells[ready.SpellKey].RangeType);
        if (!positions().TryGetValue(casterId, out var origin)) return casting.Release(casterId, false);
        Vector3 eye = origin.ToVector() + Vector3.UnitY * eyeHeight;
        DaggerfallTouchReach reach = default;
        if (target == DaggerfallSpellTarget.ByTouch)
        {
            if (!DaggerfallSpellTargets.ValidDirection(direction)) return casting.Release(casterId, false);
            reach = touch(new(casterId, eye, Vector3.Normalize(direction), Admitted: null));
        }
        DaggerfallCastResult release = casting.Release(casterId, target != DaggerfallSpellTarget.ByTouch || reach.Releases,
            eye, DaggerfallSpellTargets.ValidDirection(direction) ? Vector3.Normalize(direction) : null);
        if (release.Bundle is not { } bundle) return release;
        bundle.HarmsCivilians = harmsCivilians;
        if (casterId == actors.Player.DurableId && ready.Source == DaggerfallCastSource.Spell)
            _lastPlayerSpell = ready.SpellKey;
        Func<long, bool>? passes = SpellPasses(bundle);
        switch (target)
        {
            case DaggerfallSpellTarget.CasterOnly: return casting.Deliver(bundle, [casterId]);
            case DaggerfallSpellTarget.ByTouch: return casting.Deliver(bundle, [.. reach.Targets.Where(id => passes?.Invoke(id) != true)]);
            case DaggerfallSpellTarget.AreaAroundCaster: return casting.Deliver(bundle, targets.Area(casterId, origin.ToVector(), true, passes: passes));
            default: return release;
        }
    }

    /// <summary>
    /// The bodies a spell the player released goes through. Only harm goes through anyone: a helpful spell reaches allies
    /// and civilians, and a spell with any harm in it counts as harm, since an effect can't be delivered without the rest.
    /// Anyone else's spell meets everyone.
    /// </summary>
    private Func<long, bool>? SpellPasses(DaggerfallLiveSpell bundle) =>
        bundle.CasterId == actors.Player.DurableId && bundle.Spell.Effects.Any(DaggerfallMagicHarm.Harms)
            ? id => passesThrough(id, bundle.HarmsCivilians) : null;

    /// <summary>Actual ranged flight callers submit their physical swept segment; Engine chooses the first collision.</summary>
    internal DaggerfallCastResult DeliverSpellImpact(DaggerfallLiveSpell bundle, Vector3 from, Vector3 to)
    {
        if (casting.CheckFlight(bundle) is { } unavailable) return unavailable;
        if (bundle.Target is not (DaggerfallSpellTarget.SingleTargetAtRange or DaggerfallSpellTarget.AreaAtRange))
            return bundle.ActionSource is not null ? casting.Deliver(bundle, []) : new(DaggerfallCastOutcome.InvalidTarget, bundle);
        Vector3 delta = to - from;
        if (!DaggerfallSpellTargets.ValidDirection(delta)) return bundle.ActionSource is not null ? casting.Deliver(bundle, []) : new(DaggerfallCastOutcome.InvalidTarget, bundle);
        Func<long, bool>? passes = SpellPasses(bundle);
        SegmentContact hit = contact.Segment(bundle.CasterId, from, to, environment(), passes);
        if (hit.Kind == ContactKind.None) return new(DaggerfallCastOutcome.Released, bundle); // Still in flight.
        playImpact(bundle.Sequence, bundle.Spell.Element, hit.Point);
        if (bundle.Target == DaggerfallSpellTarget.AreaAtRange)
            return casting.Deliver(bundle, targets.Area(bundle.CasterId, hit.Point, false, passes: passes));
        return casting.Deliver(bundle, hit.ActorId is long id ? [id] : []);
    }
}
