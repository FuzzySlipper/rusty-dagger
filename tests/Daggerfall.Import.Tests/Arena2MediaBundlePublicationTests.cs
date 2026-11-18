using Daggerfall.Import.Arena2;
using System.Text;
using System.Text.Json;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class Arena2MediaBundlePublicationTests
{
    [Fact]
    public void RejectsDuplicateMediaPaths()
    {
        Arena2ClassicMediaPublication duplicate = CreateClassicMedia(artifactPath: "media/dungeon/materials/minimal.png");
        Assert.Throws<ArgumentException>(() => CreateWorldMedia(duplicate));
    }

    [Fact]
    public void RejectsClassicActionsOutsideTheCanonicalWeaponFrames()
    {
        Arena2ClassicMediaPublication classic = CreateClassicMedia();
        classic = classic with
        {
            WeaponMedia = [new ClassicWeaponMediaManifest("weapon.minimal", [WeaponActions(classic).First() with { FrameCount = 2 }])],
        };

        Assert.Throws<InvalidOperationException>(() => CreateWorldMedia(classic));
    }

    [Fact]
    public void RejectsMediaDescriptorFramesOutsideTheirAtlas()
    {
        Arena2ClassicMediaPublication classic = CreateClassicMedia();
        NormalizedMediaManifest invalidManifest = new(classic.MediaManifest.Resources.Select(resource =>
            resource.Id == "weapon.minimal"
                ? resource with { Frames = [resource.Frames[0] with { X = resource.AtlasWidth }] }
                : resource).ToArray());

        Assert.Throws<InvalidOperationException>(() => CreateWorldMedia(classic with { MediaManifest = invalidManifest }));
    }

    [Fact]
    public void RejectsDungeonFrameLayoutsThatDoNotMatchCanonicalDescriptorFrames()
    {
        Arena2DungeonMediaPublication dungeonMedia = CreateDungeonMediaWithBillboard();
        DungeonBillboardSpriteMedia billboard = dungeonMedia.Billboards.Single();
        DungeonMediaFrameLayout badLayout = billboard.Frames.Single() with
        {
            AtlasFrame = billboard.Frames.Single().AtlasFrame with { X = 1 },
        };
        dungeonMedia = dungeonMedia with
        {
            Billboards = [billboard with { Frames = [badLayout] }],
        };

        Assert.Throws<InvalidOperationException>(() => CreateWorldMedia(CreateClassicMedia(), dungeonMedia));
    }

    [Fact]
    public void RejectsIncompleteOrRemappedClassicWeaponActions()
    {
        Arena2ClassicMediaPublication classic = CreateClassicMedia();
        Assert.Throws<InvalidOperationException>(() => CreateWorldMedia(WithWeaponActions(classic, WeaponActions(classic).Take(6).ToArray())));

        ClassicWeaponActionManifest idle = WeaponActions(classic).Single(action => action.Action == ClassicDaggerWeaponAction.Idle);
        Assert.Throws<InvalidOperationException>(() => CreateWorldMedia(classic with
        {
            WeaponMedia = [new ClassicWeaponMediaManifest("weapon.minimal", WeaponActions(classic).Select(action => action.Action == ClassicDaggerWeaponAction.Idle
                ? idle with { FrameStart = 1 }
                : action).ToArray())],
        }));
    }

    [Fact]
    public void RejectsReorderedClassicWeaponActionsAndEffectsWithArbitrarySourceRecords()
    {
        Arena2ClassicMediaPublication classic = CreateClassicMedia();
        Assert.Throws<InvalidOperationException>(() => CreateWorldMedia(WithWeaponActions(classic, WeaponActions(classic).Reverse().ToArray())));

        ClassicEffectManifest effect = classic.Effects.First();
        Assert.Throws<InvalidOperationException>(() => CreateWorldMedia(classic with
        {
            Effects = classic.Effects.Select(value => value.Effect == effect.Effect
                ? value with { SourceRecordOrdinal = 99 }
                : value).ToArray(),
        }));
    }

    [Fact]
    public void RejectsEffectTimingOrLoopThatDisagreesWithCanonicalDescriptor()
    {
        Arena2ClassicMediaPublication classic = CreateClassicMedia();
        ClassicEffectManifest effect = classic.Effects.Single(value => value.Effect == ClassicEffect.Blood0);
        Assert.Throws<InvalidOperationException>(() => CreateWorldMedia(classic with
        {
            Effects = classic.Effects.Select(value => value.Effect == effect.Effect
                ? effect with { Timing = new ClassicSpriteTiming(5F, false) }
                : value).ToArray(),
        }));

        Assert.Throws<InvalidOperationException>(() => CreateWorldMedia(classic with
        {
            Effects = classic.Effects.Select(value => value.Effect == effect.Effect
                ? effect with { Timing = new ClassicSpriteTiming(10F, true) }
                : value).ToArray(),
        }));

        NormalizedMediaManifest descriptorWithChangedTiming = new(classic.MediaManifest.Resources.Select(resource =>
            resource.Id == effect.MediaId
                ? resource with { FramesPerSecond = 5F }
                : resource).ToArray());
        Assert.Throws<InvalidOperationException>(() => CreateWorldMedia(classic with { MediaManifest = descriptorWithChangedTiming }));
    }

    [Fact]
    public void AcceptsAConsistentEffectTimingOverlayInTheWorldMedia()
    {
        Arena2ClassicMediaPublication classic = CreateClassicMedia();
        string effectId = classic.Effects.First().MediaId;
        NormalizedMediaManifest media = new(classic.MediaManifest.Resources.Select(resource => resource.Id == effectId
            ? resource with { FramesPerSecond = 7F, Loop = true }
            : resource).ToArray());
        ImportPublicationPlan publication = CreateWorldMedia(classic with
        {
            MediaManifest = media,
            Effects = classic.Effects.Select(effect => effect.MediaId == effectId
                ? effect with { Timing = new ClassicSpriteTiming(7F, true) }
                : effect).ToArray(),
        });

        Assert.Contains(publication.Artifacts, artifact => artifact.RelativePath == Arena2MediaBundlePublication.ClassicMediaManifestRelativePath);
    }

    [Fact]
    public void A_material_cycle_through_a_frame_the_media_does_not_carry_is_refused_by_name()
    {
        Arena2DungeonMediaPublication dungeon = CreateDungeonMedia();
        Arena2DungeonMediaPublication animated = dungeon with
        {
            MaterialTextures = [dungeon.MaterialTextures[0] with { Animation = new(12F, ["material/texture-2-0", "material/texture-2-0-frame-1"]) }],
        };

        FormatException failure = Assert.Throws<FormatException>(() => CreateWorldMedia(CreateClassicMedia(), animated));
        Assert.Contains("material/texture-2-0-frame-1", failure.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorldMediaPublicationCarriesTheBodiesOnceWithASlotFreeSidecarAndReferencesTheClassicGroup()
    {
        Arena2ClassicMediaPublication classic = CreateClassicMedia();
        ImportPublicationArtifact grouped = classic.Artifacts.Single(artifact => artifact.RelativePath == "media/classic/weapon.png");
        ImportPublicationPlan world = CreateWorldMedia(classic, group: ClassicGroup(grouped));

        string[] paths = [.. world.Artifacts.Select(artifact => artifact.RelativePath)];
        Assert.Contains("media/dungeon/materials/minimal.png", paths);
        Assert.Contains(GeometryPublication.IndexRelativePath, paths);
        // The classic media group already publishes the weapon atlas, so the world media references it.
        Assert.DoesNotContain("media/classic/weapon.png", paths);
        Assert.Equal(["media/dungeon/materials/minimal.png"],
            world.Manifest.Artifacts.Single(artifact => artifact.RelativePath == Arena2MediaBundlePublication.DungeonMediaManifestRelativePath).DependsOnPaths);
        Assert.Equal(
            [
                .. Enum.GetValues<ClassicEffect>().Select((_, index) => $"media/classic/effect.minimal.{index}.png").Order(StringComparer.Ordinal),
                "media/classic/minimal.bin",
            ],
            world.Manifest.Artifacts.Single(artifact => artifact.RelativePath == Arena2MediaBundlePublication.ClassicMediaManifestRelativePath).DependsOnPaths);

        string dungeonJson = Encoding.UTF8.GetString(world.Artifacts.Single(artifact => artifact.RelativePath == Arena2MediaBundlePublication.DungeonMediaManifestRelativePath).Bytes.Span);
        string classicJson = Encoding.UTF8.GetString(world.Artifacts.Single(artifact => artifact.RelativePath == Arena2MediaBundlePublication.ClassicMediaManifestRelativePath).Bytes.Span);
        Assert.EndsWith("\n", dungeonJson, StringComparison.Ordinal);
        Assert.EndsWith("\n", classicJson, StringComparison.Ordinal);
        Assert.DoesNotContain("schemaVersion", dungeonJson, StringComparison.Ordinal);
        Assert.Contains("\"media\":", classicJson, StringComparison.Ordinal);
        Assert.Contains("\"weaponMedia\":", classicJson, StringComparison.Ordinal);
        Assert.DoesNotContain("encounter", dungeonJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("encounter", classicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"bytes\"", dungeonJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"bytes\"", classicJson, StringComparison.OrdinalIgnoreCase);
        DungeonMediaManifestSidecar dungeon = JsonSerializer.Deserialize<DungeonMediaManifestSidecar>(dungeonJson, PublishedJson.SectionRead)!;
        Assert.Contains(dungeon.Media.Resources, resource => resource.Id == "material/texture-2-0");

        // A classic artifact the group publishes with other bytes is two bodies at one path.
        ImportPublicationManifestArtifact stale = new(grouped.RelativePath, ContentDigest.Compute("other"u8), 5, []);
        Assert.Throws<InvalidOperationException>(() => CreateWorldMedia(classic, group: new Dictionary<string, ImportPublicationManifestArtifact>(StringComparer.Ordinal) { [stale.RelativePath] = stale }));
    }

    private static ImportPublicationPlan CreateWorldMedia(
        Arena2ClassicMediaPublication classic,
        Arena2DungeonMediaPublication? dungeonMedia = null,
        IReadOnlyDictionary<string, ImportPublicationManifestArtifact>? group = null) =>
        Arena2MediaBundlePublication.CreateWorldMedia(Arena2SitePublication.WorldMediaImporterId, dungeonMedia ?? CreateDungeonMedia(), classic, CreateGeometry(),
            group ?? new Dictionary<string, ImportPublicationManifestArtifact>(StringComparer.Ordinal));

    private static Dictionary<string, ImportPublicationManifestArtifact> ClassicGroup(ImportPublicationArtifact artifact) => new(StringComparer.Ordinal)
    {
        [artifact.RelativePath] = new(artifact.RelativePath, artifact.ContentHash, artifact.Bytes.Length, []),
    };

    private static Arena2DungeonMediaPublication CreateDungeonMedia()
    {
        ImportPublicationArtifact artifact = new("media/dungeon/materials/minimal.png", "dungeon"u8);
        NormalizedMediaManifest manifest = MediaManifestNormalizer.Normalize(
            [new GeneratedMediaArtifact("material/texture-2-0", NormalizedMediaKind.Texture, artifact.RelativePath, artifact.Bytes.ToArray(), 1, 1, null, "image/png")]);
        return new(
            [artifact],
            [new DungeonMaterialTextureMedia("texture/minimal", "material/minimal", 2, 0, 1, 1, artifact)],
            manifest,
            [],
            []);
    }

    private static Arena2DungeonMediaPublication CreateDungeonMediaWithBillboard()
    {
        const string id = "billboard/minimal";
        NormalizedSpriteAtlas atlas = CreateOneFrameAtlas(id);
        ImportPublicationArtifact artifact = new("media/dungeon/billboards/minimal.png", atlas.PngBytes);
        NormalizedMediaManifest manifest = MediaManifestNormalizer.Normalize(
            [GeneratedMediaArtifact.FromAtlas(id, NormalizedMediaKind.Billboard, artifact.RelativePath, atlas)]);
        NormalizedMediaDescriptor descriptor = manifest.Resources.Single();
        DungeonMediaFrameLayout frame = new(
            0,
            0,
            0,
            0,
            false,
            descriptor.Frames.Single(),
            new NormalizedVector2(1F, 1F));
        DungeonBillboardSpriteMedia billboard = new(
            id,
            2,
            0,
            new NormalizedVector2(0.5F, 0.5F),
            new NormalizedVector2(1F, 1F),
            new DungeonSpritePlaybackSource(5F, true),
            new DungeonSpritePlaybackSource(5F, true),
            [frame],
            artifact,
            descriptor);
        return new([artifact], [], manifest, [billboard], []);
    }

    private static Arena2ClassicMediaPublication CreateClassicMedia(string? artifactPath = null)
    {
        ImportPublicationArtifact uiArtifact = new(artifactPath ?? "media/classic/minimal.bin", "classic"u8);
        const string mediaId = "classic.minimal";
        const string weaponMediaId = "weapon.minimal";
        NormalizedSpriteAtlas weaponAtlas = CreateAtlas(weaponMediaId, 7);
        ClassicWeaponActionManifest[] actions = Enum.GetValues<ClassicDaggerWeaponAction>()
            .Select((action, record) => new ClassicWeaponActionManifest(
                action,
                record,
                record,
                1,
                ClassicWeaponScreenAlignment.Right,
                0F,
                new ClassicSpriteTiming(10F, action == ClassicDaggerWeaponAction.Idle),
                0,
                0))
            .ToArray();
        ClassicEffectManifest[] effects = Enum.GetValues<ClassicEffect>()
            .Select((effect, index) => new ClassicEffectManifest(effect, $"effect.minimal.{index}", Arena2ClassicMediaPublication.ExpectedEffectSourceRecordOrdinal(effect), new ClassicSpriteTiming(10F, false)))
            .ToArray();
        List<GeneratedMediaArtifact> generated =
        [
            new(mediaId, NormalizedMediaKind.UserInterface, uiArtifact.RelativePath, uiArtifact.Bytes.ToArray(), 0, 0, null, "application/octet-stream"),
            GeneratedMediaArtifact.FromAtlas(weaponMediaId, NormalizedMediaKind.WeaponSprite, "media/classic/weapon.png", weaponAtlas),
        ];
        List<ImportPublicationArtifact> artifacts =
        [
            uiArtifact,
            new("media/classic/weapon.png", weaponAtlas.PngBytes),
        ];
        foreach (ClassicEffectManifest effect in effects)
        {
            NormalizedSpriteAtlas effectAtlas = CreateOneFrameAtlas(effect.MediaId);
            string path = $"media/classic/{effect.MediaId}.png";
            generated.Add(GeneratedMediaArtifact.FromAtlas(effect.MediaId, NormalizedMediaKind.EffectSprite, path, effectAtlas));
            artifacts.Add(new ImportPublicationArtifact(path, effectAtlas.PngBytes));
        }

        NormalizedMediaManifest manifest = MediaManifestNormalizer.Normalize(
            generated,
            effects.Select(effect => new AuthoredMediaOverlay(
                effect.MediaId,
                true,
                FramesPerSecond: effect.Timing.FramesPerSecond,
                Loop: effect.Timing.Loop)));
        return new(
            artifacts,
            manifest,
            [new ClassicWeaponMediaManifest(weaponMediaId, actions)],
            effects,
            [],
            [],
            [],
            [],
            EmptyMapRegions(),
            [],
            []);
    }

    private static IReadOnlyList<ClassicMapRegionManifest> EmptyMapRegions() =>
        Enumerable.Range(0, 62).Select(region => new ClassicMapRegionManifest(region, [])).ToArray();

    private static IReadOnlyList<ClassicWeaponActionManifest> WeaponActions(Arena2ClassicMediaPublication publication) =>
        publication.WeaponMedia.Single().Actions;

    private static Arena2ClassicMediaPublication WithWeaponActions(Arena2ClassicMediaPublication publication, IReadOnlyList<ClassicWeaponActionManifest> actions) =>
        publication with { WeaponMedia = [publication.WeaponMedia.Single() with { Actions = actions }] };

    private static NormalizedSpriteAtlas CreateOneFrameAtlas(string id)
    {
        return CreateAtlas(id, 1);
    }

    private static NormalizedSpriteAtlas CreateAtlas(string id, int frameCount)
    {
        byte[] png = DeterministicPngEncoder.EncodeRgba8(frameCount, 1, new byte[checked(frameCount * 4)]);
        return new(
            frameCount,
            1,
            png,
            ContentDigest.Compute(png),
            Enumerable.Range(0, frameCount)
                .Select(index => new NormalizedAtlasFrame($"{id}/{index}", index, index, 0, 1, 1, 1, 1, false))
                .ToArray());
    }

    /// <summary>A geometry publication for the fixture, over a mesh the fixture's own archive carries.</summary>
    private static GeometryPublication CreateGeometry()
    {
        byte[] mesh = new byte[64];
        "v2.7"u8.CopyTo(mesh);
        byte[] bytes = new byte[Arena2FormatConstants.BsaHeaderBytes + mesh.Length + Arena2FormatConstants.NumericBsaDirectoryEntryBytes];
        bytes[0] = 1;
        bytes[2] = (byte)(Arena2FormatConstants.NumericBsaDirectoryType & 0xff);
        bytes[3] = (byte)(Arena2FormatConstants.NumericBsaDirectoryType >> 8);
        mesh.CopyTo(bytes, Arena2FormatConstants.BsaHeaderBytes);
        BitConverter.GetBytes(9004u).CopyTo(bytes, Arena2FormatConstants.BsaHeaderBytes + mesh.Length);
        BitConverter.GetBytes(mesh.Length).CopyTo(bytes, Arena2FormatConstants.BsaHeaderBytes + mesh.Length + 4);
        Arch3dMeshInventory inventory = Arch3dInventoryReader.Read(bytes, "arena2/ARCH3D.BSA");
        return GeometryPublicationBuilder.Create(new GeometryPublicationRequest(
            inventory,
            bytes,
            ["9004"],
            TextureLeafInventory.Enumerate([], "arena2")));
    }
}
