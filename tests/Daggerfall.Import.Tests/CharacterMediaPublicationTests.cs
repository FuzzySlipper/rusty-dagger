using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;
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
    /// The colours a published canvas carries come from the palette its own reference names, checked against
    /// the pixels rather than against the encoded bytes: a digest moves with whatever was written, so a pass
    /// that painted every canvas in the wrong palette would still hash to something consistent.
    /// </summary>
    [Fact]
    public void PublishesEachCanvasInThePaletteItsSourceFilePairsWith()
    {
        (Dictionary<string, ReadOnlyMemory<byte>> sources, Dictionary<string, Arena2Palette> palettes) = Corpus();
        List<(string Path, ReadOnlyMemory<byte> Bytes)> supplied = [.. sources.Select(entry => (entry.Key, entry.Value))];
        CharacterMediaInventory inventory = CharacterMediaInventory.Enumerate(supplied, new HashSet<string>(StringComparer.Ordinal), "arena2");
        CharacterMediaReferenceSet set = CharacterMediaReferences.Derive(inventory, palettes.Keys.ToHashSet(StringComparer.Ordinal));
        CharacterMediaPassResult pass = CharacterMediaPublisher.PublishAll(set, sources, palettes, inventory);
        Dictionary<string, CharacterMediaArtifact> artifacts = pass.Artifacts.ToDictionary(artifact => artifact.MediaId, StringComparer.Ordinal);

        // A NITE canvas is paired with NIGHTSKY.COL and a BODY canvas with ART_PAL.COL, and the two palettes
        // differ in every opaque pixel of a NITE canvas - which is what makes a swapped pair visible here.
        IndexedImg nite = NiteskyCanvas();
        AssertOpaquePixels(artifacts["character.nite.nite00i0.0"], nite.Pixels, palettes["NIGHTSKY.COL"]);
        AssertOpaquePixels(artifacts["character.body-unclothed.male.00.0"], BodyCanvas(), palettes["ART_PAL.COL"]);

        // The two palettes really do disagree on the NITE canvas, so the check above cannot pass by accident
        // on a corpus where every palette happened to match.
        Assert.True(
            palettes["NIGHTSKY.COL"].ToRgba(nite.Pixels.Span, PaletteAlphaMode.IndexZeroTransparent)
                .Where((color, index) => color.Alpha != 0 && palettes["ART_PAL.COL"].ToRgba(nite.Pixels.Span, PaletteAlphaMode.IndexZeroTransparent)[index] != color)
                .Any(),
            "NIGHTSKY.COL and ART_PAL.COL paint a NITE canvas identically, so this check would not catch a swapped palette.");

        // A career portrait is painted in the palette its own container carries, and that palette is not the
        // shared art palette: MAGE.CEL's frame 0 differs from ART_PAL.COL on every one of its pixels.
        CharacterMediaArtifact portrait = artifacts["character.portrait.mage.0"];
        IReadOnlyList<FlcDecoder.FlcFrameImage> frames = FlcDecoder.DecodeFrames(sources["MAGE.CEL"].Span, "MAGE.CEL", out Arena2Palette? own);
        Assert.NotNull(own);
        Assert.Equal((110, 119, frames[0].Width, frames[0].Height), (portrait.Width, portrait.Height, frames[0].Width, frames[0].Height));
        AssertOpaquePixels(portrait, frames[0].Pixels, own);
        Assert.NotEqual(
            [.. own.ToRgba(frames[0].Pixels, PaletteAlphaMode.IndexZeroTransparent)],
            [.. palettes["ART_PAL.COL"].ToRgba(frames[0].Pixels, PaletteAlphaMode.IndexZeroTransparent)]);
    }

    /// <summary>Whether every opaque pixel of a published canvas is the colour its palette gives that index.</summary>
    private static void AssertOpaquePixels(CharacterMediaArtifact artifact, ReadOnlyMemory<byte> indexed, Arena2Palette palette)
    {
        Assert.Equal(indexed.Length, artifact.Width * artifact.Height);
        DeterministicPngImage published = DeterministicPngReader.ReadRgba8(artifact.Bytes, artifact.MediaId);
        Rgba32[] expected = palette.ToRgba(indexed.Span, PaletteAlphaMode.IndexZeroTransparent);
        int compared = 0;
        for (int index = 0; index < expected.Length; index++)
        {
            byte r = published.Rgba[(index * 4) + 0];
            byte g = published.Rgba[(index * 4) + 1];
            byte b = published.Rgba[(index * 4) + 2];
            byte a = published.Rgba[(index * 4) + 3];
            Assert.Equal(
                (expected[index].Red, expected[index].Green, expected[index].Blue, expected[index].Alpha),
                (r, g, b, a));
            if (a != 0) compared++;
        }

        Assert.True(compared > 0, $"'{artifact.MediaId}' published no opaque pixel, so its palette is not observable.");
    }

    /// <summary>The indexed pixels of NITE00I0.IMG, which is a headerless 512x219 canvas.</summary>
    private static IndexedImg NiteskyCanvas()
    {
        string arena2 = Path.Combine(RepositoryRoot(), "local", "arena2");
        return ImgDecoder.DecodeHeaderless(File.ReadAllBytes(Path.Combine(arena2, "NITE00I0.IMG")), "NITE00I0.IMG");
    }

    private static ReadOnlyMemory<byte> BodyCanvas()
    {
        string arena2 = Path.Combine(RepositoryRoot(), "local", "arena2");
        return ImgDecoder.Decode(File.ReadAllBytes(Path.Combine(arena2, "BODY00I0.IMG")), "BODY00I0.IMG").Pixels;
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
        // pack names and under the same content group, so an index that drifted from the corpus, from its
        // producer, or from the group naming cannot stay committed.
        CharacterMediaReferenceSet boundSet = CharacterMediaReferences.WithBoundFiles(
            set,
            CharacterMediaReferences.FilesBoundByCharacterSheet(inventory),
            CharacterMediaReferences.CharacterSheetConsumer);
        string committed = Path.Combine(RepositoryRoot(), "content/worldrpg/media/character/character-media-inventory.json");
        byte[] regenerated = CharacterMediaPublisher.WriteIndex(pass, boundSet, "worldrpg");

        // Every field of every entry, not a sampled handful: a hand-edited byte length, dimension, family,
        // source file or palette fact has to fail here rather than pass on the fields that did not move.
        using JsonDocument published = JsonDocument.Parse(regenerated);
        using JsonDocument delivered = JsonDocument.Parse(File.ReadAllBytes(committed));
        JsonElement[] written = [.. published.RootElement.GetProperty("artifacts").EnumerateArray()];
        JsonElement[] shipped = [.. delivered.RootElement.GetProperty("artifacts").EnumerateArray()];
        Assert.Equal(written.Length, shipped.Length);
        for (int position = 0; position < written.Length; position++)
        {
            Assert.Equal(
                [.. written[position].EnumerateObject().Select(property => $"{property.Name}={property.Value.GetRawText()}")],
                [.. shipped[position].EnumerateObject().Select(property => $"{property.Name}={property.Value.GetRawText()}")]);
        }

        Assert.Equal(
            published.RootElement.GetProperty("unreadableFamilies").GetRawText(),
            delivered.RootElement.GetProperty("unreadableFamilies").GetRawText());
    }

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

        // The consumer's domain is every race the catalogs publish, so the bound files are all eight
        // races' bodies, backgrounds and head CIFs - 8 BODY backgrounds, 32 bodies, 16 head CIFs - plus
        // the three class portraits. The ninth SCBG file numbers a race the catalogs do not publish and
        // stays unbound rather than being mapped onto one.
        IReadOnlySet<string> bound = CharacterMediaReferences.FilesBoundByCharacterSheet(inventory);
        Assert.Equal(59, bound.Count);
        Assert.Equal(8, bound.Count(file => file.StartsWith("SCBG", StringComparison.Ordinal)));
        Assert.Equal(32, bound.Count(file => file.StartsWith("BODY", StringComparison.Ordinal)));
        Assert.Equal(16, bound.Count(file => file.StartsWith("FACE", StringComparison.Ordinal)));
        Assert.All(bound.Where(file => file.StartsWith("SCBG", StringComparison.Ordinal)),
            file => Assert.True(string.CompareOrdinal(file, "SCBG08I0.IMG") < 0, $"{file} names a race the catalogs do not publish."));
        Assert.Equal(["MAGE.CEL", "ROGUE.CEL", "WARRIOR.CEL"], bound.Where(file => file.EndsWith(".CEL", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        // The story and compass families have no character-sheet role, so no consumer binds them.
        Assert.DoesNotContain("CMPA00I0.BSS", bound);

        CharacterMediaReferenceSet referenced = CharacterMediaReferences.WithBoundFiles(set, bound, CharacterMediaReferences.CharacterSheetConsumer);
        Assert.Equal(240, referenced.Canvases.Count(canvas => canvas.Binding == MediaBinding.Admitted));
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
        // A canvas whose pixels the file carries states that, because a consumer that re-encodes it must not
        // lose the fact that its colours came from the container - and it names the palette it was really
        // painted in, by a digest of the container's own colours, rather than the palette a reference happens
        // to carry. A consumer that repainted a portrait with the shared art palette would get other colours.
        Assert.All(entries.Where(entry => entry.GetProperty("sourceFile").GetString()!.EndsWith(".CEL", StringComparison.Ordinal)),
            entry =>
            {
                Assert.Equal("embedded-in-source-file", entry.GetProperty("paletteSource").GetString());
                Assert.StartsWith("embedded-palette-sha256:", entry.GetProperty("palette").GetString(), StringComparison.Ordinal);
                Assert.DoesNotContain("ART_PAL", entry.GetProperty("palette").GetString(), StringComparison.Ordinal);
            });
        Assert.All(entries.Where(entry => !entry.GetProperty("sourceFile").GetString()!.EndsWith(".CEL", StringComparison.Ordinal)),
            entry =>
            {
                Assert.Equal("supplied-palette-file", entry.GetProperty("paletteSource").GetString());
                Assert.Equal(CharacterMediaReferences.PaletteFor(entry.GetProperty("sourceFile").GetString()!), entry.GetProperty("palette").GetString());
            });
    }

    /// <summary>
    /// Two references carrying one identity would publish one artifact under one name twice, which a consumer
    /// could not resolve; the enumeration derives unique identities, so this guards the primitive against a
    /// hand-built set rather than a case the derivation produces.
    /// </summary>
    [Fact]
    public void RefusesTwoReferencesThatClaimOneMediaIdentity()
    {
        (Dictionary<string, ReadOnlyMemory<byte>> sources, Dictionary<string, Arena2Palette> palettes) = Corpus();
        List<(string Path, ReadOnlyMemory<byte> Bytes)> supplied = [.. sources.Select(entry => (entry.Key, entry.Value))];
        CharacterMediaInventory inventory = CharacterMediaInventory.Enumerate(supplied, new HashSet<string>(StringComparer.Ordinal), "arena2");
        CharacterMediaReferenceSet set = CharacterMediaReferences.Derive(inventory, palettes.Keys.ToHashSet(StringComparer.Ordinal));
        string identity = set.Canvases[0].MediaId;
        CharacterMediaReferenceSet duplicated = set with { Canvases = [.. set.Canvases, set.Canvases.Single(canvas => canvas.MediaId == identity) with { Path = "FACE00I0.CIF" }] };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => CharacterMediaPublisher.PublishAll(duplicated, sources, palettes, inventory));
        Assert.Contains(identity, error.Message, StringComparison.Ordinal);
        Assert.Contains("more than one canvas reference", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A family entry says what is true of the family it names. The FACE family publishes 160 canvases here,
    /// so an entry that claimed nothing reads the FACE format would be false in the same run that reads it -
    /// while a BSS container really does read and really does have no pixel decoder.
    /// </summary>
    [Fact]
    public void StatesEachUnpublishableFamilyWithACauseThatMatchesTheRun()
    {
        (Dictionary<string, ReadOnlyMemory<byte>> sources, Dictionary<string, Arena2Palette> palettes) = Corpus();
        List<(string Path, ReadOnlyMemory<byte> Bytes)> supplied = [.. sources.Select(entry => (entry.Key, entry.Value))];
        CharacterMediaInventory inventory = CharacterMediaInventory.Enumerate(supplied, new HashSet<string>(StringComparer.Ordinal), "arena2");
        CharacterMediaReferenceSet set = CharacterMediaReferences.Derive(inventory, palettes.Keys.ToHashSet(StringComparer.Ordinal));
        CharacterMediaPassResult pass = CharacterMediaPublisher.PublishAll(set, sources, palettes, inventory);

        Assert.Equal(["BSS", "FACE"], pass.UnreadableFamilies.Select(family => family.Family));
        CharacterMediaUnreadableFamily bss = pass.UnreadableFamilies[0];
        Assert.Equal("BSS sprite container", bss.Kind);
        Assert.Contains("missing pixel decoder", bss.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing in this repository reads", bss.Reason, StringComparison.Ordinal);
        Assert.Equal("Assets/Scripts/API/BssFile.cs", bss.DonorAnchor);
        CharacterMediaUnreadableFamily grid = pass.UnreadableFamilies[1];
        Assert.Equal("fixed-cell RCI grid", grid.Kind);
        Assert.Contains("nothing in this repository slices a cell's pixels", grid.Reason, StringComparison.Ordinal);
        // The grid has no donor reader to name: the donor reads the grid and this repository enumerates its
        // cells, so naming a reader here would be naming the wrong gap.
        Assert.Equal(string.Empty, grid.DonorAnchor);

        // Every file either family names is one the pass actually refused, and the aggregate covers them
        // exactly: an entry that claimed a file the run published would be the false claim in one direction.
        string[] refusedFiles = [.. pass.Refusals
            .SelectMany(refusal => pass.UnreadableFamilies.SelectMany(family => family.Files).Where(file => refusal.Contains(file, StringComparison.Ordinal)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
        Assert.Equal(
            pass.UnreadableFamilies.SelectMany(family => family.Files).Order(StringComparer.OrdinalIgnoreCase),
            refusedFiles);
        Assert.All(pass.UnreadableFamilies.SelectMany(family => family.Files), file => Assert.DoesNotContain(
            pass.Artifacts,
            artifact => System.IO.Path.GetFileName(artifact.Reference.Path).Equals(file, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The documented character-media inventory and the supplied corpus have to agree, which is what the
    /// tool's <c>--inventory</c> argument is for: a documented file the corpus lacks would make the
    /// publication account for a family the source does not carry, and a supplied file with no row is
    /// content this publication would emit without a record.
    /// </summary>
    [Fact]
    public void TheDocumentedInventoryAndTheSuppliedCorpusAgree()
    {
        IReadOnlyList<SourceInventoryRow> rows = SourceManifestBuilder.ReadInventory(
            File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")));
        string[] documented = [.. rows
            .Where(row => row.RowType == "file" && StringComparer.Ordinal.Equals(row.FamilyId, "CNT-021"))
            .Select(row => System.IO.Path.GetFileName(row.PathOrPattern))
            .Order(StringComparer.OrdinalIgnoreCase)];
        Assert.Equal(87, documented.Length);

        string arena2 = Path.Combine(RepositoryRoot(), "local", "arena2");
        string[] supplied = [.. Directory.EnumerateFiles(arena2)
            .Select(path => System.IO.Path.GetFileName(path))
            .Where(name => name is { Length: > 0 } && CharacterMediaInventory.IsDocumentedFamily(name))
            .Select(name => name!)
            .Order(StringComparer.OrdinalIgnoreCase)];

        Assert.Equal(documented, supplied);
    }

    /// <summary>
    /// A file no reader opens inside a family this run publishes from is a file-level gap, not a format
    /// nothing reads: an entry claiming otherwise would be false in the same run that reads its siblings.
    /// </summary>
    [Fact]
    public void StatesAFileLevelGapWithoutClaimingTheFamilyIsUnreadable()
    {
        (Dictionary<string, ReadOnlyMemory<byte>> sources, Dictionary<string, Arena2Palette> palettes) = Corpus();
        // A file the name rule puts in the FACE family whose bytes no reader accepts, alongside the 220
        // canvases the family really publishes.
        sources["FACE99I0.CIF"] = new byte[5000];
        List<(string Path, ReadOnlyMemory<byte> Bytes)> supplied = [.. sources.Select(entry => (entry.Key, entry.Value))];
        CharacterMediaInventory inventory = CharacterMediaInventory.Enumerate(supplied, new HashSet<string>(StringComparer.Ordinal), "arena2");
        CharacterMediaReferenceSet set = CharacterMediaReferences.Derive(inventory, palettes.Keys.ToHashSet(StringComparer.Ordinal), sources);
        CharacterMediaPassResult pass = CharacterMediaPublisher.PublishAll(set, sources, palettes, inventory);

        // The family now has two entries, keyed by cause: the grid whose cells nothing slices, and the file
        // no reader opens. Neither may claim the family is a format nothing reads.
        CharacterMediaUnreadableFamily face = pass.UnreadableFamilies.Single(family => family.Files.Contains("FACE99I0.CIF"));
        Assert.Equal(["FACE99I0.CIF"], face.Files);
        Assert.DoesNotContain("nothing in this repository reads the FACE format", face.Reason, StringComparison.Ordinal);
        Assert.Contains("file-level gap", face.Reason, StringComparison.Ordinal);
        // The family really does publish here, which is what makes the claim above false if it were made.
        Assert.Contains(pass.Artifacts, artifact => artifact.Reference.Family == "FACE");
        // The unavailable record says the same: no reader opened this file, not that the family has none.
        CharacterMediaUnavailable unavailable = set.Unavailable.Single(entry => System.IO.Path.GetFileName(entry.Path) == "FACE99I0.CIF");
        Assert.DoesNotContain("Nothing in this repository reads this format", unavailable.Reason, StringComparison.Ordinal);
        Assert.Contains("No reader in this repository opened this file", unavailable.Reason, StringComparison.Ordinal);
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
