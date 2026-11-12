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
            ("worldrpg/packs/test.base/pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","role":"test.content","dependencies":[],"payload":"payload/base.json"}"""),
            ("worldrpg/packs/test.world/pack.json", """{"kind":"worldrpg.content-pack","id":"test.world","ruleset":"test","role":"test.content","dependencies":[{"id":"test.base"}],"payload":"payload/world.json"}"""),
            ("worldrpg/tuning/test/tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/values.json"}"""),
            ("payload/base.json", "base"),
            ("payload/world.json", "world"),
            ("payload/values.json", "tuning"));

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

    [Fact]
    public void A_pack_an_installed_container_describes_resolves_with_its_payload_read_from_the_container()
    {
        string directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "kit-installed-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string staged = Directory.CreateDirectory(Path.Combine(directory, "staged")).FullName;
            Write(staged, InstalledContent.DescriptionPath, """
                {"kind":"worldrpg.data-container","importerRevision":"rev-1","contentPacks":[
                  {"kind":"worldrpg.content-pack","id":"test.imported","ruleset":"test","role":"test.content","dependencies":[{"id":"test.base"}],"payload":"payload/imported.json"}]}
                """);
            Write(staged, "payload/imported.json", "imported");
            string container = Path.Combine(directory, "data.rpak");
            ProductContent content = Content(
                ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.imported"}],"tuning":{"id":"test.tuning"}}"""),
                ("worldrpg/packs/test.base/pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","role":"test.content","dependencies":[],"payload":"payload/base.json"}"""),
                ("worldrpg/tuning/test/tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/values.json"}"""),
                ("payload/base.json", "base"),
                ("payload/values.json", "tuning"));
            using Rusty.Engine.Testing.EngineTestHost host = Rusty.Engine.Testing.EngineTestHost.Create(new Rusty.Engine.Testing.EngineTestHostOptions());

            host.Call(engine =>
            {
                ProductContentBundle.PackContainer(engine.Content, staged, container, compress: false);
                using ProductContentBundle opened = ProductContentBundle.OpenContainer(engine.Content, container);
                InstalledContent installed = new("data.rpak", opened);

                // Without the container the pack it describes is missing, named so a host can say what to install.
                GameCompositionResolution alone = GameCompositionResolver.Resolve(content, new GameBundleId("test.bundle"));
                Assert.Contains(alone.Diagnostics, diagnostic => diagnostic.Name == CompositionDiagnosticNames.MissingContentPack);

                ResolvedGameComposition composition = GameCompositionResolver.Resolve(content, new GameBundleId("test.bundle"), [installed]).RequireComposition();
                Assert.Equal(["test.base", "test.imported"], composition.ContentPacks.Select(pack => pack.Id.Value));
                Assert.Equal("imported", Encoding.UTF8.GetString(composition.RequireContentPack(new("test.imported")).Payload.Span));
                Assert.Equal("base", Encoding.UTF8.GetString(composition.RequireContentPack(new("test.base")).Payload.Span));
                Assert.Same(installed, Assert.Single(composition.Installed));
                Assert.Equal("rev-1", installed.Description!.Revision);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_pack_both_the_product_and_an_installed_container_describe_is_a_duplicate()
    {
        string directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "kit-installed-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string staged = Directory.CreateDirectory(Path.Combine(directory, "staged")).FullName;
            Write(staged, InstalledContent.DescriptionPath, """
                {"kind":"worldrpg.data-container","importerRevision":"rev-1","contentPacks":[
                  {"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","role":"test.content","dependencies":[],"payload":"payload/base.json"}]}
                """);
            string container = Path.Combine(directory, "data.rpak");
            ProductContent content = Content(
                ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}"""),
                ("worldrpg/packs/test.base/pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","role":"test.content","dependencies":[],"payload":"payload/base.json"}"""),
                ("worldrpg/tuning/test/tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/values.json"}"""),
                ("payload/base.json", "base"),
                ("payload/values.json", "tuning"));
            using Rusty.Engine.Testing.EngineTestHost host = Rusty.Engine.Testing.EngineTestHost.Create(new Rusty.Engine.Testing.EngineTestHostOptions());

            host.Call(engine =>
            {
                ProductContentBundle.PackContainer(engine.Content, staged, container, compress: false);
                using ProductContentBundle opened = ProductContentBundle.OpenContainer(engine.Content, container);

                GameCompositionResolution resolution = GameCompositionResolver.Resolve(content, new GameBundleId("test.bundle"), [new InstalledContent("data.rpak", opened)]);

                Assert.False(resolution.IsResolved);
                Assert.Contains(resolution.Diagnostics, diagnostic => diagnostic.Message.Contains("Duplicate content pack 'test.base' (also described by 'data.rpak')", StringComparison.Ordinal));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Write(string root, string path, string text)
    {
        string file = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    [Theory]
    [InlineData("missing", "Content pack 'test.missing' is missing.")]
    [InlineData("cycle", "Content pack dependency cycle includes 'test.base'.")]
    public void Resolver_reports_invalid_dependency_graphs(string variant, string expectedDiagnostic)
    {
        string dependency = variant == "missing" ? "{\"id\":\"test.missing\"}" : "{\"id\":\"test.world\"}";
        ProductContent content = Content(
            ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}"""),
            ("worldrpg/packs/test.base/pack.json", $$"""{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","role":"test.content","dependencies":[{{dependency}}],"payload":"payload/base.json"}"""),
            ("worldrpg/packs/test.world/pack.json", """{"kind":"worldrpg.content-pack","id":"test.world","ruleset":"test","role":"test.content","dependencies":[{"id":"test.base"}],"payload":"payload/world.json"}"""),
            ("worldrpg/tuning/test/tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/values.json"}"""),
            ("payload/base.json", "base"),
            ("payload/world.json", "world"),
            ("payload/values.json", "tuning"));

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
            ("worldrpg/packs/test.base/pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"other","role":"test.content","dependencies":[],"payload":"payload/base.json"}"""),
            ("worldrpg/tuning/test/tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"other","payload":"payload/values.json"}"""),
            ("payload/base.json", "base"),
            ("payload/values.json", "tuning"));

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
            : """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/values.json"}""";

        GameCompositionResolution resolution = GameCompositionResolver.Resolve(Content(
            ("worldrpg/bundles/test.bundle.json", bundle),
            ("worldrpg/packs/test.base/pack.json", pack),
            ("worldrpg/tuning/test/tuning.json", tuning),
            ("payload/base.json", "base"),
            ("payload/values.json", "tuning")), new GameBundleId("test.bundle"));

        Assert.False(resolution.IsResolved);
        Assert.Contains(resolution.Diagnostics, diagnostic => diagnostic.Message.Contains($"Required property '{missingProperty}' is missing.", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolver_keeps_admitted_payloads_and_collections_stable_for_live_callers()
    {
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(Content(
            ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}"""),
            ("worldrpg/packs/test.base/pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","role":"test.content","dependencies":[],"payload":"payload/base.json"}"""),
            ("worldrpg/tuning/test/tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/values.json"}"""),
            ("payload/base.json", "base"), ("payload/values.json", "tuning")), new GameBundleId("test.bundle")).RequireComposition();

        Assert.False(composition.ContentPacks is ContentPack[]);
        Assert.False(composition.Bundle.ContentPacks is ContentPackReference[]);
        byte[] packPayload = composition.ContentPacks[0].Payload.ToArray();
        byte[] tuningPayload = composition.Tuning.Payload.ToArray();
        packPayload[0] = (byte)'X';
        tuningPayload[0] = (byte)'X';

        Assert.Equal("base", Encoding.UTF8.GetString(composition.ContentPacks[0].Payload.Span));
        Assert.Equal("tuning", Encoding.UTF8.GetString(composition.Tuning.Payload.Span));
    }

    [Fact]
    public void Bundles_list_by_id_with_their_authored_title_or_their_id_and_a_resolved_bundle_carries_its_title()
    {
        ProductContent content = Content(
            ("worldrpg/bundles/b.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.b","title":"  The B game ","ruleset":"test","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}"""),
            ("worldrpg/bundles/a.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.a","ruleset":"other","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}"""),
            ("worldrpg/bundles/bad.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bad","title":"","ruleset":"test","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"}}"""),
            ("worldrpg/packs/test.base/pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","role":"test.content","dependencies":[],"payload":"payload/base.json"}"""),
            ("worldrpg/tuning/test/tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/values.json"}"""),
            ("payload/base.json", "{}"), ("payload/values.json", "{}"));

        // A malformed descriptor (an empty title) is not offered; listing never resolves packs or tuning.
        Assert.Equal([new GameBundleSummary(new("test.a"), new("other"), "test.a"), new GameBundleSummary(new("test.b"), new("test"), "The B game")],
            GameCompositionResolver.ListBundles(content));
        ProductContent wellFormed = new(content.Files.ToArray().Where(file => !Encoding.UTF8.GetString(file.Path.Span).EndsWith("bad.bundle.json", StringComparison.Ordinal)).ToArray());
        Assert.Equal("The B game", GameCompositionResolver.Resolve(wellFormed, new GameBundleId("test.b")).RequireComposition().Bundle.Title);
    }

    [Fact]
    public void A_bundle_that_selects_rules_variants_is_malformed_and_named()
    {
        ProductContent content = Content(
            ("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.base"}],"tuning":{"id":"test.tuning"},"variants":[]}"""),
            ("worldrpg/packs/test.base/pack.json", """{"kind":"worldrpg.content-pack","id":"test.base","ruleset":"test","role":"test.content","dependencies":[],"payload":"payload/base.json"}"""),
            ("worldrpg/tuning/test/tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/values.json"}"""),
            ("payload/base.json", "base"), ("payload/values.json", "tuning"));

        // Even an empty list: the field has no meaning, and ignoring it would hide what the author expected.
        GameCompositionResolution resolution = GameCompositionResolver.Resolve(content, new GameBundleId("test.bundle"));

        Assert.False(resolution.IsResolved);
        Assert.Contains(resolution.Diagnostics, diagnostic => diagnostic.Message.Contains("worldrpg/bundles/test.bundle.json", StringComparison.Ordinal)
            && diagnostic.Message.Contains("Game bundle 'test.bundle' selects rules variants", StringComparison.Ordinal));
        Assert.Empty(GameCompositionResolver.ListBundles(content));
    }

    private static ProductContent Content(params (string Path, string Value)[] files) => new(files.Select(file => new ProductContentFile(Encoding.UTF8.GetBytes(file.Path), Encoding.UTF8.GetBytes(file.Value))).ToArray());
}
