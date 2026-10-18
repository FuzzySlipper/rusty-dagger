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
                [DaggerfallRulesSeams.PhysicalDelivery, DaggerfallRulesSeams.PhysicalMitigation, DaggerfallRulesSeams.PlayerGuard, DaggerfallRulesSeams.TouchDelivery, DaggerfallRulesSeams.EnemyEngagement],
                new TuningProfileId("daggerfall.contact"), [], ownsSaveState: false),
            ComposeContact),
    ];

    /// <summary>The contact variant's owners for one session, sharing one tuning read and the player's guard.</summary>
    private static DaggerfallSeamOwners ComposeContact(DaggerfallSeamContext context)
    {
        DaggerfallContactTuning tuning = DaggerfallContactTuning.Read(context.Tuning.Payload.Span);
        DaggerfallContactGuard guard = new(context, tuning);
        return new DaggerfallSeamOwners(
            PhysicalMitigation: new DaggerfallContactMitigation(context, tuning, guard),
            PhysicalDelivery: ops => new DaggerfallContactDelivery(ops, tuning),
            PlayerGuard: guard,
            TouchDelivery: ops => new DaggerfallContactTouch(ops, tuning),
            EnemyEngagement: new DaggerfallContactEngagement(tuning));
    }
}
