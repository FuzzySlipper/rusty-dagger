using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What a variant's seam owners are built from when its session is composed.</summary>
/// <param name="Definitions">The session's admitted definitions.</param>
/// <param name="Character">The player's live character, when one exists.</param>
/// <param name="Tuning">The variant's own admitted tuning profile.</param>
internal sealed record DaggerfallSeamContext(DaggerfallDefinitions Definitions, Func<DaggerfallCharacterState?> Character, TuningProfile Tuning);

/// <summary>
/// One compiled Daggerfall rules variant: its manifest and an explicit owner factory for each seam it
/// claims. The manifest's seams and the supplied owners must agree, so a variant cannot claim a seam it
/// does not replace, or replace one it does not declare.
/// </summary>
public sealed class DaggerfallRulesVariant
{
    internal DaggerfallRulesVariant(RulesVariantManifest manifest,
        Func<DaggerfallSeamContext, IDaggerfallPhysicalMitigation>? physicalMitigation = null)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        if (manifest.Ruleset != DaggerfallRuleset.Identity)
            throw new ArgumentException($"Rules variant '{manifest.Id.Value}' applies to ruleset '{manifest.Ruleset.Value}', not Daggerfall.", nameof(manifest));
        // No variant save section is composed yet: a variant that owns state would lose it on every save.
        if (manifest.OwnsSaveState)
            throw new NotSupportedException($"Rules variant '{manifest.Id.Value}' owns save state, which Daggerfall does not yet compose for variants.");
        PhysicalMitigation = physicalMitigation;
        HashSet<RulesSeamId> supplied = [];
        if (physicalMitigation is not null) supplied.Add(DaggerfallRulesSeams.PhysicalMitigation);
        if (!supplied.SetEquals(manifest.Seams))
            throw new ArgumentException($"Rules variant '{manifest.Id.Value}' declares seams [{string.Join(", ", manifest.Seams.Select(seam => seam.Value))}] but supplies owners for [{string.Join(", ", supplied.Select(seam => seam.Value))}].", nameof(manifest));
    }

    public RulesVariantManifest Manifest { get; }
    internal Func<DaggerfallSeamContext, IDaggerfallPhysicalMitigation>? PhysicalMitigation { get; }
}

/// <summary>
/// The owner selected for each Daggerfall seam: a selected variant's when it claims the seam, otherwise
/// classic's. Session composition builds each owner through this one selection.
/// </summary>
internal sealed class DaggerfallSeamSelection
{
    private readonly (DaggerfallRulesVariant Variant, ResolvedRulesVariant Resolved)? _physicalMitigation;

    private DaggerfallSeamSelection((DaggerfallRulesVariant, ResolvedRulesVariant)? physicalMitigation) => _physicalMitigation = physicalMitigation;

    internal static DaggerfallSeamSelection Classic { get; } = new(null);

    /// <summary>The variant that owns physical mitigation, or none for classic.</summary>
    internal RulesVariantId? PhysicalMitigationVariant => _physicalMitigation?.Resolved.Id;

    /// <summary>Selects owners for a resolved composition's variants from the variants this ruleset compiles.</summary>
    internal static DaggerfallSeamSelection From(IReadOnlyList<ResolvedRulesVariant> selected, IReadOnlyList<DaggerfallRulesVariant> compiled)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(compiled);
        (DaggerfallRulesVariant, ResolvedRulesVariant)? physicalMitigation = null;
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
        }
        return new(physicalMitigation);
    }

    internal IDaggerfallPhysicalMitigation BuildPhysicalMitigation(DaggerfallDefinitions definitions, Func<DaggerfallCharacterState?> character) =>
        _physicalMitigation is { } owner
            ? owner.Variant.PhysicalMitigation!(new DaggerfallSeamContext(definitions, character, owner.Resolved.Tuning))
            : new DaggerfallClassicPhysicalMitigation(character);
}
