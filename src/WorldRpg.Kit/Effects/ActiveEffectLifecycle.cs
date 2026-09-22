using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.World;

namespace WorldRpg.Kit.Effects;

/// <summary>How a ruleset has decided to admit one effect after applying its own like-kind policy.</summary>
public enum ActiveEffectAdmissionKind
{
    Apply,
    Replace,
}

/// <summary>Why a live effect left its target.</summary>
public enum ActiveEffectEndReason
{
    Cancelled,
    Expired,
    Replaced,
}

/// <summary>One stable source of an active effect, independent of the target's runtime entity.</summary>
public sealed record ActiveEffectSource
{
    public ActiveEffectSource(string key, DurableIdentityReference? owner = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Owner = owner;
    }

    public string Key { get; }
    public DurableIdentityReference? Owner { get; }
}

/// <summary>
/// Product identities and authored settings carried by a live effect. The Engine component owns
/// stacking and modifier-source provenance; this value is what save reconstruction can name again.
/// </summary>
public sealed record ActiveEffectContext
{
    public ActiveEffectContext(
        EffectInstanceId instance,
        ActiveEffectSource source,
        DurableIdentityReference? caster,
        DurableIdentityReference target,
        string settings,
        string? element,
        DurableIdentityReference? item)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings);
        Instance = instance;
        Source = source;
        Caster = caster;
        Target = target;
        Settings = settings;
        Element = element;
        Item = item;
    }

    public EffectInstanceId Instance { get; }
    public ActiveEffectSource Source { get; }
    public DurableIdentityReference? Caster { get; }
    public DurableIdentityReference Target { get; }
    public string Settings { get; }
    public string? Element { get; }
    public DurableIdentityReference? Item { get; }
}

/// <summary>One reversible contribution made by an active effect.</summary>
public interface IActiveEffectContribution
{
    void Remove();
}

/// <summary>A small contribution adapter for direct product-owned cleanup actions.</summary>
public sealed class DelegateActiveEffectContribution(Action remove) : IActiveEffectContribution
{
    private Action? _remove = remove ?? throw new ArgumentNullException(nameof(remove));

    public void Remove()
    {
        Action? remove = Interlocked.Exchange(ref _remove, null);
        remove?.Invoke();
    }
}

/// <summary>One active effect's stable context and common lifetime counter.</summary>
public sealed class ActiveEffectState
{
    internal ActiveEffectState(ActiveEffectContext context, ushort stacks, uint? remainingRounds, IReadOnlyList<IActiveEffectContribution> contributions)
    {
        Context = context;
        Stacks = stacks;
        RemainingRounds = remainingRounds;
        Contributions = contributions;
    }

    public ActiveEffectContext Context { get; }
    public ushort Stacks { get; }
    public uint? RemainingRounds { get; internal set; }
    internal IReadOnlyList<IActiveEffectContribution> Contributions { get; }
}

/// <summary>One completed lifecycle mutation, including all entries that were cleaned up.</summary>
public sealed record ActiveEffectLifecycleReceipt(
    ActiveEffectAdmissionKind? Admission,
    ActiveEffectEndReason? EndReason,
    ActiveEffectState? Current,
    IReadOnlyList<ActiveEffectState> Removed,
    EffectMutationReceipt EngineReceipt);

/// <summary>
/// Coordinates product-stable active-effect state with the attached Engine <see cref="EffectsComponent"/>.
/// Rulesets choose definitions, like-kind behavior, round payloads, and effect-specific save state;
/// this owner guarantees one cleanup path for every Engine removal.
/// </summary>
public sealed class ActiveEffectLifecycle : IDisposable
{
    private readonly EffectsComponent _effects;
    private readonly Dictionary<EffectInstanceId, ActiveEffectState> _states = [];
    private bool _disposed;

    public ActiveEffectLifecycle(EffectsComponent effects)
    {
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
    }

    public IReadOnlyList<ActiveEffectState> Active => _states.Values
        .OrderBy(value => value.Context.Instance.Value, StringComparer.Ordinal)
        .ToArray();

    public bool TryGet(EffectInstanceId instance, out ActiveEffectState? state) => _states.TryGetValue(instance, out state);

    public ActiveEffectLifecycleReceipt Admit(
        EffectDefinition definition,
        ActiveEffectAdmissionKind admission,
        ActiveEffectContext context,
        MechanicsSourceIdentity provenance,
        ushort stacks,
        uint? remainingRounds,
        IEnumerable<IActiveEffectContribution>? contributions = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(provenance);
        IReadOnlyList<IActiveEffectContribution> cleanup = CopyContributions(contributions);
        EffectMutationReceipt engineReceipt = admission switch
        {
            ActiveEffectAdmissionKind.Apply => _effects.Apply(definition, context.Instance, provenance, stacks),
            ActiveEffectAdmissionKind.Replace => _effects.Replace(definition, context.Instance, provenance, stacks),
            _ => throw new ArgumentOutOfRangeException(nameof(admission)),
        };
        List<ActiveEffectState> removed = RemoveTracked(engineReceipt.Removed, ActiveEffectEndReason.Replaced);

        if (_states.ContainsKey(context.Instance))
        {
            // An Engine entry with this key cannot be valid at this point, but refusing a split
            // state is safer than leaving a contribution that cannot be removed.
            throw new InvalidOperationException($"Effect '{context.Instance.Value}' already has lifecycle state.");
        }

        ActiveEffectState current = new(context, stacks, remainingRounds, cleanup);
        _states.Add(context.Instance, current);
        return new(admission, null, current, removed, engineReceipt);
    }

    /// <summary>Refreshes only the ruleset-owned duration while retaining one admitted Engine entry and its contributions.</summary>
    public ActiveEffectState RefreshDuration(EffectInstanceId instance, uint? remainingRounds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(instance);
        if (!_states.TryGetValue(instance, out ActiveEffectState? state))
            throw new InvalidOperationException($"Effect '{instance.Value}' is not active.");
        state.RemainingRounds = remainingRounds;
        return state;
    }

    /// <summary>Lets compiled effect policy finish its current magic-round payload through normal Engine expiry.</summary>
    public void ExpireAfterCurrentRound(EffectInstanceId instance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(instance);
        if (!_states.TryGetValue(instance, out ActiveEffectState? state))
            throw new InvalidOperationException($"Effect '{instance.Value}' is not active.");
        state.RemainingRounds = 1;
    }

    /// <summary>Runs one ordinary magic round and expires finite effects after their payload.</summary>
    public IReadOnlyList<ActiveEffectLifecycleReceipt> AdvanceMagicRound(Action<ActiveEffectState> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        List<ActiveEffectLifecycleReceipt> results = [];
        foreach (ActiveEffectState state in Active)
        {
            if (!_states.ContainsKey(state.Context.Instance)) continue;
            apply(state);
            if (!_states.TryGetValue(state.Context.Instance, out ActiveEffectState? current)
                || current.RemainingRounds is not uint remaining)
            {
                continue;
            }

            if (remaining > 1)
            {
                current.RemainingRounds = remaining - 1;
                continue;
            }

            results.Add(End(current.Context.Instance, ActiveEffectEndReason.Expired));
        }

        return results;
    }

    /// <summary>Applies the donor-style initial round to one newly admitted effect only.</summary>
    public ActiveEffectLifecycleReceipt? AdvanceInitialMagicRound(EffectInstanceId instance, Action<ActiveEffectState> apply)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(apply);
        if (!_states.TryGetValue(instance, out ActiveEffectState? state)) return null;
        apply(state);
        if (!_states.TryGetValue(instance, out ActiveEffectState? current)
            || current.RemainingRounds is not uint remaining)
        {
            return null;
        }

        if (remaining > 1)
        {
            current.RemainingRounds = remaining - 1;
            return null;
        }

        return End(instance, ActiveEffectEndReason.Expired);
    }

    /// <summary>Runs a caller-chosen number of elapsed rounds without creating another clock.</summary>
    public IReadOnlyList<ActiveEffectLifecycleReceipt> AdvanceMagicRounds(uint rounds, Action<ActiveEffectState> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        List<ActiveEffectLifecycleReceipt> results = [];
        for (uint round = 0; round < rounds; round++)
            results.AddRange(AdvanceMagicRound(apply));
        return results;
    }

    public ActiveEffectLifecycleReceipt Cancel(EffectInstanceId instance) => End(instance, ActiveEffectEndReason.Cancelled);

    /// <summary>Cancels every active entry so an owning actor or session can release contributions first.</summary>
    public IReadOnlyList<ActiveEffectLifecycleReceipt> CancelAll() => Active
        .Select(state => Cancel(state.Context.Instance))
        .ToArray();

    public IReadOnlyList<ActiveEffectLifecycleReceipt> CancelSource(ActiveEffectSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Active.Where(state => state.Context.Source == source)
            .Select(state => Cancel(state.Context.Instance))
            .ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // End bypasses admission only; it remains valid while this owner is disposing.  Every
        // entry still gets its cleanup attempt when one contribution reports a failure.
        List<Exception>? failures = null;
        foreach (ActiveEffectState state in Active)
        {
            try { _ = End(state.Context.Instance, ActiveEffectEndReason.Cancelled); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }

        ThrowFailures(failures);
    }

    private ActiveEffectLifecycleReceipt End(EffectInstanceId instance, ActiveEffectEndReason reason)
    {
        ArgumentNullException.ThrowIfNull(instance);
        EffectMutationReceipt engineReceipt = reason == ActiveEffectEndReason.Expired
            ? _effects.Expire(instance)
            : _effects.Remove(instance);
        List<ActiveEffectState> removed = RemoveTracked(engineReceipt.Removed, reason);
        return new(null, reason, null, removed, engineReceipt);
    }

    private List<ActiveEffectState> RemoveTracked(IEnumerable<ActiveEffect> effects, ActiveEffectEndReason reason)
    {
        List<ActiveEffectState> removed = [];
        List<Exception>? failures = null;
        foreach (ActiveEffect effect in effects)
        {
            if (!_states.Remove(effect.Instance, out ActiveEffectState? state))
            {
                (failures ??= []).Add(new InvalidOperationException($"Effect '{effect.Instance.Value}' was removed without lifecycle state."));
                continue;
            }

            try { Cleanup(state.Contributions); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
            removed.Add(state);
        }

        ThrowFailures(failures);
        return removed;
    }

    private static IReadOnlyList<IActiveEffectContribution> CopyContributions(IEnumerable<IActiveEffectContribution>? contributions)
    {
        if (contributions is null) return [];
        return contributions.Select(value => value ?? throw new ArgumentException("An effect contribution cannot be null.")).ToArray();
    }

    private static void Cleanup(IEnumerable<IActiveEffectContribution> contributions)
    {
        List<Exception>? failures = null;
        foreach (IActiveEffectContribution contribution in contributions.Reverse())
        {
            try { contribution.Remove(); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }

        ThrowFailures(failures);
    }

    private static void ThrowFailures(List<Exception>? failures)
    {
        if (failures is { Count: 1 }) throw failures[0];
        if (failures is not null) throw new AggregateException(failures);
    }
}
