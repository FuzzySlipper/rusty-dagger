using System.Text.Json;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Effects;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The ruleset-owned outcome of applying its like-kind policy.</summary>
internal enum DaggerfallEffectAdmissionOutcome
{
    Started,
    Refreshed,
    Replaced,
    Rejected,
}

/// <summary>The narrow completed-change signal cast, item, time, combat, and presentation owners consume.</summary>
internal enum DaggerfallEffectOutcomeKind
{
    Started,
    Refreshed,
    Replaced,
    Rejected,
    Cancelled,
    Cured,
    Expired,
}

internal sealed record DaggerfallEffectOutcome(
    DaggerfallEffectOutcomeKind Kind,
    string Instance,
    string EffectKey,
    long TargetId);

/// <summary>How Daggerfall treats another active effect of the same compiled kind on one target.</summary>
internal enum DaggerfallEffectStacking
{
    Stack,
    /// <summary>Extends only the incumbent duration; the original payload and contribution stay in force.</summary>
    RefreshDuration,
    Replace,
    Reject,
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
    Func<DaggerfallActiveEffect, IEnumerable<IActiveEffectContribution>>? Resume = null)
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

    internal static DaggerfallEffectCatalog Empty { get; } = new([]);

    internal DaggerfallEffectCatalog(IEnumerable<DaggerfallEffectDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = definitions.ToDictionary(
            definition => definition.Key,
            StringComparer.Ordinal);
    }

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
    internal DaggerfallActiveEffect(DaggerfallEffectDefinition definition, ActiveEffectContext context, ushort stacks, JsonElement state, Actor target)
    {
        Definition = definition;
        Context = context;
        Stacks = stacks;
        State = state.Clone();
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    internal DaggerfallEffectDefinition Definition { get; private set; }
    internal ActiveEffectContext Context { get; private set; }
    internal ActiveEffectState Lifecycle { get; private set; } = null!;
    /// <summary>Ruleset-owned durable payload; MagicRound and compiled policy may update it directly.</summary>
    internal JsonElement State { get; set; }
    /// <summary>Requested stack count available to compiled policy before the Engine state is attached.</summary>
    internal ushort Stacks { get; }
    internal Actor Target { get; }
    /// <summary>Compiled policy requests ordinary Engine expiry after the current magic-round payload.</summary>
    internal bool ExpireAfterCurrentRound { get; set; }

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

    internal DaggerfallEffectAdmissionOutcome Start(DaggerfallEffectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        DaggerfallEffectDefinition definition = _catalog.Require(request.EffectKey);
        ActiveEffectContext context = Context(request);
        if (_effects.ContainsKey(context.Instance))
            throw new ArgumentException($"Effect instance '{request.Instance}' is already active.", nameof(request));
        ActiveEffectLifecycle lifecycle = LifecycleFor(request.TargetId);
        DaggerfallActiveEffect[] likeKind = Active.Where(effect => effect.Lifecycle.Context.Target == context.Target
            && effect.Definition.LikeKind == definition.LikeKind).ToArray();
        if (definition.Stacking == DaggerfallEffectStacking.Reject && likeKind.Length != 0)
        {
            Publish(DaggerfallEffectOutcomeKind.Rejected, request.Instance, definition.Key, request.TargetId);
            return DaggerfallEffectAdmissionOutcome.Rejected;
        }

        if (definition.Stacking == DaggerfallEffectStacking.RefreshDuration && likeKind.Length != 0)
        {
            DaggerfallActiveEffect incumbent = likeKind.OrderBy(effect => effect.Lifecycle.Context.Instance.Value, StringComparer.Ordinal).First();
            if (incumbent.Definition.Key != definition.Key)
                throw new InvalidOperationException("Daggerfall refresh keeps one compiled effect definition; a different effect key must replace or stack.");
            // Daggerfall refresh extends the incumbent duration only.  Its original source, caster,
            // settings, element, item, stacks, state, Engine provenance, and reversible contribution
            // remain authoritative; accepting a new payload would require definition-specific atomic
            // migration rather than a generic lifecycle guess.
            lifecycle.RefreshDuration(incumbent.Lifecycle.Context.Instance, request.RemainingRounds);
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
        ActiveEffectLifecycleReceipt? initial = LifecycleFor(request.TargetId).AdvanceInitialMagicRound(context.Instance, state =>
        {
            if (_effects.TryGetValue(state.Context.Instance, out DaggerfallActiveEffect? effect)) ApplyRound(effect);
        });
        if (initial is not null)
            foreach (ActiveEffectState removed in initial.Removed) _effects.Remove(removed.Context.Instance);
        Publish(outcome == DaggerfallEffectAdmissionOutcome.Replaced
            ? DaggerfallEffectOutcomeKind.Replaced
            : DaggerfallEffectOutcomeKind.Started, context.Instance.Value, definition.Key, request.TargetId);
        if (initial is not null)
            foreach (ActiveEffectState removed in initial.Removed)
                Publish(DaggerfallEffectOutcomeKind.Expired, removed.Context.Instance.Value, definition.Key, request.TargetId);
        return outcome;
    }

    internal void Restore(IEnumerable<DaggerfallActiveEffectSave> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        foreach (DaggerfallActiveEffectSave entry in saved.OrderBy(value => value.Instance, StringComparer.Ordinal))
        {
            DaggerfallEffectDefinition definition = _catalog.Require(entry.EffectKey);
            DaggerfallEffectRequest request = entry.ToRequest();
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

    internal DaggerfallActiveEffectSave[] Capture() => Active.Select(effect => new DaggerfallActiveEffectSave(
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
        effect.State.Clone())).ToArray();

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
            active = new DaggerfallActiveEffect(definition, context, stacks, state, ActorFor(checked((long)context.Target.Value)));
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
        Completed?.Invoke(new DaggerfallEffectOutcome(kind, instance, effectKey, targetId));


}
