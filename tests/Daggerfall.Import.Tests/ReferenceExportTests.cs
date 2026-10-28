using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Daggerfall.Import.Tool.ReferenceExports;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class ReferenceExportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "daggerfall-reference-export-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void An_existing_experiment_is_preserved_when_an_export_is_repeated()
    {
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "experiment.png");
        byte[] editedImage = [1, 2, 3, 4];
        File.WriteAllBytes(file, editedImage);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => new ReferenceExport("unused", root));
        Assert.Contains("preserve existing references and experiments", error.Message, StringComparison.Ordinal);
        Assert.Equal(editedImage, File.ReadAllBytes(file));
        Assert.Single(Directory.EnumerateFiles(root));
    }

    [Fact]
    public void Organization_includes_templates_missing_from_donor_group_enumerations()
    {
        Assert.All(Enumerable.Range(0, 288), index => Assert.NotEmpty(DaggerfallItemTemplatesBuilder.GroupsForTemplate(index)));
        Assert.Equal(["Furniture"], DaggerfallItemTemplatesBuilder.GroupsForTemplate(251));
        Assert.Equal(["QuestItems", "MiscItems"], DaggerfallItemTemplatesBuilder.GroupsForTemplate(281));
        Assert.Equal("WEAPON04.CIF", ItemReferenceExport.WeaponFile(121)); // Katana shares longblade animation.
        Assert.Null(ItemReferenceExport.WeaponFile(131)); // An arrow is drawn by the bow, with no wield animation of its own.
    }

    [CorpusFact("ART_PAL.COL", "TEXTURE.234", "TEXTURE.245", "TEXTURE.252", "TEXTURE.432", "TEXTURE.433", "WEAPON02.CIF", "WEAPO102.CIF")]
    public void Item_export_covers_templates_bodies_and_artifacts_and_every_PNG_matches_its_source_pixels()
    {
        ItemReferenceExport.Run(TestData.CorpusRoot, TestData.DonorRoot, root);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
        JsonElement document = manifest.RootElement;
        JsonElement[] entries = document.GetProperty("entries").EnumerateArray().ToArray();
        JsonElement[] items = entries.Where(entry => entry.GetProperty("kind").GetString() == "item").ToArray();
        Assert.Equal(Enumerable.Range(0, 288), items.Select(entry => entry.GetProperty("index").GetInt32()));
        Assert.Equal(23, entries.Count(entry => entry.GetProperty("kind").GetString() == "artifact"));
        JsonElement cuirass = items.Single(item => item.GetProperty("index").GetInt32() == 102);
        Assert.Equal(8, cuirass.GetProperty("images").EnumerateArray().Select(image => image.GetProperty("role").GetString()!)
            .Where(role => role.StartsWith("player/", StringComparison.Ordinal)).Select(role => role.Split('/')[1]).Distinct().Count());
        JsonElement dagger = items.Single(item => item.GetProperty("index").GetInt32() == 113);
        string[] sources = dagger.GetProperty("images").EnumerateArray().Select(image => image.GetProperty("source").GetString()!).Distinct().ToArray();
        Assert.Contains("arena2/WEAPON02.CIF", sources);
        Assert.Contains("arena2/WEAPO102.CIF", sources);
        JsonElement cloak = items.Single(item => item.GetProperty("index").GetInt32() == 154);
        Assert.Contains(cloak.GetProperty("images").EnumerateArray(), image => image.GetProperty("role").GetString()!.Contains("cloak-interior", StringComparison.Ordinal));
        JsonElement flowers = items.Single(item => item.GetProperty("index").GetInt32() == 251);
        Assert.Equal("source-record-unavailable", flowers.GetProperty("worldArt").GetString());

        Arena2Palette palette = PaletteDecoder.Decode(File.ReadAllBytes(TestData.Corpus("ART_PAL.COL")), "ART_PAL.COL");
        Dictionary<string, TextureArchive> textures = [];
        Dictionary<string, WeaponCifArchive> weapons = [];
        int checkedImages = 0;
        foreach (JsonElement entry in entries)
        {
            string directory = Path.Combine(root, entry.GetProperty("directory").GetString()!);
            foreach (ReferenceImage image in entry.GetProperty("images").Deserialize<ReferenceImage[]>(ReferenceExport.Json)!)
            {
                byte[] indexed;
                if (image.Source.EndsWith(".CIF", StringComparison.Ordinal))
                {
                    if (!weapons.TryGetValue(image.Source, out WeaponCifArchive? weapon))
                        weapons[image.Source] = weapon = WeaponCifArchive.Parse(File.ReadAllBytes(TestData.Corpus(Path.GetFileName(image.Source))), image.Source);
                    WeaponCifRecordInfo info = weapon.GetRecordInfo(image.Record);
                    Assert.Equal((info.Width, info.Height), ((ushort)image.Width, (ushort)image.Height));
                    indexed = weapon.DecodeFrame(image.Record, image.Frame).Pixels.ToArray();
                }
                else
                {
                    if (!textures.TryGetValue(image.Source, out TextureArchive? texture))
                        textures[image.Source] = texture = TextureArchive.Parse(File.ReadAllBytes(TestData.Corpus(Path.GetFileName(image.Source))), image.Source);
                    IndexedTextureFrame frame = texture.DecodeFrame(image.Record, image.Frame);
                    Assert.Equal(((int)frame.Width, (int)frame.Height), (image.Width, image.Height));
                    indexed = frame.Pixels.ToArray();
                }
                byte[] expected;
                if (image.Role.EndsWith("/mask", StringComparison.Ordinal))
                {
                    expected = new byte[indexed.Length * 4];
                    for (int i = 0; i < indexed.Length; i++) if (indexed[i] == 255) expected.AsSpan(i * 4, 4).Fill(255);
                }
                else
                {
                    if (image.Role.StartsWith("player/", StringComparison.Ordinal))
                        for (int i = 0; i < indexed.Length; i++) if (indexed[i] == 255) indexed[i] = 0;
                    string color = image.Colors[0];
                    if (color != "source")
                    {
                        bool clothing = image.Source is "arena2/TEXTURE.235" or "arena2/TEXTURE.236" or "arena2/TEXTURE.237" or "arena2/TEXTURE.238"
                            or "arena2/TEXTURE.239" or "arena2/TEXTURE.240" or "arena2/TEXTURE.241" or "arena2/TEXTURE.242";
                        indexed = PaperDollItemPublication.DyeVariants(indexed, clothing)[color];
                    }
                    expected = palette.ToRgbaBytes(indexed, PaletteAlphaMode.IndexZeroTransparent);
                }
                DeterministicPngImage png = DeterministicPngReader.ReadRgba8(File.ReadAllBytes(Path.Combine(directory, image.File)), image.File);
                Assert.Equal((image.Width, image.Height), (png.Width, png.Height));
                Assert.True(expected.AsSpan().SequenceEqual(png.Rgba), $"Source pixels differ: {directory}/{image.File}");
                checkedImages++;
            }
        }
        Assert.Equal(document.GetProperty("imageCount").GetInt32(), checkedImages);
        Assert.Equal(checkedImages, Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).Count());
    }

    [CorpusFact("ART_PAL.COL", "SPELLS.STD", "ICON00I0.IMG", "MASK04I0.IMG", "FIRE00C6.CIF", "FRST00C6.CIF",
        "POIS00C6.CIF", "SHOK00C6.CIF", "MJIC00C6.CIF", "TEXTURE.375", "TEXTURE.376", "TEXTURE.377", "TEXTURE.378", "TEXTURE.379", "TEXTURE.380")]
    public void Spell_export_keeps_every_icon_and_spell_and_joins_only_applicable_world_effects()
    {
        SpellReferenceExport.Run(TestData.CorpusRoot, root);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
        JsonElement document = manifest.RootElement;
        Assert.Empty(document.GetProperty("warnings").EnumerateArray());
        Assert.Equal(Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).Count(), document.GetProperty("imageCount").GetInt32());
        JsonElement[] entries = document.GetProperty("entries").EnumerateArray().ToArray();
        JsonElement[] icons = entries.Where(entry => entry.GetProperty("kind").GetString() == "spell-icon").ToArray();
        Assert.Equal(Enumerable.Range(0, 69), icons.Select(entry => entry.GetProperty("icon").GetInt32()));
        JsonElement[] spells = entries.Where(entry => entry.GetProperty("kind").GetString() == "spell").ToArray();
        Arena2SpellCatalog source = Arena2MagicReader.ReadSpells(File.ReadAllBytes(TestData.Corpus("SPELLS.STD")), "SPELLS.STD");
        Assert.Equal(source.Spells.Count, spells.Length);
        Assert.Equal(2, spells.Count(spell => spell.GetProperty("index").GetInt32() == 58));
        Assert.Equal(spells.Length, spells.Select(spell => spell.GetProperty("directory").GetString()).Distinct().Count());
        foreach (JsonElement spell in spells)
        {
            int ordinal = spell.GetProperty("ordinal").GetInt32();
            Arena2Spell original = source.Spells[ordinal];
            Assert.Equal(original.Name, spell.GetProperty("name").GetString());
            JsonElement[] images = spell.GetProperty("images").EnumerateArray().ToArray();
            string directory = Path.Combine(root, spell.GetProperty("directory").GetString()!);
            Assert.All(images, image => Assert.True(File.Exists(Path.Combine(directory, image.GetProperty("file").GetString()!))));
            JsonElement[] casting = images.Where(image => image.GetProperty("role").GetString() == "casting").ToArray();
            Assert.Equal(Enumerable.Range(0, 6), casting.Select(image => image.GetProperty("record").GetInt32()));
            Assert.All(casting, image => Assert.Equal("arena2/" + SpellReferenceExport.Elements[original.Element].CastingFile, image.GetProperty("source").GetString()));
            // These are the classic target indices, not icon numbers or element flag values.
            bool ranged = original.RangeType is 2 or 4;
            Assert.Equal(ranged, images.Any(image => image.GetProperty("role").GetString() == "world-effects/projectile"));
            Assert.Equal(ranged, images.Any(image => image.GetProperty("role").GetString() == "world-effects/impact"));
        }

        // Donor icon 20 starts the second row; Unity's flipped texture coordinates must not flip the exported art.
        IndexedImg atlas = ImgDecoder.DecodeHeaderless(File.ReadAllBytes(TestData.Corpus("ICON00I0.IMG")), "ICON00I0.IMG");
        Arena2Palette palette = PaletteDecoder.Decode(File.ReadAllBytes(TestData.Corpus("ART_PAL.COL")), "ART_PAL.COL");
        byte[] expected = new byte[16 * 16];
        for (int row = 0; row < 16; row++) atlas.Pixels.Span.Slice((16 + row) * 320, 16).CopyTo(expected.AsSpan(row * 16));
        DeterministicPngImage image20 = DeterministicPngReader.ReadRgba8(File.ReadAllBytes(Path.Combine(root, "icons", "icon-020", "icon.png")), "exported icon 20");
        Assert.Equal(16, image20.Width);
        Assert.Equal(16, image20.Height);
        Assert.Equal(palette.ToRgbaBytes(expected, PaletteAlphaMode.IndexZeroTransparent), image20.Rgba);
        JsonElement selector = entries.First(entry => entry.GetProperty("kind").GetString() == "target");
        DeterministicPngImage target = DeterministicPngReader.ReadRgba8(File.ReadAllBytes(Path.Combine(root,
            selector.GetProperty("directory").GetString()!, "target-icon.png")), "target icon");
        Assert.All(target.Rgba.Where((_, i) => i % 4 == 3), alpha => Assert.Equal(255, alpha));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
