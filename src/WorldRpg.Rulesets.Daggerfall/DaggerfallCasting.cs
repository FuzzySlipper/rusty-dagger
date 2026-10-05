using System.Text.Json;
using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Explicit compiled bridge from normalized classic settings to an existing effect owner.</summary>
internal sealed record DaggerfallSpellBinding(int Type, int SubType, bool SupportsDuration = false,
    bool RollChanceOnCast = false, bool SupportsMagnitude = false, bool IsParalysis = false,
    bool IsDisease = false, DaggerfallMagicAllowedElements AllowedElements = DaggerfallMagicAllowedElements.Magic,
    Func<DaggerfallCastEffectState, JsonElement>? CreateState = null,
    DaggerfallMagicAllowedTargets AllowedTargets = DaggerfallMagicAllowedTargets.All,
    bool MagnitudePerRound = false, bool UntilHealed = false,
    bool UntilTriggered = false, bool BypassItemChance = false, bool SpellMaker = false);

/// <summary>Meaningful settings retained with an admitted effect, never a runtime handle.</summary>
internal sealed record DaggerfallCastEffectState(DaggerfallSpellEffectDefinition Settings, int CasterLevel,
    int Amount, int SavePercent, DaggerfallCastOrigin? Origin = null);

/// <summary>
/// Provenance for an action resource that admitted a spell without manufacturing a caster actor.
/// The resource identity and pose are durable product facts; Engine entity handles never enter a
/// cast bundle or a save payload.
/// </summary>
internal sealed record DaggerfallActionCastSource(string ActionId, ulong ResourceIdentity, long TargetId,
    Vector3 Origin, int CasterLevel)
{
    internal bool IsValid => !string.IsNullOrWhiteSpace(ActionId) && ResourceIdentity > 0 && TargetId > 0
        && CasterLevel > 0 && float.IsFinite(Origin.X) && float.IsFinite(Origin.Y) && float.IsFinite(Origin.Z);
}

/// <summary>Historical admission provenance, not a dependency on a living actor or item.</summary>
internal sealed record DaggerfallCastOrigin(long? CasterId, ulong? ItemId, DaggerfallCastSource Source,
    DaggerfallActionCastSource? ActionSource = null);

/// <summary>Computed from actual active effects; no independently retained defense state.</summary>
internal sealed record DaggerfallMagicDefense(int AbsorptionChance, int ReflectionChance,
    DaggerfallMagicActiveResistance[] Resistances, bool BlocksCasting = false, bool PreventsParalysis = false,
    ulong[]? AbsorptionItems = null, int AllResistanceChance = 0)
{
    internal static DaggerfallMagicDefense None { get; } = new(0, 0, []);
    internal static DaggerfallMagicDefense Combine(IEnumerable<DaggerfallMagicDefense> defenses)
    {
        DaggerfallMagicDefense[] values = defenses.ToArray();
        return new(values.Select(value => value.AbsorptionChance).DefaultIfEmpty().Max(),
            values.Select(value => value.ReflectionChance).DefaultIfEmpty().Max(),
            values.SelectMany(value => value.Resistances).GroupBy(value => value.Element)
                .Select(group => new DaggerfallMagicActiveResistance(group.Key, checked((int)Math.Min(100L, group.Sum(value => (long)value.Chance))))).ToArray(),
            values.Any(value => value.BlocksCasting), values.Any(value => value.PreventsParalysis),
            values.SelectMany(value => value.AbsorptionItems ?? []).Distinct().Order().ToArray(),
            values.Select(value => value.AllResistanceChance).DefaultIfEmpty().Max());
    }
}

internal enum DaggerfallCastOutcome
{
    Ready, Released, Cancelled, Unready, UnknownSpell, UnsupportedEffect, SourceUnavailable,
    InsufficientMagicka, Silenced, InvalidTarget, Immune, Absorbed, Reflected, Resisted, ChanceFailed,
    Missed, Applied, NoMatch, Refreshed, Replaced, DeliveryCompleted, IncumbentRejected, AlreadyDelivered, TargetUnavailable,
}
internal sealed record DaggerfallCastResult(DaggerfallCastOutcome Outcome, DaggerfallLiveSpell? Bundle = null);
internal sealed record DaggerfallCastEffectResult(int EffectIndex, long? TargetId, DaggerfallCastOutcome Outcome,
    int SavePercent = 100, string? Instance = null);
internal sealed record DaggerfallSpellAbsorptionResult(long TargetId, int AdmittedSpellPoints, double RestoredSpellPoints, ulong[] SourceItems);
internal enum DaggerfallCastSource { Spell, ItemUse, ItemHeld, ItemStrike, DungeonAction, Potion, Quest }
internal sealed record DaggerfallReadySpell(string SpellKey, ulong? ItemId, int Cost, DaggerfallCastSource Source);
internal sealed class DaggerfallSpellReadiness { internal DaggerfallReadySpell? Ready { get; set; } }

/// <summary>One released operation. Target callbacks consume it once; saves retain only admitted effects.</summary>
internal sealed class DaggerfallLiveSpell(long sequence, long? casterId, ulong? itemId, DaggerfallSpellDefinition spell,
    int cost, int level, DaggerfallEffectDefinition[] definitions, DaggerfallCastSource source, Vector3? origin, Vector3? direction,
    DaggerfallActionCastSource? actionSource = null, DaggerfallSpellTarget? targetOverride = null)
{
    internal DaggerfallCastSource Source { get; } = source;
    internal DaggerfallActionCastSource? ActionSource { get; set; } = actionSource;
    internal Vector3? ReleaseOrigin { get; set; } = origin;
    internal Vector3? ReleaseDirection { get; set; } = direction;
    /// <summary>
    /// The admitted simulation identity at which this transient release entered the flight owner.
    /// This is intentionally product timing provenance, not a second clock or a replay log: the
    /// session uses it to avoid charging a release for slices that happened before it existed.
    /// </summary>
    internal ulong? ReleaseGeneration { get; set; }
    internal ulong? ReleaseSimulationStep { get; set; }
    /// <summary>Elapsed movement time for a transient dungeon missile; reset on admission only.</summary>
    internal double DungeonFlightElapsedSeconds { get; set; }
    internal bool BypassSave => Source is DaggerfallCastSource.Potion or DaggerfallCastSource.ItemHeld or DaggerfallCastSource.Quest || Source == DaggerfallCastSource.ItemUse && Target == DaggerfallSpellTarget.CasterOnly;
    internal bool BypassChance => Source == DaggerfallCastSource.Potion || Source == DaggerfallCastSource.ItemUse && Target == DaggerfallSpellTarget.CasterOnly;
    internal long Sequence { get; } = sequence;
    internal long? CasterId { get; } = casterId;
    internal ulong? ItemId { get; } = itemId;
    internal DaggerfallSpellDefinition Spell { get; } = spell;
    internal DaggerfallSpellTarget Target => targetOverride ?? DaggerfallMagicCostPolicy.TargetForRangeType(Spell.RangeType);
    internal DaggerfallMagicBundleElement Element => (DaggerfallMagicBundleElement)(Spell.Element + 1);
    internal int Cost { get; } = cost;
    internal int CasterLevel { get; } = level;
    internal DaggerfallEffectDefinition[] Definitions { get; } = definitions;
    internal List<DaggerfallCastEffectResult> Results { get; } = [];
    internal List<DaggerfallSpellAbsorptionResult> Absorptions { get; } = [];
    internal bool Delivered { get; set; }
    internal bool Reflected { get; set; }
}

/// <summary>Ruleset casting coordination over canonical actors, costs, effects, draws and facts.</summary>
internal sealed partial class DaggerfallCasting(DaggerfallMagicCatalogSet catalog, DaggerfallEffectLifecycle effects,
    Func<long, Actor?> resolveActor, Func<long, DaggerfallMagicTargetProfile> profile,
    Func<ulong, bool> itemAvailable, Action<DaggerfallSkillUse> recordSkill,
    Action<DaggerfallCastResult> completed, IRandomService random, long playerId, long nextSequence = 1,
    Func<string, bool>? playerKnowsSpell = null, Func<long, int>? casterLevel = null,
    Func<long, ulong, bool>? ownsItem = null, Func<ulong?>? releaseGeneration = null,
    Func<ulong?>? releaseSimulationStep = null, Func<string, bool>? playerGrantedSpell = null)
{
    private readonly HashSet<DaggerfallLiveSpell> _pending = [];
    private readonly HashSet<DaggerfallSpellReadiness> _armed = [];
    private readonly Func<ulong?>? _releaseGeneration = releaseGeneration;
    private readonly Func<ulong?>? _releaseSimulationStep = releaseSimulationStep;
    private DaggerfallSpellReadiness? Readiness(long id) => resolveActor(id)?.Get<DaggerfallSpellReadiness>();

    private DaggerfallLiveSpell StampRelease(DaggerfallLiveSpell bundle)
    {
        bundle.ReleaseGeneration = _releaseGeneration?.Invoke();
        bundle.ReleaseSimulationStep = _releaseSimulationStep?.Invoke();
        return bundle;
    }
    internal void Rebase(Vector3 delta)
    {
        foreach (var bundle in _pending)
        {
            if (bundle.ReleaseOrigin is { } position) bundle.ReleaseOrigin = position + delta;
            if (bundle.ActionSource is { } source) bundle.ActionSource = source with { Origin = source.Origin + delta };
        }
    }

    internal void ClearTransient()
    {
        foreach (var state in _armed) state.Ready = null;
        _armed.Clear();
        ClearPending();
    }
    internal void ClearPending()
    {
        foreach (var bundle in _pending) bundle.Delivered = true;
        _pending.Clear();
    }
    internal long NextSequence { get; private set; } = nextSequence > 0 ? nextSequence
        : throw new ArgumentOutOfRangeException(nameof(nextSequence));
    /// <summary>Transient action-resource missiles are advanced by the session's Engine collision caller.</summary>
    internal IReadOnlyList<DaggerfallLiveSpell> PendingDungeonFlights =>
        _pending.Where(bundle => bundle.ActionSource is not null).OrderBy(bundle => bundle.Sequence).ToArray();

    /// <summary>
    /// All ordinary ranged releases waiting for the one session flight owner to submit their
    /// swept Engine segment.  Dungeon actions are included because they use the same live bundle
    /// and collision path; callers that need the action-only presentation use
    /// <see cref="PendingDungeonFlights"/>.
    /// </summary>
    internal IReadOnlyList<DaggerfallLiveSpell> PendingRangedFlights =>
        _pending.Where(bundle => bundle.Target is DaggerfallSpellTarget.SingleTargetAtRange or DaggerfallSpellTarget.AreaAtRange)
            .OrderBy(bundle => bundle.Sequence).ToArray();

    /// <summary>
    /// Releases owned by the one session flight step. Actorless dungeon actions retain their
    /// donor target mode so non-ranged action payloads can terminate honestly through the same
    /// impact owner, while ordinary enemy releases include only ranged target modes.
    /// </summary>
    internal IReadOnlyList<DaggerfallLiveSpell> PendingFlightOperations =>
        _pending.Where(bundle => bundle.ActionSource is not null
            || bundle.Target is DaggerfallSpellTarget.SingleTargetAtRange or DaggerfallSpellTarget.AreaAtRange)
            .OrderBy(bundle => bundle.Sequence).ToArray();

    /// <summary>Whether an actor already has an unreconciled release in the shared pending set.</summary>
    internal bool HasPending(long casterId) => _pending.Any(bundle => bundle.CasterId == casterId && !bundle.Delivered);

    /// <summary>
    /// Reuses the effect lifecycle's compiled like-kind ownership for donor duplicate suppression.
    /// No enemy-specific effect cache is retained here: every payload must already be active on the
    /// target before the spell is considered a duplicate.
    /// </summary>
    internal bool EffectsAlreadyOnTarget(string spellKey, long targetId)
    {
        if (!catalog.Spells.TryGetValue(spellKey, out DaggerfallSpellDefinition? spell)
            || !TryDefinitions(spell, out DaggerfallEffectDefinition[] definitions)) return false;
        foreach (DaggerfallEffectDefinition definition in definitions)
        {
            if (!effects.Active.Any(active => checked((long)active.Context.Target.Value) == targetId
                && active.Definition.LikeKind == definition.LikeKind)) return false;
        }
        return definitions.Length > 0;
    }
    internal DaggerfallCastResult Refuse(long casterId, DaggerfallCastOutcome reason)
    {
        if (Readiness(casterId) is { } state) { state.Ready = null; _armed.Remove(state); }
        return Finish(reason);
    }
    internal DaggerfallCastResult? CheckFlight(DaggerfallLiveSpell bundle)
    {
        if (bundle.Delivered) return new(DaggerfallCastOutcome.AlreadyDelivered, bundle);
        return SourceAvailable(bundle) ? null : Deliver(bundle, []);
    }
    internal DaggerfallReadySpell? ReadyFor(long casterId) => Readiness(casterId)?.Ready;

    internal DaggerfallCastResult Ready(long casterId, string spellKey, ulong? itemId = null, DaggerfallCastSource source = DaggerfallCastSource.Spell)
    {
        if (itemId is not null && source == DaggerfallCastSource.Spell) source = DaggerfallCastSource.ItemUse;
        if (itemId is null && source != DaggerfallCastSource.Spell) return Finish(DaggerfallCastOutcome.SourceUnavailable);
        // A refused replacement cannot leave an earlier ready spell armed.
        if (Readiness(casterId) is { } state) { state.Ready = null; _armed.Remove(state); }
        Actor? actor = ResolveSource(casterId, itemId);
        if (actor is null) return Finish(DaggerfallCastOutcome.SourceUnavailable);
        if (itemId is null && effects.MagicDefenseFor(casterId).BlocksCasting) return Finish(DaggerfallCastOutcome.Silenced);
        if (casterId == playerId && itemId is null && playerKnowsSpell is not null && !playerKnowsSpell(spellKey)) return Finish(DaggerfallCastOutcome.UnknownSpell);
        if (!catalog.Spells.TryGetValue(spellKey, out var spell) || ((!spell.IsCustom && spell.Name.StartsWith('!')) && !(casterId == playerId && itemId is null && playerGrantedSpell?.Invoke(spellKey) == true)) || spell.Effects.Count == 0)
            return Finish(DaggerfallCastOutcome.UnknownSpell);
        if (!TryDefinitions(spell, out _)) return Finish(DaggerfallCastOutcome.UnsupportedEffect);
        int cost = itemId is null ? Quote(casterId, actor, spell) : 0;
        if (!CanPay(actor, casterId, cost, itemId)) return Finish(DaggerfallCastOutcome.InsufficientMagicka);
        var readiness = actor.Get<DaggerfallSpellReadiness>();
        readiness.Ready = new(spellKey, itemId, cost, source); _armed.Add(readiness);
        return Finish(DaggerfallCastOutcome.Ready);
    }

    /// <summary>
    /// Admits a normalized dungeon action's spell ordinal through the same catalog and effect
    /// definitions used by ordinary casting.  Caster-only actions arm the player's real readiness
    /// component at no cost; other actions create an actorless missile whose source is the admitted
    /// action resource and whose target is the player.
    /// </summary>
    internal DaggerfallCastResult TriggerDungeonAction(DaggerfallActionCastSource source, int spellOrdinal,
        Vector3? targetPosition = null)
    {
        if (!source.IsValid || source.TargetId != playerId) return Finish(DaggerfallCastOutcome.SourceUnavailable);
        if (spellOrdinal < 0) return Finish(DaggerfallCastOutcome.UnknownSpell);
        string key = $"spell.{checked(spellOrdinal + 1):D3}";
        if (!catalog.Spells.TryGetValue(key, out DaggerfallSpellDefinition? spell)
            || (!spell.IsCustom && spell.Name.StartsWith('!')) || spell.Effects.Count == 0)
            return Finish(DaggerfallCastOutcome.UnknownSpell);
        if (!TryDefinitions(spell, out DaggerfallEffectDefinition[] definitions))
            return Finish(DaggerfallCastOutcome.UnsupportedEffect);

        DaggerfallSpellTarget target = DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType);
        if (target == DaggerfallSpellTarget.CasterOnly)
            return ReadyDungeonActionSpell(key, spell);

        // DaggerfallAction converts only a touch payload to a missile aimed at the player after
        // the source bundle has been admitted. Preserve every other source target mode on the
        // live operation; in particular AreaAroundCaster is not a ranged-area conversion.
        DaggerfallSpellTarget? targetOverride = target switch
        {
            DaggerfallSpellTarget.ByTouch => DaggerfallSpellTarget.SingleTargetAtRange,
            _ => null,
        };
        Vector3 origin = source.Origin + Vector3.UnitY * DaggerfallDungeonSpellPolicy.MissileOriginHeightMetres;
        Vector3? direction = null;
        if (targetPosition is Vector3 targetPoint
            && DaggerfallDungeonSpellPolicy.TryNormalizeDirection(targetPoint - source.Origin, out Vector3 normalized))
            direction = normalized;
        long sequence = NextSequence;
        NextSequence = checked(sequence + 1);
        DaggerfallLiveSpell bundle = new(sequence, null, null, spell, 0,
            DaggerfallMagicAdmissionPolicy.CalculateCasterLevel(source.CasterLevel), definitions,
            DaggerfallCastSource.DungeonAction, origin, direction, source, targetOverride);
        _pending.Add(StampRelease(bundle));
        return Finish(DaggerfallCastOutcome.Released, bundle);
    }

    private DaggerfallCastResult ReadyDungeonActionSpell(string key, DaggerfallSpellDefinition spell)
    {
        if (ResolveSource(playerId, null) is not Actor actor)
            return Finish(DaggerfallCastOutcome.SourceUnavailable);
        if (Readiness(playerId) is { } previous)
        {
            previous.Ready = null;
            _armed.Remove(previous);
        }
        if (DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType) != DaggerfallSpellTarget.CasterOnly)
            return Finish(DaggerfallCastOutcome.InvalidTarget);
        DaggerfallSpellReadiness readiness = actor.Get<DaggerfallSpellReadiness>();
        readiness.Ready = new(key, null, 0, DaggerfallCastSource.DungeonAction);
        _armed.Add(readiness);
        return Finish(DaggerfallCastOutcome.Ready);
    }

    internal int? AvailableSpellCost(long casterId, string key)
    {
        var actor=ResolveSource(casterId,null);
        return actor is not null && catalog.Spells.TryGetValue(key,out var spell)
            && !((!spell.IsCustom && spell.Name.StartsWith('!')) && !(casterId == playerId && playerGrantedSpell?.Invoke(key) == true)) && spell.Effects.Count>0 && TryDefinitions(spell,out _)
            ? Quote(casterId,actor,spell) : null;
    }

    internal void RestoreReadySpell(DaggerfallReadySpell ready)
    {
        if (!Enum.IsDefined(ready.Source) || ready.Source is DaggerfallCastSource.ItemHeld or DaggerfallCastSource.ItemStrike
            || (ready.ItemId is null) != (ready.Source is DaggerfallCastSource.Spell or DaggerfallCastSource.DungeonAction)
            || ready.Cost < 0 || ready.ItemId is not null && ready.Cost != 0
            || ResolveSource(playerId, ready.ItemId) is null
            || !catalog.Spells.TryGetValue(ready.SpellKey, out var spell) || ((!spell.IsCustom && spell.Name.StartsWith('!')) && !(ready.Source == DaggerfallCastSource.Spell && playerGrantedSpell?.Invoke(ready.SpellKey) == true)) || !TryDefinitions(spell, out _)
            || ready.Source == DaggerfallCastSource.DungeonAction && DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType) != DaggerfallSpellTarget.CasterOnly
            || ready.Source == DaggerfallCastSource.DungeonAction && ready.Cost != 0
            || ready.Source != DaggerfallCastSource.DungeonAction && ready.ItemId is null && playerKnowsSpell?.Invoke(ready.SpellKey) == false)
            throw new ArgumentException($"Saved ready spell '{ready.SpellKey}' has an unavailable source or effect.");
        var state = Readiness(playerId)!;
        state.Ready = ready; _armed.Add(state);
    }

    internal void CancelItemReferences(ulong itemId)
    {
        foreach (var state in _armed.Where(value => value.Ready?.ItemId == itemId).ToArray())
        { state.Ready = null; _armed.Remove(state); }
        foreach (var bundle in _pending.Where(value => value.ItemId == itemId).ToArray()) Deliver(bundle, []);
    }

    /// <summary>Already admitted equip/strike/self-use payloads share release and delivery without replacing readiness.</summary>
    internal DaggerfallCastResult Trigger(long casterId, string key, ulong itemId, DaggerfallCastSource source, long targetId)
    {
        if (source == DaggerfallCastSource.Spell) throw new ArgumentException("An item trigger requires item provenance.", nameof(source));
        var actor = ResolveSource(casterId, itemId);
        if (actor is null) return Finish(DaggerfallCastOutcome.SourceUnavailable);
        if (!catalog.Spells.TryGetValue(key, out var spell) || (!spell.IsCustom && spell.Name.StartsWith('!')) || spell.Effects.Count == 0)
            return Finish(DaggerfallCastOutcome.UnknownSpell);
        if (!TryDefinitions(spell, out var definitions)) return Finish(DaggerfallCastOutcome.UnsupportedEffect);
        var release = CreateBundle(actor, casterId, new(key, itemId, 0, source), spell, definitions, null, null, publishRelease: false);
        return Deliver(release.Bundle!, [targetId]);
    }

    internal bool CanCastQuestSpell(string key) => catalog.Spells.TryGetValue(key, out var spell) && spell.Effects.Count > 0 && TryDefinitions(spell, out _);

    /// <summary>Source quest bundles target the foe itself without magicka or saving throws.</summary>
    internal DaggerfallCastResult TriggerQuestSpell(long targetId, string key)
    {
        Actor? target = ResolveSource(targetId, null);
        if (target is null) return Finish(DaggerfallCastOutcome.TargetUnavailable);
        if (!catalog.Spells.TryGetValue(key, out var spell)) return Finish(DaggerfallCastOutcome.UnknownSpell);
        if (!TryDefinitions(spell, out var definitions)) return Finish(DaggerfallCastOutcome.UnsupportedEffect);
        var release = CreateBundle(target, targetId, new(key, null, 0, DaggerfallCastSource.Quest), spell,
            definitions, null, null, publishRelease: false, targetOverride: DaggerfallSpellTarget.CasterOnly);
        return Deliver(release.Bundle!, [targetId]);
    }

    /// <summary>FORM-06's cost-free Spider Touch uses the one live bundle/delivery owner.</summary>
    internal DaggerfallCastResult TriggerMonsterParalysis(long casterId, long targetId)
    {
        Actor? actor = ResolveSource(casterId, null);
        if (actor is null) return Finish(DaggerfallCastOutcome.SourceUnavailable);
        // This is source identity 66, not source ordinal 66 (which is a different spell).
        var matches = catalog.Spells.Values.Where(value => !value.IsCustom && value.Identity == 66).ToArray();
        if (matches.Length != 1) return Finish(DaggerfallCastOutcome.UnknownSpell);
        var spell = matches[0];
        if (DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType) != DaggerfallSpellTarget.ByTouch
            || spell.Effects.Count != 1 || spell.Effects[0] is not { Type: 0, SubType: -1 }
            || !TryDefinitions(spell, out var definitions)) return Finish(DaggerfallCastOutcome.UnsupportedEffect);
        // The donor's noSpellPointCost also bypasses caster silence. Target defenses, chance,
        // saves, incumbent effect state and expiry still go through ordinary delivery.
        var release = CreateBundle(actor, casterId, new(spell.Key, null, 0, DaggerfallCastSource.Spell),
            spell, definitions, null, null, publishRelease: false);
        return Deliver(release.Bundle!, [targetId]);
    }

    internal void RestoreReadySpell(string key)
    {
        if (playerKnowsSpell is not null && !playerKnowsSpell(key) || AvailableSpellCost(playerId,key) is not int cost)
            throw new ArgumentException($"Saved ready spell '{key}' is not a known available spell.");
        var state=Readiness(playerId)!;
        state.Ready=new(key,null,cost,DaggerfallCastSource.Spell); _armed.Add(state);
    }

    internal DaggerfallCastResult Cancel(long casterId)
    {
        var state = Readiness(casterId);
        bool armed = state?.Ready is not null;
        if (state is not null) { state.Ready = null; _armed.Remove(state); }
        return Finish(armed ? DaggerfallCastOutcome.Cancelled : DaggerfallCastOutcome.Unready);
    }

    /// <summary>Caller validates touch/shape using the Engine before release. Ranged delivery awaits its impact callback.</summary>
    internal DaggerfallCastResult Release(long casterId, bool targetValid, Vector3? origin = null, Vector3? direction = null)
    {
        if (resolveActor(casterId) is null) return Finish(DaggerfallCastOutcome.SourceUnavailable);
        if (ReadyFor(casterId) is not { } ready) return Finish(DaggerfallCastOutcome.Unready);
        Actor? actor = ResolveSource(casterId, ready.ItemId);
        if (actor is null) { if (Readiness(casterId) is { } state) { state.Ready = null; _armed.Remove(state); } return Finish(DaggerfallCastOutcome.SourceUnavailable); }
        if (casterId==playerId && ready.ItemId is null && ready.Source != DaggerfallCastSource.DungeonAction
            && playerKnowsSpell is not null && !playerKnowsSpell(ready.SpellKey))
            return Refuse(casterId,DaggerfallCastOutcome.UnknownSpell);
        if (ready.ItemId is null && effects.MagicDefenseFor(casterId).BlocksCasting)
            return Finish(DaggerfallCastOutcome.Silenced);
        if (!targetValid) return Finish(DaggerfallCastOutcome.InvalidTarget);
        var spell = catalog.Spells[ready.SpellKey];
        if (!TryDefinitions(spell, out var definitions)) { if (Readiness(casterId) is { } state) { state.Ready = null; _armed.Remove(state); } return Finish(DaggerfallCastOutcome.UnsupportedEffect); }
        var armed = actor.Get<DaggerfallSpellReadiness>(); armed.Ready = null; _armed.Remove(armed);
        return CreateBundle(actor, casterId, ready, spell, definitions, origin, direction);
    }

    private DaggerfallCastResult CreateBundle(Actor actor, long casterId, DaggerfallReadySpell ready,
        DaggerfallSpellDefinition spell, DaggerfallEffectDefinition[] definitions, Vector3? origin, Vector3? direction, bool publishRelease = true, DaggerfallSpellTarget? targetOverride = null)
    {
        int cost = ready.Cost;
        long sequence = NextSequence;
        NextSequence = checked(sequence + 1);
        if (ready.ItemId is null)
        {
            Track magicka = actor.Get<StatsComponent>().GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value));
            magicka.SetCurrent(magicka.Current - cost, clamp: true);
        }
        DaggerfallLiveSpell bundle = new(sequence, casterId, ready.ItemId, spell, cost,
            DaggerfallMagicAdmissionPolicy.CalculateCasterLevel(casterLevel?.Invoke(casterId) ?? actor.Get<ProgressionState>().Level), definitions, ready.Source, origin, direction, targetOverride: targetOverride);
        if (casterId == playerId && (ready.Source == DaggerfallCastSource.Spell
            || ready.Source == DaggerfallCastSource.ItemUse && bundle.Target != DaggerfallSpellTarget.CasterOnly))
            foreach (var effect in spell.Effects)
                recordSkill(new(catalog.RequireEffectCost(effect).School, DaggerfallSkillUseReason.ReleasedSpellEffect,
                    DaggerfallSkillUseOutcome.Accepted));
        _pending.Add(StampRelease(bundle));
        return publishRelease ? Finish(DaggerfallCastOutcome.Released, bundle) : new(DaggerfallCastOutcome.Released, bundle);
    }

    internal DaggerfallCastResult Deliver(DaggerfallLiveSpell bundle, IReadOnlyList<long> targets)
    {
        if (bundle.Delivered) return new(DaggerfallCastOutcome.AlreadyDelivered, bundle);
        if (!_pending.Remove(bundle)) return Finish(DaggerfallCastOutcome.SourceUnavailable, bundle);
        bundle.Delivered = true;
        if (!SourceAvailable(bundle))
        {
            for (int i = 0; i < bundle.Definitions.Length; i++)
                bundle.Results.Add(new(i, null, DaggerfallCastOutcome.SourceUnavailable));
            return Finish(DaggerfallCastOutcome.SourceUnavailable, bundle);
        }
        long[] unique = targets.Distinct().ToArray();
        if (bundle.Target == DaggerfallSpellTarget.CasterOnly && (unique.Length != 1 || bundle.CasterId is not long caster || unique[0] != caster)
            || bundle.Target is DaggerfallSpellTarget.ByTouch or DaggerfallSpellTarget.SingleTargetAtRange && unique.Length > 1)
            return Finish(DaggerfallCastOutcome.InvalidTarget, bundle);
        if (unique.Length == 0)
        {
            for (int i = 0; i < bundle.Definitions.Length; i++) bundle.Results.Add(new(i, null, DaggerfallCastOutcome.Missed));
            return Finish(DaggerfallCastOutcome.Missed, bundle);
        }
        int draw = 0;
        int Roll(int low = 1, int high = 100) => checked((int)random.DrawKeyed(new(0, "daggerfall.casting.v1",
            $"cast:{bundle.Sequence}:draw:{++draw}", low, high)).Value);
        foreach (long target in unique)
        {
            if (bundle.Target == DaggerfallSpellTarget.AreaAroundCaster && bundle.CasterId is long areaCaster && target == areaCaster) continue;
            DeliverTo(bundle, target, reflected: false, Roll);
        }
        return Finish(DaggerfallCastOutcome.DeliveryCompleted, bundle);
    }

    private void DeliverTo(DaggerfallLiveSpell bundle, long targetId, bool reflected, Func<int, int, int> roll)
    {
        Actor? target = ResolveSource(targetId, null);
        if (target is null)
        {
            for (int i = 0; i < bundle.Definitions.Length; i++) bundle.Results.Add(new(i, targetId, DaggerfallCastOutcome.TargetUnavailable));
            return;
        }
        var stats = target.Get<StatsComponent>();
        int absorbed = 0;
        HashSet<ulong> absorptionItems = [];
        for (int i = 0; i < bundle.Definitions.Length; i++)
        {
            // An earlier payload or reflected bundle may have retired a participant synchronously.
            if (!SourceAvailable(bundle))
            { bundle.Results.Add(new(i, targetId, DaggerfallCastOutcome.SourceUnavailable)); continue; }
            Actor? liveTarget = ResolveSource(targetId, null);
            if (liveTarget is null)
            { bundle.Results.Add(new(i, targetId, DaggerfallCastOutcome.TargetUnavailable)); continue; }
            target = liveTarget;
            stats = target.Get<StatsComponent>();
            var defense = effects.MagicDefenseFor(targetId);
            var definition = bundle.Definitions[i];
            var binding = definition.Spell!;
            var setting = bundle.Spell.Effects[i];
            var source = new DaggerfallMagicEffectSource(true, binding.IsParalysis, binding.IsDisease, binding.AllowedElements, bundle.Element);
            DaggerfallMagicResistanceElement resistanceElement = DaggerfallMagicAdmissionPolicy.GetElementType(source);
            int specificResistance = defense.Resistances.FirstOrDefault(value => value.Element == resistanceElement)?.Chance ?? 0;
            int resistanceChance = Math.Max(defense.AllResistanceChance, specificResistance);
            var liveProfile = profile(targetId);
            var flags = DaggerfallMagicAdmissionPolicy.GetEffectFlags(source);
            var raceFlags = liveProfile.PlayerRaceTolerances?.Immunity ?? DaggerfallMagicEffectFlags.None;
            bool hardImmune = binding.IsDisease && Read(stats, DaggerfallMechanicsIds.ImmunityDisease.Value) > 0
                || binding.IsParalysis && (Read(stats, DaggerfallMechanicsIds.ImmunityParalysis.Value) > 0 || defense.PreventsParalysis)
                || (raceFlags & flags & (DaggerfallMagicEffectFlags.Disease | DaggerfallMagicEffectFlags.Paralysis)) != 0
                || binding.IsDisease && liveProfile.CareerTolerances.Disease == DaggerfallMagicTolerance.Immune
                || binding.IsParalysis && liveProfile.CareerTolerances.Paralysis == DaggerfallMagicTolerance.Immune;
            DaggerfallCastOutcome outcome;
            if (hardImmune) outcome = DaggerfallCastOutcome.Immune;
            else if (bundle.Source is not (DaggerfallCastSource.ItemHeld or DaggerfallCastSource.Potion) && catalog.RequireEffectCost(setting).School == "destruction" && defense.AbsorptionChance > 0
                && stats.TryGetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value), out var magicka)
                && AbsorptionCost(target, bundle, setting) is int refund && magicka.Current + absorbed + refund <= magicka.Maximum.Value
                && (defense.AbsorptionItems is { Length: > 0 } || roll(1, 100) <= defense.AbsorptionChance))
            { absorbed = checked(absorbed + refund); absorptionItems.UnionWith(defense.AbsorptionItems ?? []); outcome = DaggerfallCastOutcome.Absorbed; }
            else if (!bundle.BypassSave && bundle.CasterId is long caster && targetId != caster && !bundle.Reflected
                && defense.ReflectionChance > 0 && roll(1, 100) <= defense.ReflectionChance)
            {
                bundle.Reflected = true;
                DeliverTo(bundle, caster, reflected: true, roll);
                outcome = DaggerfallCastOutcome.Reflected;
            }
            else if (!bundle.BypassSave && bundle.Target != DaggerfallSpellTarget.CasterOnly
                && resistanceChance > 0 && roll(1, 100) <= resistanceChance)
                outcome = DaggerfallCastOutcome.Resisted;
            else if (!bundle.BypassChance && !(binding.BypassItemChance && bundle.ItemId is not null)
                && binding.RollChanceOnCast && roll(1, 100) > DaggerfallMagicAdmissionPolicy.CalculateEffectChance(setting, bundle.CasterLevel)) outcome = DaggerfallCastOutcome.ChanceFailed;
            else
            {
                // Active channel admission already ran above; do not charge a second resistance roll.
                liveProfile = liveProfile with { ActiveResistances = [] };
                string instance = $"cast.{bundle.Sequence}.{targetId}.{i}.{(reflected ? "reflected" : "direct")}";
                var bundleKind = bundle.Source == DaggerfallCastSource.ItemHeld && !binding.UntilHealed
                    ? DaggerfallEffectBundleKind.HeldMagicItem : bundle.Source == DaggerfallCastSource.Potion ? DaggerfallEffectBundleKind.Potion : DaggerfallEffectBundleKind.Spell;
                uint? baseDuration = bundle.Source == DaggerfallCastSource.ItemHeld || binding.UntilHealed || binding.UntilTriggered ? null : binding.SupportsDuration ? checked((uint)Math.Max(1,
                    DaggerfallMagicAdmissionPolicy.CalculateEffectDuration(setting, bundle.CasterLevel))) : 1u;
                // Permanent attribute damage rolls its incoming payload/save even when an incumbent exists.
                int permanentAmount = binding.UntilHealed ? DaggerfallMagicAdmissionPolicy.RollEffectMagnitude(setting, bundle.CasterLevel, roll) : 0;
                int permanentPercent = !binding.UntilHealed || bundle.BypassSave || bundle.Target == DaggerfallSpellTarget.CasterOnly
                    ? 100 : DaggerfallMagicAdmissionPolicy.SavingThrow(source, liveProfile, () => roll(1, 100));
                if (permanentPercent == 0) { bundle.Results.Add(new(i, targetId, DaggerfallCastOutcome.Resisted, 0)); continue; }
                permanentAmount = (int)(permanentAmount * (permanentPercent / 100f));
                // An actorless action still carries its admitted source provenance into the
                // common effect owner. Immediate destruction uses that typed action source to
                // retain the donor's player-level power without manufacturing a caster actor.
                DaggerfallCastOrigin? origin = bundle.Source == DaggerfallCastSource.DungeonAction || binding.UntilHealed
                    ? new(bundle.CasterId, bundle.ItemId, bundle.Source, bundle.ActionSource) : null;
                long? operationalCaster = binding.UntilHealed ? null : bundle.CasterId;
                ulong? operationalItem = binding.UntilHealed ? null : bundle.ItemId;
                var preliminaryState = new DaggerfallCastEffectState(setting, bundle.CasterLevel, permanentAmount, permanentPercent, origin);
                JsonElement preliminary = binding.CreateState?.Invoke(preliminaryState)
                    ?? JsonSerializer.SerializeToElement(preliminaryState, DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
                if (effects.TryAdmitIncumbent(new(instance, definition.Key, bundle.Source == DaggerfallCastSource.Potion ? bundle.Spell.Key : $"spell.{bundle.Spell.Key}", operationalCaster,
                    targetId, setting.Key, bundle.Element.ToString(), operationalItem, 1, baseDuration, preliminary) { BundleKind = bundleKind, BundleId = $"cast.{bundle.Sequence}", BundleSequence = bundle.Sequence, BundleName = bundle.Spell.Name }, out var incumbent,
                    () =>
                    {
                        var incoming = new DaggerfallCastEffectState(setting, bundle.CasterLevel,
                            binding.UntilHealed ? permanentAmount : binding.SupportsMagnitude && !binding.MagnitudePerRound ? DaggerfallMagicAdmissionPolicy.RollEffectMagnitude(setting, bundle.CasterLevel, roll) : 0, permanentPercent, origin);
                        return binding.CreateState?.Invoke(incoming)
                            ?? JsonSerializer.SerializeToElement(incoming, DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
                    }))
                {
                    string? actual = incumbent == DaggerfallEffectAdmissionOutcome.Refreshed
                        ? effects.IncumbentFor(definition, targetId, preliminary, operationalCaster, operationalItem, bundleKind)!.Context.Instance.Value : null;
                    bundle.Results.Add(new(i, targetId, incumbent == DaggerfallEffectAdmissionOutcome.Refreshed
                        ? DaggerfallCastOutcome.Refreshed : DaggerfallCastOutcome.IncumbentRejected, permanentPercent, Instance: actual));
                    continue;
                }
                int amount = binding.UntilHealed ? permanentAmount : binding.SupportsMagnitude && !binding.MagnitudePerRound ? DaggerfallMagicAdmissionPolicy.RollEffectMagnitude(setting, bundle.CasterLevel, roll) : 0;
                int percent = binding.UntilHealed ? permanentPercent : bundle.BypassSave || bundle.Target == DaggerfallSpellTarget.CasterOnly ? 100 : DaggerfallMagicAdmissionPolicy.SavingThrow(source, liveProfile, () => roll(1, 100));
                if (percent == 0) { bundle.Results.Add(new(i, targetId, DaggerfallCastOutcome.Resisted, percent)); continue; }
                uint? duration = baseDuration;
                // Non-magnitude saves reject at zero and otherwise retain the full duration.
                if (binding.SupportsMagnitude && !binding.UntilHealed)
                { amount = (int)(amount * (percent / 100f)); duration = bundle.Source == DaggerfallCastSource.ItemHeld ? null : binding.SupportsDuration ? checked((uint)Math.Max(1, DaggerfallMagicAdmissionPolicy.CalculateEffectDuration(setting, bundle.CasterLevel))) : 1u; }
                var state = new DaggerfallCastEffectState(setting, bundle.CasterLevel, amount, percent, origin);
                JsonElement payload = binding.CreateState?.Invoke(state)
                    ?? JsonSerializer.SerializeToElement(state, DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
                var admission = effects.Start(new(instance, definition.Key, bundle.Source == DaggerfallCastSource.Potion ? bundle.Spell.Key : $"spell.{bundle.Spell.Key}", operationalCaster,
                    targetId, setting.Key, bundle.Element.ToString(), operationalItem, 1, duration, payload) { BundleId = $"cast.{bundle.Sequence}", BundleSequence = bundle.Sequence, BundleName = bundle.Spell.Name, BundleKind = bundleKind });
                outcome = admission switch
                {
                    DaggerfallEffectAdmissionOutcome.TargetUnavailable => DaggerfallCastOutcome.TargetUnavailable,
                    DaggerfallEffectAdmissionOutcome.SourceUnavailable => DaggerfallCastOutcome.SourceUnavailable,
                    DaggerfallEffectAdmissionOutcome.NoMatch => DaggerfallCastOutcome.NoMatch,
                    DaggerfallEffectAdmissionOutcome.Rejected => DaggerfallCastOutcome.IncumbentRejected,
                    DaggerfallEffectAdmissionOutcome.Refreshed => DaggerfallCastOutcome.Refreshed,
                    DaggerfallEffectAdmissionOutcome.Replaced => DaggerfallCastOutcome.Replaced,
                    _ => DaggerfallCastOutcome.Applied,
                };
                if (admission == DaggerfallEffectAdmissionOutcome.Refreshed)
                    instance = effects.IncumbentFor(definition, targetId, payload, operationalCaster, operationalItem, bundleKind)!.Context.Instance.Value;
                bundle.Results.Add(new(i, targetId, outcome, percent, outcome is DaggerfallCastOutcome.NoMatch or DaggerfallCastOutcome.SourceUnavailable or DaggerfallCastOutcome.TargetUnavailable ? null : instance));
                continue;
            }
            bundle.Results.Add(new(i, targetId, outcome));
        }
        if (absorbed > 0)
        {
            double restored = 0;
            if (ResolveSource(targetId, null)?.Entity == target.Entity)
            {
                Track magicka = stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value));
                int refund = targetId == bundle.CasterId && bundle.Cost > 0 ? Math.Min(absorbed, bundle.Cost) : absorbed;
                double before = magicka.Current;
                magicka.SetCurrent(before + refund, clamp: true);
                restored = magicka.Current - before;
            }
            bundle.Absorptions.Add(new(targetId, absorbed, restored, absorptionItems.Order().ToArray()));
        }
    }

    private int AbsorptionCost(Actor target, DaggerfallLiveSpell bundle, DaggerfallSpellEffectDefinition setting)
    {
        var cost = DaggerfallMagicCostPolicy.CalculateEffectCosts(catalog, setting, Schools(target));
        return Math.Max(5, DaggerfallMagicCostPolicy.ApplyTargetMultiplier(cost, bundle.Target).SpellPoints);
    }

    private bool SourceAvailable(DaggerfallLiveSpell bundle) => bundle.CasterId is long caster
        ? ResolveSource(caster, bundle.ItemId) is not null
        : bundle.Source == DaggerfallCastSource.DungeonAction && bundle.ItemId is null
            && bundle.ActionSource is { IsValid: true };

    private Actor? ResolveSource(long id, ulong? itemId)
    {
        if (itemId is ulong item && (!itemAvailable(item) || ownsItem?.Invoke(id, item) == false)) return null;
        Actor? actor = resolveActor(id);
        return actor is not null && actor.Get<StatsComponent>().GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current > 0 ? actor : null;
    }
    private bool TryDefinitions(DaggerfallSpellDefinition spell, out DaggerfallEffectDefinition[] definitions)
    {
        List<DaggerfallEffectDefinition> resolved = [];
        var element = (DaggerfallMagicAllowedElements)(1 << spell.Element);
        var target = (DaggerfallMagicAllowedTargets)(1 << (int)DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType));
        var allowedElements = DaggerfallMagicAllowedElements.Magic;
        foreach (var setting in spell.Effects)
        {
            if (!effects.Catalog.TryResolveSpell(setting, out var definition) || (!spell.IsCustom && (definition.Spell!.AllowedElements & element) == 0)
                || (definition.Spell!.AllowedTargets & target) == 0
                || definition.Apply is null && definition.MagicRound is null && definition.MagicDefense is null
                    && definition.MovementProtection == default && definition.Perception == default && definition.ControlRestrictions == default)
            { definitions = []; return false; }
            if (spell.IsCustom && !definition.Spell!.SpellMaker) { definitions = []; return false; }
            allowedElements |= definition.Spell!.AllowedElements;
            resolved.Add(definition);
        }
        if (spell.IsCustom && (allowedElements & element) == 0) { definitions = []; return false; }
        definitions = resolved.ToArray(); return true;
    }
    private int Quote(long casterId, Actor actor, DaggerfallSpellDefinition spell) =>
        casterId == playerId && playerGrantedSpell?.Invoke(spell.Key) == true
            ? DaggerfallMagicCostPolicy.CalculateTotalEffectCosts(catalog, spell.Effects,
                DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType), Schools(actor), minimumCastingCost: true).SpellPoints
            : DaggerfallMagicAdmissionPolicy.CalculateCastingCost(catalog, spell, Schools(actor), enchantingItem: false);
    private static IReadOnlyDictionary<string, int> Schools(Actor actor) => new[] { "destruction", "restoration", "illusion", "alteration", "thaumaturgy", "mysticism" }
        .ToDictionary(key => key, key => Read(actor.Get<StatsComponent>(), key));
    private bool CanPay(Actor actor, long casterId, int cost, ulong? itemId) => itemId is not null
        || (casterId == playerId ? actor.Get<StatsComponent>().GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value)).Current >= cost
            : actor.Get<StatsComponent>().GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value)).Current > 0);
    internal static int Read(StatsComponent stats, string key) => stats.TryGetStat(StatId.Parse(key), out var stat) ? stat.ValueInt : 0;
    private DaggerfallCastResult Finish(DaggerfallCastOutcome outcome, DaggerfallLiveSpell? bundle = null)
    {
        var result = new DaggerfallCastResult(outcome, bundle);
        // A release and its terminal delivery are distinct authoritative operations. Readiness
        // reads/cancellation are returned directly, never repeated as release/impact facts.
        if (outcome is not (DaggerfallCastOutcome.Ready or DaggerfallCastOutcome.Cancelled or DaggerfallCastOutcome.Unready)) completed(result);
        return result;
    }
}
