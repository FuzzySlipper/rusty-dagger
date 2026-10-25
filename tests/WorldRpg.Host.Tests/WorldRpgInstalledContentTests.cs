using System.Reflection;
using System.Text;
using Rusty.Engine;
using Rusty.Engine.Testing;
using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Host.Tests;

/// <summary>
/// The containers installed beside the product: the Host opens the ones its ruleset reads before composing, and
/// when one is absent, damaged or stale (or a game cannot be composed without it) it runs the ruleset's unavailable
/// screen with each problem and its next step instead of a game.
/// </summary>
public sealed class WorldRpgInstalledContentTests : IDisposable
{
    private const string CurrentRevision = "rev-current";
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "host-installed-" + Guid.NewGuid().ToString("N"))).FullName;
    private readonly EngineTestHost _host = EngineTestHost.Create(new EngineTestHostOptions());

    public void Dispose()
    {
        _host.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private string Data => Path.Combine(_root, "data.rpak");
    private string Music => Path.Combine(_root, "music.rpak");

    [Fact]
    public void A_current_container_composes_its_described_pack_and_an_absent_optional_one_is_left_out()
    {
        Pack(Data, CurrentRevision, describesPack: true);
        InstalledRuleset ruleset = new(Data, Music);

        _host.Call(engine =>
        {
            using WorldRpgProduct product = Product(engine, ruleset);

            ResolvedGameComposition composition = Assert.Single(ruleset.Composed);
            Assert.Equal(["test.imported"], composition.ContentPacks.Select(pack => pack.Id.Value));
            Assert.Equal("imported", Encoding.UTF8.GetString(composition.ContentPacks[0].Payload.Span));
            Assert.Equal([Data], composition.Installed.Select(installed => installed.Name));
            Assert.Null(ruleset.Unavailable);
        });
    }

    [Fact]
    public void An_absent_required_container_shows_that_it_is_not_imported_and_the_step_that_imports_it()
    {
        InstalledRuleset ruleset = new(Data, Music);

        _host.Call(engine =>
        {
            using WorldRpgProduct product = Product(engine, ruleset);

            string problem = Assert.Single(ruleset.Unavailable!.Problems);
            Assert.StartsWith("The test data is not imported", problem, StringComparison.Ordinal);
            Assert.Contains(Data, problem, StringComparison.Ordinal);
            Assert.EndsWith(InstalledRuleset.ReleaseStep, problem, StringComparison.Ordinal);
            Assert.Empty(ruleset.Composed);
        });
    }

    [Fact]
    public void A_damaged_container_is_named_as_unreadable_with_the_step_that_replaces_it()
    {
        System.IO.File.WriteAllBytes(Data, Encoding.UTF8.GetBytes("not a content container"));
        InstalledRuleset ruleset = new(Data, Music);

        _host.Call(engine =>
        {
            using WorldRpgProduct product = Product(engine, ruleset);

            string problem = Assert.Single(ruleset.Unavailable!.Problems);
            Assert.StartsWith("The test data (", problem, StringComparison.Ordinal);
            Assert.Contains("cannot be read, so it is damaged or incomplete", problem, StringComparison.Ordinal);
            Assert.EndsWith(InstalledRuleset.ReleaseStep, problem, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_stale_container_blocks_play_outside_the_development_loop()
    {
        Pack(Data, "rev-older", describesPack: true);
        InstalledRuleset ruleset = new(Data, Music);

        _host.Call(engine =>
        {
            using WorldRpgProduct product = Product(engine, ruleset);

            // This test build is not staged by the development loop, so it refuses stale data as a release does.
            string problem = Assert.Single(ruleset.Unavailable!.Problems);
            Assert.StartsWith("data.rpak was produced by rev-older, not rev-current.", problem, StringComparison.Ordinal);
            Assert.EndsWith(InstalledRuleset.ReleaseStep, problem, StringComparison.Ordinal);
            Assert.Empty(ruleset.Composed);
        });
    }

    [Fact]
    public void A_current_container_that_lacks_a_pack_the_bundle_names_says_no_game_can_be_composed()
    {
        Pack(Data, CurrentRevision, describesPack: false);
        InstalledRuleset ruleset = new(Data, Music);

        _host.Call(engine =>
        {
            using WorldRpgProduct product = Product(engine, ruleset);

            string problem = Assert.Single(ruleset.Unavailable!.Problems);
            Assert.StartsWith("No game can be composed: Content pack 'test.imported' is missing.", problem, StringComparison.Ordinal);
            Assert.EndsWith(InstalledRuleset.ReleaseStep, problem, StringComparison.Ordinal);
        });
    }

    private void Pack(string container, string revision, bool describesPack)
    {
        string staged = Directory.CreateDirectory(Path.Combine(_root, "staged-" + Guid.NewGuid().ToString("N"))).FullName;
        string packs = describesPack
            ? """{"kind":"worldrpg.content-pack","id":"test.imported","ruleset":"test","role":"test.content","dependencies":[],"payload":"payload/imported.json"}"""
            : string.Empty;
        Write(staged, InstalledContent.DescriptionPath, $$"""{"kind":"worldrpg.data-container","importerRevision":"{{revision}}","contentPacks":[{{packs}}]}""");
        Write(staged, "payload/imported.json", "imported");
        _host.Call(engine => ProductContentBundle.PackContainer(engine.Content, staged, container, compress: false));
    }

    private static WorldRpgProduct Product(IEngineContext engine, InstalledRuleset ruleset) =>
        new(new ProductCreateContext(new PersistencelessEngine(engine).Context, new ProductContent(ContentFiles(), engine.Content), EmptyInput()),
            ruleset, new GameBundleId("test.bundle"));

    private static void Write(string root, string path, string text)
    {
        string file = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        System.IO.File.WriteAllText(file, text);
    }

    private static ProductContentFile[] ContentFiles() =>
    [
        File("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","id":"test.bundle","ruleset":"test","contentPacks":[{"id":"test.imported"}],"tuning":{"id":"test.tuning"}}"""),
        File("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","id":"test.tuning","ruleset":"test","payload":"payload/tuning.json"}"""),
        File("payload/tuning.json", "{}"),
    ];

    private static ProductContentFile File(string path, string value) => new(Encoding.UTF8.GetBytes(path), Encoding.UTF8.GetBytes(value));

    private static ProductInputConfiguration EmptyInput() => new(
        new InputBinding(1, 1, 1),
        new InputContext("test"u8.ToArray()),
        Array.Empty<ProductInputDescriptor>(), Array.Empty<ProductInputMapping>());

    /// <summary>A ruleset that reads one required and one optional installed container, and records what the Host made of them.</summary>
    private sealed class InstalledRuleset(string data, string music) : IGameRuleset, IInstalledContentRuleset
    {
        internal const string ReleaseStep = "Import the test data.";

        public RulesetId Id => new("test");

        internal List<ResolvedGameComposition> Composed { get; } = [];

        internal GameUnavailableContext? Unavailable { get; private set; }

        public IReadOnlyList<InstalledContentRequirement> InstalledContent { get; } =
        [
            new(data, "the test data", Required: true, DevelopmentStep: "Run the test import.", ReleaseStep: ReleaseStep),
            new(music, "the test music", Required: false, DevelopmentStep: "Run the test import with music.", ReleaseStep: "Import the test music."),
        ];

        public string? CheckInstalledContent(InstalledContent content) =>
            content.Description is { } description && description.Revision != CurrentRevision
                ? $"{Path.GetFileName(content.Name)} was produced by {description.Revision}, not {CurrentRevision}."
                : null;

        public IGameSession CreateUnavailableSession(GameUnavailableContext context)
        {
            Unavailable = context;
            return new QuietSession();
        }

        public IGameSession CreateSession(GameSessionContext context)
        {
            Composed.Add(context.Composition);
            return new QuietSession();
        }
    }

    private sealed class QuietSession : IGameSession
    {
        public void PublishInitial()
        {
        }

        public ProductUpdateResult Update(ProductUpdate update) => ProductUpdateResult.None;

        public void Dispose()
        {
        }
    }

    /// <summary>The test host's services, with persistence refused as a product without a persistence root refuses it.</summary>
    private class PersistencelessEngine : DispatchProxy
    {
        private IEngineContext _engine = null!;

        internal PersistencelessEngine(IEngineContext engine) : this() => _engine = engine;

        public PersistencelessEngine()
        {
        }

        internal IEngineContext Context
        {
            get
            {
                IEngineContext proxy = Create<IEngineContext, PersistencelessEngine>();
                ((PersistencelessEngine)(object)proxy)._engine = _engine;
                return proxy;
            }
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_Persistence" => throw new NotSupportedException("No persistence in these tests."),
            _ => targetMethod!.Invoke(_engine, args),
        };
    }
}
