using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The character/face canvases a presentation reference names have to become admitted bytes, not stay
/// pending forever. These checks publish real corpus canvases and pin the two refusals that keep a missing
/// canvas visible.
/// </summary>
public sealed class CharacterMediaPublicationTests
{
    [Fact]
    public void PublishesEachCanvasOfAFamilyInItsOwnPalette()
    {
        (Dictionary<string, ReadOnlyMemory<byte>> sources, Dictionary<string, Arena2Palette> palettes) = Corpus();
        CharacterCanvasReference[] references =
        [
            new("character.head.breton.male", "FACE00I0.CIF", "FACE", "head", "an unstated consumer", MediaBinding.RequiredPending, 3, "ART_PAL.COL", [], "the canvas the reference names"),
            new("character.head.breton.female", "FACE00I0.CIF", "FACE", "head", "an unstated consumer", MediaBinding.RequiredPending, 0, "ART_PAL.COL", [], "the canvas the reference names"),
            // The face grid's cells are enumerated but their pixels are not decodable here, so it is
            // refused with that reason rather than published as a shape with no image.
            new("character.faction-face.00", "FACES.CIF", "FACE", "faction-face", "an unstated consumer", MediaBinding.RequiredPending, 0, "ART_PAL.COL", [], "the canvas the reference names"),
        ];

        CharacterMediaPublicationResult published = CharacterMediaPublication.Publish("FACE", references, sources, palettes);

        Assert.Equal(2, published.Artifacts.Count);
        // Artifacts come out in canvas order, which is the order the reader enumerates records in.
        Assert.Equal(["character.head.breton.female", "character.head.breton.male"], published.Artifacts.Select(artifact => artifact.MediaId));
        Assert.True(published.Refusals.Count == 1, $"refused: {string.Join(" | ", published.Refusals)}");
        Assert.Contains("character.faction-face.00", published.Refusals[0], StringComparison.Ordinal);
        Assert.Contains("FACES.CIF", published.Refusals[0], StringComparison.Ordinal);
        Assert.Equal(published.PublishedMediaIds.Count, published.PublishedMediaIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(published.Artifacts, artifact =>
        {
            // A PNG signature, and the published path is derived from the media identity rather than
            // from the source file, so two files carrying one identity cannot collide.
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], artifact.Bytes.Take(4));
            Assert.StartsWith("media/character/", artifact.RelativePath, StringComparison.Ordinal);
            Assert.DoesNotContain(".CIF", artifact.RelativePath, StringComparison.Ordinal);
            Assert.True(artifact.Width > 0 && artifact.Height > 0);
        });
        // A career portrait publishes from its animation, in the palette the container carries.
        CharacterCanvasReference[] portraits =
        [
            new("character.career.mage.portrait", "MAGE.CEL", "CEL", "career-portrait", "an unstated consumer", MediaBinding.RequiredPending, 0, "ART_PAL.COL", [], "the canvas the reference names"),
        ];
        CharacterMediaPublicationResult publishedPortrait = CharacterMediaPublication.Publish("CEL", portraits, sources, palettes);
        Assert.True(publishedPortrait.Refusals.Count == 0, $"refused: {string.Join(" | ", publishedPortrait.Refusals)}");
        CharacterMediaArtifact portrait = Assert.Single(publishedPortrait.Artifacts);
        Assert.Equal("media/character/character-career-mage-portrait.png", portrait.RelativePath);
        Assert.True(portrait.Width > 0 && portrait.Height > 0 && portrait.Bytes.Length > 100);

        // A family the references do not carry publishes nothing rather than guessing at a file list.
        Assert.Empty(CharacterMediaPublication.Publish("BODY", references, sources, palettes).Artifacts);
    }

    [Fact]
    public void RefusesAMissingFileAndACanvasThatDoesNotExist()
    {
        (Dictionary<string, ReadOnlyMemory<byte>> sources, Dictionary<string, Arena2Palette> palettes) = Corpus();
        CharacterCanvasReference[] references =
        [
            new("character.faction-face.00", "NOTHERE.CIF", "FACE", "faction-face", "an unstated consumer", MediaBinding.RequiredPending, 0, "ART_PAL.COL", [], "the canvas the reference names"),
            new("character.faction-face.01", "FACES.CIF", "FACE", "faction-face", "an unstated consumer", MediaBinding.RequiredPending, 999, "ART_PAL.COL", [], "the canvas the reference names"),
        ];

        CharacterMediaPublicationResult published = CharacterMediaPublication.Publish("FACE", references, sources, palettes);

        Assert.Empty(published.Artifacts);
        Assert.Equal(2, published.Refusals.Count);
        Assert.Contains(published.Refusals, refusal => refusal.Contains("character.faction-face.00", StringComparison.Ordinal) && refusal.Contains("NOTHERE.CIF", StringComparison.Ordinal));
        Assert.Contains(published.Refusals, refusal => refusal.Contains("character.faction-face.01", StringComparison.Ordinal) && refusal.Contains("999", StringComparison.Ordinal));
    }

    [Fact]
    public void PublishesAHeaderlessCanvasAndRefusesAPaletteTheCorpusDoesNotCarry()
    {
        (Dictionary<string, ReadOnlyMemory<byte>> sources, Dictionary<string, Arena2Palette> palettes) = Corpus();
        CharacterCanvasReference[] references =
        [
            // A headerless canvas: the reader establishes the shape from the file length, and the
            // headered reader cannot open it at all.
            new("character.nite.00", "NITE00I0.IMG", "NITE", "nite", "an unstated consumer", MediaBinding.RequiredPending, 0, "NIGHTSKY.COL", [], "the canvas the reference names"),
            // A palette the corpus does not carry is a refusal, because painting it with a default
            // publishes every colour wrong while looking successful.
            new("character.nite.01", "NITE01I0.IMG", "NITE", "nite", "an unstated consumer", MediaBinding.RequiredPending, 0, "NOTHERE.COL", [], "the canvas the reference names"),
            // So is a palette that exists but belongs to another family: ART_PAL.COL is supplied, and
            // pairing the NITE family with it publishes every opaque pixel wrong.
            new("character.nite.02", "NITE02I0.IMG", "NITE", "nite", "an unstated consumer", MediaBinding.RequiredPending, 0, "ART_PAL.COL", [], "the canvas the reference names"),
        ];

        CharacterMediaPublicationResult published = CharacterMediaPublication.Publish("NITE", references, sources, palettes);

        Assert.True(published.Refusals.Count == 2, $"refused: {string.Join(" | ", published.Refusals)}");
        Assert.Contains(published.Refusals, refusal => refusal.Contains("character.nite.01", StringComparison.Ordinal) && refusal.Contains("NOTHERE.COL", StringComparison.Ordinal));
        Assert.Contains(published.Refusals, refusal => refusal.Contains("character.nite.02", StringComparison.Ordinal) && refusal.Contains("NIGHTSKY.COL", StringComparison.Ordinal));
        CharacterMediaArtifact nite = Assert.Single(published.Artifacts);
        Assert.Equal("character.nite.00", nite.MediaId);
        // 512x219 is the shape the NITE file's length establishes.
        Assert.Equal((512, 219), (nite.Width, nite.Height));
    }

    /// <summary>
    /// The whole pass over the real corpus: every readable canvas becomes an artifact, the formats this
    /// repository cannot read are named with their files, and the committed index is byte-for-byte what
    /// the pass regenerates - so an index that drifted from the corpus cannot stay committed.
    /// </summary>
    [Fact]
    public void PublishesEveryReadableCanvasAndRegeneratesTheCommittedIndex()
    {
        (Dictionary<string, ReadOnlyMemory<byte>> sourceBytes, Dictionary<string, Arena2Palette> palettes) = Corpus();
        List<(string Path, ReadOnlyMemory<byte> Bytes)> sources = [.. sourceBytes.Select(entry => (entry.Key, entry.Value))];
        CharacterMediaInventory inventory = CharacterMediaInventory.Enumerate(sources, new HashSet<string>(StringComparer.Ordinal), "arena2");
        CharacterMediaReferenceSet set = CharacterMediaReferences.Derive(inventory, palettes.Keys.ToHashSet(StringComparer.Ordinal));
        CharacterMediaPassResult pass = CharacterMediaPublisher.PublishAll(set, sourceBytes, palettes, inventory);

        // The census, as counts rather than a claim: every family the inventory enumerates is published
        // from, and the totals are the readers' own.
        Assert.Equal(264, pass.Artifacts.Count);
        Assert.Equal(157, pass.Refusals.Count);
        Assert.Equal(87, inventory.Files.Count);
        Assert.Equal(
            [4, 9, 9, 10, 32, 40, 160],
            new[]
            {
                pass.Artifacts.Count(artifact => artifact.Reference.Family == "NITE"),
                pass.Artifacts.Count(artifact => artifact.Reference.Family == "SCBG"),
                pass.Artifacts.Count(artifact => artifact.Reference.Family == "CHAR"),
                pass.Artifacts.Count(artifact => artifact.Reference.Family == "CUST"),
                pass.Artifacts.Count(artifact => artifact.Reference.Family == "BODY"),
                pass.Artifacts.Count(artifact => artifact.Reference.Family == "CEL"),
                pass.Artifacts.Count(artifact => artifact.Reference.Family == "FACE"),
            });
        Assert.All(pass.Artifacts, artifact =>
        {
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], artifact.Bytes.Take(4));
            Assert.StartsWith("media/character/", artifact.RelativePath, StringComparison.Ordinal);
            Assert.True(artifact.Width > 0 && artifact.Height > 0);
            // A published canvas is one of the references the corpus enumerated: an artifact for a
            // canvas nothing asked for would be a second source of subjects.
            Assert.Contains(set.Canvases, canvas => canvas.MediaId == artifact.MediaId);
        });

        // The two gaps, each naming its files and its real reason, and neither of them a placeholder
        // family: the cells exist and cannot be sliced, and the BSS frames were never extracted.
        Assert.Equal(["BSS", "FACE"], pass.UnreadableFamilies.Select(family => family.Family));
        Assert.Equal(["CMPA00I0.BSS", "CMPA01I0.BSS", "CMPA02I0.BSS"], pass.UnreadableFamilies[0].Files);
        Assert.Equal(["FACES.CIF"], pass.UnreadableFamilies[1].Files);
        Assert.Equal("Assets/Scripts/API/BssFile.cs", pass.UnreadableFamilies[0].DonorAnchor);
        Assert.Equal(string.Empty, pass.UnreadableFamilies[1].DonorAnchor);
        // Every refusal names the canvas and the file it needed, so a missing canvas is legible rather
        // than a total that quietly came up short.
        Assert.All(pass.Refusals, refusal =>
        {
            Assert.StartsWith("'character.", refusal, StringComparison.Ordinal);
            Assert.Contains(pass.UnreadableFamilies.SelectMany(family => family.Files), file => refusal.Contains(file, StringComparison.Ordinal));
        });

        // The committed index is the pass's own output, regenerated for the same consumer the committed
        // pack names: the same 264 canvases with the same digests, the same bindings, and the same two
        // families absent, so an index that drifted from the corpus or from its producer cannot stay.
        int playerRace = PlayerDonorRaceId();
        CharacterMediaReferenceSet boundSet = CharacterMediaReferences.WithBoundFiles(
            set,
            CharacterMediaReferences.FilesBoundByCharacterSheet(inventory, playerRace),
            CharacterMediaReferences.CharacterSheetConsumer);
        string committed = Path.Combine(RepositoryRoot(), "content/worldrpg/media/character/character-media-inventory.json");
        byte[] regenerated = CharacterMediaPublisher.WriteIndex(pass, boundSet);

        // The publication states group-relative paths because it does not name the content group; the
        // committed index is content-relative because that is the name a consumer reads. The group prefix
        // is the only difference, which is what keeps the two conventions from drifting apart silently.
        using JsonDocument published = JsonDocument.Parse(regenerated);
        using JsonDocument delivered = JsonDocument.Parse(File.ReadAllBytes(committed));
        JsonElement[] written = [.. published.RootElement.GetProperty("artifacts").EnumerateArray()];
        JsonElement[] shipped = [.. delivered.RootElement.GetProperty("artifacts").EnumerateArray()];
        Assert.Equal(written.Length, shipped.Length);
        for (int position = 0; position < written.Length; position++)
        {
            Assert.Equal(written[position].GetProperty("mediaId").GetString(), shipped[position].GetProperty("mediaId").GetString());
            Assert.Equal($"worldrpg/{written[position].GetProperty("relativePath").GetString()}", shipped[position].GetProperty("relativePath").GetString());
            Assert.Equal(written[position].GetProperty("sha256").GetString(), shipped[position].GetProperty("sha256").GetString());
            Assert.Equal(written[position].GetProperty("binding").GetString(), shipped[position].GetProperty("binding").GetString());
            Assert.Equal(written[position].GetProperty("consumer").GetString(), shipped[position].GetProperty("consumer").GetString());
        }

        Assert.Equal(
            published.RootElement.GetProperty("unreadableFamilies").GetRawText(),
            delivered.RootElement.GetProperty("unreadableFamilies").GetRawText());
    }

    /// <summary>
    /// The donor race value of the pack's player actor, read from the published pack: the binding is the
    /// sheet's own race, so the check that it is right reads the same authored fact the tool does.
    /// </summary>
    private static int PlayerDonorRaceId()
    {
        using JsonDocument pack = JsonDocument.Parse(File.ReadAllBytes(PackPath()));
        JsonElement player = pack.RootElement.GetProperty("actors").EnumerateArray()
            .Single(actor => actor.GetProperty("id").GetString() == "player");
        string race = player.GetProperty("race").GetString()!;
        return pack.RootElement.GetProperty("catalogs").GetProperty("races").EnumerateArray()
            .Single(value => value.GetProperty("id").GetString() == race)
            .GetProperty("donorRaceId").GetInt32();
    }

    private static string PackPath() => Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json");

    /// <summary>
    /// A binding is what a published consumer establishes, not what the pass published: the sheet's own
    /// rule binds one race's paper-doll layers and every career portrait, and a canvas bound by a consumer
    /// the pass could not publish is refused rather than published as a reference to nothing.
    /// </summary>
    [Fact]
    public void BindsOnlyWhatAConsumerResolvesAndRefusesABindingWithNoArtifact()
    {
        (Dictionary<string, ReadOnlyMemory<byte>> sources, Dictionary<string, Arena2Palette> palettes) = Corpus();
        List<(string Path, ReadOnlyMemory<byte> Bytes)> supplied = [.. sources.Select(entry => (entry.Key, entry.Value))];
        CharacterMediaInventory inventory = CharacterMediaInventory.Enumerate(supplied, new HashSet<string>(StringComparer.Ordinal), "arena2");
        CharacterMediaReferenceSet set = CharacterMediaReferences.Derive(inventory, palettes.Keys.ToHashSet(StringComparer.Ordinal));

        // Breton is donor race 1, so the bound files are that race's own bodies, background and heads -
        // the female files carry ten above the male's number - plus the three class portraits.
        IReadOnlySet<string> bound = CharacterMediaReferences.FilesBoundByCharacterSheet(inventory, 1);
        Assert.Equal(
            [
                "BODY00I0.IMG", "BODY00I1.IMG", "BODY10I0.IMG", "BODY10I1.IMG",
                "FACE00I0.CIF", "FACE10I0.CIF", "MAGE.CEL", "ROGUE.CEL", "SCBG00I0.IMG", "WARRIOR.CEL",
            ],
            bound.Order(StringComparer.Ordinal));
        // A race with no paper-doll media binds nothing beyond the portraits rather than being mapped onto
        // another race's art.
        Assert.Equal(["MAGE.CEL", "ROGUE.CEL", "WARRIOR.CEL"], CharacterMediaReferences.FilesBoundByCharacterSheet(inventory, 18).Order(StringComparer.Ordinal));

        CharacterMediaReferenceSet referenced = CharacterMediaReferences.WithBoundFiles(set, bound, CharacterMediaReferences.CharacterSheetConsumer);
        Assert.Equal(
            set.Canvases.Count(canvas => bound.Contains(System.IO.Path.GetFileName(canvas.Path))),
            referenced.Canvases.Count(canvas => canvas.Binding == MediaBinding.Admitted));
        Assert.All(referenced.Canvases.Where(canvas => canvas.Binding == MediaBinding.Admitted),
            canvas => Assert.Equal(CharacterMediaReferences.CharacterSheetConsumer, canvas.Consumer));
        // The rewrite does not touch a reference no consumer bound: it keeps the inventory's own label for
        // a file nobody claims rather than acquiring a consumer, and it changes no identity.
        Assert.All(referenced.Canvases.Where(canvas => canvas.Binding != MediaBinding.Admitted), canvas =>
        {
            Assert.Equal(MediaBinding.RequiredPending, canvas.Binding);
            Assert.Equal(
                set.Canvases.Single(original => original.MediaId == canvas.MediaId).Consumer,
                canvas.Consumer);
        });
        Assert.Equal(set.Canvases.Select(canvas => canvas.MediaId), referenced.Canvases.Select(canvas => canvas.MediaId));

        CharacterMediaPassResult pass = CharacterMediaPublisher.PublishAll(set, sources, palettes, inventory);
        // Every bound canvas resolves: the index is written from the references and would refuse a name
        // with no artifact, so this is what makes the binding checkable rather than aspirational.
        byte[] index = CharacterMediaPublisher.WriteIndex(pass, referenced);
        using JsonDocument document = JsonDocument.Parse(index);
        JsonElement[] entries = [.. document.RootElement.GetProperty("artifacts").EnumerateArray()];
        Assert.Equal(pass.Artifacts.Count, entries.Length);
        Assert.Equal(
            bound.Sum(file => set.Canvases.Count(canvas => System.IO.Path.GetFileName(canvas.Path) == file)),
            entries.Count(entry => entry.GetProperty("binding").GetString() == "admitted"));
        Assert.All(entries.Where(entry => entry.GetProperty("binding").GetString() == "admitted"),
            entry => Assert.Equal(CharacterMediaReferences.CharacterSheetConsumer, entry.GetProperty("consumer").GetString()));
        // A canvas whose pixels the file carries states that, because a consumer that re-encodes it must
        // not lose the fact that its colours came from the container.
        Assert.All(entries.Where(entry => entry.GetProperty("sourceFile").GetString()!.EndsWith(".CEL", StringComparison.Ordinal)),
            entry => Assert.Equal("embedded-in-source-file", entry.GetProperty("paletteSource").GetString()));
        Assert.All(entries.Where(entry => !entry.GetProperty("sourceFile").GetString()!.EndsWith(".CEL", StringComparison.Ordinal)),
            entry => Assert.Equal("supplied-palette-file", entry.GetProperty("paletteSource").GetString()));
    }

    /// <summary>
    /// A canvas a consumer is said to bind has to resolve to an artifact, or the pack would claim a live
    /// consumer draws art that was never emitted. The refusal names the canvas.
    /// </summary>
    [Fact]
    public void RefusesABoundCanvasTheCorpusCannotSupply()
    {
        (Dictionary<string, ReadOnlyMemory<byte>> sources, Dictionary<string, Arena2Palette> palettes) = Corpus();
        List<(string Path, ReadOnlyMemory<byte> Bytes)> supplied = [.. sources.Select(entry => (entry.Key, entry.Value))];
        CharacterMediaInventory inventory = CharacterMediaInventory.Enumerate(supplied, new HashSet<string>(StringComparer.Ordinal), "arena2");
        CharacterMediaReferenceSet set = CharacterMediaReferences.Derive(inventory, palettes.Keys.ToHashSet(StringComparer.Ordinal));
        // The consumer is bound to a file the pass cannot publish, which is the state a binding must never
        // reach: the artifact list would come up short while the reference looked resolved.
        CharacterMediaReferenceSet broken = CharacterMediaReferences.WithBoundFiles(set, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NITE00I0.IMG" }, CharacterMediaReferences.CharacterSheetConsumer);
        Dictionary<string, ReadOnlyMemory<byte>> withoutNite = new(sources, StringComparer.Ordinal);
        withoutNite.Remove("NITE00I0.IMG");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => CharacterMediaPublisher.PublishAll(broken, withoutNite, palettes, inventory));
        Assert.Contains("NITE00I0.IMG", error.Message, StringComparison.Ordinal);
        Assert.Contains("binds", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole supplied character-media corpus and the two palettes these families are read with, so a
    /// check here reads the same bytes the publication does rather than a fixture shaped like them.
    /// </summary>
    private static (Dictionary<string, ReadOnlyMemory<byte>> Sources, Dictionary<string, Arena2Palette> Palettes) Corpus()
    {
        string arena2 = Path.Combine(RepositoryRoot(), "local", "arena2");
        Dictionary<string, ReadOnlyMemory<byte>> sources = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(arena2).OrderBy(path => path, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            if (CharacterMediaInventory.IsDocumentedFamily(name))
            {
                sources[name] = File.ReadAllBytes(path);
            }
        }

        Dictionary<string, Arena2Palette> palettes = new(StringComparer.Ordinal)
        {
            ["ART_PAL.COL"] = PaletteDecoder.Decode(File.ReadAllBytes(Path.Combine(arena2, "ART_PAL.COL")), "arena2/ART_PAL.COL"),
            // The NITE family is paired with its own palette, and the two differ in every opaque pixel.
            ["NIGHTSKY.COL"] = PaletteDecoder.Decode(File.ReadAllBytes(Path.Combine(arena2, "NIGHTSKY.COL")), "arena2/NIGHTSKY.COL"),
        };
        return (sources, palettes);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }
}
