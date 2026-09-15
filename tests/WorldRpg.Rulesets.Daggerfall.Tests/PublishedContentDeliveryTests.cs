using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published classic media as admitted content a caller reads by name.
/// </summary>
/// <remarks>
/// The delivery half of the UI-art work: artifacts are published into the same content tree the
/// product admits, so a consumer resolves one by its content-relative name instead of by a filesystem
/// convention or a hand-kept index. The inventory beside them is generated from the publication, and
/// this checks it against the bytes on disk rather than trusting either side alone.
/// </remarks>
public sealed class PublishedContentDeliveryTests
{
    [Fact]
    public void Reads_a_published_artifact_by_name_from_admitted_content()
    {
        ProductContent content = AdmittedContent();

        // The death screen is the artifact this work started from, and it is reachable by its
        // content-relative name with no filesystem convention in the caller.
        Assert.True(content.TryReadFile("worldrpg/media/ui/screen-death.png", out ProductContentFile screen));
        Assert.NotEmpty(screen.Bytes.ToArray());
        Assert.Equal("worldrpg/media/ui/screen-death.png", Encoding.UTF8.GetString(screen.Path.Span));

        // The images the product already drew are inside the admitted tree too, which is what makes the
        // bundled-path fallback unnecessary rather than merely unfashionable.
        Assert.True(content.TryReadFile("worldrpg/media/ui/hud-chrome-main.png", out ProductContentFile chrome));
        Assert.NotEmpty(chrome.Bytes.ToArray());

        // A name the content does not carry fails as a miss rather than resolving to something else.
        Assert.False(content.TryReadFile("worldrpg/media/ui/screen-no-such-artifact.png", out _));

        // The directory read sees the published group without being told its members: this is what the
        // generated inventory replaces a hand-maintained list with.
        Assert.Contains(content.ReadDirectory("worldrpg/media/ui"), file => Encoding.UTF8.GetString(file.Path.Span) == "worldrpg/media/ui/screen-death.png");
        Assert.Contains(content.ReadDirectory("worldrpg/media/audio"), file => Encoding.UTF8.GetString(file.Path.Span).StartsWith("worldrpg/media/audio/", StringComparison.Ordinal));
    }

    [Fact]
    public void The_generated_inventory_agrees_with_the_bytes_it_indexes()
    {
        ProductContent content = AdmittedContent();
        JsonElement inventory = JsonDocument.Parse(content.ReadBytes("worldrpg/media/classic-media-inventory.json").ToArray()).RootElement;
        Assert.Equal(1, inventory.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("daggerfall-import-tool classic-media", inventory.GetProperty("generator").GetString());

        // Every entry the inventory lists is present and hashes to what it says, and every published
        // artifact is listed: an index that drifts from its content is worse than none, because a
        // consumer trusts it.
        HashSet<string> listed = [];
        foreach (JsonElement artifact in inventory.GetProperty("artifacts").EnumerateArray())
        {
            string path = artifact.GetProperty("path").GetString()!;
            byte[] bytes = content.ReadBytes(path).ToArray();
            Assert.Equal(artifact.GetProperty("byteLength").GetInt64(), bytes.Length);
            Assert.Equal(artifact.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            listed.Add(path);
        }

        // Each content group has its own generated index, so the classic index accounts for the classic
        // group and the character index accounts for the character canvases. An artifact no index names
        // fails here, which is what makes a hand-written artifact or a dropped entry visible.
        HashSet<string> published = [.. GeneratedContentFiles(content)
            .Where(path => !path.EndsWith("classic-media-inventory.json", StringComparison.Ordinal) && !path.Contains("/character/", StringComparison.Ordinal))];
        Assert.Equal(published.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
        // The published group carries the seventy-four media artifacts and the sound catalog that
        // describes the whole archive, and the inventory indexes both because both are content.
        Assert.Contains("worldrpg/media/audio/classic-sound-catalog.json", listed);
        Assert.Equal(80, listed.Count);

        // The character canvases are admitted content in the same tree: they have their own generated
        // index beside them, and both indexes state the bytes they describe rather than trusting them.
        HashSet<string> characterListed = [];
        GeneratedIndex characters = Index("worldrpg/media/character/character-media-inventory.json");
        foreach (JsonElement artifact in characters.Artifacts)
        {
            // The index states the content-relative name under the same key the classic index uses, so
            // one reader can resolve either group rather than special-casing the key.
            string path = artifact.GetProperty("path").GetString()!;
            byte[] bytes = content.ReadBytes(path).ToArray();
            Assert.Equal(artifact.GetProperty("byteLength").GetInt64(), bytes.Length);
            Assert.Equal(artifact.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            characterListed.Add(path);
        }

        // The canvases this repository cannot publish are stated in that index too, so a consumer that
        // finds no artifact for a face or a story sprite can tell "not published" from "not readable".
        (string Family, string Kind, string Reason, string[] Files, string Anchor)[] unreachable =
        [
            .. characters.Unreadable.Select(family => (
                family.GetProperty("family").GetString()!,
                family.GetProperty("kind").GetString()!,
                family.GetProperty("reason").GetString()!,
                family.GetProperty("files").EnumerateArray().Select(file => file.GetString()!).ToArray(),
                family.GetProperty("donorAnchor").GetString()!)),
        ];
        Assert.Equal(["BSS", "FACE"], unreachable.Select(family => family.Family));
        Assert.Equal(["CMPA00I0.BSS", "CMPA01I0.BSS", "CMPA02I0.BSS"], unreachable[0].Files);
        Assert.Equal(["FACES.CIF"], unreachable[1].Files);
        Assert.Equal("Assets/Scripts/API/BssFile.cs", unreachable[0].Anchor);
        // The face grid has no donor reader to name: the donor reads the grid, this repository enumerates
        // its cells and cannot slice their pixels, which is a different gap from a missing reader.
        Assert.Empty(unreachable[1].Anchor);
        Assert.All(unreachable, family =>
        {
            Assert.False(string.IsNullOrWhiteSpace(family.Kind));
            // Both reasons are about the same loss: the file carries canvases whose pixels are not here,
            // rather than a canvas published at the wrong shape or with guessed colours.
            Assert.Contains("pixels", family.Reason, StringComparison.Ordinal);
        });

        HashSet<string> characterPublished = [.. GeneratedContentFiles(content)
            .Where(path => path.StartsWith("worldrpg/media/character/", StringComparison.Ordinal) && !path.EndsWith("character-media-inventory.json", StringComparison.Ordinal))];
        // Every artifact the index names is an admitted file, and the group carries no other: an
        // artifact written without an index entry, or an entry with no artifact, fails here.
        Assert.Equal(264, characterListed.Count);
        Assert.Equal(240, characters.Artifacts.Count(artifact => artifact.GetProperty("binding").GetString() == "admitted"));
        Assert.Equal(
            [.. characterPublished.Except(characterListed).Order(StringComparer.Ordinal)],
            [.. characterListed.Except(characterPublished).Order(StringComparer.Ordinal)]);

        // The families this repository cannot read at all are stated with their files and the donor
        // anchor, so a consumer that finds no artifact for one of them can tell "not published" from
        // "not readable" instead of guessing.
        (string Family, string Kind, string Reason, string[] Files, string Anchor)[] unreadable =
        [
            .. inventory.GetProperty("unreadableFamilies").EnumerateArray()
                .Select(family => (
                    family.GetProperty("family").GetString()!,
                    family.GetProperty("kind").GetString()!,
                    family.GetProperty("reason").GetString()!,
                    family.GetProperty("files").EnumerateArray().Select(file => file.GetString()!).ToArray(),
                    family.GetProperty("donorAnchor").GetString()!)),
        ];
        Assert.Equal([".CEL", ".BSS"], unreadable.Select(family => family.Family));
        Assert.Equal(["MAGE.CEL", "ROGUE.CEL", "WARRIOR.CEL"], unreadable.Single(family => family.Family == ".CEL").Files);
        Assert.Equal(["CMPA00I0.BSS", "CMPA01I0.BSS", "CMPA02I0.BSS"], unreadable.Single(family => family.Family == ".BSS").Files);
        Assert.All(unreadable, family => Assert.StartsWith("Assets/Scripts/API/", family.Anchor, StringComparison.Ordinal));
        // The reason says which half is missing: these containers are read here, so the gap is a missing
        // publisher rather than a missing decoder, and the kind names what the file actually carries.
        Assert.Equal(["class-question animation", "compass sprite bank"], unreadable.Select(family => family.Kind).Order(StringComparer.Ordinal));
        Assert.All(unreadable, family =>
        {
            Assert.Contains("no publisher", family.Reason, StringComparison.Ordinal);
            Assert.DoesNotContain("no decoder", family.Reason, StringComparison.Ordinal);
        });

        // The six screens drawn with the palette inside their own file state that conversion fact with
        // its donor anchor, so a consumer can tell why their colours are what they are - and nothing
        // else in the inventory claims a palette it does not carry.
        (string MediaId, string Source, int Scale, string Anchor)[] palettes =
        [
            .. inventory.GetProperty("artifacts").EnumerateArray()
                .Where(entry => entry.TryGetProperty("palette", out _))
                .Select(entry => (
                    entry.GetProperty("mediaId").GetString()!,
                    entry.GetProperty("palette").GetProperty("source").GetString()!,
                    entry.GetProperty("palette").GetProperty("channelScale").GetInt32(),
                    entry.GetProperty("palette").GetProperty("donorAnchor").GetString()!)),
        ];
        Assert.Equal(
            ["screen.character-generation", "screen.death", "screen.pick.02", "screen.prison", "screen.start-menu", "screen.title"],
            palettes.Select(palette => palette.MediaId).Order(StringComparer.Ordinal));
        Assert.All(palettes, palette =>
        {
            Assert.Equal("embedded-in-source-file", palette.Source);
            Assert.Equal(4, palette.Scale);
            Assert.Equal("ImgFile.ReadPalette", palette.Anchor);
        });
    }

    /// <summary>
    /// The character presentation section is the reference record a character or social UI binds. Its
    /// enumeration must match the corpus (#7933 counted the FACE family's canvases from the files), and a
    /// reference it cannot yet resolve must say so rather than looking like a working binding.
    /// </summary>
    [Fact]
    public void The_character_presentation_references_enumerate_their_canvases_and_state_what_is_pending()
    {
        ProductContent content = AdmittedContent();
        JsonElement presentation = JsonDocument.Parse(content.ReadBytes("worldrpg/payloads/daggerfall.base.json").ToArray())
            .RootElement.GetProperty("characterPresentation");

        // #7933's enumeration: the 17 FACE files supply 221 canvases - 16 files of ten records each plus
        // the 61-cell FACES.CIF grid - and the four NITE files are one canvas each at the shape their
        // length establishes.
        JsonElement[] files = [.. presentation.GetProperty("files").EnumerateArray()];
        JsonElement[] faces = [.. files.Where(file => file.GetProperty("family").GetString() == "FACE")];
        Assert.Equal(17, faces.Length);
        Assert.Equal(221, faces.Sum(file => file.GetProperty("canvasCount").GetInt32()));
        Assert.Equal(4, files.Count(file => file.GetProperty("family").GetString()!.StartsWith("NITE", StringComparison.Ordinal)));
        Assert.All(files.Where(file => file.GetProperty("family").GetString()!.StartsWith("NITE", StringComparison.Ordinal)),
            file => Assert.Equal(1, file.GetProperty("canvasCount").GetInt32()));

        // A file kept unread or unbound states why; a silent omission is what the task forbids.
        Assert.All(files, file =>
        {
            Assert.False(string.IsNullOrWhiteSpace(file.GetProperty("family").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(file.GetProperty("outcome").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(file.GetProperty("reason").GetString()));
        });
        Assert.Contains(files, file => file.GetProperty("outcome").GetString() == "unreferenced");

        // Every published reference states either that a consumer binds it or that it is still pending,
        // with the file and palette it would need, and names the consumer when one binds it.
        JsonElement[] layers = [.. presentation.GetProperty("layers").EnumerateArray()];
        JsonElement[] factionFaces = [.. presentation.GetProperty("faces").EnumerateArray()];
        foreach (JsonElement[] references in new[] { layers, factionFaces })
        {
            Assert.NotEmpty(references);
            Assert.All(references, reference =>
            {
                // The two states the binding actually has. Checking for a value no code can write would
                // be a tautology, which is what this assertion used to be.
                Assert.Contains(reference.GetProperty("binding").GetString(), new[] { "admitted", "requiredPending" });
                Assert.False(string.IsNullOrWhiteSpace(reference.GetProperty("mediaId").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(reference.GetProperty("sourceFile").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(reference.GetProperty("palette").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(reference.GetProperty("consumer").GetString()));
            });
        }

        // The canvases are published, so a reference the character sheet resolves is admitted rather than
        // pending forever - and the sheet resolves every race the catalogs publish, so all 200 layers are
        // bound. Every faction face stays required-pending: their grid's cells have no published artifact
        // and no consumer. Both counts are pinned so that binding a file has to move this deliberately.
        Assert.Equal(200, layers.Count(layer => layer.GetProperty("binding").GetString() == "admitted"));
        Assert.DoesNotContain(layers, layer => layer.GetProperty("binding").GetString() == "requiredPending");
        Assert.All(layers, layer => Assert.Equal("the character sheet", layer.GetProperty("consumer").GetString()));
        // The faction faces are a grid nothing here slices the pixels of, so they are neither published
        // nor bound and the count is the whole grid.
        Assert.Equal(61, factionFaces.Count(face => face.GetProperty("binding").GetString() == "requiredPending"));

        // The three supplied career portraits are what any career's sheet draws, so they are bound by the
        // same consumer, and the careers the corpus depicts no portrait for say so rather than borrowing
        // another class's art.
        JsonElement[] careers = [.. presentation.GetProperty("careers").EnumerateArray()];
        Assert.Equal(3, careers.Length);
        Assert.All(careers, portrait =>
        {
            Assert.Equal("admitted", portrait.GetProperty("binding").GetString());
            Assert.Equal("the character sheet", portrait.GetProperty("consumer").GetString());
            // The section states the palette the pixels are really in. A classic animation carries its own,
            // so naming the family's paired palette here would describe colours the portrait does not have -
            // and the generated index, which a consumer resolves the bytes through, states the same fact.
            Assert.StartsWith("embedded-palette-sha256:", portrait.GetProperty("palette").GetString(), StringComparison.Ordinal);
        });
        JsonElement characterIndex = JsonDocument.Parse(
            content.ReadBytes("worldrpg/media/character/character-media-inventory.json").ToArray()).RootElement;
        Dictionary<string, string> painted = [];
        foreach (JsonElement artifact in characterIndex.GetProperty("artifacts").EnumerateArray())
        {
            painted[artifact.GetProperty("mediaId").GetString()!] = artifact.GetProperty("palette").GetString()!;
        }

        // Every reference whose canvas was published states the same palette as the index entry for that
        // canvas: a consumer resolving either record paints the same colours. The faction faces have no
        // artifact and so no entry, which is the pending state the section already states.
        foreach (JsonElement[] references in new[] { layers, factionFaces, careers })
        {
            Assert.All(references.Where(reference => painted.ContainsKey(reference.GetProperty("mediaId").GetString()!)), reference =>
            {
                string mediaId = reference.GetProperty("mediaId").GetString()!;
                Assert.Equal(painted[mediaId], reference.GetProperty("palette").GetString());
            });
        }

        Assert.All(factionFaces, face => Assert.False(painted.ContainsKey(face.GetProperty("mediaId").GetString()!)));

        // A supplied file whose canvases could not be published is recorded as unreadable with the
        // refusal that names it - not as a file that read and went unused, which would say the opposite
        // of what the generated media index states about the same bytes.
        JsonElement[] unfiles = [.. files.Where(file => file.GetProperty("outcome").GetString() == "unreadable")];
        Assert.Equal(
            ["CMPA00I0.BSS", "CMPA01I0.BSS", "CMPA02I0.BSS", "FACES.CIF"],
            unfiles.Select(file => file.GetProperty("path").GetString()).Order(StringComparer.Ordinal));
        Assert.All(unfiles, file =>
        {
            Assert.True(file.GetProperty("canvasCount").GetInt32() > 0);
            Assert.StartsWith("'character.", file.GetProperty("reason").GetString(), StringComparison.Ordinal);
            Assert.Contains(file.GetProperty("path").GetString()!, file.GetProperty("reason").GetString(), StringComparison.Ordinal);
        });

        Assert.Equal(61, presentation.GetProperty("faces").GetArrayLength());
        Assert.Equal(200, presentation.GetProperty("layers").GetArrayLength());
    }

    /// <summary>
    /// The admitted references are exactly the ones the character sheet resolves, checked against the
    /// sheet's own projection for every race and career the pack publishes: a binding is a claim that a live
    /// consumer draws the canvas, so this is what makes it a fact about the product rather than a label the
    /// producer wrote for itself.
    /// </summary>
    [Fact]
    public void The_admitted_character_references_are_the_ones_the_character_sheet_resolves()
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(
            File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));
        DaggerfallCharacterPresentationSet set = definitions.CharacterPresentation;
        JsonElement presentation = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")))
            .RootElement.GetProperty("characterPresentation");

        // The consumer is run for every race the catalogs publish, because that is its domain: the player
        // actor declares one race, and binding by that actor alone would leave seven races' layers labelled
        // unclaimed while RequireRace resolves them.
        DaggerfallActorDefinition player = definitions.Actors[definitions.Actors.Keys.Single(id => id.Value == "player")];
        HashSet<string> resolved = [];
        foreach (DaggerfallRaceDefinition race in definitions.Catalogs.Races)
        {
            // The same actor with each published race: what the consumer resolves is the question, and the
            // race is the axis the binding turns on.
            CharacterIdentityPresentation identity = CharacterIdentityPresentation.From(definitions, player with { Race = race.Id })!;
            Assert.Equal(25, identity.Media.Length);
            foreach (string mediaId in identity.Media.Select(medium => medium.MediaId)) resolved.Add(mediaId);
        }

        // Every race's own paper doll, so an admitted layer the sheet cannot resolve and a layer it does
        // resolve but the pack leaves pending both fail here.
        HashSet<string> admittedLayers =
        [
            .. presentation.GetProperty("layers").EnumerateArray()
                .Where(layer => layer.GetProperty("binding").GetString() == "admitted")
                .Select(layer => layer.GetProperty("mediaId").GetString()!),
        ];
        Assert.Equal(200, admittedLayers.Count);
        Assert.Equal(resolved.Order(StringComparer.Ordinal), admittedLayers.Order(StringComparer.Ordinal));

        // A career portrait is bound by the family rule rather than by this pack's one player career: the
        // sheet draws whichever class an actor declares, so every portrait the corpus supplies is admitted -
        // and each career the pack depicts resolves to one of them.
        HashSet<string> admittedPortraits =
        [
            .. presentation.GetProperty("careers").EnumerateArray()
                .Where(career => career.GetProperty("binding").GetString() == "admitted")
                .Select(career => career.GetProperty("mediaId").GetString()!),
        ];
        Assert.Equal(3, admittedPortraits.Count);
        foreach (DaggerfallCareerPortraitDefinition portrait in set.Careers.Values)
        {
            Assert.Contains(portrait.MediaId, admittedPortraits);
        }

        // The faction faces are the one family no consumer resolves, and they are pinned as such: their
        // grid's cells have no artifact and the pack says pending rather than claiming a reader.
        Assert.DoesNotContain(set.FactionFaces, face => admittedLayers.Contains(face.MediaId));
        Assert.All(set.FactionFaces, face => Assert.Equal("an unstated consumer", face.Consumer));
    }

    [Fact]
    public void The_generated_inventory_states_the_media_identity_of_each_published_artifact()
    {
        ProductContent content = AdmittedContent();
        JsonElement inventory = JsonDocument.Parse(content.ReadBytes(DaggerfallUiArt.InventoryPath).ToArray()).RootElement;
        Dictionary<string, (string Path, long ByteLength)> identified = new(StringComparer.Ordinal);
        foreach (JsonElement artifact in inventory.GetProperty("artifacts").EnumerateArray())
        {
            if (!artifact.TryGetProperty("mediaId", out JsonElement mediaId)) continue;
            string path = artifact.GetProperty("path").GetString()!;
            long byteLength = artifact.GetProperty("byteLength").GetInt64();
            Assert.True(identified.TryAdd(mediaId.GetString()!, (path, byteLength)), $"The inventory names '{mediaId}' twice.");
            // Every stated identity resolves to admitted bytes of the stated length under that name.
            Assert.Equal(byteLength, content.ReadBytes(path).Length);
        }

        // The twelve classic UI images the group publishes, the authored skins, and every item icon
        // the pack names for its catalog: the death screen is the artifact this delivery path exists
        // for, and the service screens are the ones a UI task binds by slot.
        Assert.Equal(("worldrpg/media/ui/screen-death.png", 63124L), identified["screen.death"]);
        Assert.Equal("worldrpg/media/ui/window-character-sheet-chrome.png", identified["window.character-sheet.chrome"].Path);
        Assert.Equal("worldrpg/media/ui/window-book-reader.png", identified["window.book.reader"].Path);
        Assert.Equal("worldrpg/media/ui/window-rest-panel.png", identified["window.rest.panel"].Path);
        Assert.Equal("worldrpg/media/ui/window-merchant-cost.png", identified["window.merchant.cost"].Path);
        Assert.Equal("worldrpg/media/ui/window-guild-service.png", identified["window.guild.service"].Path);
        Assert.Equal("worldrpg/media/ui/window-bank-panel.png", identified["window.bank.panel"].Path);
        // The companions the donor composes those screens from are published beside the panels.
        Assert.Equal("worldrpg/media/ui/window-rest-hours-past.png", identified["window.rest.hours-past"].Path);
        Assert.Equal("worldrpg/media/ui/window-rest-hours-remaining.png", identified["window.rest.hours-remaining"].Path);
        Assert.Equal("worldrpg/media/ui/window-guild-member.png", identified["window.guild.member"].Path);
        Assert.Equal("worldrpg/media/ui/window-merchant-buttons-buy.png", identified["window.merchant.buttons.buy"].Path);
        Assert.Equal("worldrpg/media/ui/window-merchant-buttons-identify.png", identified["window.merchant.buttons.identify"].Path);
        Assert.Equal("worldrpg/media/ui/inventory-icons/inventory-icon-iron-dagger.png", identified["inventory.icon.iron-dagger"].Path);
        Assert.Equal("worldrpg/media/ui/authored/inventory-skin-panel-slate-v1.png", identified["inventory.skin.panel-slate.v1"].Path);
        Assert.Equal(79, identified.Count);

        // The identities the group states are the identities the pack publishes for the same images,
        // so a consumer that asks by media name cannot be answered with a different artifact.
        JsonElement classic = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/imports/privateers-hold/media/classic/manifest.json"))).RootElement;
        Dictionary<string, string> packPaths = [];
        foreach (JsonElement resource in classic.GetProperty("media").GetProperty("resources").EnumerateArray())
            packPaths[resource.GetProperty("id").GetString()!] = resource.GetProperty("relativePath").GetString()!;
        Assert.Equal("media/ui/inventory-icons/inventory-icon-iron-dagger.png", packPaths["inventory.icon.iron-dagger"]);
        Assert.Equal("media/ui/window-character-sheet-chrome.png", packPaths["window.character-sheet.chrome"]);
        Assert.Equal(
            Path.GetFileName(packPaths["inventory.icon.iron-dagger"]),
            Path.GetFileName(identified["inventory.icon.iron-dagger"].Path));

        // Every identity both sides name must be the same file, so a republish that moved one side and
        // not the other cannot pass on one sampled row. The committed bundle publication used to trail
        // this group: it named none of the death screen, the five screens that carry their own palette,
        // or the window chrome, and the list of identities it was allowed to trail is gone with the
        // republish that carried them. Nothing trails now, which the empty difference states rather
        // than a list of permitted exceptions that a later republish could quietly satisfy again.
        Assert.Equal(
            [.. identified.Keys.Where(packPaths.ContainsKey).Order(StringComparer.Ordinal)],
            [.. packPaths.Keys.Where(identified.ContainsKey).Order(StringComparer.Ordinal)]);
        Assert.Empty(identified.Keys.Except(packPaths.Keys));
        Assert.All(
            identified.Keys.Where(packPaths.ContainsKey),
            id => Assert.Equal(Path.GetFileName(packPaths[id]), Path.GetFileName(identified[id].Path)));
    }

    [Fact]
    public void The_published_sound_catalog_names_clips_the_admitted_media_carries()
    {
        ProductContent content = AdmittedContent();
        JsonElement catalog = JsonDocument.Parse(content.ReadBytes("worldrpg/media/audio/classic-sound-catalog.json").ToArray()).RootElement;

        // The catalog is the availability record for the whole archive, delivered by name like any
        // other artifact, so a consumer can see every clip and its disposition rather than assuming
        // the six the product plays today.
        Assert.Equal(1, catalog.GetProperty("schemaVersion").GetInt32());
        JsonElement[] clips = [.. catalog.GetProperty("clips").EnumerateArray()];
        Assert.Equal(459, clips.Length);
        JsonElement[] admitted = [.. clips.Where(clip => clip.GetProperty("disposition").GetString() == "admitted")];
        Assert.Equal(6, admitted.Length);
        Assert.All(clips.Where(clip => clip.GetProperty("disposition").GetString() != "admitted"), clip => Assert.Equal(JsonValueKind.Null, clip.GetProperty("mediaId").ValueKind));

        // Which archive clip each cue is belongs to the product rather than to the producer that just
        // stated it, so the binding is pinned here as literals: a republish that swapped two identities
        // would move the file and the importer's table together and otherwise stay green.
        Assert.Equal([106, 108, 109, 110, 111, 112], admitted.Select(clip => clip.GetProperty("ordinal").GetInt32()));
        Assert.Equal(
            ["audio.melee.dagger.swing", "audio.melee.hit.1", "audio.melee.hit.2", "audio.melee.hit.3", "audio.melee.hit.4", "audio.melee.hit.5"],
            admitted.Select(clip => clip.GetProperty("mediaId").GetString()));

        // Every admitted reference resolves inside admitted content: the classic media manifest the
        // pack reads carries exactly those media identities, so the catalog's references are the ones
        // the product can follow rather than names only the importer knows.
        JsonElement manifest = JsonDocument.Parse(content.ReadBytes("worldrpg/imports/privateers-hold/media/classic/manifest.json").ToArray()).RootElement;
        HashSet<string> carried = [.. manifest.GetProperty("media").GetProperty("resources").EnumerateArray().Select(resource => resource.GetProperty("id").GetString()!)];
        Assert.All(admitted, clip => Assert.Contains(clip.GetProperty("mediaId").GetString()!, carried));
        Assert.Equal(admitted.Length, manifest.GetProperty("audio").EnumerateArray().Count());
    }

    [Fact]
    public void Published_ui_slots_name_admitted_media_and_retain_their_source_digest()
    {
        string root = RepositoryRoot();
        JsonElement inventory = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/media/classic-media-inventory.json"))).RootElement;

        // A slot is what a consumer binds, so the published inventory has to name one for every UI
        // image the pack carries, together with the media identity and the bytes it was decoded to.
        Dictionary<string, List<(string Path, string Sha256, string MediaId)>> slots = new(StringComparer.Ordinal);
        foreach (JsonElement artifact in inventory.GetProperty("artifacts").EnumerateArray())
        {
            if (!artifact.TryGetProperty("slot", out JsonElement slot)) continue;
            string mediaId = artifact.GetProperty("mediaId").GetString()!;
            Assert.StartsWith("worldrpg/media/ui/", artifact.GetProperty("path").GetString()!, StringComparison.Ordinal);
            Assert.NotEmpty(mediaId);
            if (!slots.TryGetValue(slot.GetString()!, out List<(string Path, string Sha256, string MediaId)>? filled)) slots[slot.GetString()!] = filled = [];
            // One artifact per media identity: a slot may hold several, but the same identity twice
            // would leave a consumer unable to tell which bytes belong to which part.
            Assert.DoesNotContain(filled, entry => entry.MediaId == mediaId);
            filled.Add((artifact.GetProperty("path").GetString()!, artifact.GetProperty("sha256").GetString()!, mediaId));
        }

        Assert.Equal(
            [
                "bank", "book", "characterGeneration", "characterSheet", "death", "guild", "hudChrome", "hudVitalFatigue",
                "hudVitalHealth", "hudVitalMagicka", "inventory", "merchant", "pick", "prison", "rest", "startMenu", "title",
            ],
            slots.Keys.OrderBy(name => name, StringComparer.Ordinal));

        // A slot names a screen. Where the donor composes a screen from several images, the slot holds
        // every part: a rest dialog without its hour counters, a trade window without its button bars
        // or a guild popup without its member art is a fragment, not the screen the slot names.
        Assert.Equal(3, slots["rest"].Count);
        Assert.Equal(6, slots["merchant"].Count);
        Assert.Equal(2, slots["guild"].Count);
        Assert.Single(slots["book"]);
        Assert.Single(slots["bank"]);
        Assert.Equal(
            ["window.rest.hours-past", "window.rest.hours-remaining", "window.rest.panel"],
            slots["rest"].Select(entry => entry.MediaId).Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "window.merchant.buttons.buy", "window.merchant.buttons.identify", "window.merchant.buttons.repair",
                "window.merchant.buttons.sell", "window.merchant.buttons.sell-gold", "window.merchant.cost",
            ],
            slots["merchant"].Select(entry => entry.MediaId).Order(StringComparer.Ordinal));
        Assert.Equal(["window.guild.member", "window.guild.service"], slots["guild"].Select(entry => entry.MediaId).Order(StringComparer.Ordinal));

        Assert.Contains("window.book.reader", Media("book"));
        Assert.Contains("window.inventory.chrome", Media("inventory"));
        Assert.Contains("window.bank.panel", Media("bank"));

        // Every published part resolves to delivered bytes that still hash to the recorded digest.
        foreach ((string slot, List<(string Path, string Sha256, string MediaId)> parts) in slots)
        {
            foreach ((string path, string sha256, _) in parts)
            {
                string file = Path.Combine(root, "content", path);
                Assert.True(File.Exists(file), $"Published UI slot '{slot}' names '{path}', which is not part of the delivered content.");
                Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
            }
        }

        string[] Media(string slot) => [.. slots[slot].Select(entry => entry.MediaId)];
    }

    /// <summary>
    /// The screens the DOM's own mode table names are the published artifacts it can resolve.
    /// </summary>
    /// <remarks>
    /// The DOM decides which screen a mode shows from a table it owns, and that table is the one place a
    /// screen identity is written in the client. Nothing in the C# suites can execute the DOM, so this
    /// reads the table it ships and holds it to the product's own contract in the three ways that are
    /// checkable from here: the mode names are the ones the product publishes, every screen is one the
    /// inventory delivered, and every delivered screen is either shown by a mode or named by the client as
    /// having none.
    /// <para>
    /// What this does not check is which published screen a mode is bound to, because nothing in the
    /// content states that pairing: the inventory records the screen a slot fills and the mode is a wire
    /// name the ruleset derives, and the two vocabularies differ where the domains do ("dead" against
    /// "death"). Pointing the title mode at some other published screen would leave the title screen
    /// delivered and unselected, which the accounting above fails on; pointing it at one screen while a
    /// second screen takes the first's place is indistinguishable to a reader that only knows both names
    /// exist. Closing that needs an artifact-to-mode statement, which is a decision about the content
    /// rather than a test.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_dom_mode_screen_table_names_published_screens_for_published_modes()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/ui/screens.ts"));
        // The mode is written as the shared constant in one entry and as a literal in the other, so both
        // spellings are read: the point is the pair, not how the client spells the mode in the table.
        (string Mode, string Screen)[] table =
        [
            .. Regex.Matches(source, @"\{\s*mode:\s*(?:'(?<literal>[^']+)'|(?<constant>[A-Za-z_][A-Za-z0-9_]*))\s*,\s*screen:\s*'(?<screen>[^']+)'\s*\}")
                .Select(match => (
                    match.Groups["literal"].Success ? match.Groups["literal"].Value : TitleModeConstant(source, match.Groups["constant"].Value),
                    match.Groups["screen"].Value)),
        ];
        Assert.NotEmpty(table);

        // Every screen the table names is an artifact the published inventory resolves, so a mode cannot
        // name a screen the product never delivered.
        using JsonDocument inventory = JsonDocument.Parse(
            AdmittedContent().ReadBytes("worldrpg/media/classic-media-inventory.json").ToArray());
        // The inventory indexes the sound catalog beside the art, so a screen is an entry that carries
        // both an identity and the slot it fills.
        Dictionary<string, string> slotOf = [];
        foreach (JsonElement artifact in inventory.RootElement.GetProperty("artifacts").EnumerateArray())
        {
            if (artifact.TryGetProperty("mediaId", out JsonElement identity) && identity.ValueKind == JsonValueKind.String
                && artifact.TryGetProperty("slot", out JsonElement slot) && slot.ValueKind == JsonValueKind.String)
            {
                slotOf[identity.GetString()!] = slot.GetString()!;
            }
        }

        // Each screen the table names is one the inventory published, which is what makes the mode's
        // screen arrive at all: a name with no artifact behind it is a mode that shows nothing.
        Assert.All(table, entry =>
        {
            Assert.True(slotOf.TryGetValue(entry.Screen, out string? slot), $"'{entry.Screen}' is not a published screen.");
            Assert.False(string.IsNullOrWhiteSpace(slot));
        });

        // Every screen the product delivers is accounted for: one the table shows, or one the client
        // states has no mode yet. That is what makes a *swap* fail rather than pass on both names existing
        // - pointing the title mode at the prison screen would leave the title screen delivered and
        // selected by nothing, which is exactly the state the mode-less list exists to make deliberate.
        string[] modeLess = [.. Regex.Matches(source, @"MODE_LESS_SCREENS[^=]*=\s*\[(?<items>[^\]]*)\]", RegexOptions.Singleline)
            .SelectMany(match => Regex.Matches(match.Groups["items"].Value, "'(?<screen>[^']+)'"))
            .Select(match => match.Groups["screen"].Value)];
        string[] delivered = [.. slotOf.Keys.Where(identity => identity.StartsWith("screen.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        string[] accounted = [.. table.Select(entry => entry.Screen).Concat(modeLess).Order(StringComparer.Ordinal)];
        Assert.Equal(delivered, accounted);

        // The client's whole claim about which modes own a screen and which screen each owns: the two
        // whose screen replaces the HUD rather than joining it, in the order the table lists them. The
        // pairing is pinned as literals the way the mode list already was, because which published screen
        // a mode shows is the client's claim about the product and this is the only place outside the
        // client it is written down. The mode and the inventory's slot name differ where the domains do -
        // the dead mode shows the screen slotted "death" - so this states the pair rather than deriving it.
        Assert.Equal(
            [("title", "screen.title"), ("dead", "screen.death")],
            table);

        // The mode the entry screen is keyed by is stated once on each side rather than written out twice
        // where a rename could miss one.
        Assert.Contains($"TITLE_MODE = '{DaggerfallHudProjection.TitleModeName}'", source, StringComparison.Ordinal);

        // And the client's own reader uses that constant for both halves of the entry screen: the condition
        // the mode is tested by and the screen the mode is resolved to. Those two have to be the same mode -
        // a reader that showed one mode's screen under another mode's condition would leave the screen it
        // names delivered and never shown - and this is the only place outside the client that can say so.
        string consumer = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/ui/main.ts"));
        Assert.Contains("=== TITLE_MODE", consumer, StringComparison.Ordinal);
        Assert.Contains("screenForMode(TITLE_MODE)", consumer, StringComparison.Ordinal);

        // The action the entry screen sends is the one the product answers, and the wire is one word: a
        // rename on either side leaves the button that does nothing.
        Assert.Contains($"BEGIN_ACTION = '{WorldRpgProduct.EntryScreenAction}'", source, StringComparison.Ordinal);
    }

    /// <summary>The value a name in the client's table stands for, read from where it is declared.</summary>
    private static string TitleModeConstant(string source, string name)
    {
        Match declared = Regex.Match(source, $@"const {name} = '(?<value>[^']+)'");
        Assert.True(declared.Success, $"'{name}' is used in the table and not declared in the file.");
        return declared.Groups["value"].Value;
    }

    private static ProductContent AdmittedContent()
    {
        string contentRoot = Path.Combine(RepositoryRoot(), "content");
        string selected = Path.Combine(contentRoot, "worldrpg");
        ProductContentFile[] files = [.. Directory.GetFiles(selected, "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(
                Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')),
                File.ReadAllBytes(path)))];
        return new ProductContent(files);
    }

    /// <summary>
    /// Every admitted content-file name in the published group, read through the same directory listing a
    /// consumer would see rather than from the filesystem.
    /// </summary>
    private static IEnumerable<string> GeneratedContentFiles(ProductContent content) =>
        content.ReadDirectory("worldrpg/media", recursive: true).Select(file => Encoding.UTF8.GetString(file.Path.Span));

    /// <summary>One generated content-group index, parsed, with its entries already checked to be an array.</summary>
    private readonly record struct GeneratedIndex(JsonElement Root, JsonElement[] Artifacts)
    {
        /// <summary>The families whose canvases the producer could not publish, with the reason it gave.</summary>
        internal JsonElement[] Unreadable => [.. Root.GetProperty("unreadableFamilies").EnumerateArray()];
    }

    /// <summary>
    /// Reads one generated index from admitted content. Two content groups publish one - the classic
    /// media and the character canvases - so the path is supplied rather than assumed.
    /// </summary>
    private static GeneratedIndex Index(string path)
    {
        JsonElement root = JsonDocument.Parse(AdmittedContent().ReadBytes(path).ToArray()).RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        return new(root, [.. root.GetProperty("artifacts").EnumerateArray()]);
    }

    private static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "AGENTS.md")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException("The repository root was not found above the test output directory.");
    }
}
