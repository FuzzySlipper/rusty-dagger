using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The character, face and story-art inventory: every documented family file with its
/// canvas, key and candidate use, the files no decoder reads, and any key two files claim.
/// </summary>
public sealed class CharacterMediaInventoryTests
{
    [Fact]
    public void Reconciles_every_documented_family_count()
    {
        CharacterMediaInventory inventory = ReadInventory();

        // The documented counts are 32;17;9;10;4;9;3;3 for BODY, FACE (including FACES.CIF),
        // CHAR, CUST, NITE, SCBG, CEL and BSS, and the corpus supplies exactly those.
        Assert.Equal(87, inventory.Files.Count);
        Assert.All(CharacterMediaInventory.DocumentedFamilies, family =>
            Assert.Equal(family.Count, inventory.Family(family.Prefix).Count()));
        Assert.Equal(17, inventory.Family("FACE").Count());
        Assert.Contains(inventory.Family("FACE"), file => file.Path == "FACES.CIF");
    }

    [Fact]
    public void Reads_the_families_the_repository_has_decoders_for()
    {
        CharacterMediaInventory inventory = ReadInventory();

        // Every supplied file but six reads: the face CIFs are sequences of IMG records and
        // FACES.CIF is the fixed-cell grid the classic reader names, so the six files retained
        // with a reason are the three CEL and three BSS files whose readers this repository
        // does not have.
        // Every documented family now has a reader here: the class portraits through the FLC
        // container and the story sprites through their own, so nothing in this corpus is retained
        // unreadable any more.
        Assert.Equal(87, inventory.Files.Count(file => file.Decode != Arena2CanvasKind.Unread));
        Assert.Empty(inventory.Unsupported);
        Assert.All(inventory.Files, file => Assert.Contains(file.Family, "BODY FACE CHAR CUST NITE SCBG CEL BSS".Split(' ')));
        Assert.All(inventory.Files, file => Assert.False(string.IsNullOrWhiteSpace(file.UseCandidate)));
    }

    [Fact]
    public void Enumerates_the_canvases_inside_a_file_rather_than_one_canvas_per_file()
    {
        CharacterMediaInventory inventory = ReadInventory();

        // Each face CIF is ten records and FACES.CIF is 61 grid cells, so the face family
        // supplies 221 canvases in 17 files: a file count would report 17 and hide 204.
        Assert.Equal(221, inventory.Family("FACE").Sum(file => file.CanvasCount));
        Assert.All(inventory.Family("FACE"), file =>
            Assert.Equal(file.Path == "FACES.CIF" ? 61 : 10, file.CanvasCount));

        // The night and rest art is a documented headerless shape of 512x219, not a compressed
        // IMG record: the four files read at the shape their length establishes.
        Assert.All(inventory.Family("NITE"), file =>
        {
            Assert.Equal(Arena2CanvasKind.HeaderlessCanvas, file.Decode);
            Assert.Equal((512, 219), (file.Canvases[0].Width, file.Canvases[0].Height));
        });
    }

    [Fact]
    public void Retains_every_unbound_file_with_its_candidate_use()
    {
        CharacterMediaInventory inventory = ReadInventory();

        Assert.All(inventory.Unbound, file =>
        {
            Assert.Equal(string.Empty, file.Consumer);
            Assert.False(string.IsNullOrWhiteSpace(file.UseCandidate));
            Assert.Contains("candidate use", file.Note, StringComparison.Ordinal);
        });
        Assert.NotEmpty(inventory.Unbound);
    }

    [Fact]
    public void Reports_any_key_two_files_claim()
    {
        CharacterMediaInventory inventory = ReadInventory();

        // FACE00I0.CIF and FACES.CIF are different keys, so the corpus has no clash; the
        // report is what makes a clash visible rather than resolved by ordering.
        Assert.Empty(inventory.DuplicateKeys);

        CharacterMediaInventory clashing = CharacterMediaInventory.Enumerate(
            [("BODY00I0.IMG", ValidImage()), ("body00i0.IMG", ValidImage())],
            new HashSet<string>(StringComparer.Ordinal),
            "fixture consumer",
            "fixture");

        (string Key, IReadOnlyList<string> Files) clash = Assert.Single(clashing.DuplicateKeys);
        Assert.Equal("BODY00I0", clash.Key);
        // Which files clash, not just how many: a count alone passes for the wrong pair.
        Assert.Equal(["BODY00I0.IMG", "body00i0.IMG"], clash.Files);
    }

    [Fact]
    public void Names_the_reader_a_format_needs_rather_than_calling_the_source_bad()
    {
        CharacterMediaInventory inventory = ReadInventory();

        // The two families that used to name a missing reader now read: a class portrait through the
        // FLC container and a story sprite through its own, so the note records the shape instead.
        Assert.All(inventory.Family("CEL"), portrait => Assert.Equal(Arena2CanvasKind.FlcAnimation, portrait.Decode));
        Assert.All(inventory.Family("BSS"), sprite => Assert.Equal(Arena2CanvasKind.BssFrames, sprite.Decode));
        Assert.All(inventory.Family("BSS"), sprite => Assert.DoesNotContain("does not have", sprite.Note, StringComparison.Ordinal));
    }

    [Fact]
    public void Keeps_a_binding_whether_or_not_the_file_reads()
    {
        // Binding and decodability are separate facts: a consumer that binds a file still binds it
        // whatever a reader makes of it, and the record says both.
        CharacterMediaInventory inventory = CharacterMediaInventory.Enumerate(
            [("MAGE.CEL", File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/MAGE.CEL")))],
            new HashSet<string>(["MAGE.CEL"], StringComparer.Ordinal),
            "the fixture consumer",
            "fixture");

        CharacterMediaRecord record = Assert.Single(inventory.Files);
        Assert.Equal(MediaBinding.Admitted, record.Binding);

        // The portrait reads, and its 15 frames are its canvases; the binding is unaffected by that.
        Assert.Equal(Arena2CanvasKind.FlcAnimation, record.Decode);
        Assert.Equal(15, record.CanvasCount);
        Assert.Equal("the fixture consumer", record.Consumer);
    }

    [Fact]
    public void The_documented_inventory_carries_the_families_it_claims()
    {
        // The family counts come from the manifest's CNT-021 row; this checks the corpus against
        // the documented vector in both directions rather than trusting either side, which is
        // what keeps this table from being a second, unchecked source.
        string line = File.ReadLines(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv"))
            .Single(value => value.StartsWith("CNT-021,family,", StringComparison.Ordinal));
        int[] documented = [.. line.Split(',')[5].Split(';', StringSplitOptions.TrimEntries).Select(int.Parse)];

        CharacterMediaInventory inventory = ReadInventory();
        int[] measured = [.. CharacterMediaInventory.DocumentedFamilies.Select(entry => inventory.Family(entry.Prefix).Count())];
        Assert.Equal(CharacterMediaInventory.DocumentedFamilies.Length, documented.Length);
        Assert.Equal(documented, measured);
    }

    [Fact]
    public void Derives_each_canvas_palette_and_refuses_one_that_is_not_supplied()
    {
        CharacterMediaInventory inventory = ReadInventory();
        HashSet<string> supplied = [CharacterMediaReferences.ArtPalette, CharacterMediaReferences.NightskyPalette];
        CharacterMediaReferenceSet set = CharacterMediaReferences.Derive(inventory, supplied);
        IReadOnlyList<CharacterCanvasReference> references = set.Canvases;

        // One reference per readable canvas, and none for a file nothing reads: the six unread
        // files supply no canvas to publish rather than one invented for them.
        Assert.Equal(inventory.Files.Where(file => file.Decode != Arena2CanvasKind.Unread).Sum(file => file.CanvasCount), references.Count);
        Assert.DoesNotContain(references, reference => inventory.Files.Single(file => file.Path == reference.Path).Decode == Arena2CanvasKind.Unread);

        // ... and they are not omitted either: every unread file is accounted for with the donor
        // reader its format would need, three CEL and three BSS, and every canvas carries the
        // binding the inventory recorded so a published reference says who claims it.
        // Nothing in this corpus is unavailable now that both container readers exist.
        Assert.Empty(set.Unavailable);
        Assert.All(set.Unavailable, entry => Assert.Equal(inventory.Files.Single(file => file.Path == entry.Path).Binding, entry.Binding));
        Assert.All(references, reference => Assert.Equal(inventory.Files.Single(file => file.Path == reference.Path).Binding, reference.Binding));

        // The classic reader's palette rule: NITE files read with NIGHTSKY.COL, everything else with
        // ART_PAL.COL, and the four NITE files in this corpus prove the rule is applied rather than
        // asserted for a family the corpus does not carry.
        Assert.All(references, reference => Assert.Equal(CharacterMediaReferences.PaletteFor(reference.Path), reference.Palette));
        Assert.Contains(references, reference => reference.Palette == CharacterMediaReferences.NightskyPalette && reference.Path.StartsWith("NITE", StringComparison.Ordinal));
        Assert.Contains(references, reference => reference.Palette == CharacterMediaReferences.ArtPalette && reference.Path.StartsWith("BODY", StringComparison.Ordinal));

        // Paper-doll companions follow the donor naming: a male body of race 0 belongs with its
        // clothed variant, its head and its background, and a female head of race 0 with her bodies
        // and the same race's background.
        CharacterCanvasReference maleBody = references.First(reference => reference.Path == "BODY00I0.IMG");
        Assert.Equal(["BODY00I1.IMG", "FACE00I0.CIF", "SCBG00I0.IMG"], maleBody.Companions);
        CharacterCanvasReference femaleHead = references.First(reference => reference.Path == "FACE10I0.CIF");
        Assert.Equal(["BODY10I0.IMG", "BODY10I1.IMG", "SCBG00I0.IMG"], femaleHead.Companions);

        // A family with no paper-doll rule claims no companion rather than guessing one.
        CharacterCanvasReference other = references.First(reference => reference.Path.StartsWith("CUST", StringComparison.Ordinal));
        Assert.Empty(other.Companions);
        Assert.Contains("no paper-doll companion rule", other.Reason, StringComparison.Ordinal);

        // Every canvas has one stable identity, derived from the file rather than from position, and
        // the concrete paper-doll names follow the donor's layers rather than the file's spelling.
        Assert.Equal(references.Count, references.Select(reference => reference.MediaId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("character.body-unclothed.male.00.0", references.First(reference => reference.Path == "BODY00I0.IMG").MediaId);
        Assert.Equal("character.body-clothed.female.00.0", references.First(reference => reference.Path == "BODY10I1.IMG").MediaId);
        Assert.Equal("character.head.female.00.3", references.First(reference => reference.Path == "FACE10I0.CIF" && reference.CanvasIndex == 3).MediaId);
        Assert.All(references, reference => Assert.StartsWith("character.", reference.MediaId, StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.MediaId.Contains(' ', StringComparison.Ordinal));

        // The inventory admits a lower-case name, so the derivation's identity and companion rules
        // have to as well: one supplied trio cannot be a paper-doll layer to one rule and an
        // unknown file to another.
        CharacterMediaInventory lowerCase = CharacterMediaInventory.Enumerate(
            [("body00i0.img", ValidImage()), ("body00i1.img", ValidImage()), ("face00i0.cif", ValidImage()), ("scbg00i0.img", ValidImage())],
            new HashSet<string>(StringComparer.Ordinal),
            "none",
            "fixture");
        CharacterMediaReferenceSet lowerSet = CharacterMediaReferences.Derive(lowerCase, supplied);
        CharacterCanvasReference body = lowerSet.Canvases.Single(reference => reference.Path == "body00i0.img");
        Assert.Equal("character.body-unclothed.male.00.0", body.MediaId);
        Assert.Equal(["BODY00I1.IMG", "FACE00I0.CIF", "SCBG00I0.IMG"], body.Companions);

        // A name that merely resembles a paper-doll file is not one: the identity falls back to the
        // family rather than claiming a race and layer the donor does not have.
        CharacterMediaInventory shapes = CharacterMediaInventory.Enumerate(
            [("BODY08I0.IMG", ValidImage()), ("BODY18I0.IMG", ValidImage()), ("FACE000I0.CIF", ValidImage())],
            new HashSet<string>(StringComparer.Ordinal),
            "none",
            "fixture");
        CharacterMediaReferenceSet shapeSet = CharacterMediaReferences.Derive(shapes, supplied);
        Assert.Equal("character.body.body08i0.0", shapeSet.Canvases.Single(reference => reference.Path == "BODY08I0.IMG").MediaId);
        Assert.Equal("character.body.body18i0.0", shapeSet.Canvases.Single(reference => reference.Path == "BODY18I0.IMG").MediaId);
        Assert.Equal("character.face.face000i0.0", shapeSet.Canvases.Single(reference => reference.Path == "FACE000I0.CIF").MediaId);

        // A paper-doll layer whose companion the corpus does not supply is refused by name too: the
        // donor's naming says which layers belong together, and publishing one alone would publish an
        // incomplete paper doll rather than a gap.
        CharacterMediaInventory incomplete = CharacterMediaInventory.Enumerate(
            [("BODY00I0.IMG", ValidImage()), ("BODY00I1.IMG", ValidImage()), ("SCBG00I0.IMG", ValidImage())],
            new HashSet<string>(StringComparer.Ordinal),
            "none",
            "fixture");
        InvalidOperationException alone = Assert.Throws<InvalidOperationException>(() => CharacterMediaReferences.Derive(incomplete, supplied));
        Assert.Contains("BODY00I0.IMG", alone.Message, StringComparison.Ordinal);
        Assert.Contains("FACE00I0.CIF", alone.Message, StringComparison.Ordinal);
        Assert.Contains("incomplete paper doll", alone.Message, StringComparison.Ordinal);

        // A canvas whose palette is not supplied is refused by name, not painted with a default.
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => CharacterMediaReferences.Derive(inventory, new HashSet<string>(StringComparer.Ordinal) { CharacterMediaReferences.ArtPalette }));
        Assert.Contains("NIGHTSKY.COL", refused.Message, StringComparison.Ordinal);
        Assert.Contains("does not supply", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Publishes_each_race_layers_with_the_donor_race_value_mapped_to_its_media_index()
    {
        CharacterMediaInventory inventory = ReadInventory();
        HashSet<string> supplied = [CharacterMediaReferences.ArtPalette, CharacterMediaReferences.NightskyPalette];
        CharacterMediaReferenceSet set = CharacterMediaReferences.Derive(inventory, supplied);

        // The eight playable races carry the donor's one-based values, as the catalog does.
        DaggerfallRaceKey[] races =
        [
            new("breton", 1, Source()), new("redguard", 2, Source()), new("nord", 3, Source()), new("dark-elf", 4, Source()),
            new("high-elf", 5, Source()), new("wood-elf", 6, Source()), new("khajiit", 7, Source()), new("argonian", 8, Source()),
        ];
        // The classic corpus supplies three class portraits, named for the classes they depict.
        Dictionary<string, string> careers = new(StringComparer.Ordinal)
        {
            ["class00"] = "Mage", ["class01"] = "Rogue", ["class02"] = "Warrior",
            ["class03"] = "Battle Mage", ["class04"] = "Nightblade",
        };
        DaggerfallCharacterPresentation presentation = DaggerfallCharacterPresentationBuilder.Build(inventory, supplied, races, careers);

        // Donor value minus one is the media index: Breton's background is SCBG00, Redguard's SCBG01.
        Assert.Equal("SCBG00I0.IMG", presentation.Layers.Single(layer => layer.Race == "breton" && layer.Layer == "background").SourceFile);
        Assert.Equal("SCBG01I0.IMG", presentation.Layers.Single(layer => layer.Race == "redguard" && layer.Layer == "background").SourceFile);
        Assert.Equal("BODY00I0.IMG", presentation.Layers.Single(layer => layer.Race == "breton" && layer.Layer == "body.male.unclothed").SourceFile);
        Assert.Equal("BODY17I1.IMG", presentation.Layers.Single(layer => layer.Race == "argonian" && layer.Layer == "body.female.clothed").SourceFile);

        // Every layer resolves to a published canvas, and the section refuses one that does not.
        HashSet<string> published = [.. set.Canvases.Select(reference => reference.MediaId)];
        presentation.Validate(published);
        DaggerfallCharacterPresentation broken = presentation with
        {
            Layers = [.. presentation.Layers, new DaggerfallCharacterLayer("breton", 1, "head.male.99", "character.head.male.00.99", "FACE00I0.CIF", CharacterMediaReferences.ArtPalette, MediaBinding.Admitted)],
        };
        InvalidOperationException dangling = Assert.Throws<InvalidOperationException>(() => broken.Validate(published));
        Assert.Contains("resolve to nothing", dangling.Message, StringComparison.Ordinal);

        // Every race the corpus draws contributes its background, four bodies and twenty heads, and
        // every layer names a palette rather than defaulting to one.
        Assert.Equal(8 * (1 + 4 + (2 * DaggerfallCharacterPresentationBuilder.HeadsPerRaceAndGender)), presentation.Layers.Count);

        // Every supplied file is accounted for, including the six nothing reads and the readable
        // files no layer uses: a family cannot go missing between the inventory and the pack.
        Assert.Equal(87, presentation.Files.Count);
        Assert.DoesNotContain(presentation.Files, file => file.Outcome == CharacterFileOutcome.Unreadable);
        Assert.Equal(87, presentation.Files.Count(file => file.Outcome != CharacterFileOutcome.Unreadable));
        // Fifty-seven files a layer draws from and thirty that read and no layer uses yet; the two
        // counts account for every supplied file, which is the property worth holding.
        Assert.Equal(60, presentation.Files.Count(file => file.Outcome == CharacterFileOutcome.Referenced));
        Assert.Equal(27, presentation.Files.Count(file => file.Outcome == CharacterFileOutcome.Unreferenced));
        Assert.Equal(87, presentation.Files.Count);
        Assert.All(presentation.Files, file => Assert.False(string.IsNullOrWhiteSpace(file.Reason)));
        Assert.Contains(presentation.Files, file => file.Path == "CMPA00I0.BSS" && file.Outcome == CharacterFileOutcome.Unreferenced);
        // The class portraits read now, and no layer uses them yet: the career references that would
        // are the next increment, so they are readable and unreferenced rather than unavailable.
        Assert.Contains(presentation.Files, file => file.Path == "MAGE.CEL" && file.Outcome == CharacterFileOutcome.Referenced);
        // The faction face grid is the section's non-racial family: sixty-one cells a social or
        // escort view resolves by faction index, so it is referenced rather than unreferenced.
        Assert.Contains(presentation.Files, file => file.Path == "FACES.CIF" && file.Outcome == CharacterFileOutcome.Referenced);
        Assert.Equal(61, presentation.Faces.Count);
        Assert.Equal(Enumerable.Range(0, 61), presentation.Faces.Select(face => face.Index));
        Assert.All(presentation.Faces, face => Assert.Equal("FACES.CIF", face.SourceFile));
        Assert.Equal("character.faction-face.00", presentation.Faces[0].MediaId);
        Assert.Equal("character.faction-face.60", presentation.Faces[60].MediaId);
        Assert.Equal(CharacterMediaReferences.ArtPalette, presentation.Faces[0].Palette);
        Assert.All(presentation.Layers, layer => Assert.Equal(CharacterMediaReferences.ArtPalette, layer.Palette));

        // A race value with no paper-doll subclass is recorded rather than mapped onto the next index.
        // A career the corpus does not depict says so rather than borrowing another class's art.
        Assert.Equal(3, presentation.Careers.Count);
        Assert.Equal(["class00", "class01", "class02"], presentation.Careers.Select(portrait => portrait.CareerId));
        Assert.Equal("character.portrait.mage.0", presentation.Careers[0].MediaId);
        Assert.Equal(("MAGE.CEL", 15), (presentation.Careers[0].SourceFile, presentation.Careers[0].FrameCount));
        Assert.Equal(("ROGUE.CEL", 10), (presentation.Careers[1].SourceFile, presentation.Careers[1].FrameCount));
        Assert.Equal(("WARRIOR.CEL", 15), (presentation.Careers[2].SourceFile, presentation.Careers[2].FrameCount));
        Assert.Equal(["class03", "class04"], presentation.CareersWithoutPortrait.Select(entry => entry.CareerId));
        Assert.All(presentation.CareersWithoutPortrait, entry => Assert.Contains("no class portrait named for", entry.Reason, StringComparison.Ordinal));

        DaggerfallCharacterPresentation beyond = DaggerfallCharacterPresentationBuilder.Build(
            inventory, supplied, [.. races, new DaggerfallRaceKey("vampire", 9, Source())], careers);
        Assert.DoesNotContain(beyond.Layers, layer => layer.Race == "vampire");
        Assert.Contains(beyond.RacesWithoutMedia, entry => entry.Race == "vampire" && entry.Reason.Contains("no subclass", StringComparison.Ordinal));
    }

    /// <summary>The documented source every race in this fixture comes from.</summary>
    private static DaggerfallCatalogSource Source() => new("CNT-021", "docs/coverage/content-source-manifest.csv");

    [Fact]
    public void Reports_the_same_records_whatever_order_the_sources_arrive_in()
    {
        // The consumer/disposition report is deterministic: the same files in any input
        // order produce the same sequence.
        List<(string Path, ReadOnlyMemory<byte> Bytes)> sources =
        [
            ("SCBG00I0.IMG", ValidImage()), ("MAGE.CEL", new byte[16]), ("BODY00I0.IMG", ValidImage()),
        ];
        CharacterMediaInventory forward = CharacterMediaInventory.Enumerate(sources, new HashSet<string>(StringComparer.Ordinal), "none", "fixture");
        CharacterMediaInventory reversed = CharacterMediaInventory.Enumerate([.. Enumerable.Reverse(sources)], new HashSet<string>(StringComparer.Ordinal), "none", "fixture");

        Assert.Equal(forward.Files.Select(file => file.Path), reversed.Files.Select(file => file.Path));
        Assert.Equal(forward.Files.Select(file => file.Binding), reversed.Files.Select(file => file.Binding));
    }

    [Fact]
    public void Refuses_a_path_supplied_twice_or_not_at_all()
    {
        // A path is the identity and the consumer binds against it, so a missing or repeated
        // path is refused rather than producing two records for one file or a null.
        Assert.Throws<ArgumentException>(() => CharacterMediaInventory.Enumerate(
            [(null!, ValidImage())], new HashSet<string>(StringComparer.Ordinal), "none", "fixture"));
        Assert.Throws<ArgumentException>(() => CharacterMediaInventory.Enumerate(
            [("BODY00I0.IMG", ValidImage()), ("BODY00I0.IMG", ValidImage())], new HashSet<string>(StringComparer.Ordinal), "none", "fixture"));
    }

    [Fact]
    public void Refuses_a_file_in_no_documented_family()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => CharacterMediaInventory.Enumerate(
            [("NOTAFAMILY.IMG", new byte[16])],
            new HashSet<string>(StringComparer.Ordinal),
            "fixture consumer",
            "fixture"));

        Assert.Contains("none of the documented character media families", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Binds_only_the_files_a_consumer_names()
    {
        CharacterMediaInventory inventory = CharacterMediaInventory.Enumerate(
            [("SCBG00I0.IMG", ValidImage())],
            new HashSet<string>(["SCBG00I0.IMG"], StringComparer.Ordinal),
            "the fixture consumer",
            "fixture");

        CharacterMediaRecord record = Assert.Single(inventory.Files);
        Assert.Equal(MediaBinding.Admitted, record.Binding);
        Assert.Equal("the fixture consumer", record.Consumer);
        Assert.Equal("SCBG", record.Family);
    }

    private static byte[] ValidImage() => File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/MAIN00I0.IMG"));

    private static CharacterMediaInventory ReadInventory()
    {
        string root = RepositoryRoot();
        string[] extensions = [".IMG", ".CIF", ".CEL", ".BSS"];
        List<(string Path, ReadOnlyMemory<byte> Bytes)> sources = [];
        foreach (string path in Directory.GetFiles(Path.Combine(root, "local/arena2")))
        {
            string name = Path.GetFileName(path);
            string extension = Path.GetExtension(name).ToUpperInvariant();
            if (!extensions.Contains(extension))
            {
                continue;
            }

            bool documented = CharacterMediaInventory.DocumentedFamilies.Any(family =>
                family.Prefix is "CEL" or "BSS"
                    ? extension == $".{family.Prefix}"
                    : name.StartsWith(family.Prefix, StringComparison.OrdinalIgnoreCase));
            if (documented)
            {
                sources.Add((name, File.ReadAllBytes(path)));
            }
        }

        return CharacterMediaInventory.Enumerate(sources, new HashSet<string>(StringComparer.Ordinal), "no published consumer yet", "local/arena2");
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
