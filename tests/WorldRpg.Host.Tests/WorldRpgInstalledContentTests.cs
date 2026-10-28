using System.Reflection;
using System.Text;
using System.Text.Json;
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

    [Theory]
    [InlineData(InstalledContent.DescriptionPath)]
    [InlineData("payload/imported.json")]
    [InlineData(InstalledRuleset.SessionReadPath)]
    public void A_container_entry_found_damaged_after_it_opens_is_named_as_unreadable(string damaged)
    {
        // The Engine checks the header and inventory when a container opens and decompresses an entry only when
        // it is read, so a damaged compressed entry is found by the description check, by composition reading
        // the selected pack, or by the session reading its data.
        Pack(Data, CurrentRevision, describesPack: true, compress: true);
        DamageEntry(Data, damaged);
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

    [Fact]
    public void An_import_on_the_unavailable_screen_opens_the_game_it_installed_without_a_restart()
    {
        InstalledRuleset ruleset = new(Data, Music);

        _host.Call(engine =>
        {
            using WorldRpgProduct product = Product(engine, ruleset);
            product.Start();

            // The required container's import is offered; the optional one's is not.
            Assert.Same(InstalledRuleset.DataImport, ruleset.Unavailable!.Importable!.Import);
            Assert.Empty(ruleset.Composed);
            ImportingSession importing = Assert.Single(ruleset.Importing);

            // The session's import installs the container, as the declared tool would, and asks the host to reopen.
            importing.Install = () => Pack(Data, CurrentRevision, describesPack: true, within: engine);
            product.Update(new ProductUpdate(new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d), []));

            Assert.True(importing.Disposed);
            ResolvedGameComposition composition = Assert.Single(ruleset.Composed);
            Assert.Equal(["test.imported"], composition.ContentPacks.Select(pack => pack.Id.Value));
            Assert.Equal(ProductMode.Title, product.Mode);
        });
    }

    private void Pack(string container, string revision, bool describesPack, bool compress = false, IEngineContext? within = null)
    {
        string staged = Directory.CreateDirectory(Path.Combine(_root, "staged-" + Guid.NewGuid().ToString("N"))).FullName;
        string packs = describesPack
            ? """{"kind":"worldrpg.content-pack","id":"test.imported","ruleset":"test","role":"test.content","dependencies":[],"payload":"payload/imported.json"}"""
            : string.Empty;
        // Padding lets every entry compress, so that each is stored as zstd when the pack is compressed.
        string padding = compress ? new(' ', 4096) : string.Empty;
        Write(staged, InstalledContent.DescriptionPath, $$"""{"kind":"worldrpg.data-container","importerRevision":"{{revision}}","contentPacks":[{{packs}}]}{{padding}}""");
        Write(staged, "payload/imported.json", "imported" + padding);
        Write(staged, InstalledRuleset.SessionReadPath, "detail" + padding);
        // Inside a product call the container is packed through that call's Engine, as a tool's would be beside it.
        if (within is not null) ProductContentBundle.PackContainer(within.Content, staged, container, compress);
        else _host.Call(engine => ProductContentBundle.PackContainer(engine.Content, staged, container, compress));
    }

    /// <summary>
    /// Breaks one compressed entry's zstd frame while the container's header and inventory stay valid: the
    /// header's first two fields after the magic are the inventory's offset and length.
    /// </summary>
    private static void DamageEntry(string container, string path)
    {
        byte[] bytes = System.IO.File.ReadAllBytes(container);
        int offset = checked((int)BitConverter.ToUInt64(bytes, 8));
        int length = checked((int)BitConverter.ToUInt64(bytes, 16));
        using JsonDocument inventory = JsonDocument.Parse(bytes.AsMemory(offset, length));
        JsonElement entry = inventory.RootElement.GetProperty("entries").EnumerateArray().Single(candidate => candidate.GetProperty("path").GetString() == path);
        Assert.True(entry.TryGetProperty("zstdLength", out _), $"'{path}' must be stored compressed for a damaged frame to be found on read.");
        "XXXX"u8.CopyTo(bytes.AsSpan(checked((int)entry.GetProperty("offset").GetUInt64())));
        System.IO.File.WriteAllBytes(container, bytes);
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

        internal static readonly InstalledContentImport DataImport = new("TestImport", "Choose the test files", (folder, install) => [folder, install]);

        internal List<ImportingSession> Importing { get; } = [];

        /// <summary>The entry the session reads from the required container as it is created.</summary>
        internal const string SessionReadPath = "payload/detail.json";

        public RulesetId Id => new("test");

        internal List<ResolvedGameComposition> Composed { get; } = [];

        internal GameUnavailableContext? Unavailable { get; private set; }

        public IReadOnlyList<InstalledContentRequirement> InstalledContent { get; } =
        [
            new(data, "the test data", Required: true, DevelopmentStep: "Run the test import.", ReleaseStep: ReleaseStep, Import: DataImport),
            new(music, "the test music", Required: false, DevelopmentStep: "Run the test import with music.", ReleaseStep: "Import the test music."),
        ];

        public string? CheckInstalledContent(InstalledContent content) =>
            content.Description is { } description && description.Revision != CurrentRevision
                ? $"{Path.GetFileName(content.Name)} was produced by {description.Revision}, not {CurrentRevision}."
                : null;

        public IGameSession CreateUnavailableSession(GameUnavailableContext context)
        {
            Unavailable = context;
            ImportingSession session = new();
            Importing.Add(session);
            return session;
        }

        public IGameSession CreateSession(GameSessionContext context)
        {
            Composed.Add(context.Composition);
            foreach (InstalledContent installed in context.Composition.Installed)
                if (installed.Contains(SessionReadPath)) installed.Container.ReadBytes(SessionReadPath);
            return new QuietSession();
        }
    }

    /// <summary>An unavailable session whose import, once given one, installs the container in its next update.</summary>
    private sealed class ImportingSession : IInstalledContentImportSession
    {
        internal Action? Install { get; set; }

        internal bool Disposed { get; private set; }

        private bool _installed;

        public void PublishInitial()
        {
        }

        public ProductUpdateResult Update(ProductUpdate update)
        {
            if (Install is { } install) { install(); Install = null; _installed = true; }
            return ProductUpdateResult.None;
        }

        public bool TakeReopenRequest()
        {
            bool installed = _installed;
            _installed = false;
            return installed;
        }

        public void Dispose() => Disposed = true;
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
