using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using EquipmentRead = WorldRpg.Kit.Inventory.EquipmentRead;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The session's live reads every seam owner may be built over.</summary>
/// <param name="Definitions">The session's admitted definitions.</param>
/// <param name="Character">The player's live character, when one exists.</param>
/// <param name="Random">The session's Engine random service, for keyed draws.</param>
/// <param name="EquipmentOf">An actor's current equipment.</param>
/// <param name="StatsOf">An actor's live stats, or null for one that is gone.</param>
internal sealed record DaggerfallSeamInputs(DaggerfallDefinitions Definitions, Func<DaggerfallCharacterState?> Character,
    IRandomService Random, Func<long, EquipmentRead> EquipmentOf, Func<long, StatsComponent?> StatsOf);

/// <summary>What a variant's seam owners are built from when its session is composed: the session's reads and the variant's own tuning.</summary>
internal sealed record DaggerfallSeamContext(DaggerfallSeamInputs Session, TuningProfile Tuning)
{
    internal DaggerfallDefinitions Definitions => Session.Definitions;
}

/// <summary>
/// One compiled Daggerfall rules variant: its manifest and an explicit owner factory for each seam it
/// claims. The manifest's seams and the supplied owners must agree, so a variant cannot claim a seam it
/// does not replace, or replace one it does not declare.
/// </summary>
public sealed class DaggerfallRulesVariant
{
    internal DaggerfallRulesVariant(RulesVariantManifest manifest,
        Func<DaggerfallSeamContext, IDaggerfallPhysicalMitigation>? physicalMitigation = null,
        Func<DaggerfallSeamContext, IDaggerfallPhysicalHarmOperations, IDaggerfallPhysicalDelivery>? physicalDelivery = null)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        if (manifest.Ruleset != DaggerfallRuleset.Identity)
            throw new ArgumentException($"Rules variant '{manifest.Id.Value}' applies to ruleset '{manifest.Ruleset.Value}', not Daggerfall.", nameof(manifest));
        // No variant save section is composed yet: a variant that owns state would lose it on every save.
        if (manifest.OwnsSaveState)
            throw new NotSupportedException($"Rules variant '{manifest.Id.Value}' owns save state, which Daggerfall does not yet compose for variants.");
        PhysicalMitigation = physicalMitigation;
        PhysicalDelivery = physicalDelivery;
        HashSet<RulesSeamId> supplied = [];
        if (physicalMitigation is not null) supplied.Add(DaggerfallRulesSeams.PhysicalMitigation);
        if (physicalDelivery is not null) supplied.Add(DaggerfallRulesSeams.PhysicalDelivery);
        if (!supplied.SetEquals(manifest.Seams))
            throw new ArgumentException($"Rules variant '{manifest.Id.Value}' declares seams [{string.Join(", ", manifest.Seams.Select(seam => seam.Value))}] but supplies owners for [{string.Join(", ", supplied.Select(seam => seam.Value))}].", nameof(manifest));
    }

    public RulesVariantManifest Manifest { get; }
    internal Func<DaggerfallSeamContext, IDaggerfallPhysicalMitigation>? PhysicalMitigation { get; }
    internal Func<DaggerfallSeamContext, IDaggerfallPhysicalHarmOperations, IDaggerfallPhysicalDelivery>? PhysicalDelivery { get; }
}

/// <summary>
/// The owner selected for each Daggerfall seam: a selected variant's when it claims the seam, otherwise
/// classic's. Session composition builds each owner through this one selection.
/// </summary>
internal sealed class DaggerfallSeamSelection
{
    private readonly (DaggerfallRulesVariant Variant, ResolvedRulesVariant Resolved)? _physicalMitigation;
    private readonly (DaggerfallRulesVariant Variant, ResolvedRulesVariant Resolved)? _physicalDelivery;

    private DaggerfallSeamSelection((DaggerfallRulesVariant, ResolvedRulesVariant)? physicalMitigation,
        (DaggerfallRulesVariant, ResolvedRulesVariant)? physicalDelivery)
    {
        _physicalMitigation = physicalMitigation;
        _physicalDelivery = physicalDelivery;
    }

    internal static DaggerfallSeamSelection Classic { get; } = new(null, null);

    /// <summary>The variant that owns physical mitigation, or none for classic.</summary>
    internal RulesVariantId? PhysicalMitigationVariant => _physicalMitigation?.Resolved.Id;

    /// <summary>The variant that owns physical delivery, or none for classic.</summary>
    internal RulesVariantId? PhysicalDeliveryVariant => _physicalDelivery?.Resolved.Id;

    /// <summary>Selects owners for a resolved composition's variants from the variants this ruleset compiles.</summary>
    internal static DaggerfallSeamSelection From(IReadOnlyList<ResolvedRulesVariant> selected, IReadOnlyList<DaggerfallRulesVariant> compiled)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(compiled);
        (DaggerfallRulesVariant, ResolvedRulesVariant)? physicalMitigation = null, physicalDelivery = null;
        foreach (ResolvedRulesVariant resolved in selected)
        {
            DaggerfallRulesVariant variant = compiled.SingleOrDefault(candidate => candidate.Manifest.Id == resolved.Id)
                ?? throw new InvalidOperationException($"The composition selects rules variant '{resolved.Id.Value}', which this Daggerfall ruleset does not compile.");
            if (variant.PhysicalMitigation is not null)
            {
                if (physicalMitigation is not null)
                    throw new InvalidOperationException($"Rules variants '{physicalMitigation.Value.Item2.Id.Value}' and '{resolved.Id.Value}' both claim seam '{DaggerfallRulesSeams.PhysicalMitigation.Value}'.");
                physicalMitigation = (variant, resolved);
            }
            if (variant.PhysicalDelivery is not null)
            {
                if (physicalDelivery is not null)
                    throw new InvalidOperationException($"Rules variants '{physicalDelivery.Value.Item2.Id.Value}' and '{resolved.Id.Value}' both claim seam '{DaggerfallRulesSeams.PhysicalDelivery.Value}'.");
                physicalDelivery = (variant, resolved);
            }
        }
        return new(physicalMitigation, physicalDelivery);
    }

    internal IDaggerfallPhysicalMitigation BuildPhysicalMitigation(DaggerfallSeamInputs session) =>
        _physicalMitigation is { } owner
            ? owner.Variant.PhysicalMitigation!(new DaggerfallSeamContext(session, owner.Resolved.Tuning))
            : new DaggerfallClassicPhysicalMitigation(session.Character);

    /// <summary>The selected physical delivery owner, built over the shared harm operations it delivers through.</summary>
    internal Func<IDaggerfallPhysicalHarmOperations, IDaggerfallPhysicalDelivery> PhysicalDelivery(DaggerfallSeamInputs session) =>
        _physicalDelivery is { } owner
            ? ops => owner.Variant.PhysicalDelivery!(new DaggerfallSeamContext(session, owner.Resolved.Tuning), ops)
            : ops => new DaggerfallClassicPhysicalDelivery(ops);
}
