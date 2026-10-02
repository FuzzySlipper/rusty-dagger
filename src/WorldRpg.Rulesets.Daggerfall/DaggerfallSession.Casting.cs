using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    internal DaggerfallCasting Casting { get; private set; } = null!;
    internal DaggerfallCastResult ReadyPlayerSpell(string key)
    {
        if (!State.Character.KnownSpells.Contains(key))
        return Casting.Refuse(State.Actors.Player.DurableId, DaggerfallCastOutcome.UnknownSpell);
        State.Character.RequireKnownSpell(key);
        return Casting.Ready(State.Actors.Player.DurableId, key);
    }

    internal DaggerfallMagicTargetProfile MagicProfile(long id) => DaggerfallMagicProfiles.Create(
        (CastActor(id) ?? throw new ArgumentException($"Casting actor {id} is unavailable.")).Get<StatsComponent>(),
        _roster.Definitions.GetValueOrDefault(id), id == State.Actors.Player.DurableId ? State.Character : null,
        _definitions, State.Effects.MagicDefenseFor(id));

    private Actor? CastActor(long id) => id == State.Actors.Player.DurableId ? State.Actors.Player.Actor
        : State.Actors.TryGet(id, out ActorState actor) ? actor.Actor : null;

    /// <summary>Releases the ready source at its live position. Ranged bundles await Engine collision delivery.</summary>
    internal DaggerfallCastResult ReleaseReadySpell(long casterId, Vector3 direction)
    {
        if (Casting.ReadyFor(casterId) is not { } ready) return new(DaggerfallCastOutcome.Unready);
        var target = DaggerfallMagicCostPolicy.TargetForRangeType(_definitions.Magic.Spells[ready.SpellKey].RangeType);
        if (!CurrentPositions().TryGetValue(casterId, out var origin)) return Casting.Release(casterId, false);
        long? touch = null;
        if (target == DaggerfallSpellTarget.ByTouch)
        {
            if (!ValidDirection(direction)) return Casting.Release(casterId, false);
            SpatialHit hit = _spatial.CastCapsule(origin.ToVector() + Vector3.UnitY * _tuning.Camera.EyeHeight,
                0d, .25d, Vector3.Normalize(direction) * 3f, SpellColliders(casterId),
                _sites.Projection.CharacterEnvironment(State.PlayerControl.Motion));
            touch = ActorForSpellHit(hit);
        }
        DaggerfallCastResult release = Casting.Release(casterId, target != DaggerfallSpellTarget.ByTouch || touch.HasValue,
            origin.ToVector() + Vector3.UnitY * _tuning.Camera.EyeHeight, ValidDirection(direction) ? Vector3.Normalize(direction) : null);
        if (release.Bundle is not { } bundle) return release;
        switch (target)
        {
            case DaggerfallSpellTarget.CasterOnly: return Casting.Deliver(bundle, [casterId]);
            case DaggerfallSpellTarget.ByTouch: return Casting.Deliver(bundle, [touch!.Value]);
            case DaggerfallSpellTarget.AreaAroundCaster: return Casting.Deliver(bundle, AreaSpellTargets(casterId, origin.ToVector(), true));
            default: return release;
        }
    }

    /// <summary>Actual ranged flight callers submit their physical swept segment; Engine chooses the first collision.</summary>
    internal DaggerfallCastResult DeliverSpellImpact(DaggerfallLiveSpell bundle, Vector3 from, Vector3 to)
    {
        if (Casting.CheckFlight(bundle) is { } unavailable) return unavailable;
        if (bundle.Target is not (DaggerfallSpellTarget.SingleTargetAtRange or DaggerfallSpellTarget.AreaAtRange))
            return new(DaggerfallCastOutcome.InvalidTarget, bundle);
        Vector3 delta = to - from;
        if (!ValidDirection(delta)) return new(DaggerfallCastOutcome.InvalidTarget, bundle);
        SpatialHit hit = CastSpellRay(bundle.CasterId, from, delta, delta.Length());
        if (!hit.Present) return new(DaggerfallCastOutcome.Released, bundle); // Still in flight.
        if (bundle.Target == DaggerfallSpellTarget.AreaAtRange)
            return Casting.Deliver(bundle, AreaSpellTargets(bundle.CasterId, hit.Point, false));
        long? target = ActorForSpellHit(hit);
        return Casting.Deliver(bundle, target is long id ? [id] : []);
    }

    private SpatialHit CastSpellRay(long caster, Vector3 origin, Vector3 direction, float distance) => _spatial.CastRay(
        origin, Vector3.Normalize(direction), distance,
        SpellColliders(caster), _sites.Projection.CharacterEnvironment(State.PlayerControl.Motion));

    private SpatialEntityCollider[] SpellColliders(long caster)
    {
        // Query-local envelopes projected from canonical actor positions, never another collision world.
        var colliders = State.Actors.All.Where(actor => actor.DurableId != caster && !actor.IsDefeated).Select(actor =>
            new SpatialEntityCollider(actor.Actor.Entity.Value, actor.Position.ToVector() - new Vector3(.3f, 0f, .3f),
                actor.Position.ToVector() + new Vector3(.3f, 1.8f, .3f), 0, 0, true, false, false));
        if (State.Actors.Player.DurableId != caster && !State.Actors.Player.IsDefeated)
            colliders = colliders.Append(_spatial.ProjectCharacterCollider(State.PlayerControl, State.Actors.Player.Actor.Entity.Value));
        return colliders.ToArray();
    }
    private long? ActorForSpellHit(SpatialHit hit)
    {
        if (!hit.Present || hit.Kind != SpatialHitKind.Entity) return null;
        if (State.Actors.Player.Actor.Entity.Value == hit.Entity) return State.Actors.Player.DurableId;
        return State.Actors.All.Where(actor => actor.Actor.Entity.Value == hit.Entity).Select(actor => (long?)actor.DurableId).SingleOrDefault();
    }
    private long[] AreaSpellTargets(long caster, Vector3 center, bool excludeCaster, double radius = 4d, bool exclusive = false)
    {
        return NearbyContacts(caster, center, CurrentPositions().Where(pair => !excludeCaster || pair.Key != caster), radius, exclusive)
            .Keys.Order().ToArray();
    }
    /// <summary>Engine supplies current distances; callers decide strict range and target eligibility without a second spatial index.</summary>
    private Dictionary<long, double> NearbyContacts(long observer, Vector3 center,
        IEnumerable<KeyValuePair<long, WorldRpg.Kit.Controls.WorldPoint>> positions, double radius, bool exclusive)
    {
        var candidates = positions.Select(pair => new PerceptionTarget(checked((ulong)pair.Key), pair.Value.ToVector())).ToArray();
        if (candidates.Length == 0) return [];
        PerceptionQueryRequest request = new(_spatial.Session,
            new[] { new PerceptionObserver(checked((ulong)observer), center, Vector3.UnitZ, radius, -1d, 1d) }, candidates,
            ReadOnlyMemory<SpatialEntityCollider>.Empty, 0, 0, 64);
        Dictionary<long, double> contacts = [];
        PerceptionReadoutResult receipt;
        do
        {
            receipt = _engine.Perception.QueryVisibility(request);
            // Classic detection and blast radii do not test line of sight.
            foreach (var pair in receipt.Pairs.ToArray())
                if (exclusive ? pair.Distance < radius : pair.Distance <= radius) contacts[checked((long)pair.Target)] = pair.Distance;
            if (receipt.HasNextPairCursor) request = request with { PairCursor = receipt.NextPairCursor, ExpectedProjectionIdentity = receipt.ProjectionIdentity };
        } while (receipt.HasNextPairCursor);
        return contacts;
    }
    private static bool ValidDirection(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && value.LengthSquared() > .000001f;
}
