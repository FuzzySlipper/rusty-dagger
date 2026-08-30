using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class Arena2ClassicMediaPublicationTests
{
    [Fact]
    public void RegeneratesClosedClassicMediaBytesAndTypedSemanticMappings()
    {
        Arena2ClassicMediaInputs inputs = CreateInputs();

        Arena2ClassicMediaPublication first = Arena2ClassicMediaPublication.Create(inputs);
        Arena2ClassicMediaPublication second = Arena2ClassicMediaPublication.Create(inputs);

        Assert.Equal(49, first.Artifacts.Count);
        Assert.Equal(49, first.MediaManifest.Resources.Count);
        Assert.Equal(16, first.Sources.Count);
        Assert.Equal(first.Artifacts.Select(artifact => artifact.RelativePath).OrderBy(path => path, StringComparer.Ordinal), first.Artifacts.Select(artifact => artifact.RelativePath));
        Assert.Equal(first.Artifacts.Select(artifact => artifact.RelativePath), second.Artifacts.Select(artifact => artifact.RelativePath));
        Assert.All(first.Artifacts.Zip(second.Artifacts), pair => Assert.Equal(pair.First.Bytes.ToArray(), pair.Second.Bytes.ToArray()));

        Assert.Equal(7, first.WeaponActions.Count);
        Assert.Equal(31, first.WeaponActions.Sum(action => action.FrameCount));
        Assert.Equal(ClassicDaggerWeaponAction.Idle, first.WeaponActions[0].Action);
        Assert.True(first.WeaponActions[0].Timing.Loop);
        Assert.All(first.WeaponActions.Skip(1), action => Assert.False(action.Timing.Loop));
        Assert.All(first.WeaponActions, action => Assert.Equal(10F, action.Timing.FramesPerSecond));
        Assert.Equal(4, first.Effects.Count);
        Assert.All(first.Effects, effect => Assert.False(effect.Timing.Loop));
        Assert.Equal(6, first.Audio.Count);
        Assert.Equal(6, first.UiImages.Count);
        Assert.Equal(31, first.InventoryIcons.Count);
        Assert.Equal(240, first.Font.Glyphs.Count);

        byte[] weapon = Artifact(first, "media/combat/weapon-dagger-steel-atlas.png");
        AssertPng(weapon, 3840, 600);
        byte[] font = Artifact(first, "media/fonts/font-classic-0003-atlas.png");
        AssertPng(font, 256, 240);
        Assert.NotNull(first.MediaManifest.Resources.Single(resource => resource.Id == "weapon.dagger.steel").Frames);
        Assert.Equal(31, first.MediaManifest.Resources.Single(resource => resource.Id == "weapon.dagger.steel").Frames.Count);
        Assert.Contains(first.Artifacts, artifact => artifact.RelativePath == "media/ui/inventory-icons/inventory-icon-iron-dagger.png");
        Assert.Contains(first.Artifacts, artifact => artifact.RelativePath == "media/ui/inventory-icons/inventory-icon-arrow.png");

        byte[] wave = Artifact(first, "media/audio/audio-melee-dagger-swing.wav");
        AssertWave(wave);
        Assert.Equal("arena2/DAGGER.SND", first.Sources.Single(source => source.SourcePath == "arena2/DAGGER.SND").SourcePath);
    }

    [Fact]
    public void RejectsMalformedClassicSourcesAndFixedSourceTableGaps()
    {
        Arena2ClassicMediaInputs malformed = CreateInputs() with { Weapon02Cif = [1] };
        Assert.Throws<Arena2FormatException>(() => Arena2ClassicMediaPublication.Create(malformed));

        Arena2ClassicMediaInputs missingInventoryRecord = CreateInputs() with { Texture245 = CreateTextureArchive(1) };
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena2ClassicMediaPublication.Create(missingInventoryRecord));
    }

    [Fact]
    public void EnforcesAtlasAndArtifactByteQuotas()
    {
        Arena2ClassicMediaInputs inputs = CreateInputs();
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena2ClassicMediaPublication.Create(inputs, new Arena2ClassicMediaPublicationOptions(MaximumAtlasDimension: 319)));
        Assert.Throws<InvalidOperationException>(() => Arena2ClassicMediaPublication.Create(inputs, new Arena2ClassicMediaPublicationOptions(MaximumArtifactBytes: 1)));
        Assert.Throws<InvalidOperationException>(() => Arena2ClassicMediaPublication.Create(inputs, new Arena2ClassicMediaPublicationOptions(MaximumTotalArtifactBytes: 1)));
    }

    [Fact]
    public void ProfileOwnsAdjustablePresentationAndAdmittedUiInventoryMappings()
    {
        Arena2ClassicMediaInputs inputs = CreateInputs();
        Arena2ClassicMediaPublication baseline = Arena2ClassicMediaPublication.Create(inputs);
        Arena2ClassicMediaProfile profile = new(
            WeaponMediaId: "weapon.dagger.profiled",
            WeaponActions: baseline.WeaponActions
                .Select(action => new ClassicWeaponActionPresentation(
                    action.Action,
                    action.Action == ClassicDaggerWeaponAction.Idle ? ClassicWeaponScreenAlignment.Left : action.Alignment,
                    action.Action == ClassicDaggerWeaponAction.Idle ? 0F : action.ScreenOffset,
                    action.Action == ClassicDaggerWeaponAction.Idle ? new ClassicSpriteTiming(12F, true) : action.Timing))
                .ToArray(),
            Effects: baseline.Effects
                .Select(effect => new ClassicEffectPresentation(
                    effect.Effect,
                    effect.Effect == ClassicEffect.Blood0 ? "effect.blood.profiled" : effect.MediaId,
                    effect.Effect == ClassicEffect.Blood0 ? new ClassicSpriteTiming(6F, false) : effect.Timing))
                .ToArray(),
            UiImages: baseline.UiImages
                .Select(image => new ClassicUiImagePresentation(
                    image.Image,
                    image.MediaId,
                    image.Image == ClassicUiImage.HudChromeMain ? "MAIN03I0.IMG" : image.SourceFile))
                .ToArray(),
            InventoryIcons: baseline.InventoryIcons
                .Select(icon => new ClassicInventoryIconPresentation(
                    icon.ItemId,
                    icon.ItemId == "iron-dagger" ? "inventory.icon.profiled-dagger" : icon.MediaId,
                    icon.TextureArchive,
                    icon.SourceRecordOrdinal))
                .ToArray(),
            Presentation:
            [
                new ClassicMediaPresentation("weapon.dagger.profiled", "Profiled dagger", new(0.5F, 0F), new(1F, 2F)),
                new ClassicMediaPresentation("effect.blood.profiled", "Profiled blood", new(0.5F, 0.5F)),
            ]);

        Arena2ClassicMediaPublication publication = Arena2ClassicMediaPublication.Create(inputs, profile);

        Assert.Equal("weapon.dagger.profiled", publication.MediaManifest.Resources.Single(resource => resource.Kind == NormalizedMediaKind.WeaponSprite).Id);
        ClassicWeaponActionManifest idle = publication.WeaponActions.Single(action => action.Action == ClassicDaggerWeaponAction.Idle);
        Assert.Equal(ClassicWeaponScreenAlignment.Left, idle.Alignment);
        Assert.Equal(12F, idle.Timing.FramesPerSecond);
        Assert.Equal("MAIN03I0.IMG", publication.UiImages.Single(image => image.Image == ClassicUiImage.HudChromeMain).SourceFile);
        Assert.Equal("inventory.icon.profiled-dagger", publication.InventoryIcons.Single(icon => icon.ItemId == "iron-dagger").MediaId);

        NormalizedMediaDescriptor effect = publication.MediaManifest.Resources.Single(resource => resource.Id == "effect.blood.profiled");
        Assert.Equal(6F, effect.FramesPerSecond);
        Assert.False(effect.Loop!.Value);
        Assert.Equal("Profiled blood", effect.DisplayName);
        Assert.Contains(publication.Artifacts, artifact => artifact.RelativePath == "media/combat/weapon-dagger-profiled-atlas.png");
        Assert.Contains(publication.Artifacts, artifact => artifact.RelativePath == "media/ui/inventory-icons/inventory-icon-iron-dagger.png");
    }

    [Fact]
    public void PreservesTypedAuthoredUiBytesAndPortableManifestProvenance()
    {
        byte[] png = DeterministicPngEncoder.EncodeRgba8(1, 1, [10, 20, 30, 255]);
        Arena2ClassicMediaProfile profile = new(
            Presentation: [new ClassicMediaPresentation("inventory.skin.panel-slate.v1", "Slate panel", new(0.5F, 0.5F), new(4F, 2F))],
            AuthoredUiManifest: new("ui-authored-assets.json", "{\"schemaVersion\":1}"u8.ToArray()),
            AuthoredUiAssets:
            [
                new ClassicAuthoredUiAsset(
                    "inventory.skin.panel-slate.v1",
                    "media/ui/authored/inventory-skin-panel-slate-v1.png",
                    "ui-original/inventory-panel-slate-v1.png",
                    png,
                    "test-generator",
                    "original test panel"),
            ]);

        Arena2ClassicMediaPublication publication = Arena2ClassicMediaPublication.Create(CreateInputs(), profile);

        Assert.Equal(png, Artifact(publication, "media/ui/authored/inventory-skin-panel-slate-v1.png"));
        Assert.Equal("authored-ui/ui-authored-assets.json", publication.AuthoredUiManifestSource!.SourcePath);
        Assert.Contains(publication.Sources, source => source.SourcePath == "authored-ui/ui-original/inventory-panel-slate-v1.png");
        ClassicAuthoredUiAssetManifest asset = Assert.Single(publication.AuthoredUiAssets);
        Assert.Equal("ui-original/inventory-panel-slate-v1.png", asset.SourceLabel);
        NormalizedMediaDescriptor descriptor = publication.MediaManifest.Resources.Single(resource => resource.Id == asset.Id);
        Assert.Equal(asset.RelativePath, descriptor.RelativePath);
        Assert.Equal(ContentDigest.Compute(png), descriptor.ContentDigest);
        Assert.Equal("Slate panel", descriptor.DisplayName);
    }

    [Fact]
    public void RejectsInvalidProfileAndAuthoredUiInputs()
    {
        Arena2ClassicMediaProfile incompleteActions = new(WeaponActions: [new(ClassicDaggerWeaponAction.Idle, ClassicWeaponScreenAlignment.Left, 0F, new ClassicSpriteTiming(10F, true))]);
        Assert.Throws<ArgumentException>(() => Arena2ClassicMediaPublication.Create(CreateInputs(), incompleteActions));

        Arena2ClassicMediaProfile invalidPng = new(AuthoredUiAssets:
        [
            new ClassicAuthoredUiAsset("inventory.skin.invalid", "media/ui/authored/invalid.png", "ui-original/invalid.png", [1], "generator", "prompt"),
        ]);
        Assert.Throws<ArgumentException>(() => Arena2ClassicMediaPublication.Create(CreateInputs(), invalidPng));

        Arena2ClassicMediaProfile collidingProvenance = new(
            AuthoredUiManifest: new("ui-original/same.png", [1]),
            AuthoredUiAssets:
            [
                new ClassicAuthoredUiAsset("inventory.skin.same", "media/ui/authored/same.png", "ui-original/same.png", DeterministicPngEncoder.EncodeRgba8(1, 1, [0, 0, 0, 0]), "generator", "prompt"),
            ]);
        Assert.Throws<ArgumentException>(() => Arena2ClassicMediaPublication.Create(CreateInputs(), collidingProvenance));
    }

    [Fact]
    public void RegeneratesTheSelectedClosureFromOperatorSuppliedArena2WhenAvailable()
    {
        string arena2 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../local/arena2"));
        if (!File.Exists(Path.Combine(arena2, "WEAPON02.CIF"))) return;

        Arena2ClassicMediaPublication publication = Arena2ClassicMediaPublication.Create(new(
            Read(arena2, "WEAPON02.CIF"), Read(arena2, "ART_PAL.COL"), Read(arena2, "TEXTURE.380"), Read(arena2, "PAL.PAL"), Read(arena2, "DAGGER.SND"),
            Read(arena2, "MAIN00I0.IMG"), Read(arena2, "MAIN03I0.IMG"), Read(arena2, "MAIN04I0.IMG"), Read(arena2, "MAIN05I0.IMG"), Read(arena2, "INVE00I0.IMG"), Read(arena2, "INFO00I0.IMG"),
            Read(arena2, "TEXTURE.207"), Read(arena2, "TEXTURE.216"), Read(arena2, "TEXTURE.234"), Read(arena2, "TEXTURE.245"), Read(arena2, "FONT0003.FNT")));

        Assert.Equal(31, publication.WeaponActions.Sum(action => action.FrameCount));
        Assert.Equal(49, publication.Artifacts.Count);
        AssertPng(Artifact(publication, "media/combat/weapon-dagger-steel-atlas.png"), 3840, 600);
        Assert.All(publication.Audio, clip => Assert.Equal(11_025U, clip.SampleRate));
    }

    private static byte[] Artifact(Arena2ClassicMediaPublication publication, string path) => publication.Artifacts.Single(artifact => artifact.RelativePath == path).Bytes.ToArray();

    private static void AssertPng(byte[] png, int width, int height)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(width, ReadBigEndian(png, 16));
        Assert.Equal(height, ReadBigEndian(png, 20));
    }

    private static void AssertWave(byte[] wave)
    {
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(1, ReadLittleEndian16(wave, 20));
        Assert.Equal(1, ReadLittleEndian16(wave, 22));
        Assert.Equal(11_025, ReadLittleEndian32(wave, 24));
        Assert.Equal(8, ReadLittleEndian16(wave, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wave, 36, 4));
    }

    private static Arena2ClassicMediaInputs CreateInputs() => new(
        CreateWeaponCif(),
        CreatePalette(),
        CreateTextureArchive(4),
        CreatePalette(),
        CreateNumericBsa(113),
        CreateHeaderedImage(1),
        CreateHeaderedImage(2),
        CreateHeaderedImage(3),
        CreateHeaderedImage(4),
        new byte[320 * 200],
        new byte[320 * 200],
        CreateTextureArchive(17),
        CreateTextureArchive(2),
        CreateTextureArchive(27),
        CreateTextureArchive(37),
        CreateFont());

    private static byte[] Read(string directory, string fileName) => File.ReadAllBytes(Path.Combine(directory, fileName));

    private static byte[] CreatePalette()
    {
        byte[] palette = new byte[768];
        for (int index = 0; index < 256; index++)
        {
            palette[index * 3] = (byte)index;
            palette[(index * 3) + 1] = (byte)(255 - index);
            palette[(index * 3) + 2] = (byte)(index / 2);
        }

        return palette;
    }

    private static byte[] CreateHeaderedImage(byte pixel)
    {
        byte[] image = new byte[13];
        WriteInt16(image, 0, 0);
        WriteInt16(image, 2, 0);
        WriteUInt16(image, 4, 1);
        WriteUInt16(image, 6, 1);
        WriteUInt16(image, 8, 0);
        WriteUInt16(image, 10, 1);
        image[12] = pixel;
        return image;
    }

    private static byte[] CreateWeaponCif()
    {
        List<byte> bytes = [];
        AppendInt16(bytes, 0);
        AppendInt16(bytes, 0);
        AppendInt16(bytes, 1);
        AppendInt16(bytes, 1);
        AppendUInt16(bytes, 0);
        AppendUInt16(bytes, 1);
        bytes.Add(1);
        for (int action = 0; action < 6; action++)
        {
            AppendUInt16(bytes, 1);
            AppendUInt16(bytes, 1);
            AppendUInt16(bytes, 2);
            AppendInt16(bytes, 0);
            AppendInt16(bytes, 0);
            AppendInt16(bytes, 0);
            for (int frame = 0; frame < 31; frame++)
            {
                AppendUInt16(bytes, frame < 5 ? checked((ushort)(76 + (frame * 2))) : (ushort)0);
            }

            AppendUInt16(bytes, 86);
            for (int frame = 0; frame < 5; frame++)
            {
                bytes.Add(0);
                bytes.Add((byte)(action + frame + 2));
            }
        }

        return bytes.ToArray();
    }

    private static byte[] CreateTextureArchive(int recordCount)
    {
        int recordsOffset = 26 + (recordCount * 20);
        const int recordHeaderBytes = 28;
        const int rowStride = 256;
        byte[] output = new byte[recordsOffset + (recordCount * (recordHeaderBytes + rowStride))];
        WriteInt16(output, 0, checked((short)recordCount));
        for (int index = 0; index < recordCount; index++)
        {
            int recordOffset = recordsOffset + (index * (recordHeaderBytes + rowStride));
            WriteInt32(output, 28 + (index * 20), recordOffset);
            WriteInt16(output, recordOffset + 4, 1);
            WriteInt16(output, recordOffset + 6, 1);
            WriteUInt16(output, recordOffset + 8, 0);
            WriteInt32(output, recordOffset + 14, 28);
            WriteUInt16(output, recordOffset + 20, 1);
            output[recordOffset + recordHeaderBytes] = checked((byte)(index + 1));
        }

        return output;
    }

    private static byte[] CreateNumericBsa(int recordCount)
    {
        List<byte> result = [];
        AppendInt16(result, checked((short)recordCount));
        AppendUInt16(result, 0x0200);
        for (int index = 0; index < recordCount; index++) result.Add((byte)index);
        for (int index = 0; index < recordCount; index++)
        {
            AppendUInt32(result, checked((uint)(10_000 + index)));
            AppendInt32(result, 1);
        }

        return result.ToArray();
    }

    private static byte[] CreateFont()
    {
        const int glyphCount = 240;
        const int headerBytes = 4;
        const int tableBytes = glyphCount * 4;
        const int glyphBytes = 32;
        byte[] font = new byte[headerBytes + tableBytes + (glyphCount * glyphBytes)];
        WriteUInt16(font, 0, 8);
        WriteUInt16(font, 2, 16);
        for (int index = 0; index < glyphCount; index++)
        {
            int offset = headerBytes + tableBytes + (index * glyphBytes);
            WriteUInt16(font, headerBytes + (index * 4), checked((ushort)offset));
            WriteUInt16(font, headerBytes + (index * 4) + 2, checked((ushort)(index % 16)));
            font[offset + 1] = 1;
        }

        return font;
    }

    private static int ReadBigEndian(byte[] bytes, int offset) => bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3];
    private static int ReadLittleEndian16(byte[] bytes, int offset) => bytes[offset] | (bytes[offset + 1] << 8);
    private static int ReadLittleEndian32(byte[] bytes, int offset) => bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);
    private static void WriteInt16(byte[] bytes, int offset, short value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
    private static void WriteUInt16(byte[] bytes, int offset, ushort value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
    private static void WriteInt32(byte[] bytes, int offset, int value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
    private static void AppendInt16(List<byte> bytes, short value) => bytes.AddRange(BitConverter.GetBytes(value));
    private static void AppendUInt16(List<byte> bytes, ushort value) => bytes.AddRange(BitConverter.GetBytes(value));
    private static void AppendUInt32(List<byte> bytes, uint value) => bytes.AddRange(BitConverter.GetBytes(value));
    private static void AppendInt32(List<byte> bytes, int value) => bytes.AddRange(BitConverter.GetBytes(value));
}
