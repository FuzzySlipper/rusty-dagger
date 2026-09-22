using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One actor's mechanics state, including Daggerfall-authored sources that Engine capture leaves to the product.</summary>
internal sealed record DaggerfallStatsSave(
    StatsComponentSnapshot Snapshot,
    DaggerfallStatSourceSave[] Sources);

/// <summary>The two Daggerfall-owned source identities that can be rebound to a freshly restored actor.</summary>
internal enum DaggerfallStatSourceIdentityKind
{
    Intrinsic,
    Effect,
}

/// <summary>Durable provenance for an authored stat source; the runtime entity is supplied at restore.</summary>
internal sealed record DaggerfallStatSourceIdentitySave(
    DaggerfallStatSourceIdentityKind Kind,
    string InstanceId,
    string? SourceId = null,
    ushort? Stack = null);

/// <summary>A source is rebound to the freshly restored actor rather than retaining an old runtime entity.</summary>
internal sealed record DaggerfallStatSourceSave(
    string StatId,
    string SourceStatId,
    DaggerfallStatSourceIdentitySave Identity,
    string DefinitionId,
    short Priority,
    DaggerfallStatContributionSave[] Contributions);

internal sealed record DaggerfallStatContributionSave(
    string StatId,
    string StackingGroupId,
    MechanicsStackingPolicy Stacking,
    StatModifierKind Kind,
    double Value);

/// <summary>Restored mechanics plus the newly issued removal handles for captured ordinary modifiers.</summary>
internal sealed class DaggerfallRestoredStats(
    StatsComponent component,
    IReadOnlyDictionary<string, IReadOnlyList<StatModifierHandle>> modifierHandles)
{
    internal StatsComponent Component { get; } = component;
    internal IReadOnlyDictionary<string, IReadOnlyList<StatModifierHandle>> ModifierHandles { get; } = modifierHandles;
}

/// <summary>Current-state save boundary for Daggerfall mechanics.</summary>
internal static class DaggerfallStatsSaveBoundary
{
    internal static DaggerfallStatsSave Capture(StatsComponent component, EntityId actor)
    {
        ArgumentNullException.ThrowIfNull(component);
        StatsComponentSnapshot snapshot = StatsComponentCapture.Capture(component);
        List<DaggerfallStatSourceSave> sources = [];
        foreach (StatCapture captured in snapshot.Stats)
        {
            Stat stat = component.GetStat(StatId.Parse(captured.Id));
            foreach (StatSource source in stat.Sources)
            {
                sources.Add(new DaggerfallStatSourceSave(
                    captured.Id,
                    source.Contributions.FirstOrDefault()?.Stat.Value
                        ?? throw new InvalidOperationException($"Daggerfall stat '{captured.Id}' has a source without contributions."),
                    CaptureIdentity(source.Identity, actor, captured.Id),
                    source.Definition.Value,
                    source.Priority,
                    [.. source.Contributions.Select(CaptureContribution)]));
            }
        }

        return new(snapshot, [.. sources]);
    }

    internal static DaggerfallRestoredStats Restore(DaggerfallStatsSave saved, EntityId actor)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(saved.Snapshot);
        ArgumentNullException.ThrowIfNull(saved.Sources);
        Dictionary<string, DaggerfallStatSourceSave[]> sources = saved.Sources
            .GroupBy(source => source.StatId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<StatModifierHandle>> handles = new(StringComparer.Ordinal);
        StatsComponent rebuilt = StatsComponentCapture.Rebuild(saved.Snapshot, (captured, stat, restoredHandles) =>
        {
            handles.Add(captured.Id, restoredHandles);
            if (!sources.TryGetValue(captured.Id, out DaggerfallStatSourceSave[]? statSources)) return;
            string sourceStatId = statSources[0].SourceStatId;
            if (statSources.Any(source => !string.Equals(source.SourceStatId, sourceStatId, StringComparison.Ordinal)))
                throw new ArgumentException($"Daggerfall save carries incompatible source targets for stat '{captured.Id}'.", nameof(saved));
            stat.SetSources(
                StatId.Parse(sourceStatId),
                statSources.Select(source => RestoreSource(source, actor)));
        });

        foreach (string statId in sources.Keys)
        {
            if (!handles.ContainsKey(statId))
                throw new ArgumentException($"Daggerfall save carries an authored source for unknown stat '{statId}'.", nameof(saved));
        }
        return new DaggerfallRestoredStats(rebuilt, handles);
    }

    private static DaggerfallStatSourceIdentitySave CaptureIdentity(MechanicsSourceIdentity identity, EntityId actor, string statId) => identity switch
    {
        IntrinsicSourceIdentity intrinsic when intrinsic.Entity == actor => new(DaggerfallStatSourceIdentityKind.Intrinsic, intrinsic.Instance.Value),
        EffectSourceIdentity effect when effect.Entity == actor => new(DaggerfallStatSourceIdentityKind.Effect, effect.Effect.Value, effect.Source.Value, effect.Stack),
        _ => throw new InvalidOperationException($"Daggerfall stat '{statId}' has a source that is not owned by this actor and cannot be rebound safely."),
    };

    private static DaggerfallStatContributionSave CaptureContribution(StatContributionDefinition value) =>
        value.Contribution switch
        {
            StatContribution.Add add => new(value.Stat.Value, value.Group.Value, value.Stacking, add.Kind, add.Amount),
            StatContribution.Multiply multiply => new(value.Stat.Value, value.Group.Value, value.Stacking, multiply.Kind, multiply.Factor),
            StatContribution.Minimum minimum => new(value.Stat.Value, value.Group.Value, value.Stacking, minimum.Kind, minimum.Value),
            StatContribution.Maximum maximum => new(value.Stat.Value, value.Group.Value, value.Stacking, maximum.Kind, maximum.Value),
            _ => throw new InvalidOperationException($"Daggerfall cannot save unknown stat contribution '{value.Contribution.GetType().Name}'."),
        };

    private static StatSource RestoreSource(DaggerfallStatSourceSave saved, EntityId actor)
    {
        if (string.IsNullOrWhiteSpace(saved.StatId)
            || string.IsNullOrWhiteSpace(saved.SourceStatId)
            || saved.Identity is null
            || string.IsNullOrWhiteSpace(saved.DefinitionId)
            || saved.Contributions is null
            || saved.Contributions.Length == 0)
        {
            throw new ArgumentException("Daggerfall authored stat sources must be complete.", nameof(saved));
        }

        return new StatSource(
            RestoreIdentity(saved.Identity, actor),
            SourceDefinitionId.Parse(saved.DefinitionId),
            saved.Priority,
            saved.Contributions.Select(RestoreContribution));
    }

    private static MechanicsSourceIdentity RestoreIdentity(DaggerfallStatSourceIdentitySave saved, EntityId actor)
    {
        if (string.IsNullOrWhiteSpace(saved.InstanceId))
            throw new ArgumentException("Daggerfall authored stat source identity must include an instance.", nameof(saved));
        return saved.Kind switch
        {
            DaggerfallStatSourceIdentityKind.Intrinsic when saved.SourceId is null && saved.Stack is null =>
                new IntrinsicSourceIdentity(actor, SourceInstanceId.Parse(saved.InstanceId)),
            DaggerfallStatSourceIdentityKind.Effect when !string.IsNullOrWhiteSpace(saved.SourceId) && saved.Stack is ushort stack =>
                new EffectSourceIdentity(actor, EffectInstanceId.Parse(saved.InstanceId), stack, SourceDefinitionId.Parse(saved.SourceId)),
            _ => throw new ArgumentException("Daggerfall authored stat source identity is invalid.", nameof(saved)),
        };
    }

    private static StatContributionDefinition RestoreContribution(DaggerfallStatContributionSave saved)
    {
        if (string.IsNullOrWhiteSpace(saved.StatId) || string.IsNullOrWhiteSpace(saved.StackingGroupId) || !double.IsFinite(saved.Value))
            throw new ArgumentException("Daggerfall authored stat contributions must be complete and finite.", nameof(saved));
        StatContribution contribution = saved.Kind switch
        {
            StatModifierKind.Add => new StatContribution.Add(saved.Value),
            StatModifierKind.Multiply => new StatContribution.Multiply(saved.Value),
            StatModifierKind.Minimum => new StatContribution.Minimum(saved.Value),
            StatModifierKind.Maximum => new StatContribution.Maximum(saved.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(saved), saved.Kind, "Daggerfall authored stat contribution kind is invalid."),
        };
        return new StatContributionDefinition(
            StatId.Parse(saved.StatId),
            StackingGroupId.Parse(saved.StackingGroupId),
            saved.Stacking,
            contribution);
    }
}
