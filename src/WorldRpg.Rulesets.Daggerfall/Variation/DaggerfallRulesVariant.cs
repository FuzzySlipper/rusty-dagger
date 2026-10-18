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
/// <param name="PlayerArmorValueModifier">
/// The shift the player's worn enchantments give its whole armor value, in the donor's armor-value units:
/// negative is stronger armor, positive weaker.
/// </param>
internal sealed record DaggerfallSeamInputs(DaggerfallDefinitions Definitions, Func<DaggerfallCharacterState?> Character,
    IRandomService Random, Func<long, EquipmentRead> EquipmentOf, Func<long, StatsComponent?> StatsOf, Func<int> PlayerArmorValueModifier);

/// <summary>What a variant's seam owners are built from when its session is composed: the session's reads and the variant's own tuning.</summary>
internal sealed record DaggerfallSeamContext(DaggerfallSeamInputs Session, TuningProfile Tuning)
{
    internal DaggerfallDefinitions Definitions => Session.Definitions;
}

/// <summary>
/// The owners one variant supplies for the seams it claims, composed together so owners of one variant can
/// share what they need (a guard the mitigation reads, for example). A seam a variant does not claim has no owner here.
/// </summary>
internal sealed record DaggerfallSeamOwners(
    IDaggerfallPhysicalMitigation? PhysicalMitigation = null,
    Func<IDaggerfallPhysicalHarmOperations, IDaggerfallPhysicalDelivery>? PhysicalDelivery = null,
    IDaggerfallPlayerGuard? PlayerGuard = null,
    Func<DaggerfallTouchOperations, IDaggerfallTouchDelivery>? TouchDelivery = null,
    IDaggerfallEnemyEngagement? EnemyEngagement = null)
{
    internal IEnumerable<RulesSeamId> Seams
    {
        get
        {
            if (PhysicalMitigation is not null) yield return DaggerfallRulesSeams.PhysicalMitigation;
            if (PhysicalDelivery is not null) yield return DaggerfallRulesSeams.PhysicalDelivery;
            if (PlayerGuard is not null) yield return DaggerfallRulesSeams.PlayerGuard;
            if (TouchDelivery is not null) yield return DaggerfallRulesSeams.TouchDelivery;
            if (EnemyEngagement is not null) yield return DaggerfallRulesSeams.EnemyEngagement;
        }
    }
}

/// <summary>
/// One compiled Daggerfall rules variant: its manifest and the one explicit composition that builds an owner
/// for each seam it claims. Composition fails when the owners it builds disagree with the manifest's seams,
/// so a variant cannot claim a seam it does not replace, or replace one it does not declare.
/// </summary>
public sealed class DaggerfallRulesVariant
{
    private readonly Func<DaggerfallSeamContext, DaggerfallSeamOwners> _compose;

    internal DaggerfallRulesVariant(RulesVariantManifest manifest, Func<DaggerfallSeamContext, DaggerfallSeamOwners> compose)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _compose = compose ?? throw new ArgumentNullException(nameof(compose));
        if (manifest.Ruleset != DaggerfallRuleset.Identity)
            throw new ArgumentException($"Rules variant '{manifest.Id.Value}' applies to ruleset '{manifest.Ruleset.Value}', not Daggerfall.", nameof(manifest));
        // No variant save section is composed yet: a variant that owns state would lose it on every save.
        if (manifest.OwnsSaveState)
            throw new NotSupportedException($"Rules variant '{manifest.Id.Value}' owns save state, which Daggerfall does not yet compose for variants.");
        RulesSeamId[] unknown = [.. manifest.Seams.Where(seam => !DaggerfallRulesSeams.All.Contains(seam))];
        if (unknown.Length != 0)
            throw new ArgumentException($"Rules variant '{manifest.Id.Value}' claims seams [{string.Join(", ", unknown.Select(seam => seam.Value))}] Daggerfall does not declare.", nameof(manifest));
    }

    public RulesVariantManifest Manifest { get; }

    /// <summary>Builds this variant's owners for one session.</summary>
    internal DaggerfallSeamOwners Compose(DaggerfallSeamContext context)
    {
        DaggerfallSeamOwners owners = _compose(context) ?? throw new InvalidOperationException($"Rules variant '{Manifest.Id.Value}' composed no owners.");
        RulesSeamId[] supplied = [.. owners.Seams];
        if (!supplied.ToHashSet().SetEquals(Manifest.Seams))
            throw new InvalidOperationException($"Rules variant '{Manifest.Id.Value}' declares seams [{string.Join(", ", Manifest.Seams.Select(seam => seam.Value))}] but supplies owners for [{string.Join(", ", supplied.Select(seam => seam.Value))}].");
        return owners;
    }
}

/// <summary>The owner composed for every Daggerfall seam in one session: each selected variant's, otherwise classic's.</summary>
internal sealed record DaggerfallComposedSeams(
    IDaggerfallPhysicalMitigation PhysicalMitigation,
    Func<IDaggerfallPhysicalHarmOperations, IDaggerfallPhysicalDelivery> PhysicalDelivery,
    IDaggerfallPlayerGuard PlayerGuard,
    Func<DaggerfallTouchOperations, IDaggerfallTouchDelivery> TouchDelivery,
    IDaggerfallEnemyEngagement EnemyEngagement);

/// <summary>
/// Which variant owns each Daggerfall seam: a selected variant's when it claims the seam, otherwise
/// classic's. Session composition builds every owner through this one selection.
/// </summary>
internal sealed class DaggerfallSeamSelection
{
    private readonly IReadOnlyList<(DaggerfallRulesVariant Variant, ResolvedRulesVariant Resolved)> _selected;
    private readonly IReadOnlyDictionary<RulesSeamId, RulesVariantId> _owners;

    private DaggerfallSeamSelection(IReadOnlyList<(DaggerfallRulesVariant, ResolvedRulesVariant)> selected, IReadOnlyDictionary<RulesSeamId, RulesVariantId> owners)
    {
        _selected = selected;
        _owners = owners;
    }

    internal static DaggerfallSeamSelection Classic { get; } = new([], new Dictionary<RulesSeamId, RulesVariantId>());

    /// <summary>The variant that owns physical mitigation, or none for classic.</summary>
    internal RulesVariantId? PhysicalMitigationVariant => Owner(DaggerfallRulesSeams.PhysicalMitigation);

    /// <summary>The variant that owns physical delivery, or none for classic.</summary>
    internal RulesVariantId? PhysicalDeliveryVariant => Owner(DaggerfallRulesSeams.PhysicalDelivery);

    /// <summary>The variant that owns the player's guard, or none for classic, which has no guard.</summary>
    internal RulesVariantId? PlayerGuardVariant => Owner(DaggerfallRulesSeams.PlayerGuard);

    /// <summary>The variant that owns touch delivery, or none for classic.</summary>
    internal RulesVariantId? TouchDeliveryVariant => Owner(DaggerfallRulesSeams.TouchDelivery);

    /// <summary>The variant that owns enemy engagement, or none for classic.</summary>
    internal RulesVariantId? EnemyEngagementVariant => Owner(DaggerfallRulesSeams.EnemyEngagement);

    private RulesVariantId? Owner(RulesSeamId seam) => _owners.TryGetValue(seam, out RulesVariantId owner) ? owner : null;

    /// <summary>Selects owners for a resolved composition's variants from the variants this ruleset compiles.</summary>
    internal static DaggerfallSeamSelection From(IReadOnlyList<ResolvedRulesVariant> selected, IReadOnlyList<DaggerfallRulesVariant> compiled)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(compiled);
        List<(DaggerfallRulesVariant, ResolvedRulesVariant)> variants = [];
        Dictionary<RulesSeamId, RulesVariantId> owners = [];
        foreach (ResolvedRulesVariant resolved in selected)
        {
            DaggerfallRulesVariant variant = compiled.SingleOrDefault(candidate => candidate.Manifest.Id == resolved.Id)
                ?? throw new InvalidOperationException($"The composition selects rules variant '{resolved.Id.Value}', which this Daggerfall ruleset does not compile.");
            foreach (RulesSeamId seam in variant.Manifest.Seams)
                if (!owners.TryAdd(seam, resolved.Id))
                    throw new InvalidOperationException($"Rules variants '{owners[seam].Value}' and '{resolved.Id.Value}' both claim seam '{seam.Value}'.");
            variants.Add((variant, resolved));
        }
        return new(variants, owners);
    }

    /// <summary>Composes every selected variant once over the session's reads, then fills each unclaimed seam with classic's owner.</summary>
    internal DaggerfallComposedSeams Build(DaggerfallSeamInputs session)
    {
        IDaggerfallPhysicalMitigation? mitigation = null;
        Func<IDaggerfallPhysicalHarmOperations, IDaggerfallPhysicalDelivery>? delivery = null;
        IDaggerfallPlayerGuard? guard = null;
        Func<DaggerfallTouchOperations, IDaggerfallTouchDelivery>? touch = null;
        IDaggerfallEnemyEngagement? engagement = null;
        foreach ((DaggerfallRulesVariant variant, ResolvedRulesVariant resolved) in _selected)
        {
            DaggerfallSeamOwners owners = variant.Compose(new DaggerfallSeamContext(session, resolved.Tuning));
            mitigation ??= owners.PhysicalMitigation;
            delivery ??= owners.PhysicalDelivery;
            guard ??= owners.PlayerGuard;
            touch ??= owners.TouchDelivery;
            engagement ??= owners.EnemyEngagement;
        }
        return new(mitigation ?? new DaggerfallClassicPhysicalMitigation(session.Character),
            delivery ?? (ops => new DaggerfallClassicPhysicalDelivery(ops)),
            guard ?? DaggerfallNoPlayerGuard.Instance,
            touch ?? (ops => new DaggerfallClassicTouchDelivery(ops)),
            engagement ?? DaggerfallClassicEnemyEngagement.Instance);
    }
}
