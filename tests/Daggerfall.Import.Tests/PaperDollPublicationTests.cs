using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The paper doll's placements and worn-item images: each canvas at its header offset less the donor's
/// paper-doll origin, every record an item template reaches in every dye, and the mask published apart.
/// </summary>
public sealed class PaperDollPublicationTests
{
    [Fact]
    public void A_template_reaches_its_variants_its_cloak_interior_and_its_right_hand_record_across_bodies()
    {
        PaperDollItemTemplate helm = new(107, "Armor", 245, 27, 6, false, false);
        PaperDollItemTemplate longsword = new(120, "Weapons", 234, 12, 0, true, false);
        PaperDollItemTemplate claymore = new(122, "Weapons", 234, 4, 0, false, false);
        PaperDollItemTemplate cloak = new(154, "MensClothing", 239, 21, 6, false, true);
        IReadOnlyDictionary<int, IReadOnlySet<int>> reach = PaperDollItemPublication.ReachableRecords([helm, longsword, claymore, cloak]);

        // Armour is drawn from a female (245-248) or male (249-252) archive per body morphology.
        Assert.Equal([245, 246, 247, 248, 249, 250, 251, 252], reach.Keys.Where(key => key >= 245).Order());
        Assert.Equal([27, 28, 29, 30, 31, 32], reach[251].Order());
        // A one-handed weapon in the right hand is the next record; a two-handed one has only its own.
        Assert.Equal([4, 12, 13], reach[234].Order());
        // A cloak's first record is its interior and its variants follow it, for every body morphology.
        Assert.Equal([21, 22, 23, 24, 25, 26, 27], reach[241].Order());
        Assert.Equal([239, 240, 241, 242], reach.Keys.Where(key => key is >= 239 and <= 242).Order());
    }

    [CorpusFact("BODY00I0.IMG", "FACE00I0.CIF", "SCBG00I0.IMG")]
    public void Places_the_body_and_head_at_their_header_offsets_less_the_paper_doll_origin()
    {
        Dictionary<string, ReadOnlyMemory<byte>> sources = new(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(TestData.CorpusRoot).Select(Path.GetFileName).Where(name => name!.StartsWith("BODY", StringComparison.Ordinal)
            || name.StartsWith("FACE", StringComparison.Ordinal) || name.StartsWith("SCBG", StringComparison.Ordinal))!)
            sources[file!] = File.ReadAllBytes(TestData.Corpus(file!));
        CharacterMediaInventory inventory = CharacterMediaInventory.Enumerate([.. sources.Select(entry => (entry.Key, entry.Value))],
            new HashSet<string>(StringComparer.Ordinal), "no published consumer yet", "arena2");
        DaggerfallCharacterPresentation presentation = DaggerfallCharacterPresentationBuilder.Build(inventory,
            new HashSet<string>([CharacterMediaReferences.ArtPalette, CharacterMediaReferences.NightskyPalette]),
            [new DaggerfallRaceKey("breton", 1, new DaggerfallCatalogSource("arena2/test"))], new Dictionary<string, string>(), sources: sources);

        DaggerfallCharacterLayer Layer(string name) => presentation.Layers.Single(layer => layer.Race == "breton" && layer.Layer == name);
        // BODY00I0's header puts it at (222, 41) on the inventory screen; the doll starts at (200, 8).
        Assert.Equal(new DaggerfallPaperDollPlacement(22, 33, 69, 145), Layer("body.male.unclothed").Placement);
        Assert.Equal(new DaggerfallPaperDollPlacement(46, 9, 20, 27), Layer("head.male.0").Placement);
        // The background is placed by the doll's frame - the donor's (8, 7) crop of it - not by its header.
        Assert.Null(Layer("background").Placement);
        Assert.Equal(new DaggerfallPaperDollFrame(110, 184, -8, -7, 125, 198), presentation.PaperDoll);
    }

    [CorpusFact("ART_PAL.COL", "TEXTURE.234", "TEXTURE.237", "TEXTURE.251", "TEXTURE.432", "TEXTURE.433")]
    public void Publishes_each_reached_record_placed_dyed_and_masked_as_the_donor_draws_it()
    {
        Dictionary<int, ReadOnlyMemory<byte>> archives = PaperDollItemPublication.Archives.ToDictionary(number => number, number => Read($"TEXTURE.{number:000}"));
        Arena2Palette palette = PaletteDecoder.Decode(Read("ART_PAL.COL").Span, "arena2/ART_PAL.COL");
        PaperDollItemPass pass = PaperDollItemPublication.Publish(archives, palette,
        [
            new(107, "Armor", 245, 27, 6, false, false),
            new(120, "Weapons", 234, 12, 0, true, false),
            new(202, "WomensClothing", 235, 50, 5, false, false),
        ]);

        PaperDollItemImage Image(int archive, int record) => pass.Images.Single(image => image.Archive == archive && image.Record == record);

        // The longsword in the left hand is record 12 and in the right hand 13, each at its own header offset.
        PaperDollItemImage left = Image(234, 12), right = Image(234, 13);
        TextureArchive weapons = TextureArchive.Parse(archives[234].Span, "arena2/TEXTURE.234");
        TextureRecordInfo header = weapons.GetRecordInfo(12);
        Assert.Equal(new DaggerfallPaperDollPlacement(header.XOffset - 200, header.YOffset - 8, header.Width, header.Height), left.Placement);
        Assert.NotEqual(left.Placement, right.Placement);
        // Steel, leather and chain keep the record; iron swaps the metal range, so it is its own image.
        Assert.Equal(PaperDollItemPublication.MediaId(234, 12), left.Dyes["steel"]);
        Assert.Equal(PaperDollItemPublication.MediaId(234, 12, "iron"), left.Dyes["iron"]);
        Assert.Contains(pass.Artifacts, artifact => artifact.MediaId == left.Dyes["iron"]);

        // A helm's mask clears the hair beneath it, so the mask is published beside the helm.
        Assert.Contains(pass.Images.Where(image => image.Archive == 251), image => image.MaskMediaId is not null);
        Assert.All(pass.Images.Where(image => image.MaskMediaId is not null), image => Assert.Contains(pass.Artifacts, artifact => artifact.MediaId == image.MaskMediaId));

        // The donor corrects the header of two human-female short-shirt records to (237, 43).
        Assert.Equal(new DaggerfallPaperDollPlacement(37, 35, Image(237, 52).Placement.Width, Image(237, 52).Placement.Height), Image(237, 52).Placement);
        // Clothing's blue dye is the record as stored; red swaps the clothing range.
        Assert.Equal(PaperDollItemPublication.MediaId(237, 50), Image(237, 50).Dyes["blue"]);
        Assert.Equal(10, Image(237, 50).Dyes.Count);

        // The artifact archives are published whole, whatever the templates reach, each record undyed.
        foreach (int number in PaperDollItemPublication.ArtifactArchives)
        {
            int records = TextureArchive.Parse(archives[number].Span, $"arena2/TEXTURE.{number}").RecordCount;
            Assert.Equal(Enumerable.Range(0, records), pass.Images.Where(image => image.Archive == number).Select(image => image.Record));
            Assert.All(pass.Images.Where(image => image.Archive == number), image => Assert.Equal(
                new Dictionary<string, string> { [PaperDollItemPublication.UnchangedDye] = PaperDollItemPublication.MediaId(number, image.Record) }, image.Dyes));
        }
        // The donor's artifact table names records up to 26.
        Assert.True(TextureArchive.Parse(archives[432].Span, "arena2/TEXTURE.432").RecordCount > 26);
    }

    private static ReadOnlyMemory<byte> Read(string file) => File.ReadAllBytes(TestData.Corpus(file));
}
