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
            ("worldrpg/content-packs/test.base.pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","dependencies":[],"payload":"payload/base.json"}"""),
            ("worldrpg/content-packs/test.world.pack.json", """{"kind":"worldrpg.content-pack","id":"test.world","ruleset":"test","dependencies":[{"id":"test.base"}],"payload":"payload/world.json"}"""),
            ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/tuning.json"}"""),
            ("payload/base.json", "base"),
            ("payload/world.json", "world"),
            ("payload/tuning.json", "tuning"));

        ResolvedGameComposition composition = GameCompositionResolver.Resolve(content, new GameBundleId("test.bundle")).RequireComposition();

        Assert.Equal(["test.base", "test.world"], composition.ContentPacks.Select(pack => pack.Id.Value));
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
            ("worldrpg/content-packs/test.base.pack.json", $$"""{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","dependencies":[{{dependency}}],"payload":"payload/base.json"}"""),
            ("worldrpg/content-packs/test.world.pack.json", """{"kind":"worldrpg.content-pack","id":"test.world","ruleset":"test","dependencies":[{"id":"test.base"}],"payload":"payload/world.json"}"""),
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
            ("worldrpg/content-packs/test.base.pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"other","dependencies":[],"payload":"payload/base.json"}"""),
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
    [InlineData("tuning", "payload")]
    public void Resolver_reports_required_current_descriptor_fields(string descriptor, string missingProperty)
    {
        string bundle = descriptor == "bundle"
            ? """{"kind":"worldrpg.game-bundle","id":"test.bundle","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}"""
            : """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}""";
        string pack = descriptor == "pack"
            ? """{"kind":"worldrpg.content-pack","id":"test.base","dependencies":[],"payload":"payload/base.json"}"""
            : """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","dependencies":[],"payload":"payload/base.json"}""";
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
            ("worldrpg/content-packs/test.base.pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","dependencies":[],"payload":"payload/base.json"}"""),
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

    private static ProductContent Content(params (string Path, string Value)[] files) => new(files.Select(file => new ProductContentFile(Encoding.UTF8.GetBytes(file.Path), Encoding.UTF8.GetBytes(file.Value))).ToArray());
}
