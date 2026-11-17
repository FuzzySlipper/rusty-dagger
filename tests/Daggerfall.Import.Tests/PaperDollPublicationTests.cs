using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The paper doll's placements and worn-item images: each canvas at its header offset less the donor's
/// paper-doll origin, every record of the worn-item archives in every dye, the mask published apart, and every
/// record of the inventory image archives.
/// </summary>
public sealed class PaperDollPublicationTests
{
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
            new HashSet<string>([CharacterMediaReferences.ArtPalette, CharacterMediaReferences.NightskyPalette]), sources: sources);

        DaggerfallCharacterLayer Layer(string name) => presentation.Layers.Single(layer => layer.DonorRaceId == 1 && layer.Layer == name);
        // BODY00I0's header puts it at (222, 41) on the inventory screen; the doll starts at (200, 8).
        Assert.Equal(new DaggerfallPaperDollPlacement(22, 33, 69, 145), Layer("body.male.unclothed").Placement);
        Assert.Equal(new DaggerfallPaperDollPlacement(46, 9, 20, 27), Layer("head.male.0").Placement);
        // The background is placed by the doll's frame - the donor's (8, 7) crop of it - not by its header.
        Assert.Null(Layer("background").Placement);
        Assert.Equal(new DaggerfallPaperDollFrame(110, 184, -8, -7, 125, 198), presentation.PaperDoll);
    }

    [CorpusFact("ART_PAL.COL", "TEXTURE.234", "TEXTURE.237", "TEXTURE.251", "TEXTURE.432", "TEXTURE.433", "TEXTURE.207", "TEXTURE.254")]
    public void Publishes_every_worn_record_placed_dyed_and_masked_as_the_donor_draws_it()
    {
        Dictionary<int, ReadOnlyMemory<byte>> archives = PaperDollItemPublication.Archives.Concat(PaperDollItemPublication.IconArchives)
            .ToDictionary(number => number, number => Read($"TEXTURE.{number:000}"));
        Arena2Palette palette = PaletteDecoder.Decode(Read("ART_PAL.COL").Span, "arena2/ART_PAL.COL");
        PaperDollItemPass pass = PaperDollItemPublication.Publish(archives, palette);

        // Every record of every worn-item archive is published, whichever template draws it.
        foreach (int number in PaperDollItemPublication.Archives)
            Assert.Equal(Enumerable.Range(0, TextureArchive.Parse(archives[number].Span, $"arena2/TEXTURE.{number}").RecordCount),
                pass.Images.Where(image => image.Archive == number).Select(image => image.Record));

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

        // The artifact archives' records are published undyed.
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

    [CorpusFact("ART_PAL.COL", "TEXTURE.207", "TEXTURE.209", "TEXTURE.254")]
    public void Publishes_every_record_of_the_inventory_image_archives_once()
    {
        Dictionary<int, ReadOnlyMemory<byte>> archives = PaperDollItemPublication.Archives.Concat(PaperDollItemPublication.IconArchives)
            .ToDictionary(number => number, number => Read($"TEXTURE.{number:000}"));
        Arena2Palette palette = PaletteDecoder.Decode(Read("ART_PAL.COL").Span, "arena2/ART_PAL.COL");
        PaperDollItemPass pass = PaperDollItemPublication.Publish(archives, palette);

        foreach (int number in PaperDollItemPublication.IconArchives)
        {
            int records = TextureArchive.Parse(archives[number].Span, $"arena2/TEXTURE.{number}").RecordCount;
            Assert.Equal(Enumerable.Range(0, records).Select(record => $"item.{number}.{record}"), pass.Icons.Where(id => id.StartsWith($"item.{number}.", StringComparison.Ordinal)));
            Assert.Contains($"arena2/TEXTURE.{number:000}", pass.Sources);
        }
        Assert.Equal(pass.Icons.Count, pass.Icons.Distinct().Count());
        IndexedTextureFrame arrow = TextureArchive.Parse(archives[207].Span, "arena2/TEXTURE.207").DecodeFrame(16, 0);
        PaperDollArtifact published = pass.Artifacts.Single(artifact => artifact.MediaId == "item.207.16");
        Assert.Equal((arrow.Width, arrow.Height), (published.Width, published.Height));
        Assert.Equal(pass.Icons, PaperDollItemPublication.Section(pass)["icons"]!.AsArray().Select(id => id!.GetValue<string>()));
    }

    private static ReadOnlyMemory<byte> Read(string file) => File.ReadAllBytes(TestData.Corpus(file));
}
