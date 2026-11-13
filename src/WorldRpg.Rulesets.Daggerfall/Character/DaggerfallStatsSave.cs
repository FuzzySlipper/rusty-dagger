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
