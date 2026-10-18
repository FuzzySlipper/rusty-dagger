using System.Text;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallRulesVariantTests
{
    private static RulesVariantManifest Manifest(string id, RulesSeamId[]? seams = null, string ruleset = "daggerfall", bool ownsSaveState = false) =>
        new(new(id), new(ruleset), seams ?? [DaggerfallRulesSeams.PhysicalMitigation], new("test.variant-tuning"), [], ownsSaveState);

    private static DaggerfallRulesVariant Variant(string id, Func<DaggerfallSeamContext, IDaggerfallPhysicalMitigation>? owner = null) =>
        new(Manifest(id), context => new DaggerfallSeamOwners(PhysicalMitigation: (owner ?? (_ => new Absorb()))(context)));

    [Fact]
    public void A_variant_supplies_an_owner_for_exactly_the_seams_it_declares()
    {
        Assert.Throws<ArgumentException>(() => new DaggerfallRulesVariant(Manifest("test.foreign", ruleset: "other"), _ => new()));
        Assert.Throws<ArgumentException>(() => new DaggerfallRulesVariant(Manifest("test.unknown", seams: [new RulesSeamId("test.nowhere")]), _ => new()));
        // A variant that owned save state would lose it on every save until a variant save section is composed.
        Assert.Throws<NotSupportedException>(() => new DaggerfallRulesVariant(Manifest("test.stateful", ownsSaveState: true), _ => new()));

        DaggerfallRulesVariant missing = new(Manifest("test.claims"), _ => new());
        DaggerfallRulesVariant extra = new(Manifest("test.contact", seams: [DaggerfallRulesSeams.PhysicalMitigation]),
            _ => new(PhysicalMitigation: new Absorb(), PhysicalDelivery: ops => new DaggerfallClassicPhysicalDelivery(ops)));
        foreach (DaggerfallRulesVariant variant in new[] { missing, extra })
        {
            DaggerfallSeamSelection selection = DaggerfallSeamSelection.From(Resolve([variant.Manifest.Id.Value], variant).Variants, [variant]);
            Assert.Throws<InvalidOperationException>(() => selection.Build(Inputs));
        }
    }

    [Fact]
    public void A_selected_variant_composes_its_owners_once_with_its_tuning_and_classic_fills_the_rest()
    {
        int compositions = 0;
        DaggerfallSeamContext? built = null;
        DaggerfallRulesVariant contact = new(Manifest("test.contact", seams: [DaggerfallRulesSeams.PhysicalDelivery]),
            context => { compositions++; built = context; return new(PhysicalDelivery: ops => new DaggerfallClassicPhysicalDelivery(ops)); });
        ResolvedGameComposition composition = Resolve(["test.contact"], contact);

        DaggerfallSeamSelection selection = DaggerfallSeamSelection.From(composition.Variants, [contact]);
        DaggerfallComposedSeams seams = selection.Build(Inputs);

        Assert.Equal(1, compositions);
        Assert.Equal(new RulesVariantId("test.contact"), selection.PhysicalDeliveryVariant);
        Assert.Null(selection.PhysicalMitigationVariant);
        Assert.Null(selection.PlayerGuardVariant);
        Assert.IsType<DaggerfallClassicPhysicalDelivery>(seams.PhysicalDelivery(null!));
        Assert.IsType<DaggerfallClassicPhysicalMitigation>(seams.PhysicalMitigation);
        Assert.Same(DaggerfallNoPlayerGuard.Instance, seams.PlayerGuard);
        Assert.Equal("variant tuning", Encoding.UTF8.GetString(built!.Tuning.Payload.Span));
        Assert.Same(TestPayload.Definitions, built.Definitions);
    }

    [Fact]
    public void Classic_owns_every_seam_and_has_no_guard()
    {
        DaggerfallComposedSeams seams = DaggerfallSeamSelection.From([], [Variant("test.armor")]).Build(Inputs);
        Assert.IsType<DaggerfallClassicPhysicalMitigation>(seams.PhysicalMitigation);
        Assert.IsType<DaggerfallClassicPhysicalDelivery>(seams.PhysicalDelivery(null!));
        Assert.Same(DaggerfallNoPlayerGuard.Instance, seams.PlayerGuard);
        Assert.False(seams.PlayerGuard.Raised);
        Assert.IsType<DaggerfallClassicTouchDelivery>(seams.TouchDelivery(null!));
        Assert.Null(DaggerfallSeamSelection.Classic.TouchDeliveryVariant);
        Assert.Null(DaggerfallSeamSelection.Classic.PhysicalDeliveryVariant);
    }

    [Fact]
    public void Two_selected_variants_claiming_one_seam_are_refused_at_selection()
    {
        DaggerfallRulesVariant first = Variant("test.armor"), second = Variant("test.hide");
        Assert.Throws<InvalidOperationException>(() => DaggerfallSeamSelection.From(
            [.. Resolve(["test.armor"], first).Variants, .. Resolve(["test.hide"], second).Variants], [first, second]));
    }

    [Fact]
    public void A_composition_naming_a_variant_this_ruleset_does_not_compile_is_refused()
    {
        ResolvedGameComposition composition = Resolve(["test.armor"], Variant("test.armor"));
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => DaggerfallSeamSelection.From(composition.Variants, []));
        Assert.Contains("test.armor", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ruleset_publishes_the_manifests_of_the_variants_it_compiles()
    {
        DaggerfallRulesVariant armor = Variant("test.armor");
        Assert.Equal([armor.Manifest], new DaggerfallRuleset([armor]).CompiledVariants);
        Assert.Empty(new DaggerfallRuleset().CompiledVariants);
    }

    private static ResolvedGameComposition Resolve(string[] variants, params DaggerfallRulesVariant[] compiled) => GameCompositionResolver.Resolve(Content(
            ("worldrpg/bundles/test.bundle.json", $$"""{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"daggerfall","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"},"variants":[{{string.Join(",", variants.Select(id => $"\"{id}\""))}}]}"""),
            ("worldrpg/content-packs/test.base.pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"daggerfall","role":"daggerfall.base","dependencies":[],"payload":"payload/base.json"}"""),
            ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"daggerfall","payload":"payload/tuning.json"}"""),
            ("worldrpg/tuning/test-variant.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.variant-tuning","ruleset":"daggerfall","payload":"payload/variant-tuning.json"}"""),
            ("payload/base.json", "base"), ("payload/tuning.json", "tuning"), ("payload/variant-tuning.json", "variant tuning")),
        new GameBundleId("test.bundle"), [.. compiled.Select(variant => variant.Manifest)]).RequireComposition();

    /// <summary>Session reads for building owners; nothing here reads equipment.</summary>
    private static DaggerfallSeamInputs Inputs => new(TestPayload.Definitions, () => null, RandomMaximum.Create(),
        _ => throw new NotSupportedException("These owners read no equipment."), _ => null, () => 0);

    private static ProductContent Content(params (string Path, string Value)[] files) =>
        new(files.Select(file => new ProductContentFile(Encoding.UTF8.GetBytes(file.Path), Encoding.UTF8.GetBytes(file.Value))).ToArray());

    private sealed class Absorb : IDaggerfallPhysicalMitigation
    {
        public void Negate(NegationEvent negation, DaggerfallPhysicalHarm harm) { }
        public void Mitigate(MitigationEvent mitigation, DaggerfallPhysicalHarm harm) => mitigation.Absorbed += 1;
    }
}
