using System.Text.Json;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Effects;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The ruleset-owned outcome of admission and the initial payload.</summary>
internal enum DaggerfallEffectAdmissionOutcome
{
    Started,
    Refreshed,
    Replaced,
    Rejected,
    NoMatch,
}

/// <summary>The narrow completed-change signal cast, item, time, combat, and presentation owners consume.</summary>
internal enum DaggerfallEffectOutcomeKind
{
    Started,
    Refreshed,
    Replaced,
    Rejected,
    NoMatch,
    Cancelled,
    Cured,
    Expired,
}

internal sealed record DaggerfallEffectOutcome(
    DaggerfallEffectOutcomeKind Kind,
    string Instance,
    string EffectKey,
    long TargetId)
{
    internal DaggerfallEffectFeedback Feedback { get; init; }
}

internal enum DaggerfallEffectFeedback { None, MagicSparkle }

/// <summary>How Daggerfall treats another active effect of the same compiled kind on one target.</summary>
internal enum DaggerfallEffectStacking
{
    Stack,
    /// <summary>Extends only the incumbent duration; the original payload and contribution stay in force.</summary>
    RefreshDuration,
    Replace,
    Reject,
}

/// <summary>Typed movement meaning supplied by compiled effect families; the movement owner never infers it from effect names.</summary>
internal readonly record struct DaggerfallMovementProtection(bool PreventsFallDamage, bool GrantsLevitation = false, bool EnhancesClimbing = false);

/// <summary>
/// Typed perception meaning supplied by a compiled effect family. Perception reads this
/// projection instead of inferring concealment or language bonuses from effect keys or payloads.
/// </summary>
internal readonly record struct DaggerfallPerceptionEffectState(
    bool Invisible = false,
    bool Blending = false,
    bool Shade = false,
    int ComprehendLanguagesBonus = 0)
{
    internal DaggerfallPerceptionEffectState Validate()
    {
        if (ComprehendLanguagesBonus < 0)
            throw new ArgumentOutOfRangeException(nameof(ComprehendLanguagesBonus));
        return this;
    }

    internal DaggerfallPerceptionEffectState Combine(DaggerfallPerceptionEffectState other) => new DaggerfallPerceptionEffectState(
        Invisible || other.Invisible,
        Blending || other.Blending,
        Shade || other.Shade,
        checked(ComprehendLanguagesBonus + other.ComprehendLanguagesBonus)).Validate();
}

/// <summary>One compiled Daggerfall effect policy. Future effect families provide their own payload and state meaning here.</summary>
internal sealed record DaggerfallEffectDefinition(
    string Key,
    string LikeKind,
    DaggerfallEffectStacking Stacking,
    ushort MaximumInstances,
    ushort MaximumStacks,
    Func<DaggerfallActiveEffect, IEnumerable<IActiveEffectContribution>>? Apply = null,
    Action<DaggerfallActiveEffect>? MagicRound = null,
    Func<DaggerfallActiveEffect, IEnumerable<IActiveEffectContribution>>? Resume = null,
    DaggerfallMovementProtection MovementProtection = default,
    DaggerfallPerceptionEffectState Perception = default,
    DaggerfallEffectFeedback Feedback = DaggerfallEffectFeedback.None,
    DaggerfallSpellBinding? Spell = null,
    Func<DaggerfallActiveEffect, DaggerfallMagicDefense>? MagicDefense = null,
    Action<DaggerfallActiveEffect, JsonElement>? RefreshState = null,
    bool ExtendIncumbentDuration = false,
    WorldRpg.Kit.Controls.ActorControlRestrictions ControlRestrictions = default,
    Func<JsonElement, JsonElement, bool>? IncumbentSettingsMatch = null,
    bool SourceScopedIncumbent = false)
{
    internal EffectDefinition ToEngineDefinition(string source) => new(
        EffectDefinitionId.Parse($"daggerfall.{Key}"),
        StackingGroupId.Parse($"daggerfall.{LikeKind}"),
        Stacking switch
        {
            DaggerfallEffectStacking.Stack or DaggerfallEffectStacking.Reject => EffectStackingPolicy.IndependentByProvenance,
            DaggerfallEffectStacking.RefreshDuration => EffectStackingPolicy.Refresh,
            DaggerfallEffectStacking.Replace => EffectStackingPolicy.Replace,
            _ => throw new ArgumentOutOfRangeException(nameof(Stacking)),
        },
        MaximumInstances,
        MaximumStacks,
        [SourceDefinitionId.Parse($"daggerfall.{source}")]);
}

/// <summary>Explicit compiled effect definitions. It is deliberately not runtime discovery.</summary>
internal sealed class DaggerfallEffectCatalog
{
    private readonly IReadOnlyDictionary<string, DaggerfallEffectDefinition> _definitions;
    private readonly IReadOnlyDictionary<(int Type, int SubType), DaggerfallEffectDefinition> _spells;

    internal static DaggerfallEffectCatalog Empty { get; } = new([]);

    internal DaggerfallEffectCatalog(IEnumerable<DaggerfallEffectDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = definitions.ToDictionary(
            definition => definition.Key,
            StringComparer.Ordinal);
        _spells = _definitions.Values.Where(value => value.Spell is not null)
            .ToDictionary(value => (value.Spell!.Type, value.Spell.SubType));
    }

    internal bool TryResolveSpell(DaggerfallSpellEffectDefinition effect, out DaggerfallEffectDefinition definition) =>
        _spells.TryGetValue((effect.Type, effect.SubType), out definition!);

    internal DaggerfallEffectDefinition Require(string key) => _definitions.TryGetValue(key, out DaggerfallEffectDefinition? definition)
        ? definition
        : throw new ArgumentException($"The selected Daggerfall ruleset does not define effect '{key}'.", nameof(key));
}

/// <summary>The stable Daggerfall values a cast, item, or service supplies when it creates a live effect.</summary>
internal sealed record DaggerfallEffectRequest(
    string Instance,
    string EffectKey,
    string Source,
    long? CasterId,
    long TargetId,
    string Settings,
    string? Element,
    ulong? ItemId,
    ushort Stacks,
    uint? RemainingRounds,
    JsonElement State);

/// <summary>One live Daggerfall effect's policy state and its Kit lifecycle entry.</summary>
internal sealed class DaggerfallActiveEffect
{
    internal DaggerfallActiveEffect(DaggerfallEffectDefinition definition, ActiveEffectContext context, ushort stacks, JsonElement state, Func<Actor> source, Actor target)
    {
        Definition = definition;
        Context = context;
        Stacks = stacks;
        State = state.Clone();
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    internal DaggerfallEffectDefinition Definition { get; private set; }
    internal ActiveEffectContext Context { get; private set; }
    internal ActiveEffectState Lifecycle { get; private set; } = null!;
    /// <summary>Ruleset-owned durable payload; MagicRound and compiled policy may update it directly.</summary>
    internal JsonElement State { get; set; }
    /// <summary>Requested stack count available to compiled policy before the Engine state is attached.</summary>
    internal ushort Stacks { get; }
    private readonly Func<Actor> _source;
    /// <summary>The live caster resolved at application time, or the target if that caster was retired.</summary>
    internal Actor Source => _source();
    internal Actor Target { get; }
    /// <summary>Compiled policy requests ordinary Engine expiry after the current magic-round payload.</summary>
    internal bool ExpireAfterCurrentRound { get; set; }
    /// <summary>The compiled initial payload found no condition in its selected cure scope.</summary>
    internal bool NoMatchingCondition { get; set; }

    internal void Attach(ActiveEffectState lifecycle)
    {
        Lifecycle = lifecycle;
        Context = lifecycle.Context;
    }

}

/// <summary>
/// Daggerfall's compiled policy over Kit's Engine-backed active-effect lifecycle. It owns effect
/// keys, like-kind selection, bounded donor-style catch-up, and the DTO that persistence restores.
/// </summary>
internal sealed class DaggerfallEffectLifecycle : IDisposable
{
    internal const uint MaximumElapsedCatchupRounds = 2 * 24 * 60;
    private readonly ActorsState _actors;
    private readonly DaggerfallEffectCatalog _catalog;
    private readonly Dictionary<EffectInstanceId, DaggerfallActiveEffect> _effects = [];
    private readonly Dictionary<long, ActiveEffectLifecycle> _lifecycles = [];

    internal event Action<DaggerfallEffectOutcome>? Completed;

    internal DaggerfallEffectLifecycle(ActorsState actors, DaggerfallEffectCatalog catalog)
    {
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    internal IReadOnlyList<DaggerfallActiveEffect> Active => _effects.Values
        .OrderBy(effect => effect.Lifecycle.Context.Instance.Value, StringComparer.Ordinal)
        .ToArray();

    internal DaggerfallEffectCatalog Catalog => _catalog;

    internal DaggerfallMagicDefense MagicDefenseFor(long targetId) => DaggerfallMagicDefense.Combine(
        _effects.Values.Where(effect => checked((long)effect.Context.Target.Value) == targetId)
            .Select(effect => effect.Definition.MagicDefense?.Invoke(effect) ?? DaggerfallMagicDefense.None));

    internal WorldRpg.Kit.Controls.ActorControlRestrictions ControlsFor(long targetId)
    {
        WorldRpg.Kit.Controls.ActorControlRestrictions restrictions = default;
        bool preventsParalysis = MagicDefenseFor(targetId).PreventsParalysis;
        foreach (var effect in Active.Where(effect => checked((long)effect.Context.Target.Value) == targetId))
            if (!preventsParalysis || effect.Definition.Spell?.IsParalysis != true)
                restrictions = restrictions.Combine(effect.Definition.ControlRestrictions);
        return restrictions;
    }

    /// <summary>Reads current compiled effect meaning for one target without retaining an independent movement-effect cache.</summary>
    internal bool PreventsFallDamage(long targetId) => _effects.Values.Any(effect =>
        checked((long)effect.Lifecycle.Context.Target.Value) == targetId
        && effect.Definition.MovementProtection.PreventsFallDamage);

    internal bool GrantsLevitation(long targetId) => _effects.Values.Any(effect =>
        checked((long)effect.Lifecycle.Context.Target.Value) == targetId
        && effect.Definition.MovementProtection.GrantsLevitation);

    internal bool EnhancesClimbing(long targetId) => _effects.Values.Any(effect =>
        checked((long)effect.Lifecycle.Context.Target.Value) == targetId
        && effect.Definition.MovementProtection.EnhancesClimbing);

    /// <summary>
    /// Projects the active typed perception meanings for one target. The lifecycle owns the
    /// active-effect set, so callers never maintain a second concealment or comprehension cache.
    /// </summary>
    internal DaggerfallPerceptionEffectState PerceptionFor(long targetId)
    {
        if (targetId <= 0) throw new ArgumentOutOfRangeException(nameof(targetId));
        DaggerfallPerceptionEffectState perception = default;
        foreach (DaggerfallActiveEffect effect in Active)
        {
            if (checked((long)effect.Lifecycle.Context.Target.Value) != targetId) continue;
            perception = perception.Combine(effect.Definition.Perception);
        }
        return perception.Validate();
    }

    internal bool IsLikeKind(DaggerfallActiveEffect effect, DaggerfallEffectDefinition definition, long targetId, JsonElement incoming,
        long? casterId = null, ulong? itemId = null) =>
        checked((long)effect.Context.Target.Value) == targetId && effect.Definition.LikeKind == definition.LikeKind
        && (!definition.SourceScopedIncumbent || effect.Context.Caster?.Value == (ulong?)casterId && effect.Context.Item?.Value == itemId)
        && (definition.IncumbentSettingsMatch?.Invoke(effect.State, incoming) ?? true);

    /// <summary>Incoming like-kind effects settle their incumbent before a new effect's saving throw.</summary>
    internal bool TryAdmitIncumbent(DaggerfallEffectRequest request, out DaggerfallEffectAdmissionOutcome outcome,
        Func<JsonElement>? incomingState = null)
    {
        DaggerfallEffectDefinition definition = _catalog.Require(request.EffectKey);
        if ((definition.Stacking is DaggerfallEffectStacking.RefreshDuration or DaggerfallEffectStacking.Reject
                || definition.IncumbentSettingsMatch is not null)
            && Active.Any(effect => IsLikeKind(effect, definition, request.TargetId, request.State, request.CasterId, request.ItemId)))
        {
            outcome = Start(definition.RefreshState is not null && incomingState is not null
                ? request with { State = incomingState() } : request);
            return true;
        }
        outcome = default;
        return false;
    }

    internal DaggerfallEffectAdmissionOutcome Start(DaggerfallEffectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        DaggerfallEffectDefinition definition = _catalog.Require(request.EffectKey);
        ValidateLifetime(definition, request);
        ActiveEffectContext context = Context(request);
        if (_effects.ContainsKey(context.Instance))
            throw new ArgumentException($"Effect instance '{request.Instance}' is already active.", nameof(request));
        ActiveEffectLifecycle lifecycle = LifecycleFor(request.TargetId);
        DaggerfallActiveEffect[] likeKind = Active.Where(effect => IsLikeKind(effect, definition, request.TargetId, request.State, request.CasterId, request.ItemId)).ToArray();
        if (definition.Stacking == DaggerfallEffectStacking.Reject && likeKind.Length != 0)
        {
            Publish(DaggerfallEffectOutcomeKind.Rejected, request.Instance, definition.Key, request.TargetId);
            return DaggerfallEffectAdmissionOutcome.Rejected;
        }

        if ((definition.Stacking == DaggerfallEffectStacking.RefreshDuration || definition.IncumbentSettingsMatch is not null) && likeKind.Length != 0)
        {
            DaggerfallActiveEffect incumbent = likeKind.OrderBy(effect => effect.Lifecycle.Context.Instance.Value, StringComparer.Ordinal).First();
            if (incumbent.Definition.Key != definition.Key)
                throw new InvalidOperationException("Daggerfall refresh keeps one compiled effect definition; a different effect key must replace or stack.");
            // Only an explicitly compiled family may merge incoming state (such as a shield top-up).
            definition.RefreshState?.Invoke(incumbent, request.State);
            uint? refreshed = definition.ExtendIncumbentDuration
                ? incumbent.Lifecycle.RemainingRounds is uint prior && request.RemainingRounds is uint added ? checked(prior + added) : null
                : request.RemainingRounds;
            lifecycle.RefreshDuration(incumbent.Lifecycle.Context.Instance, refreshed);
            Publish(DaggerfallEffectOutcomeKind.Refreshed, incumbent.Lifecycle.Context.Instance.Value, definition.Key, request.TargetId);
            return DaggerfallEffectAdmissionOutcome.Refreshed;
        }

        ActiveEffectAdmissionKind admission = definition.Stacking == DaggerfallEffectStacking.Replace
            ? ActiveEffectAdmissionKind.Replace
            : ActiveEffectAdmissionKind.Apply;
        Admit(definition, context, request.Stacks, request.RemainingRounds, request.State, admission);
        DaggerfallEffectAdmissionOutcome outcome = admission == ActiveEffectAdmissionKind.Replace && likeKind.Length != 0
            ? DaggerfallEffectAdmissionOutcome.Replaced
            : DaggerfallEffectAdmissionOutcome.Started;
        // The donor applies a newly assigned effect once before the next minute tick. Restore does
        // not come through Start(), so it never repeats this work.
        bool noMatch = false;
        ActiveEffectLifecycleReceipt? initial = LifecycleFor(request.TargetId).AdvanceInitialMagicRound(context.Instance, state =>
        {
            if (_effects.TryGetValue(state.Context.Instance, out DaggerfallActiveEffect? effect))
            {
                ApplyRound(effect);
                noMatch = effect.NoMatchingCondition;
            }
        });
        if (initial is not null)
            foreach (ActiveEffectState removed in initial.Removed) _effects.Remove(removed.Context.Instance);
        if (noMatch) outcome = DaggerfallEffectAdmissionOutcome.NoMatch;
        Publish(noMatch ? DaggerfallEffectOutcomeKind.NoMatch : outcome == DaggerfallEffectAdmissionOutcome.Replaced
            ? DaggerfallEffectOutcomeKind.Replaced
            : DaggerfallEffectOutcomeKind.Started, context.Instance.Value, definition.Key, request.TargetId);
        if (initial is not null)
            foreach (ActiveEffectState removed in initial.Removed)
                Publish(DaggerfallEffectOutcomeKind.Expired, removed.Context.Instance.Value, definition.Key, request.TargetId);
        return outcome;
    }

    private static void ValidateLifetime(DaggerfallEffectDefinition definition, DaggerfallEffectRequest request)
    {
        if (definition.Spell?.UntilHealed == true
            && (request.RemainingRounds is not null || request.CasterId is not null || request.ItemId is not null))
            throw new ArgumentException("Permanent attribute damage must retain target-owned lifetime and historical cast origin in its state.");
    }

    internal void Restore(IEnumerable<DaggerfallActiveEffectSave> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        foreach (DaggerfallActiveEffectSave entry in saved.OrderBy(value => value.Instance, StringComparer.Ordinal))
        {
            DaggerfallEffectDefinition definition = _catalog.Require(entry.EffectKey);
            DaggerfallEffectRequest request = entry.ToRequest();
            ValidateLifetime(definition, request);
            ActiveEffectContext context = Context(request);
            if (_effects.ContainsKey(context.Instance))
                throw new ArgumentException($"Saved effect instance '{request.Instance}' appears more than once.", nameof(saved));
            Admit(definition, context, request.Stacks, request.RemainingRounds, request.State, ActiveEffectAdmissionKind.Apply, resumed: true);
        }
    }

    internal void AdvanceOrdinaryRound() => AdvanceRounds(1);

    internal uint AdvanceElapsedRounds(long elapsedRounds)
    {
        if (elapsedRounds <= 0) return 0;
        uint rounds = checked((uint)Math.Min(elapsedRounds, MaximumElapsedCatchupRounds));
        AdvanceRounds(rounds);
        return rounds;
    }

    internal bool Cancel(EffectInstanceId instance)
        => End(instance, DaggerfallEffectOutcomeKind.Cancelled);

    /// <summary>
    /// Ends an active effect because Daggerfall policy cured it.  The policy that selects a cure
    /// remains with the disease, poison, service, or quest caller; this lifecycle only guarantees
    /// that its contributions and scheduled magic-round work leave together.
    /// </summary>
    internal bool Cure(EffectInstanceId instance)
        => End(instance, DaggerfallEffectOutcomeKind.Cured);

    private bool End(EffectInstanceId instance, DaggerfallEffectOutcomeKind outcome)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!_effects.TryGetValue(instance, out DaggerfallActiveEffect? active)) return false;
        LifecycleFor(checked((long)active.Lifecycle.Context.Target.Value)).Cancel(instance);
        _effects.Remove(instance);
        Publish(outcome, instance.Value, active.Definition.Key,
            checked((long)active.Lifecycle.Context.Target.Value));
        return true;
    }

    internal int CancelSource(string source, ulong? itemId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        EffectInstanceId[] matches = Active
            .Where(effect => effect.Lifecycle.Context.Source.Key == source
                && (itemId is null || effect.Lifecycle.Context.Item?.Value == itemId))
            .Select(effect => effect.Lifecycle.Context.Instance)
            .ToArray();
        foreach (EffectInstanceId instance in matches) _ = Cancel(instance);
        return matches.Length;
    }

    /// <summary>Ends effects attached to an actor before its runtime entity can be retired.</summary>
    internal int CancelTarget(long targetId) => CancelMatching(effect =>
        checked((long)effect.Lifecycle.Context.Target.Value) == targetId);

    /// <summary>Ends effects that would retain a retired actor as target or caster.</summary>
    internal int CancelActorReferences(long actorId) => CancelMatching(effect =>
        checked((long)effect.Lifecycle.Context.Target.Value) == actorId
        || effect.Lifecycle.Context.Caster?.Value == checked((ulong)actorId));

    /// <summary>
    /// Detaches effects whose targets leave the live site while retaining their complete durable
    /// requests for that site's delta. Effects on another live target remain active even when an
    /// unloaded actor is their caster; their context keeps the caster identity without holding the
    /// retired actor wrapper.
    /// </summary>
    internal DaggerfallActiveEffectSave[] SuspendTargets(IEnumerable<long> targetIds)
    {
        ArgumentNullException.ThrowIfNull(targetIds);
        HashSet<long> targets = [.. targetIds];
        DaggerfallActiveEffect[] suspended = Active
            .Where(effect => targets.Contains(checked((long)effect.Lifecycle.Context.Target.Value)))
            .ToArray();
        DaggerfallActiveEffectSave[] saved = suspended.Select(Capture).ToArray();
        foreach (DaggerfallActiveEffect effect in suspended)
        {
            long targetId = checked((long)effect.Lifecycle.Context.Target.Value);
            LifecycleFor(targetId).Cancel(effect.Lifecycle.Context.Instance);
            _effects.Remove(effect.Lifecycle.Context.Instance);
        }
        foreach (long targetId in targets)
        {
            if (Active.Any(effect => checked((long)effect.Lifecycle.Context.Target.Value) == targetId)) continue;
            if (_lifecycles.Remove(targetId, out ActiveEffectLifecycle? lifecycle)) lifecycle.Dispose();
        }
        return saved;
    }

    /// <summary>Ends effects that would retain a unique item identity before that item is destroyed.</summary>
    internal int CancelItemReferences(ulong itemId) => CancelMatching(effect =>
        effect.Lifecycle.Context.Item?.Value == itemId);

    private int CancelMatching(Func<DaggerfallActiveEffect, bool> match)
    {
        EffectInstanceId[] matches = Active.Where(match)
            .Select(effect => effect.Lifecycle.Context.Instance)
            .ToArray();
        foreach (EffectInstanceId instance in matches) _ = Cancel(instance);
        return matches.Length;
    }

    public void Dispose()
    {
        List<Exception>? failures = null;
        foreach (EffectInstanceId instance in Active.Select(effect => effect.Lifecycle.Context.Instance).ToArray())
        {
            try { _ = Cancel(instance); }
            catch (Exception failure)
            {
                // Kit removed the Engine entry before reporting contribution cleanup failure.
                _effects.Remove(instance);
                (failures ??= []).Add(failure);
            }
        }
        foreach (ActiveEffectLifecycle lifecycle in _lifecycles.Values)
        {
            try { lifecycle.Dispose(); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
        _effects.Clear();
        _lifecycles.Clear();
        if (failures is { Count: 1 }) throw failures[0];
        if (failures is not null) throw new AggregateException(failures);
    }

    internal DaggerfallActiveEffectSave[] Capture() => Active.Select(Capture).ToArray();

    private static DaggerfallActiveEffectSave Capture(DaggerfallActiveEffect effect) => new(
        effect.Lifecycle.Context.Instance.Value,
        effect.Definition.Key,
        effect.Lifecycle.Context.Source.Key,
        effect.Lifecycle.Context.Caster?.Value is ulong caster ? checked((long)caster) : null,
        checked((long)effect.Lifecycle.Context.Target.Value),
        effect.Lifecycle.Context.Settings,
        effect.Lifecycle.Context.Element,
        effect.Lifecycle.Context.Item?.Value,
        effect.Lifecycle.RemainingRounds,
        effect.Lifecycle.Stacks,
        effect.State.Clone());

    private void AdvanceRounds(uint rounds)
    {
        // A catch-up can complete effects on several targets.  Actor identity, then each
        // lifecycle's own stable instance order, makes the externally observable completions
        // deterministic for save, cure, and presentation callers.
        foreach (ActiveEffectLifecycle lifecycle in _lifecycles.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray())
        {
            IReadOnlyList<ActiveEffectLifecycleReceipt> ended = lifecycle.AdvanceMagicRounds(rounds, state =>
            {
                if (_effects.TryGetValue(state.Context.Instance, out DaggerfallActiveEffect? effect)) ApplyRound(effect);
            });
            foreach (ActiveEffectLifecycleReceipt receipt in ended)
                foreach (ActiveEffectState removed in receipt.Removed)
                {
                    if (_effects.Remove(removed.Context.Instance, out DaggerfallActiveEffect? active))
                        Publish(DaggerfallEffectOutcomeKind.Expired, removed.Context.Instance.Value, active.Definition.Key,
                            checked((long)removed.Context.Target.Value));
                }
        }
    }

    private void ApplyRound(DaggerfallActiveEffect active)
    {
        active.Definition.MagicRound?.Invoke(active);
        if (active.ExpireAfterCurrentRound)
            LifecycleFor(checked((long)active.Lifecycle.Context.Target.Value)).ExpireAfterCurrentRound(active.Lifecycle.Context.Instance);
    }

    private void Admit(DaggerfallEffectDefinition definition, ActiveEffectContext context, ushort stacks, uint? remainingRounds,
        JsonElement state, ActiveEffectAdmissionKind admission, bool resumed = false)
    {
        ActiveEffectLifecycle lifecycle = LifecycleFor(checked((long)context.Target.Value));
        DaggerfallActiveEffect? active = null;
        List<IActiveEffectContribution> contributions = [];
        try
        {
            Actor target = ActorFor(checked((long)context.Target.Value));
            active = new DaggerfallActiveEffect(definition, context, stacks, state,
                () => SourceFor(context, target), target);
            contributions.AddRange(Apply(active, resumed));
            ActiveEffectLifecycleReceipt receipt = lifecycle.Admit(definition.ToEngineDefinition(context.Source.Key), admission, context,
                Provenance(checked((long)context.Target.Value), context), stacks, remainingRounds, contributions);
            active.Attach(receipt.Current!);
            _effects.Add(context.Instance, active);
            foreach (ActiveEffectState removed in receipt.Removed) _effects.Remove(removed.Context.Instance);
        }
        catch
        {
            foreach (IActiveEffectContribution contribution in contributions.AsEnumerable().Reverse()) contribution.Remove();
            throw;
        }
    }

    private ActiveEffectLifecycle LifecycleFor(long targetId)
    {
        if (_lifecycles.TryGetValue(targetId, out ActiveEffectLifecycle? lifecycle)) return lifecycle;
        EffectsComponent component = targetId == _actors.Player.DurableId
            ? _actors.Player.Effects
            : _actors.Get(targetId).Effects;
        lifecycle = new ActiveEffectLifecycle(component);
        _lifecycles.Add(targetId, lifecycle);
        return lifecycle;
    }

    private Actor ActorFor(long targetId) => targetId == _actors.Player.DurableId
        ? _actors.Player.Actor
        : _actors.Get(targetId).Actor;

    private Actor SourceFor(ActiveEffectContext context, Actor target)
    {
        if (context.Caster is not { } caster) return target;
        long casterId = checked((long)caster.Value);
        if (casterId == _actors.Player.DurableId) return _actors.Player.Actor;
        return _actors.TryGet(casterId, out ActorState actor) ? actor.Actor : target;
    }

    private static List<IActiveEffectContribution> Apply(DaggerfallActiveEffect effect, bool resumed = false)
    {
        Func<DaggerfallActiveEffect, IEnumerable<IActiveEffectContribution>>? factory = resumed
            ? effect.Definition.Resume
            : effect.Definition.Apply;
        if (resumed && effect.Definition.Apply is not null && factory is null)
            throw new InvalidOperationException($"Daggerfall effect '{effect.Definition.Key}' applies contributions but does not define resume cleanup.");
        return factory?.Invoke(effect)
            .Select(value => value ?? throw new ArgumentException("An effect contribution cannot be null."))
            .ToList() ?? [];
    }

    private static void Cleanup(IEnumerable<IActiveEffectContribution> contributions)
    {
        foreach (IActiveEffectContribution contribution in contributions.Reverse()) contribution.Remove();
    }

    private static ActiveEffectContext Context(DaggerfallEffectRequest request)
    {
        if (request.TargetId <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        if (request.CasterId is <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        if (request.ItemId == 0) throw new ArgumentOutOfRangeException(nameof(request));
        return new(
            EffectInstanceId.Parse(request.Instance),
            new ActiveEffectSource(request.Source),
            request.CasterId is long caster ? ActorsState.Identity(caster) : null,
            ActorsState.Identity(request.TargetId),
            request.Settings,
            request.Element,
            request.ItemId is ulong item ? new DurableIdentityReference(DurableIdentityKind.Item, item) : null);
    }

    private static MechanicsSourceIdentity Provenance(long targetId, ActiveEffectContext context) => new EffectSourceIdentity(
        null,
        context.Instance,
        1,
        SourceDefinitionId.Parse($"daggerfall.{context.Source.Key}"));

    private void Publish(DaggerfallEffectOutcomeKind kind, string instance, string effectKey, long targetId) =>
        Completed?.Invoke(new DaggerfallEffectOutcome(kind, instance, effectKey, targetId) { Feedback = _catalog.Require(effectKey).Feedback });


}
