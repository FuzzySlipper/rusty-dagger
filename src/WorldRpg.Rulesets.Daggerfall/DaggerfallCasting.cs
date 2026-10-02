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
    bool UntilTriggered = false, bool BypassItemChance = false);

/// <summary>Meaningful settings retained with an admitted effect, never a runtime handle.</summary>
internal sealed record DaggerfallCastEffectState(DaggerfallSpellEffectDefinition Settings, int CasterLevel,
    int Amount, int SavePercent, DaggerfallCastOrigin? Origin = null);

/// <summary>Historical admission provenance, not a dependency on a living actor or item.</summary>
internal sealed record DaggerfallCastOrigin(long CasterId, ulong? ItemId, DaggerfallCastSource Source);

/// <summary>Computed from actual active effects; no independently retained defense state.</summary>
internal sealed record DaggerfallMagicDefense(int AbsorptionChance, int ReflectionChance,
    DaggerfallMagicActiveResistance[] Resistances, bool BlocksCasting = false, bool PreventsParalysis = false)
{
    internal static DaggerfallMagicDefense None { get; } = new(0, 0, []);
    internal static DaggerfallMagicDefense Combine(IEnumerable<DaggerfallMagicDefense> defenses)
    {
        DaggerfallMagicDefense[] values = defenses.ToArray();
        return new(values.Select(value => value.AbsorptionChance).DefaultIfEmpty().Max(),
            values.Select(value => value.ReflectionChance).DefaultIfEmpty().Max(),
            values.SelectMany(value => value.Resistances).GroupBy(value => value.Element)
                .Select(group => new DaggerfallMagicActiveResistance(group.Key, checked((int)Math.Min(100L, group.Sum(value => (long)value.Chance))))).ToArray(),
            values.Any(value => value.BlocksCasting), values.Any(value => value.PreventsParalysis));
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
internal enum DaggerfallCastSource { Spell, ItemUse, ItemHeld, ItemStrike }
internal sealed record DaggerfallReadySpell(string SpellKey, ulong? ItemId, int Cost, DaggerfallCastSource Source);
internal sealed class DaggerfallSpellReadiness { internal DaggerfallReadySpell? Ready { get; set; } }

/// <summary>One released operation. Target callbacks consume it once; saves retain only admitted effects.</summary>
internal sealed class DaggerfallLiveSpell(long sequence, long casterId, ulong? itemId, DaggerfallSpellDefinition spell,
    int cost, int level, DaggerfallEffectDefinition[] definitions, DaggerfallCastSource source, Vector3? origin, Vector3? direction)
{
    internal DaggerfallCastSource Source { get; } = source;
    internal Vector3? ReleaseOrigin { get; } = origin;
    internal Vector3? ReleaseDirection { get; } = direction;
    internal bool BypassSave => Source == DaggerfallCastSource.ItemHeld || Source == DaggerfallCastSource.ItemUse && Target == DaggerfallSpellTarget.CasterOnly;
    internal bool BypassChance => Source == DaggerfallCastSource.ItemUse && Target == DaggerfallSpellTarget.CasterOnly;
    internal long Sequence { get; } = sequence;
    internal long CasterId { get; } = casterId;
    internal ulong? ItemId { get; } = itemId;
    internal DaggerfallSpellDefinition Spell { get; } = spell;
    internal DaggerfallSpellTarget Target => DaggerfallMagicCostPolicy.TargetForRangeType(Spell.RangeType);
    internal DaggerfallMagicBundleElement Element => (DaggerfallMagicBundleElement)(Spell.Element + 1);
    internal int Cost { get; } = cost;
    internal int CasterLevel { get; } = level;
    internal DaggerfallEffectDefinition[] Definitions { get; } = definitions;
    internal List<DaggerfallCastEffectResult> Results { get; } = [];
    internal bool Delivered { get; set; }
    internal bool Reflected { get; set; }
}

/// <summary>Ruleset casting coordination over canonical actors, costs, effects, draws and facts.</summary>
internal sealed class DaggerfallCasting(DaggerfallMagicCatalogSet catalog, DaggerfallEffectLifecycle effects,
    Func<long, Actor?> resolveActor, Func<long, DaggerfallMagicTargetProfile> profile,
    Func<ulong, bool> itemAvailable, Action<DaggerfallSkillUse> recordSkill,
    Action<DaggerfallCastResult> completed, IRandomService random, long playerId, long nextSequence = 1, Func<string, bool>? playerKnowsSpell = null, Func<long, int>? casterLevel = null)
{
    private readonly HashSet<DaggerfallLiveSpell> _pending = [];
    private readonly HashSet<DaggerfallSpellReadiness> _armed = [];
    private DaggerfallSpellReadiness? Readiness(long id) => resolveActor(id)?.Get<DaggerfallSpellReadiness>();
    internal void ClearTransient()
    {
        foreach (var state in _armed) state.Ready = null;
        _armed.Clear();
        foreach (var bundle in _pending) bundle.Delivered = true;
        _pending.Clear();
    }
    internal long NextSequence { get; private set; } = nextSequence > 0 ? nextSequence
        : throw new ArgumentOutOfRangeException(nameof(nextSequence));
    internal DaggerfallCastResult Refuse(long casterId, DaggerfallCastOutcome reason)
    {
        if (Readiness(casterId) is { } state) { state.Ready = null; _armed.Remove(state); }
        return Finish(reason);
    }
    internal DaggerfallCastResult? CheckFlight(DaggerfallLiveSpell bundle)
    {
        if (bundle.Delivered) return new(DaggerfallCastOutcome.AlreadyDelivered, bundle);
        return ResolveSource(bundle.CasterId, bundle.ItemId) is null ? Deliver(bundle, []) : null;
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
        if (!catalog.Spells.TryGetValue(spellKey, out var spell) || spell.Name.StartsWith('!') || spell.Effects.Count == 0)
            return Finish(DaggerfallCastOutcome.UnknownSpell);
        if (!TryDefinitions(spell, out _)) return Finish(DaggerfallCastOutcome.UnsupportedEffect);
        int cost = itemId is null ? Quote(actor, spell) : 0;
        if (!CanPay(actor, casterId, cost, itemId)) return Finish(DaggerfallCastOutcome.InsufficientMagicka);
        var readiness = actor.Get<DaggerfallSpellReadiness>();
        readiness.Ready = new(spellKey, itemId, cost, source); _armed.Add(readiness);
        return Finish(DaggerfallCastOutcome.Ready);
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
        if (ready.ItemId is null && effects.MagicDefenseFor(casterId).BlocksCasting)
            return Finish(DaggerfallCastOutcome.Silenced);
        if (!targetValid) return Finish(DaggerfallCastOutcome.InvalidTarget);
        var spell = catalog.Spells[ready.SpellKey];
        if (!TryDefinitions(spell, out var definitions)) { if (Readiness(casterId) is { } state) { state.Ready = null; _armed.Remove(state); } return Finish(DaggerfallCastOutcome.UnsupportedEffect); }
        int cost = ready.Cost;
        var armed = actor.Get<DaggerfallSpellReadiness>(); armed.Ready = null; _armed.Remove(armed);
        long sequence = NextSequence;
        NextSequence = checked(sequence + 1);
        if (ready.ItemId is null)
        {
            Track magicka = actor.Get<StatsComponent>().GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value));
            magicka.SetCurrent(magicka.Current - cost, clamp: true);
        }
        DaggerfallLiveSpell bundle = new(sequence, casterId, ready.ItemId, spell, cost,
            DaggerfallMagicAdmissionPolicy.CalculateCasterLevel(casterLevel?.Invoke(casterId) ?? actor.Get<ProgressionState>().Level), definitions, ready.Source, origin, direction);
        if (casterId == playerId && (ready.Source == DaggerfallCastSource.Spell
            || ready.Source == DaggerfallCastSource.ItemUse && bundle.Target != DaggerfallSpellTarget.CasterOnly))
            foreach (var effect in spell.Effects)
                recordSkill(new(catalog.RequireEffectCost(effect).School, DaggerfallSkillUseReason.ReleasedSpellEffect,
                    DaggerfallSkillUseOutcome.Accepted));
        _pending.Add(bundle);
        return Finish(DaggerfallCastOutcome.Released, bundle);
    }

    internal DaggerfallCastResult Deliver(DaggerfallLiveSpell bundle, IReadOnlyList<long> targets)
    {
        if (bundle.Delivered) return new(DaggerfallCastOutcome.AlreadyDelivered, bundle);
        if (!_pending.Remove(bundle)) return Finish(DaggerfallCastOutcome.SourceUnavailable, bundle);
        bundle.Delivered = true;
        if (ResolveSource(bundle.CasterId, bundle.ItemId) is null)
        {
            for (int i = 0; i < bundle.Definitions.Length; i++)
                bundle.Results.Add(new(i, null, DaggerfallCastOutcome.SourceUnavailable));
            return Finish(DaggerfallCastOutcome.SourceUnavailable, bundle);
        }
        long[] unique = targets.Distinct().ToArray();
        if (bundle.Target == DaggerfallSpellTarget.CasterOnly && (unique.Length != 1 || unique[0] != bundle.CasterId)
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
            if (bundle.Target == DaggerfallSpellTarget.AreaAroundCaster && target == bundle.CasterId) continue;
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
        for (int i = 0; i < bundle.Definitions.Length; i++)
        {
            // An earlier payload or reflected bundle may have retired a participant synchronously.
            if (ResolveSource(bundle.CasterId, bundle.ItemId) is null)
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
            else if (bundle.Source != DaggerfallCastSource.ItemHeld && catalog.RequireEffectCost(setting).School == "destruction" && defense.AbsorptionChance > 0
                && stats.TryGetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value), out var magicka)
                && AbsorptionCost(target, bundle, setting) is int refund && magicka.Current + absorbed + refund <= magicka.Maximum.Value
                && roll(1, 100) <= defense.AbsorptionChance)
            { absorbed = checked(absorbed + refund); outcome = DaggerfallCastOutcome.Absorbed; }
            else if (!bundle.BypassSave && targetId != bundle.CasterId && !bundle.Reflected && defense.ReflectionChance > 0 && roll(1, 100) <= defense.ReflectionChance)
            {
                bundle.Reflected = true;
                DeliverTo(bundle, bundle.CasterId, reflected: true, roll);
                outcome = DaggerfallCastOutcome.Reflected;
            }
            else if (!bundle.BypassSave && bundle.Target != DaggerfallSpellTarget.CasterOnly && defense.Resistances.FirstOrDefault(value => value.Element == DaggerfallMagicAdmissionPolicy.GetElementType(source))
                is { } resistance && roll(1, 100) <= resistance.Chance) outcome = DaggerfallCastOutcome.Resisted;
            else if (!bundle.BypassChance && !(binding.BypassItemChance && bundle.ItemId is not null)
                && binding.RollChanceOnCast && roll(1, 100) > DaggerfallMagicAdmissionPolicy.CalculateEffectChance(setting, bundle.CasterLevel)) outcome = DaggerfallCastOutcome.ChanceFailed;
            else
            {
                // Active channel admission already ran above; do not charge a second resistance roll.
                liveProfile = liveProfile with { ActiveResistances = [] };
                string instance = $"cast.{bundle.Sequence}.{targetId}.{i}.{(reflected ? "reflected" : "direct")}";
                uint? baseDuration = binding.UntilHealed || binding.UntilTriggered ? null : binding.SupportsDuration ? checked((uint)Math.Max(1,
                    DaggerfallMagicAdmissionPolicy.CalculateEffectDuration(setting, bundle.CasterLevel))) : 1u;
                // Permanent attribute damage rolls its incoming payload/save even when an incumbent exists.
                int permanentAmount = binding.UntilHealed ? DaggerfallMagicAdmissionPolicy.RollEffectMagnitude(setting, bundle.CasterLevel, roll) : 0;
                int permanentPercent = !binding.UntilHealed || bundle.BypassSave || bundle.Target == DaggerfallSpellTarget.CasterOnly
                    ? 100 : DaggerfallMagicAdmissionPolicy.SavingThrow(source, liveProfile, () => roll(1, 100));
                if (permanentPercent == 0) { bundle.Results.Add(new(i, targetId, DaggerfallCastOutcome.Resisted, 0)); continue; }
                permanentAmount = (int)(permanentAmount * (permanentPercent / 100f));
                DaggerfallCastOrigin? origin = binding.UntilHealed ? new(bundle.CasterId, bundle.ItemId, bundle.Source) : null;
                long? operationalCaster = binding.UntilHealed ? null : bundle.CasterId;
                ulong? operationalItem = binding.UntilHealed ? null : bundle.ItemId;
                var preliminaryState = new DaggerfallCastEffectState(setting, bundle.CasterLevel, permanentAmount, permanentPercent, origin);
                JsonElement preliminary = binding.CreateState?.Invoke(preliminaryState)
                    ?? JsonSerializer.SerializeToElement(preliminaryState, DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
                if (effects.TryAdmitIncumbent(new(instance, definition.Key, $"spell.{bundle.Spell.Key}", operationalCaster,
                    targetId, setting.Key, bundle.Element.ToString(), operationalItem, 1, baseDuration, preliminary), out var incumbent,
                    () =>
                    {
                        var incoming = new DaggerfallCastEffectState(setting, bundle.CasterLevel,
                            binding.UntilHealed ? permanentAmount : binding.SupportsMagnitude && !binding.MagnitudePerRound ? DaggerfallMagicAdmissionPolicy.RollEffectMagnitude(setting, bundle.CasterLevel, roll) : 0, permanentPercent, origin);
                        return binding.CreateState?.Invoke(incoming)
                            ?? JsonSerializer.SerializeToElement(incoming, DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
                    }))
                {
                    string? actual = incumbent == DaggerfallEffectAdmissionOutcome.Refreshed
                        ? effects.IncumbentFor(definition, targetId, preliminary, operationalCaster, operationalItem)!.Context.Instance.Value : null;
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
                { amount = (int)(amount * (percent / 100f)); duration = binding.SupportsDuration ? checked((uint)Math.Max(1, DaggerfallMagicAdmissionPolicy.CalculateEffectDuration(setting, bundle.CasterLevel))) : 1u; }
                var state = new DaggerfallCastEffectState(setting, bundle.CasterLevel, amount, percent, origin);
                JsonElement payload = binding.CreateState?.Invoke(state)
                    ?? JsonSerializer.SerializeToElement(state, DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
                var admission = effects.Start(new(instance, definition.Key, $"spell.{bundle.Spell.Key}", operationalCaster,
                    targetId, setting.Key, bundle.Element.ToString(), operationalItem, 1, duration, payload) { BundleId = $"cast.{bundle.Sequence}", BundleSequence = bundle.Sequence, BundleName = bundle.Spell.Name, BundleKind = bundle.Source == DaggerfallCastSource.ItemHeld ? DaggerfallEffectBundleKind.HeldMagicItem : DaggerfallEffectBundleKind.Spell });
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
                    instance = effects.IncumbentFor(definition, targetId, payload, operationalCaster, operationalItem)!.Context.Instance.Value;
                bundle.Results.Add(new(i, targetId, outcome, percent, outcome is DaggerfallCastOutcome.NoMatch or DaggerfallCastOutcome.SourceUnavailable or DaggerfallCastOutcome.TargetUnavailable ? null : instance));
                continue;
            }
            bundle.Results.Add(new(i, targetId, outcome));
        }
        if (absorbed > 0 && ResolveSource(targetId, null)?.Entity == target.Entity)
        {
            Track magicka = stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value));
            if (targetId == bundle.CasterId && bundle.Cost > 0) absorbed = Math.Min(absorbed, bundle.Cost);
            magicka.SetCurrent(magicka.Current + absorbed, clamp: true);
        }
    }

    private int AbsorptionCost(Actor target, DaggerfallLiveSpell bundle, DaggerfallSpellEffectDefinition setting)
    {
        var cost = DaggerfallMagicCostPolicy.CalculateEffectCosts(catalog, setting, Schools(target));
        return Math.Max(5, DaggerfallMagicCostPolicy.ApplyTargetMultiplier(cost, bundle.Target).SpellPoints);
    }
    private Actor? ResolveSource(long id, ulong? itemId)
    {
        if (itemId is ulong item && !itemAvailable(item)) return null;
        Actor? actor = resolveActor(id);
        return actor is not null && actor.Get<StatsComponent>().GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current > 0 ? actor : null;
    }
    private bool TryDefinitions(DaggerfallSpellDefinition spell, out DaggerfallEffectDefinition[] definitions)
    {
        List<DaggerfallEffectDefinition> resolved = [];
        var element = (DaggerfallMagicAllowedElements)(1 << spell.Element);
        var target = (DaggerfallMagicAllowedTargets)(1 << (int)DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType));
        foreach (var setting in spell.Effects)
        {
            if (!effects.Catalog.TryResolveSpell(setting, out var definition) || (definition.Spell!.AllowedElements & element) == 0
                || (definition.Spell.AllowedTargets & target) == 0
                || definition.Apply is null && definition.MagicRound is null && definition.MagicDefense is null
                    && definition.MovementProtection == default && definition.Perception == default && definition.ControlRestrictions == default)
            { definitions = []; return false; }
            resolved.Add(definition);
        }
        definitions = resolved.ToArray(); return true;
    }
    private int Quote(Actor actor, DaggerfallSpellDefinition spell) => DaggerfallMagicAdmissionPolicy.CalculateCastingCost(catalog, spell, Schools(actor), enchantingItem: false);
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
