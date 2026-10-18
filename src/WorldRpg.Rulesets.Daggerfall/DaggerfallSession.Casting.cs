using WorldRpg.Kit.Combat;
using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
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

    private string _spellResult="";
    private string? _lastPlayerSpell;
    internal DaggerfallSpellbookView ReadSpells() => new(
        State.Character.KnownSpells.Select(key =>
        {
            int? cost = Casting.AvailableSpellCost(State.Actors.Player.DurableId, key);
            return new DaggerfallKnownSpellView(key, State.Character.IsGrantedSpell(key) ? _definitions.Magic.Spells[key].Name.TrimStart('!') : _definitions.Magic.Spells[key].Name, cost ?? 0, cost is not null);
        }).ToArray(),
        Casting.ReadyFor(State.Actors.Player.DurableId) is { Source: DaggerfallCastSource.Spell or DaggerfallCastSource.DungeonAction } ready ? ready.SpellKey : null,
        _spellResult, ReadSpellSale(), ReadSpellInformation(), ReadSpellMaker(), ReadPotionMaker(), ReadItemMaker(), ReadSummoning());
    private void ChangeSpell(WorldRpg.Rulesets.Daggerfall.Presentation.DaggerfallPlayerUiAction action)
    {
        var result=action.Kind switch
        {
            WorldRpg.Rulesets.Daggerfall.Presentation.DaggerfallUiActionKind.SpellReady=>ReadyPlayerSpell(action.Key!),
            WorldRpg.Rulesets.Daggerfall.Presentation.DaggerfallUiActionKind.SpellUnready=>Casting.Cancel(State.Actors.Player.DurableId),
            _=>ReleaseReadySpell(State.Actors.Player.DurableId,_input.ResolveCurrentLook(State.PlayerControl).Forward),
        };
        _spellResult=CastOutcomeText(result.Outcome);
        Presentation.SetOutcome(_spellResult);
    }

    /// <summary>Player wording for the result of readying, cancelling or releasing the player's spell.</summary>
    internal static string CastOutcomeText(DaggerfallCastOutcome outcome) => outcome switch
    {
        DaggerfallCastOutcome.Ready=>"Spell ready.",
        DaggerfallCastOutcome.Cancelled or DaggerfallCastOutcome.Unready=>"No spell ready.",
        DaggerfallCastOutcome.UnknownSpell or DaggerfallCastOutcome.SourceUnavailable=>"That spell is not known or available.",
        DaggerfallCastOutcome.UnsupportedEffect=>"That spell has unavailable effects.",
        DaggerfallCastOutcome.InsufficientMagicka=>"Not enough magicka.",
        DaggerfallCastOutcome.InvalidTarget or DaggerfallCastOutcome.TargetUnavailable=>"Aim at a valid spell target.",
        DaggerfallCastOutcome.Silenced=>"You cannot cast while silenced.",
        _=>"Spell cast.",
    };

    internal DaggerfallMagicTargetProfile MagicProfile(long id) => DaggerfallMagicProfiles.Create(
        (CastActor(id) ?? throw new ArgumentException($"Casting actor {id} is unavailable.")).Get<StatsComponent>(),
        _roster.Definitions.GetValueOrDefault(id), id == State.Actors.Player.DurableId ? State.Character : null,
        _definitions, State.Effects.MagicDefenseFor(id));

    private Actor? CastActor(long id) => id == State.Actors.Player.DurableId ? State.Actors.Player.Actor
        : IsSpellEligibleActor(id) && State.Actors.TryGet(id, out ActorState actor) ? actor.Actor : null;

    private bool IsSpellEligibleActor(long id) =>
        State.Npcs.IsGameplayActive(id)
        && (!_roster.Definitions.TryGetValue(id, out DaggerfallActorDefinition? definition)
            || definition.Kind != DaggerfallActorKinds.StaticNpc);

    /// <summary>
    /// Releases one AI-selected spell through the same readiness, cost, flight and effect owners
    /// used by player and dungeon action callers. Visibility was already admitted by the behavior
    /// perception query; ranged releases then await the shared swept Engine flight.
    /// </summary>
    private DaggerfallCastResult ExecuteEnemySpell(DaggerfallEnemySpellAttempt attempt)
    {
        DaggerfallCastResult ready = Casting.Ready(attempt.ActorId, attempt.SpellKey);
        if (ready.Outcome != DaggerfallCastOutcome.Ready) return ready;
        Vector3 origin = attempt.Origin.ToVector();
        if (attempt.Target is DaggerfallSpellTarget.SingleTargetAtRange or DaggerfallSpellTarget.AreaAtRange)
            origin += attempt.Direction * DaggerfallDungeonSpellPolicy.MissileArmLengthMetres;
        DaggerfallCastResult release = Casting.Release(attempt.ActorId, true, origin, attempt.Direction);
        if (release.Bundle is not { } bundle) return release;

        return bundle.Target switch
        {
            DaggerfallSpellTarget.CasterOnly => Casting.Deliver(bundle, [attempt.ActorId]),
            DaggerfallSpellTarget.ByTouch => Casting.Deliver(bundle, [DaggerfallActorIdentity.PlayerEntityId]),
            DaggerfallSpellTarget.AreaAroundCaster => Casting.Deliver(bundle,
                AreaSpellTargets(attempt.ActorId, origin, excludeCaster: true)),
            _ => release,
        };
    }

    /// <summary>
    /// Applies the donor's ranged admission boundary through the one Engine spatial owner. The
    /// projectile starts at the authored arm distance, checks its radius at that origin, then
    /// sweeps to the admitted aim. Only the intended player entity is an allowed hit; static cover
    /// and another actor reject the action before readiness or magicka deduction.
    /// </summary>
    private bool EnemyRangedSpellPathClear(DaggerfallEnemySpellAttempt attempt)
    {
        if (attempt.Target is not (DaggerfallSpellTarget.SingleTargetAtRange or DaggerfallSpellTarget.AreaAtRange))
            return true;
        Vector3 origin = attempt.Origin.ToVector();
        Vector3 aim = attempt.Aim.ToVector();
        Vector3 delta = aim - origin;
        if (!ValidDirection(delta)) return false;
        float distance = delta.Length();
        float armDistance = DaggerfallDungeonSpellPolicy.MissileArmLengthMetres;
        if (!float.IsFinite(distance) || distance <= armDistance) return false;
        Vector3 direction = Vector3.Normalize(delta);
        Vector3 launch = origin + direction * armDistance;
        ReadOnlyMemory<SpatialEntityCollider> colliders = _contact.Colliders(attempt.ActorId);
        CharacterStepEnvironment environment = _sites.CharacterEnvironment(State.PlayerControl.Motion);
        SpatialHit atLaunch = _spatial.OverlapCapsule(launch, 0d, .45d, colliders, environment);
        if (BlocksEnemySpellPath(atLaunch)) return false;
        SpatialHit alongPath = _spatial.CastCapsule(launch, 0d, .45d,
            direction * (distance - armDistance), colliders, environment);
        return !BlocksEnemySpellPath(alongPath);
    }

    private bool BlocksEnemySpellPath(SpatialHit hit) => hit.Present
        && !(hit.Kind == SpatialHitKind.Entity && _contact.ActorFor(hit) == DaggerfallActorIdentity.PlayerEntityId);

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
                0d, .25d, Vector3.Normalize(direction) * 3f, _contact.Colliders(casterId),
                _sites.CharacterEnvironment(State.PlayerControl.Motion));
            touch = _contact.ActorFor(hit);
        }
        DaggerfallCastResult release = Casting.Release(casterId, target != DaggerfallSpellTarget.ByTouch || touch.HasValue,
            origin.ToVector() + Vector3.UnitY * _tuning.Camera.EyeHeight, ValidDirection(direction) ? Vector3.Normalize(direction) : null);
        if (release.Bundle is not { } bundle) return release;
        if (casterId == State.Actors.Player.DurableId && ready.Source == DaggerfallCastSource.Spell)
            _lastPlayerSpell = ready.SpellKey;
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
            return bundle.ActionSource is not null ? Casting.Deliver(bundle, []) : new(DaggerfallCastOutcome.InvalidTarget, bundle);
        Vector3 delta = to - from;
        if (!ValidDirection(delta)) return bundle.ActionSource is not null ? Casting.Deliver(bundle, []) : new(DaggerfallCastOutcome.InvalidTarget, bundle);
        SegmentContact contact = _contact.Segment(bundle.CasterId, from, to, _sites.CharacterEnvironment(State.PlayerControl.Motion));
        if (contact.Kind == ContactKind.None) return new(DaggerfallCastOutcome.Released, bundle); // Still in flight.
        _appearance.PlaySpellImpact(bundle.Sequence, bundle.Spell.Element, contact.Point);
        if (bundle.Target == DaggerfallSpellTarget.AreaAtRange)
            return Casting.Deliver(bundle, AreaSpellTargets(bundle.CasterId, contact.Point, false));
        return Casting.Deliver(bundle, contact.ActorId is long id ? [id] : []);
    }

    /// <summary>The upright body every site actor presents to spell and harm contact.</summary>
    internal static readonly ActorBodyEnvelope ActorContactBody = new(.3f, 1.8f);
    private long[] AreaSpellTargets(long? caster, Vector3 center, bool excludeCaster, double radius = 4d, bool exclusive = false)
    {
        long observer = caster ?? State.Actors.Player.DurableId;
        return NearbyContacts(observer, center, CurrentPositions()
                .Where(pair => IsSpellEligibleActor(pair.Key) && (!excludeCaster || caster is not long source || pair.Key != source)), radius, exclusive)
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
            ReadOnlyMemory<SpatialEntityCollider>.Empty, 0, 64);
        Dictionary<long, double> contacts = [];
        PerceptionReadoutResult receipt;
        do
        {
            receipt = _engine.Perception.QueryVisibility(request);
            // Classic detection and blast radii do not test line of sight.
            foreach (var pair in receipt.Pairs.ToArray())
                if (exclusive ? pair.Distance < radius : pair.Distance <= radius) contacts[checked((long)pair.Target)] = pair.Distance;
            if (receipt.HasNextPairCursor) request = request with { PairCursor = receipt.NextPairCursor };
        } while (receipt.HasNextPairCursor);
        return contacts;
    }
    private static bool ValidDirection(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && value.LengthSquared() > .000001f;
}

internal sealed record DaggerfallKnownSpellView(string Key, string Name, int Cost, bool CanCast = true);
internal sealed record DaggerfallSpellbookView(DaggerfallKnownSpellView[] Available, string? Ready, string Result,
    DaggerfallSpellSaleView? Sale = null, DaggerfallSpellInformation? Information = null, DaggerfallSpellMakerView? Maker = null, DaggerfallPotionMakerView? PotionMaker = null, DaggerfallItemMakerView? ItemMaker = null, DaggerfallSummoningView? Summoning = null);
