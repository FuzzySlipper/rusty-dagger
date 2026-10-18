using System.Text;
using Rusty.Engine;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class GameCompositionTests
{
    [Fact]
    public void Resolver_orders_dependencies_and_admits_the_current_payloads_once()
    {
        ProductContent content = Content(
            ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.base"},{"id":"test.world"}],"tuning":{"id":"test.tuning"}}"""),
            ("worldrpg/content-packs/test.base.pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","role":"test.content","dependencies":[],"payload":"payload/base.json"}"""),
            ("worldrpg/content-packs/test.world.pack.json", """{"kind":"worldrpg.content-pack","id":"test.world","ruleset":"test","role":"test.content","dependencies":[{"id":"test.base"}],"payload":"payload/world.json"}"""),
            ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/tuning.json"}"""),
            ("payload/base.json", "base"),
            ("payload/world.json", "world"),
            ("payload/tuning.json", "tuning"));

        ResolvedGameComposition composition = GameCompositionResolver.Resolve(content, new GameBundleId("test.bundle")).RequireComposition();

        Assert.Equal(["test.base", "test.world"], composition.ContentPacks.Select(pack => pack.Id.Value));
        Assert.All(composition.ContentPacks, pack => Assert.Equal(new ContentPackRoleId("test.content"), pack.Role));
        Assert.Equal("test", composition.Ruleset.Value);
        Assert.Equal("test.tuning", composition.Tuning.Id.Value);
        Assert.Equal("test.bundle", composition.Identity.Bundle.Value);
        Assert.Equal("test", composition.Identity.Ruleset.Value);
        Assert.Equal(["test.base", "test.world"], composition.Identity.ContentPacks.Select(pack => pack.Value));
        Assert.Equal(["test.base", "test.world"], composition.Identity.ContentPackIdentities.Select(pack => pack.Id.Value));
        Assert.Equal("test.tuning", composition.Identity.Tuning.Value);
        Assert.Equal("world", Encoding.UTF8.GetString(composition.Content.ReadBytes("payload/world.json").Span));
    }

    [Theory]
    [InlineData("missing", "Content pack 'test.missing' is missing.")]
    [InlineData("cycle", "Content pack dependency cycle includes 'test.base'.")]
    public void Resolver_reports_invalid_dependency_graphs(string variant, string expectedDiagnostic)
    {
        string dependency = variant == "missing" ? "{\"id\":\"test.missing\"}" : "{\"id\":\"test.world\"}";
        ProductContent content = Content(
            ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}"""),
            ("worldrpg/content-packs/test.base.pack.json", $$"""{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","role":"test.content","dependencies":[{{dependency}}],"payload":"payload/base.json"}"""),
            ("worldrpg/content-packs/test.world.pack.json", """{"kind":"worldrpg.content-pack","id":"test.world","ruleset":"test","role":"test.content","dependencies":[{"id":"test.base"}],"payload":"payload/world.json"}"""),
            ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/tuning.json"}"""),
            ("payload/base.json", "base"),
            ("payload/world.json", "world"),
            ("payload/tuning.json", "tuning"));

        GameCompositionResolution resolution = GameCompositionResolver.Resolve(content, new GameBundleId("test.bundle"));

        Assert.False(resolution.IsResolved);
        Assert.Contains(resolution.Diagnostics, diagnostic => diagnostic.Message.Contains(expectedDiagnostic, StringComparison.Ordinal));
    }

    [Fact]
    public void Resolver_rejects_duplicate_descriptors_and_mismatched_tuning_ruleset()
    {
        ProductContent content = Content(
            ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}"""),
            ("worldrpg/bundles/test-copy.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}"""),
            ("worldrpg/content-packs/test.base.pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"other","role":"test.content","dependencies":[],"payload":"payload/base.json"}"""),
            ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"other","payload":"payload/tuning.json"}"""),
            ("payload/base.json", "base"),
            ("payload/tuning.json", "tuning"));

        GameCompositionResolution resolution = GameCompositionResolver.Resolve(content, new GameBundleId("test.bundle"));

        Assert.False(resolution.IsResolved);
        Assert.Contains(resolution.Diagnostics, diagnostic => diagnostic.Message.Contains("Duplicate game bundle", StringComparison.Ordinal));
        Assert.Contains(resolution.Diagnostics, diagnostic => diagnostic.Message.Contains("belongs to ruleset", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("bundle", "ruleset")]
    [InlineData("pack", "ruleset")]
    [InlineData("pack-role", "role")]
    [InlineData("tuning", "payload")]
    public void Resolver_reports_required_current_descriptor_fields(string descriptor, string missingProperty)
    {
        string bundle = descriptor == "bundle"
            ? """{"kind":"worldrpg.game-bundle","id":"test.bundle","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}"""
            : """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}""";
        string pack = descriptor switch
        {
            "pack" => """{"kind":"worldrpg.content-pack","id":"test.base","role":"test.content","dependencies":[],"payload":"payload/base.json"}""",
            "pack-role" => """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","dependencies":[],"payload":"payload/base.json"}""",
            _ => """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","role":"test.content","dependencies":[],"payload":"payload/base.json"}""",
        };
        string tuning = descriptor == "tuning"
            ? """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test"}"""
            : """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/tuning.json"}""";

        GameCompositionResolution resolution = GameCompositionResolver.Resolve(Content(
            ("worldrpg/bundles/test.bundle.json", bundle),
            ("worldrpg/content-packs/test.base.pack.json", pack),
            ("worldrpg/tuning/test.tuning.json", tuning),
            ("payload/base.json", "base"),
            ("payload/tuning.json", "tuning")), new GameBundleId("test.bundle"));

        Assert.False(resolution.IsResolved);
        Assert.Contains(resolution.Diagnostics, diagnostic => diagnostic.Message.Contains($"Required property '{missingProperty}' is missing.", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolver_keeps_admitted_payloads_and_collections_stable_for_live_callers()
    {
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(Content(
            ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}"""),
            ("worldrpg/content-packs/test.base.pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","role":"test.content","dependencies":[],"payload":"payload/base.json"}"""),
            ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/tuning.json"}"""),
            ("payload/base.json", "base"), ("payload/tuning.json", "tuning")), new GameBundleId("test.bundle")).RequireComposition();

        Assert.False(composition.ContentPacks is ContentPack[]);
        Assert.False(composition.Bundle.ContentPacks is ContentPackReference[]);
        byte[] packPayload = composition.ContentPacks[0].Payload.ToArray();
        byte[] tuningPayload = composition.Tuning.Payload.ToArray();
        packPayload[0] = (byte)'X';
        tuningPayload[0] = (byte)'X';

        Assert.Equal("base", Encoding.UTF8.GetString(composition.ContentPacks[0].Payload.Span));
        Assert.Equal("tuning", Encoding.UTF8.GetString(composition.Tuning.Payload.Span));
    }

    private static readonly RulesSeamId Delivery = new("test.delivery"), Mitigation = new("test.mitigation");

    private static RulesVariantManifest Variant(string id, string ruleset = "test", RulesSeamId[]? seams = null, string tuning = "test.variant-tuning",
        string[]? packs = null) =>
        new(new(id), new(ruleset), seams ?? [Mitigation], new(tuning), (packs ?? []).Select(pack => new ContentPackId(pack)), ownsSaveState: false);

    private static ProductContent VariantContent(string variants) => Content(
        ("worldrpg/bundles/test.bundle.json", $$"""{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"},"variants":{{variants}}}"""),
        ("worldrpg/content-packs/test.base.pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","role":"test.content","dependencies":[],"payload":"payload/base.json"}"""),
        ("worldrpg/content-packs/test.variant.pack.json", """{"kind":"worldrpg.content-pack","id":"test.variant","ruleset":"test","role":"test.variant-content","dependencies":[{"id":"test.base"}],"payload":"payload/variant.json"}"""),
        ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/tuning.json"}"""),
        ("worldrpg/tuning/test-variant.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.variant-tuning","ruleset":"test","payload":"payload/variant-tuning.json"}"""),
        ("worldrpg/tuning/other.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"other.tuning","ruleset":"other","payload":"payload/tuning.json"}"""),
        ("payload/base.json", "base"), ("payload/variant.json", "variant"),
        ("payload/tuning.json", "tuning"), ("payload/variant-tuning.json", "variant tuning"));

    [Fact]
    public void A_bundle_selects_compiled_variants_with_their_own_tuning_and_packs_and_records_them_in_its_identity()
    {
        RulesVariantManifest[] compiled = [Variant("test.contact", seams: [Delivery], packs: ["test.variant"]), Variant("test.armor")];

        ResolvedGameComposition composition = GameCompositionResolver.Resolve(VariantContent("""["test.contact","test.armor"]"""),
            new GameBundleId("test.bundle"), compiled).RequireComposition();

        Assert.Equal(["test.contact", "test.armor"], composition.Variants.Select(variant => variant.Id.Value));
        Assert.Equal(["test.contact", "test.armor"], composition.Identity.Variants.Select(variant => variant.Value));
        Assert.Equal(["test.contact", "test.armor"], composition.Bundle.Variants.Select(variant => variant.Value));
        Assert.Equal("variant tuning", Encoding.UTF8.GetString(composition.Variants[0].Tuning.Payload.Span));
        Assert.Equal("tuning", Encoding.UTF8.GetString(composition.Tuning.Payload.Span)); // The bundle's own profile is untouched.
        Assert.Equal(["test.base", "test.variant"], composition.ContentPacks.Select(pack => pack.Id.Value));
    }

    [Fact]
    public void A_bundle_without_variants_resolves_to_the_rulesets_own_rules()
    {
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(VariantContent("[]"), new GameBundleId("test.bundle"),
            [Variant("test.contact")]).RequireComposition();
        Assert.Empty(composition.Variants);
        Assert.Empty(composition.Identity.Variants);
    }

    [Theory]
    [InlineData("""["test.unknown"]""", CompositionDiagnosticNames.UnknownVariant, "does not compile")]
    [InlineData("""["test.foreign"]""", CompositionDiagnosticNames.VariantRulesetMismatch, "applies to ruleset 'other'")]
    [InlineData("""["test.contact","test.armor"]""", CompositionDiagnosticNames.VariantSeamCollision, "both claim seam 'test.mitigation'")]
    public void Resolver_names_a_variant_selection_it_cannot_compose(string variants, string name, string message)
    {
        RulesVariantManifest[] compiled = [Variant("test.contact"), Variant("test.armor"), Variant("test.foreign", ruleset: "other", tuning: "other.tuning")];

        GameCompositionResolution resolution = GameCompositionResolver.Resolve(VariantContent(variants), new GameBundleId("test.bundle"), compiled);

        Assert.False(resolution.IsResolved);
        CompositionDiagnostic diagnostic = Assert.Single(resolution.Diagnostics, value => value.Name == name);
        Assert.Contains(message, diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("test.missing-tuning", null, "Tuning profile 'test.missing-tuning' is missing.")]
    [InlineData("other.tuning", null, "Tuning profile 'other.tuning' belongs to ruleset 'other', not 'test'.")]
    [InlineData("test.variant-tuning", "test.missing-pack", "Content pack 'test.missing-pack' is missing.")]
    public void A_variants_tuning_and_packs_are_reported_like_the_bundles_own(string tuning, string? pack, string message)
    {
        RulesVariantManifest[] compiled = [Variant("test.contact", tuning: tuning, packs: pack is null ? [] : [pack])];

        GameCompositionResolution resolution = GameCompositionResolver.Resolve(VariantContent("""["test.contact"]"""), new GameBundleId("test.bundle"), compiled);

        Assert.False(resolution.IsResolved);
        Assert.Contains(resolution.Diagnostics, diagnostic => diagnostic.Message.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void Resolver_rejects_a_repeated_variant_selection_and_duplicate_compiled_manifests()
    {
        GameCompositionResolution repeated = GameCompositionResolver.Resolve(VariantContent("""["test.contact","test.contact"]"""),
            new GameBundleId("test.bundle"), [Variant("test.contact")]);
        Assert.Contains(repeated.Diagnostics, diagnostic => diagnostic.Message.Contains("selects a rules variant more than once", StringComparison.Ordinal));

        GameCompositionResolution duplicate = GameCompositionResolver.Resolve(VariantContent("[]"), new GameBundleId("test.bundle"),
            [Variant("test.contact"), Variant("test.contact", seams: [Delivery])]);
        Assert.Contains(duplicate.Diagnostics, diagnostic => diagnostic.Name == CompositionDiagnosticNames.DuplicateVariantManifest);
        Assert.False(duplicate.IsResolved);
    }

    [Fact]
    public void A_manifest_claims_each_seam_once()
    {
        Assert.Throws<ArgumentException>(() => Variant("test.contact", seams: [Delivery, Delivery]));
        Assert.Throws<ArgumentException>(() => Variant("test.contact", packs: ["test.variant", "test.variant"]));
    }

    private static ProductContent Content(params (string Path, string Value)[] files) => new(files.Select(file => new ProductContentFile(Encoding.UTF8.GetBytes(file.Path), Encoding.UTF8.GetBytes(file.Value))).ToArray());
}
