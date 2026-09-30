using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed partial class NormalizedRuntimeSeamTests
{
    private const string SiteRole = "daggerfall.site";

    /// <summary>Every game bundle committed under the content root, by id.</summary>
    public static TheoryData<string> CommittedBundles()
    {
        TheoryData<string> bundles = [];
        foreach (string path in Directory.GetFiles(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/bundles"), "*.bundle.json").Order(StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            bundles.Add(document.RootElement.GetProperty("id").GetString()!);
        }
        return bundles;
    }

    /// <summary>
    /// A committed bundle is only real if the ruleset can start a game from it: this resolves each one,
    /// admits every pack it selects through the real readers, creates the session through the compiled
    /// ruleset and takes one admitted update. The session starts at the first site pack the bundle selects.
    /// </summary>
    [Theory]
    [MemberData(nameof(CommittedBundles))]
    public void Every_committed_bundle_creates_a_session_through_the_real_ruleset(string bundleId)
    {
        string root = TestData.RepositoryRoot;
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId(bundleId)).RequireComposition();
        ContentPack firstSite = composition.ContentPacks.First(pack => pack.Role.Value == SiteRole);
        DaggerfallSiteProfile start = DaggerfallSiteContent.Read(FullContent(root), firstSite.Payload, TestPayload.Definitions);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, start);
        SpatialFake spatial = SpatialFake.Create(start.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        CapturingDaggerfallRuleset ruleset = new(videosEnabled: false);

        using (WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId(bundleId)))
        {
            product.Start();
            product.Begin();
            Assert.Equal(ProductMode.Playing, product.Mode);
            ProductUpdateFacts facts = new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d);
            Assert.Equal(ProductUpdateResult.None, product.Update(new ProductUpdate(facts, ReadOnlySpan<ProductInputEvent>.Empty)));
            Assert.Equal(1, spatial.StepCalls);
            DaggerfallSession session = ruleset.RequireSession();
            Assert.Equal(start.Site, session.Site.Active);
            product.Shutdown();
        }
    }

    /// <summary>
    /// A site's audio bodies are opened through the bundle its payload names, and the SDK stages only the
    /// bundles the Host declares, so every site payload's declaration must match one Host item exactly.
    /// </summary>
    [Fact]
    public void Every_site_payload_names_an_audio_bundle_the_host_stages_at_its_publication_root()
    {
        string root = TestData.RepositoryRoot;
        Dictionary<string, string> declared = XDocument.Load(Path.Combine(root, "src/WorldRpg.Host/WorldRpg.Host.csproj"))
            .Descendants("RustyEngineContentBundle")
            .ToDictionary(item => item.Attribute("Include")!.Value, item => item.Attribute("Root")!.Value, StringComparer.Ordinal);
        (string Root, string Bundle)[] sites = [.. SiteAudioBundles(root)];

        Assert.NotEmpty(sites);
        Assert.All(sites, site =>
        {
            Assert.True(declared.TryGetValue(site.Bundle, out string? stagedRoot), $"The Host does not declare site audio bundle '{site.Bundle}'.");
            Assert.Equal(site.Root, stagedRoot);
        });
    }

    [Fact]
    public void Admission_refuses_a_pack_role_the_ruleset_does_not_interpret()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => CreateWithPackRole("daggerfall.quests.mages", "daggerfall.unknown"));

        Assert.Contains("declares role 'daggerfall.unknown'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Admission_requires_exactly_one_base_pack()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => CreateWithPackRole("daggerfall.base", "daggerfall.blocks"));

        Assert.Contains("selects 0 'daggerfall.base' content packs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_corpus_id_is_admitted_from_what_its_payload_states()
    {
        string root = TestData.RepositoryRoot;
        JsonObject payload = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.quests.mages.json")))!.AsObject();
        payload["id"] = "a-corpus-no-code-names";

        IReadOnlyList<DaggerfallClassicQuestCorpusReceipt> receipts = DaggerfallClassicQuestCorpusContent.Read(
            new ProductContent(Array.Empty<ProductContentFile>()), Encoding.UTF8.GetBytes(payload.ToJsonString()), TestPayload.Definitions);

        Assert.NotEmpty(receipts);
        Assert.All(receipts, receipt => Assert.True(receipt.IsOrdinaryOffer));
    }

    /// <summary>Resolves the default bundle with one pack's declared role replaced, then creates a session.</summary>
    private static IGameSession CreateWithPackRole(string packId, string role)
    {
        string root = TestData.RepositoryRoot;
        ProductContent full = FullContent(root);
        string manifestPath = $"worldrpg/content-packs/{packId}.pack.json";
        JsonObject manifest = JsonNode.Parse(full.ReadBytes(manifestPath).Span)!.AsObject();
        manifest["role"] = role;
        ProductContentFile[] files = [.. full.Files.ToArray().Select(file => Encoding.UTF8.GetString(file.Path.Span) == manifestPath
            ? new ProductContentFile(file.Path.ToArray(), Encoding.UTF8.GetBytes(manifest.ToJsonString()))
            : file)];
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(new ProductContent(files), new GameBundleId("daggerfall.privateers-hold")).RequireComposition();
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        return new DaggerfallRuleset(videosEnabled: false).CreateSession(new GameSessionContext(engine.Context, composition));
    }
}
