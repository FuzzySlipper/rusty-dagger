using WorldRpg.Kit;

namespace WorldRpg.Rulesets.Daggerfall.Variants;

/// <summary>
/// Every compiled Daggerfall rules variant, in a fixed order. A game bundle selects them by id; the
/// Host composes this catalog into the Daggerfall ruleset at its explicit built-in seam.
/// </summary>
public static class DaggerfallVariants
{
    /// <summary>The contact + mitigation variant: no physical to-hit roll; blows that connect are deflected or absorbed.</summary>
    public static readonly RulesVariantId Contact = new("daggerfall.contact");

    public static IReadOnlyList<DaggerfallRulesVariant> All { get; } =
    [
        new DaggerfallRulesVariant(
            new RulesVariantManifest(Contact, DaggerfallRuleset.Identity,
                [DaggerfallRulesSeams.PhysicalDelivery, DaggerfallRulesSeams.PhysicalMitigation],
                new TuningProfileId("daggerfall.contact"), [], ownsSaveState: false),
            physicalMitigation: context => new DaggerfallContactMitigation(context, DaggerfallContactTuning.Read(context.Tuning.Payload.Span)),
            physicalDelivery: (context, ops) => new DaggerfallContactDelivery(ops, DaggerfallContactTuning.Read(context.Tuning.Payload.Span))),
    ];
}
