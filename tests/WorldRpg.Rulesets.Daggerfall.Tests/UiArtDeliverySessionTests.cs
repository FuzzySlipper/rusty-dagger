using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Published UI art the projection carries to the DOM.</summary>
public sealed class UiArtDeliverySessionTests
{
    // The facts around this one pin the projection: what it carries, how often, how it is chunked and
    // what it refuses. The DOM half - adopting a block, retrying a revision it lacks, repainting panels
    // when art arrives late, showing the death screen - has no behaviour harness in this repository, and
    // the death screen's on-screen rendering additionally needs a live damage path that does not exist
    // yet (task #8262). Those are checked by a real host run, not here.
    [Fact]
    public void The_projection_carries_the_published_ui_art_the_dom_draws()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        List<string> releases = [];
        ContentFake content = new(releases);
        DaggerfallSiteProfile inputs = ReadInputs(root);
        PopulateContent(content, inputs);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        session.PublishInitial();
        Dictionary<string, object?> hud = Assert.IsType<Dictionary<string, object?>>(engine.Published());
        Dictionary<string, object?> art = Assert.IsType<Dictionary<string, object?>>(hud["uiArt"]);
        Assert.Equal(hud["uiArtRevision"], art["revision"]);

        Dictionary<string, object?> images = Assert.IsType<object?[]>(art["images"])
            .Cast<Dictionary<string, object?>>()
            .ToDictionary(image => Assert.IsType<string>(image["id"]), image => (object?)Assert.IsType<string>(image["image"]), StringComparer.Ordinal);
        // One artifact per identity the DOM draws: the mode screen, the chrome, the three authored
        // inventory skins, every admitted item icon, the adult, faction and child portraits the
        // quest escort HUD selects, and the head and body of each racial form (vampire, were-creature)
        // the HUD draws while it is held. Other paper-doll backgrounds and bodies stay with the sheet.
        string[] heads = [.. definitions.CharacterPresentation.Races.Values
            .SelectMany(race => race.Layers.Where(layer => layer.Kind == DaggerfallCharacterLayerKind.Head))
            .Select(layer => layer.MediaId).Distinct(StringComparer.Ordinal)];
        string[] factionFaces = [.. definitions.CharacterPresentation.FactionFaces.Select(face => face.MediaId)];
        string[] childFaces = [.. definitions.CharacterPresentation.ChildFaces.Select(face => face.MediaId)];
        Assert.Equal(160, heads.Length);
        Assert.Equal(61, factionFaces.Length);
        Assert.Equal(4, childFaces.Length);
        string[] racialForms = [.. definitions.CharacterPresentation.RacialForms.Values
            .SelectMany(form => new[] { form.HeadMediaId, form.BodyMediaId }).Distinct(StringComparer.Ordinal)];
        string[] portraits = [.. heads, .. factionFaces, .. childFaces, .. racialForms];
        Assert.Equal(10 + inputs.ClassicPresentation.InventoryIcons.Count + portraits.Length, images.Count);
        Assert.All(images.Values, image => Assert.StartsWith("data:image/png;base64,", Assert.IsType<string>(image), StringComparison.Ordinal));

        // The bytes are the published artifacts, read from admitted content by their content name.
        Assert.Equal(
            $"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/media/ui/screen-death.png")))}",
            images["screen.death"]);
        // Every supplied screen arrives byte for byte from the artifact the inventory names, so a screen
        // published from the wrong file - or from a re-encode that lost its palette - fails here rather
        // than only differing in prefix.
        foreach ((string id, string file) in new[]
        {
            ("screen.character-generation", "screen-character-generation"),
            ("screen.pick.02", "screen-pick-02"),
            ("screen.prison", "screen-prison"),
            ("screen.start-menu", "screen-start-menu"),
            ("screen.title", "screen-title"),
        })
        {
            Assert.Equal(
                $"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "content", "worldrpg", "media", "ui", $"{file}.png")))}",
                images[id]);
        }
        // The set is exactly what this presentation draws - the screens it shows plus the icons the
        // pack names - so an artifact silently added to or dropped from the payload fails here.
        string[] expected =
        [
            "screen.death",
            "window.character-sheet.chrome",
            // The supplied screens a mode is shown with, each published in the palette its own file
            // carries.
            "screen.character-generation",
            "screen.pick.02",
            "screen.prison",
            "screen.start-menu",
            "screen.title",
            // The panel frames are published art now, not files staged beside the UI bundle.
            "inventory.skin.grid-slot-slate.v1",
            "inventory.skin.panel-slate.v1",
            "inventory.skin.titlebar-slate.v1",
            .. inputs.ClassicPresentation.InventoryIcons.Values,
            .. portraits,
        ];
        Assert.Equal([.. expected.Order(StringComparer.Ordinal)], [.. images.Keys.Order(StringComparer.Ordinal)]);

        // One icon byte for byte, from the path the inventory states, so a wrong file under a right
        // identity cannot pass on a prefix check.
        using JsonDocument inventory = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content", DaggerfallUiArt.InventoryPath)));
        string iconPath = inventory.RootElement.GetProperty("artifacts").EnumerateArray()
            .Where(artifact => artifact.TryGetProperty("mediaId", out JsonElement mediaId) && mediaId.GetString() == "inventory.icon.iron-dagger")
            .Single()
            .GetProperty("path").GetString()!;
        Assert.Equal(
            $"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "content", iconPath)))}",
            images["inventory.icon.iron-dagger"]);

        // Every escort portrait arrives from its character inventory artifact, with the same bytes
        // as the published canvas rather than a name or a data-URL prefix alone.
        using JsonDocument characters = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content", DaggerfallUiArt.CharacterInventoryPath)));
        Dictionary<string, string> characterPaths = characters.RootElement.GetProperty("artifacts").EnumerateArray()
            .ToDictionary(artifact => artifact.GetProperty("mediaId").GetString()!, artifact => artifact.GetProperty("path").GetString()!);
        Assert.All(portraits, id => Assert.Equal(
            $"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "content", characterPaths[id])))}", images[id]));
    }

    /// <summary>
    /// The DOM draws art only by the identities the session publishes. Every media identity the DOM
    /// writes as a literal - a mode screen, the sheet chrome, a panel skin - is one the projection's art
    /// set carries, and the DOM names no media file of its own: art arrives as published bytes or not
    /// at all, so a renamed or dropped artifact cannot leave a panel pointing at nothing.
    /// </summary>
    [Fact]
    public void Every_art_identity_the_dom_names_is_one_the_projection_publishes()
    {
        string root = TestData.RepositoryRoot;
        List<string> releases = [];
        ContentFake content = new(releases);
        DaggerfallSiteProfile inputs = ReadInputs(root);
        PopulateContent(content, inputs);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));
        session.PublishInitial();
        Dictionary<string, object?> hud = Assert.IsType<Dictionary<string, object?>>(engine.Published());
        Dictionary<string, object?> art = Assert.IsType<Dictionary<string, object?>>(hud["uiArt"]);
        HashSet<string> published = [.. Assert.IsType<object?[]>(art["images"]).Cast<Dictionary<string, object?>>()
            .Select(image => Assert.IsType<string>(image["id"]))];
        // A media identity is dotted, and its first segment is a family the published set uses; the
        // DOM's own dotted names (the payload contracts, the "dagger.ui" intent) belong to no family.
        HashSet<string> families = [.. published.Select(id => id.Split('.')[0])];

        string[] sources = [.. Directory.GetFiles(Path.Combine(root, "src/ui"), "*.ts").Where(path => !path.EndsWith(".d.ts", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        Assert.NotEmpty(sources);
        List<string> named = [];
        List<string> unpublished = [];
        foreach (string path in sources)
        {
            foreach (Match literal in Regex.Matches(File.ReadAllText(path), @"['""`](?<id>[a-z][a-z0-9-]*(?:\.[a-z0-9-]+)+)['""`]"))
            {
                string id = literal.Groups["id"].Value;
                if (!families.Contains(id.Split('.')[0])) continue;
                named.Add(id);
                if (!published.Contains(id)) unpublished.Add($"{Path.GetFileName(path)}: {id}");
            }
        }
        Assert.Empty(unpublished);
        // The check sees the identities the DOM is known to draw by name, so a pattern that stopped
        // matching them cannot pass on an empty set.
        Assert.Contains("screen.prison", named);
        Assert.Contains("window.character-sheet.chrome", named);
        Assert.Contains("inventory.skin.panel-slate.v1", named);

        // No media file is named by the DOM: images are data the projection carries, never a path the
        // client stages beside itself.
        string[] files = [.. Directory.GetFiles(Path.Combine(root, "src/ui"), "*.*")
            .Where(path => path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".css", StringComparison.Ordinal))
            .Where(path => !path.EndsWith(".d.ts", StringComparison.Ordinal))
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"[\w./-]+\.(?:png|jpe?g|gif|webp|bmp|svg|wav|ogg|mp3|mp4|webm)\b", RegexOptions.IgnoreCase)
                .Select(match => $"{Path.GetFileName(path)}: {match.Value}"))];
        Assert.Empty(files);
    }

    [Fact]
    public void Character_steps_use_the_published_screens_and_semantic_actions_over_one_draft()
    {
        var inputs = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        var spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: RandomMinimum.Create());
        using var session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));
        session.ApplyProductMode(WorldRpg.Kit.ProductMode.Title);
        ulong step = 0;
        Send("{\"action\":\"character-begin\"}");
        Assert.Equal("character-pick", Creation()["mode"]);
        var original = session.State.Character.Identity;
        Assert.False(session.RequestsBegin([Ui("{\"action\":\"begin\"}")]));
        Questions();
        Assert.Equal("character-generation", Creation()["mode"]);
        var hud = Assert.IsType<Dictionary<string, object?>>(engine.Published());
        var art = Assert.IsType<Dictionary<string, object?>>(hud["uiArt"]);
        var images = Assert.IsType<object?[]>(art["images"]).Cast<Dictionary<string, object?>>()
            .ToDictionary(image => (string)image["id"]!, image => (string)image["image"]!);
        foreach (var (id, file) in new[] { ("screen.character-generation", "screen-character-generation"), ("screen.pick.02", "screen-pick-02") })
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/media/ui", file + ".png"))), images[id]);
        Assert.Equal(new[] { "screen.pick.02" }, Assert.IsType<object?[]>(hud["pickScreens"]).Cast<string>());
        for (int index = 0; index < 10; index++)
        {
            var quiz = session.State.Character.ReadClassQuiz()!;
            var wire = Assert.IsType<Dictionary<string, object?>>(Creation()["classQuiz"]);
            Assert.Equal((double)index, wire["answered"]);
            Assert.Equal(quiz.Question.Text, Assert.IsType<Dictionary<string, object?>>(wire["question"])["text"]);
            Send($"{{\"action\":\"character-class-answer\",\"question\":{quiz.Question.Number},\"answer\":0}}");
        }
        Assert.Equal("character-pick", Creation()["mode"]);
        Assert.Null(Creation()["classQuiz"]);
        Assert.NotNull(session.State.Character.Pending!.Background);
        Assert.Equal(original, session.State.Character.Identity);
        Questions();
        Send("{\"action\":\"character-class-back\"}");
        Assert.Equal("character-pick", Creation()["mode"]);
        Send("{\"action\":\"character-cancel\"}");
        Assert.Null(Creation()["mode"]);
        Assert.True(session.RequestsBegin([Ui("{\"action\":\"begin\"}")]));
        Assert.Equal(0, spatial.StepCalls);

        Dictionary<string, object?> Creation()
        {
            session.PublishInitial();
            var hud = Assert.IsType<Dictionary<string, object?>>(engine.Published());
            return Assert.IsType<Dictionary<string, object?>>(Assert.IsType<Dictionary<string, object?>>(hud["character"])["creation"]);
        }
        void Questions()
        {
            var draft = session.State.Character.Pending!;
            Send(JsonSerializer.Serialize(new { action = "character-class-questions", name = "Aubk-i", race = "khajiit", gender = "female", faceIndex = 0, reflexes = 2, career = draft.CareerId }));
            Assert.Equal("Aubk-i", session.State.Character.Pending!.Name);
            Assert.Equal("khajiit", session.State.Character.Pending.RaceId);
        }
        void Send(string json) => session.Update(new ProductUpdate(OuterUpdate(++step), [Ui(json)]));
        static ProductInputEvent Ui(string json) => Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload, PayloadContract = "dagger.ui.action.v1"u8.ToArray(), PayloadData = Encoding.UTF8.GetBytes(json),
        };
    }

    [Fact]
    public void Every_admitted_pick_artifact_is_delivered_as_a_set()
    {
        var inputs = ReadInputs(TestData.RepositoryRoot);
        ContentFake content = new([]);
        PopulateContent(content, inputs);
        var inventory = JsonNode.Parse(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content", DaggerfallUiArt.InventoryPath)))!;
        var artifacts = inventory["artifacts"]!.AsArray();
        var pick = artifacts.Select(node => node!.AsObject()).Single(node => (string?)node["mediaId"] == "screen.pick.02");
        // The operator corpus has one pick. A second admitted part exercises set consumption with
        // real artifact bytes; it does not rename the corpus's separate PICK03 start-menu screen.
        var second = pick.DeepClone().AsObject();
        second["mediaId"] = "screen.pick.background";
        artifacts.Add(second);
        content.Add(DaggerfallUiArt.InventoryPath, Encoding.UTF8.GetBytes(inventory.ToJsonString()));
        var art = DaggerfallUiArt.Read(content, [.. inputs.ClassicPresentation.InventoryIcons.Values]);
        Assert.Equal(new[] { "screen.pick.02", "screen.pick.background" }, art.PickScreens);
        Assert.Equal(art.Images.Single(image => image.Id == "screen.pick.02").Image,
            art.Images.Single(image => image.Id == "screen.pick.background").Image);
        Assert.DoesNotContain("screen.start-menu", art.PickScreens);
    }

    [Fact]
    public void Admitted_art_uses_its_current_bytes_without_runtime_digest_or_revision_hashing()
    {
        string root = TestData.RepositoryRoot;
        List<string> releases = [];
        DaggerfallSiteProfile inputs = ReadInputs(root);
        string[] icons = [.. inputs.ClassicPresentation.InventoryIcons.Values];

        ContentFake original = new(releases);
        PopulateContent(original, inputs);
        DaggerfallUiArt baseline = DaggerfallUiArt.Read(original, icons);

        JsonNode inventory = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "content", DaggerfallUiArt.InventoryPath)))!;
        JsonObject screen = inventory["artifacts"]!.AsArray().Select(node => node!.AsObject())
            .Single(artifact => (string?)artifact["mediaId"] == "screen.death");
        string path = (string)screen["path"]!;
        byte[] edited = File.ReadAllBytes(Path.Combine(root, "content", path));
        edited[^1] ^= 0xFF;

        // The current admitted bytes are what the DOM receives. Runtime does not derive a cache key
        // by hashing every image merely to detect that a trusted local publication changed.
        ContentFake republished = new(releases);
        PopulateContent(republished, inputs);
        republished.Add(path, edited);
        screen["byteLength"] = edited.Length;
        screen["sha256"] = Convert.ToHexStringLower(SHA256.HashData(edited));
        republished.Add(DaggerfallUiArt.InventoryPath, Encoding.UTF8.GetBytes(inventory.ToJsonString()));
        DaggerfallUiArt changedArt = DaggerfallUiArt.Read(republished, icons);
        Assert.NotEqual(baseline.Revision, changedArt.Revision);
        Assert.NotEqual(
            baseline.Images.Single(image => image.Id == "screen.death").Image,
            changedArt.Images.Single(image => image.Id == "screen.death").Image);

        // Runtime presentation trusts the admitted resource by name. The inventory's digest is build
        // provenance, not an every-session integrity protocol, so an edited admitted byte is served.
        ContentFake stale = new(releases);
        PopulateContent(stale, inputs);
        stale.Add(path, edited);
        Assert.NotEqual(
            baseline.Images.Single(image => image.Id == "screen.death").Image,
            DaggerfallUiArt.Read(stale, icons).Images.Single(image => image.Id == "screen.death").Image);
    }

    [Fact]
    public void Art_larger_than_one_admitted_read_is_joined_from_bounded_chunks()
    {
        List<string> releases = [];
        ContentFake content = new(releases);
        // An artifact past the Engine's per-read bound: the reader has to ask more than once and the
        // bytes it publishes have to be the whole artifact, not the first slice.
        byte[] large = new byte[(1024 * 1024) + 7];
        for (int index = 0; index < large.Length; index++) large[index] = (byte)(index % 251);
        byte[] chrome = [1, 2, 3, 4];
        const string Screen = "worldrpg/media/ui/screen-death.png";
        const string Chrome = "worldrpg/media/ui/window-character-sheet-chrome.png";
        byte[] skin = [9, 8, 7];
        byte[] titlebar = [6, 5, 4];
        byte[] slot = [3, 2, 1];
        const string Skin = "worldrpg/media/ui/authored/inventory-skin-panel-slate-v1.png";
        const string Titlebar = "worldrpg/media/ui/authored/inventory-titlebar-slate-v1.png";
        const string Slot = "worldrpg/media/ui/authored/inventory-grid-slot-slate-v1.png";
        content.Add(Screen, large);
        content.Add(Chrome, chrome);
        content.Add(Skin, skin);
        content.Add(Titlebar, titlebar);
        content.Add(Slot, slot);
        // The supplied screens are served from the published group rather than fabricated: this fixture
        // states a minimal inventory, so every identity the art set carries has to be present here, and
        // reading the real artifacts keeps its entries honest about their bytes and digests.
        string[] supplied = ["screen-character-generation", "screen-pick-02", "screen-prison", "screen-start-menu", "screen-title"];
        Dictionary<string, (string Path, byte[] Bytes)> published = [];
        foreach (string name in supplied)
        {
            string path = $"worldrpg/media/ui/{name}.png";
            byte[] bytes = File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content", "worldrpg", "media", "ui", $"{name}.png"));
            published[name] = (path, bytes);
            content.Add(path, bytes);
        }
        content.Add(DaggerfallUiArt.InventoryPath, Encoding.UTF8.GetBytes(new JsonObject
        {
            ["generator"] = "fixture",
            ["artifacts"] = new JsonArray(
                Entry("screen.death", Screen, large),
                Entry("window.character-sheet.chrome", Chrome, chrome),
                Entry("inventory.skin.panel-slate.v1", Skin, skin),
                Entry("inventory.skin.titlebar-slate.v1", Titlebar, titlebar),
                Entry("inventory.skin.grid-slot-slate.v1", Slot, slot),
                Entry("screen.character-generation", published["screen-character-generation"].Path, published["screen-character-generation"].Bytes),
                Entry("screen.pick.02", published["screen-pick-02"].Path, published["screen-pick-02"].Bytes),
                Entry("screen.prison", published["screen-prison"].Path, published["screen-prison"].Bytes),
                Entry("screen.start-menu", published["screen-start-menu"].Path, published["screen-start-menu"].Bytes),
                Entry("screen.title", published["screen-title"].Path, published["screen-title"].Bytes)),
        }.ToJsonString()));

        DaggerfallUiArt art = DaggerfallUiArt.Read(content, []);
        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(large)}", art.Images.Single(image => image.Id == "screen.death").Image);
        Assert.Equal(2, content.Reads(Screen));
        Assert.Equal(1, content.Reads(Chrome));
        Assert.Equal(1, content.Reads(Skin));

        static JsonObject Entry(string mediaId, string path, byte[] bytes) => new()
        {
            ["path"] = path,
            ["byteLength"] = bytes.Length,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            ["mediaId"] = mediaId,
            ["slot"] = mediaId == "screen.pick.02" ? "pick" : null,
        };
    }

    [Fact]
    public void Art_the_dom_holds_travels_once_and_comes_back_when_the_dom_asks_for_it()
    {
        string root = TestData.RepositoryRoot;
        List<string> releases = [];
        ContentFake content = new(releases);
        DaggerfallSiteProfile inputs = ReadInputs(root);
        PopulateContent(content, inputs);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        session.PublishInitial();
        Dictionary<string, object?> first = Assert.IsType<Dictionary<string, object?>>(engine.Published());
        string revision = Assert.IsType<string>(first["uiArtRevision"]);
        Assert.Contains("uiArt", first.Keys);

        // The block does not ride every admitted update: it is worth hundreds of kilobytes.
        session.Update(new ProductUpdate(OuterUpdate(2), [Input(InputEventKind.DirectDigital)]));
        Dictionary<string, object?> second = Assert.IsType<Dictionary<string, object?>>(engine.Published());
        Assert.DoesNotContain("uiArt", second.Keys);
        Assert.Equal(revision, second["uiArtRevision"]);

        // A newly attached client needs the art even while the title screen holds gameplay.
        session.PublishInitial();
        Dictionary<string, object?> attached = Assert.IsType<Dictionary<string, object?>>(engine.Published());
        Assert.Contains("uiArt", attached.Keys);
        Assert.Equal(revision, attached["uiArtRevision"]);

        // A DOM that reloaded holds nothing and names the revision it is missing.
        ProductInputEvent ask = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes($"{{\"action\":\"art-request\",\"revision\":\"{revision}\"}}"),
        };
        int before = engine.PublishedHistory().Count;
        session.Update(new ProductUpdate(OuterUpdate(3), [ask]));
        // An admitted update publishes per step and once more at its end, so the block is answered by
        // whichever snapshot follows the request rather than necessarily by the update's last one.
        Assert.Contains(engine.PublishedHistory().Skip(before), snapshot =>
            snapshot is Dictionary<string, object?> fields
            && fields.ContainsKey("uiArt")
            && string.Equals(Assert.IsType<string>(fields["uiArtRevision"]), revision, StringComparison.Ordinal));
    }

    [Fact]
    public void An_artifact_the_published_inventory_does_not_describe_refuses_the_session_by_name()
    {
        string root = TestData.RepositoryRoot;
        List<string> releases = [];
        ContentFake content = new(releases);
        DaggerfallSiteProfile inputs = ReadInputs(root);
        PopulateContent(content, inputs);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));

        // A group whose inventory no longer describes the death screen must refuse to start rather
        // than run with a blank screen, and the refusal has to name what was looked for and where.
        JsonNode inventory = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "content", DaggerfallUiArt.InventoryPath)))!;
        JsonArray artifacts = inventory["artifacts"]!.AsArray();
        for (int index = artifacts.Count - 1; index >= 0; index--)
        {
            if (string.Equals(artifacts[index]!["mediaId"]?.GetValue<string>(), "screen.death", StringComparison.Ordinal)) artifacts.RemoveAt(index);
        }

        content.Add(DaggerfallUiArt.InventoryPath, Encoding.UTF8.GetBytes(inventory.ToJsonString()));
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults)));
        Assert.Contains("screen.death", failure.Message, StringComparison.Ordinal);
        Assert.Contains(DaggerfallUiArt.InventoryPath, failure.Message, StringComparison.Ordinal);
    }
}
