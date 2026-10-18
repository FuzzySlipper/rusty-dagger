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
        new(Manifest(id), owner ?? (_ => new Absorb()));

    [Fact]
    public void A_variant_supplies_an_owner_for_exactly_the_seams_it_declares()
    {
        Assert.Throws<ArgumentException>(() => new DaggerfallRulesVariant(Manifest("test.claims"), physicalMitigation: null));
        Assert.Throws<ArgumentException>(() => new DaggerfallRulesVariant(Manifest("test.silent", seams: []), _ => new Absorb()));
        Assert.Throws<ArgumentException>(() => new DaggerfallRulesVariant(Manifest("test.foreign", ruleset: "other"), _ => new Absorb()));
        // A variant that owned save state would lose it on every save until a variant save section is composed.
        Assert.Throws<NotSupportedException>(() => new DaggerfallRulesVariant(Manifest("test.stateful", ownsSaveState: true), _ => new Absorb()));
        Assert.Throws<ArgumentException>(() => new DaggerfallRulesVariant(Manifest("test.contact", seams: [DaggerfallRulesSeams.PhysicalDelivery])));
        Assert.Throws<ArgumentException>(() => new DaggerfallRulesVariant(Manifest("test.contact", seams: [DaggerfallRulesSeams.PhysicalMitigation]),
            _ => new Absorb(), (_, ops) => new DaggerfallClassicPhysicalDelivery(ops)));
    }

    [Fact]
    public void A_selected_variant_claiming_delivery_builds_its_owner_over_the_shared_operations_with_its_tuning()
    {
        DaggerfallSeamContext? built = null;
        DaggerfallRulesVariant contact = new(Manifest("test.contact", seams: [DaggerfallRulesSeams.PhysicalDelivery]),
            physicalDelivery: (context, ops) => { built = context; return new DaggerfallClassicPhysicalDelivery(ops); });
        ResolvedGameComposition composition = Resolve(["test.contact"], contact);

        DaggerfallSeamSelection selection = DaggerfallSeamSelection.From(composition.Variants, [contact]);
        Assert.Equal(new RulesVariantId("test.contact"), selection.PhysicalDeliveryVariant);
        Assert.Null(selection.PhysicalMitigationVariant);
        Assert.IsType<DaggerfallClassicPhysicalDelivery>(selection.PhysicalDelivery(Inputs)(null!));
        Assert.Equal("variant tuning", Encoding.UTF8.GetString(built!.Tuning.Payload.Span));
        Assert.Null(DaggerfallSeamSelection.Classic.PhysicalDeliveryVariant);
    }

    [Fact]
    public void Classic_owns_every_seam_no_selected_variant_claims()
    {
        DaggerfallSeamSelection selection = DaggerfallSeamSelection.From([], [Variant("test.armor")]);
        Assert.Null(selection.PhysicalMitigationVariant);
        Assert.IsType<DaggerfallClassicPhysicalMitigation>(selection.BuildPhysicalMitigation(Inputs));
        Assert.IsType<DaggerfallClassicPhysicalMitigation>(DaggerfallSeamSelection.Classic.BuildPhysicalMitigation(Inputs));
    }

    [Fact]
    public void A_selected_variant_builds_its_owner_from_its_own_tuning()
    {
        DaggerfallSeamContext? built = null;
        DaggerfallRulesVariant armor = Variant("test.armor", context => { built = context; return new Absorb(); });
        ResolvedGameComposition composition = Resolve(["test.armor"], armor);

        DaggerfallSeamSelection selection = DaggerfallSeamSelection.From(composition.Variants, [armor]);
        IDaggerfallPhysicalMitigation owner = selection.BuildPhysicalMitigation(Inputs);

        Assert.IsType<Absorb>(owner);
        Assert.Equal(new RulesVariantId("test.armor"), selection.PhysicalMitigationVariant);
        Assert.Equal("variant tuning", Encoding.UTF8.GetString(built!.Tuning.Payload.Span));
        Assert.Same(TestPayload.Definitions, built.Definitions);
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
        _ => throw new NotSupportedException("These owners read no equipment."), _ => null);

    private static ProductContent Content(params (string Path, string Value)[] files) =>
        new(files.Select(file => new ProductContentFile(Encoding.UTF8.GetBytes(file.Path), Encoding.UTF8.GetBytes(file.Value))).ToArray());

    private sealed class Absorb : IDaggerfallPhysicalMitigation
    {
        public void Negate(NegationEvent negation, DaggerfallPhysicalHarm harm) { }
        public void Mitigate(MitigationEvent mitigation, DaggerfallPhysicalHarm harm) => mitigation.Absorbed += 1;
    }
}
