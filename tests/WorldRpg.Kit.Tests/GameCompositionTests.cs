using System.Text;
using Rusty.Engine;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class GameCompositionTests
{
    [Fact]
    public void Resolver_orders_dependencies_and_fingerprints_the_selected_immutable_payloads()
    {
        ProductContent first = Content(
            ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","version":1,"ruleset":"test","contentPacks":[{"id":"test.base","version":1},{"id":"test.world","version":1}],"tuning":{"id":"test.tuning","version":1}}"""),
            ("worldrpg/content-packs/test.base.pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","version":1,"dependencies":[],"payload":"payload/base.json"}"""),
            ("worldrpg/content-packs/test.world.pack.json", """{"kind":"worldrpg.content-pack","id":"test.world","version":1,"dependencies":[{"id":"test.base","version":1}],"payload":"payload/world.json"}"""),
            ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","version":1,"ruleset":"test","payload":"payload/tuning.json"}"""),
            ("payload/base.json", "base"),
            ("payload/world.json", "world"),
            ("payload/tuning.json", "tuning"));

        ResolvedGameComposition composition = GameCompositionResolver.Resolve(first, new GameBundleId("test.bundle")).RequireComposition();
        ResolvedGameComposition repeat = GameCompositionResolver.Resolve(first, new GameBundleId("test.bundle")).RequireComposition();

        Assert.Equal(["test.base", "test.world"], composition.ContentPacks.Select(pack => pack.Id.Value));
        Assert.Equal("test", composition.Ruleset.Value);
        Assert.Equal("test.tuning", composition.Tuning.Id.Value);
        Assert.Equal(composition.Fingerprint, repeat.Fingerprint);
        Assert.Matches("^[0-9a-f]{64}$", composition.Fingerprint);
    }

    [Theory]
    [InlineData("missing", "Content pack 'test.missing' is missing.")]
    [InlineData("cycle", "Content pack dependency cycle includes 'test.base'.")]
    [InlineData("version", "requires version 2")]
    public void Resolver_reports_invalid_dependency_graphs(string variant, string expectedDiagnostic)
    {
        string dependency = variant switch
        {
            "missing" => "{\"id\":\"test.missing\",\"version\":1}",
            "cycle" => "{\"id\":\"test.world\",\"version\":1}",
            _ => "{\"id\":\"test.base\",\"version\":2}",
        };
        ProductContent content = Content(
            ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","version":1,"ruleset":"test","contentPacks":[{"id":"test.base","version":1}],"tuning":{"id":"test.tuning","version":1}}"""),
            ("worldrpg/content-packs/test.base.pack.json", $$"""{"kind":"worldrpg.content-pack","id":"test.base","version":1,"dependencies":[{{dependency}}],"payload":"payload/base.json"}"""),
            ("worldrpg/content-packs/test.world.pack.json", """{"kind":"worldrpg.content-pack","id":"test.world","version":1,"dependencies":[{"id":"test.base","version":1}],"payload":"payload/world.json"}"""),
            ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","version":1,"ruleset":"test","payload":"payload/tuning.json"}"""),
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
            ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","version":1,"ruleset":"test","contentPacks":[{"id":"test.base","version":1}],"tuning":{"id":"test.tuning","version":1}}"""),
            ("worldrpg/bundles/test-copy.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","version":1,"ruleset":"test","contentPacks":[{"id":"test.base","version":1}],"tuning":{"id":"test.tuning","version":1}}"""),
            ("worldrpg/content-packs/test.base.pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","version":1,"dependencies":[],"payload":"payload/base.json"}"""),
            ("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","version":1,"ruleset":"other","payload":"payload/tuning.json"}"""),
            ("payload/base.json", "base"),
            ("payload/tuning.json", "tuning"));

        GameCompositionResolution resolution = GameCompositionResolver.Resolve(content, new GameBundleId("test.bundle"));

        Assert.False(resolution.IsResolved);
        Assert.Contains(resolution.Diagnostics, diagnostic => diagnostic.Message.Contains("Duplicate game bundle", StringComparison.Ordinal));
        Assert.Contains(resolution.Diagnostics, diagnostic => diagnostic.Message.Contains("belongs to ruleset", StringComparison.Ordinal));
    }

    private static ProductContent Content(params (string Path, string Value)[] files) => new(files.Select(file => new ProductContentFile(Encoding.UTF8.GetBytes(file.Path), Encoding.UTF8.GetBytes(file.Value))).ToArray());
}
