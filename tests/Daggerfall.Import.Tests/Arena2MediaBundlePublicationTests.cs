using System.Text;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class Arena2MediaBundlePublicationTests
{
    [Fact]
    public void CreatesCanonicalMediaSidecarsAndACompleteDependencyClosure()
    {
        Arena2MediaBundlePublication first = CreateBundle(reverseClassicActions: false);
        Arena2MediaBundlePublication second = CreateBundle(reverseClassicActions: true);

        Assert.Equal(first.Plan.Artifacts.Select(artifact => artifact.RelativePath), second.Plan.Artifacts.Select(artifact => artifact.RelativePath));
        Assert.Equal(first.Plan.Artifacts.Select(artifact => artifact.Bytes.ToArray()), second.Plan.Artifacts.Select(artifact => artifact.Bytes.ToArray()));
        Assert.Equal(2, first.Document.Provenance.Sources.Count);
        Assert.Equal(first.Document.Provenance.Sources.OrderBy(source => source.SourcePath, StringComparer.Ordinal).Select(source => source.SourcePath), first.Document.Provenance.Sources.Select(source => source.SourcePath));

        ImportPublicationManifestArtifact dungeonSidecar = first.Plan.Manifest.Artifacts.Single(artifact => artifact.RelativePath == Arena2MediaBundlePublication.DungeonMediaManifestRelativePath);
        Assert.Equal(["media/dungeon/materials/minimal.png"], dungeonSidecar.DependsOnPaths);
        ImportPublicationManifestArtifact classicSidecar = first.Plan.Manifest.Artifacts.Single(artifact => artifact.RelativePath == Arena2MediaBundlePublication.ClassicMediaManifestRelativePath);
        Assert.Equal(
            [
                "media/classic/effect-0.png",
                "media/classic/effect-1.png",
                "media/classic/effect-2.png",
                "media/classic/effect-3.png",
                "media/classic/font.bin",
                "media/classic/minimal.bin",
                "media/classic/weapon.png",
            ],
            classicSidecar.DependsOnPaths);
        ImportPublicationManifestArtifact normalized = first.Plan.Manifest.Artifacts.Single(artifact => artifact.RelativePath == Arena2MediaBundlePublication.NormalizedDocumentRelativePath);
        Assert.Equal(
            [
                Arena2MediaBundlePublication.ClassicMediaManifestRelativePath,
                Arena2MediaBundlePublication.DungeonMediaManifestRelativePath,
                "resources/test/catalog.json",
                "spatial/test/collision-navigation.json",
                "spatial/test/static-mesh.json",
            ],
            normalized.DependsOnPaths);

        string dungeonJson = Encoding.UTF8.GetString(first.Plan.Artifacts.Single(artifact => artifact.RelativePath == Arena2MediaBundlePublication.DungeonMediaManifestRelativePath).Bytes.Span);
        string classicJson = Encoding.UTF8.GetString(first.Plan.Artifacts.Single(artifact => artifact.RelativePath == Arena2MediaBundlePublication.ClassicMediaManifestRelativePath).Bytes.Span);
        Assert.EndsWith("\n", dungeonJson, StringComparison.Ordinal);
        Assert.EndsWith("\n", classicJson, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": 1", dungeonJson, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\": 1", classicJson, StringComparison.Ordinal);
        Assert.DoesNotContain("encounter", dungeonJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("encounter", classicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"bytes\"", dungeonJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"bytes\"", classicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/", Encoding.UTF8.GetString(first.Plan.Artifacts.Single(artifact => artifact.RelativePath == Arena2MediaBundlePublication.NormalizedDocumentRelativePath).Bytes.Span), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsConflictingSourceProvenanceAndDuplicateMediaPaths()
    {
        DungeonNormalizationResult dungeon = CreateDungeon();
        Arena2DungeonMediaPublication dungeonMedia = CreateDungeonMedia();
        Arena2ClassicMediaPublication conflict = CreateClassicMedia(sourceDigest: ContentDigest.Compute("different"u8));
        Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.Create(dungeon, dungeonMedia, conflict));

        Arena2ClassicMediaPublication duplicate = CreateClassicMedia(artifactPath: "media/dungeon/materials/minimal.png");
        Assert.Throws<ArgumentException>(() => Arena2MediaBundlePublication.Create(dungeon, dungeonMedia, duplicate));
    }

    [Fact]
    public void RejectsClassicActionsOutsideTheCanonicalWeaponFrames()
    {
        Arena2ClassicMediaPublication classic = CreateClassicMedia();
        classic = classic with
        {
            WeaponActions = [classic.WeaponActions.First() with { FrameCount = 2 }],
        };

        Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.Create(CreateDungeon(), CreateDungeonMedia(), classic));
    }

    [Fact]
    public void RejectsMediaDescriptorFramesOutsideTheirAtlas()
    {
        Arena2ClassicMediaPublication classic = CreateClassicMedia();
        NormalizedMediaManifest invalidManifest = new(classic.MediaManifest.Resources.Select(resource =>
            resource.Id == "weapon.minimal"
                ? resource with { Frames = [resource.Frames[0] with { X = resource.AtlasWidth }] }
                : resource).ToArray());

        Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.Create(
            CreateDungeon(),
            CreateDungeonMedia(),
            classic with { MediaManifest = invalidManifest }));
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

        Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.Create(CreateDungeon(), dungeonMedia, CreateClassicMedia()));
    }

    [Fact]
    public void RejectsIncompleteOrRemappedClassicWeaponActions()
    {
        Arena2ClassicMediaPublication classic = CreateClassicMedia();
        Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.Create(
            CreateDungeon(),
            CreateDungeonMedia(),
            classic with { WeaponActions = classic.WeaponActions.Take(6).ToArray() }));

        ClassicWeaponActionManifest idle = classic.WeaponActions.Single(action => action.Action == ClassicDaggerWeaponAction.Idle);
        Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.Create(
            CreateDungeon(),
            CreateDungeonMedia(),
            classic with
            {
                WeaponActions = classic.WeaponActions.Select(action => action.Action == ClassicDaggerWeaponAction.Idle
                    ? idle with { SourceRecordOrdinal = 6 }
                    : action).ToArray(),
            }));
    }

    [Fact]
    public void RejectsEffectTimingOrLoopThatDisagreesWithCanonicalDescriptor()
    {
        Arena2ClassicMediaPublication classic = CreateClassicMedia();
        ClassicEffectManifest effect = classic.Effects.Single(value => value.Effect == ClassicEffect.Blood0);
        Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.Create(
            CreateDungeon(),
            CreateDungeonMedia(),
            classic with
            {
                Effects = classic.Effects.Select(value => value.Effect == effect.Effect
                    ? effect with { Timing = new ClassicSpriteTiming(5F, false) }
                    : value).ToArray(),
            }));

        Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.Create(
            CreateDungeon(),
            CreateDungeonMedia(),
            classic with
            {
                Effects = classic.Effects.Select(value => value.Effect == effect.Effect
                    ? effect with { Timing = new ClassicSpriteTiming(10F, true) }
                    : value).ToArray(),
            }));

        NormalizedMediaManifest descriptorWithChangedTiming = new(classic.MediaManifest.Resources.Select(resource =>
            resource.Id == effect.MediaId
                ? resource with { FramesPerSecond = 5F }
                : resource).ToArray());
        Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.Create(
            CreateDungeon(),
            CreateDungeonMedia(),
            classic with { MediaManifest = descriptorWithChangedTiming }));
    }

    private static Arena2MediaBundlePublication CreateBundle(bool reverseClassicActions)
    {
        Arena2ClassicMediaPublication classic = CreateClassicMedia();
        if (reverseClassicActions)
        {
            classic = classic with { WeaponActions = classic.WeaponActions.Reverse().ToArray() };
        }

        return Arena2MediaBundlePublication.Create(CreateDungeon(), CreateDungeonMedia(), classic);
    }

    private static DungeonNormalizationResult CreateDungeon()
    {
        const string staticArtifactId = "artifact/static";
        const string collisionArtifactId = "artifact/collision";
        const string resourcesArtifactId = "artifact/resources";
        NormalizedBounds bounds = new(
            NormalizedBounds.CurrentSchemaVersion,
            new NormalizedVector3(0F, 0F, 0F),
            new NormalizedVector3(1F, 1F, 1F));
        NormalizedNavigationSurface navigation = new(
            NormalizedNavigationSurface.CurrentSchemaVersion,
            "navigation/test",
            collisionArtifactId,
            NavigationDerivationConfig.ClassicDefault,
            []);
        NormalizedResourceCatalogEntry texture = new(
            NormalizedResourceCatalogEntry.CurrentSchemaVersion,
            "texture/minimal",
            NormalizedResourceKind.Texture,
            resourcesArtifactId,
            [],
            []);
        NormalizedResourceCatalogEntry material = new(
            NormalizedResourceCatalogEntry.CurrentSchemaVersion,
            "material/minimal",
            NormalizedResourceKind.Material,
            resourcesArtifactId,
            [texture.Id],
            []);
        NormalizedMesh mesh = new(
            NormalizedMesh.CurrentSchemaVersion,
            "mesh/test",
            staticArtifactId,
            [new NormalizedVector3(0F, 0F, 0F), new NormalizedVector3(1F, 0F, 0F), new NormalizedVector3(0F, 0F, 1F)],
            [new NormalizedVector3(0F, 1F, 0F), new NormalizedVector3(0F, 1F, 0F), new NormalizedVector3(0F, 1F, 0F)],
            [new NormalizedVector2(0F, 0F), new NormalizedVector2(1F, 0F), new NormalizedVector2(0F, 1F)],
            [new NormalizedTriangle(0, 1, 2)],
            [new NormalizedMaterialGroup(material.Id, 0, 1, true)]);
        NormalizedWorld world = new(
            NormalizedWorld.CurrentSchemaVersion,
            "mesh/test",
            [mesh.Id],
            navigation.Id,
            null,
            null,
            [],
            [],
            [],
            [],
            []);
        DungeonSpatialPublication spatial = DungeonSpatialPublication.Create(
            staticArtifactId,
            "spatial/test/static-mesh.json",
            collisionArtifactId,
            "spatial/test/collision-navigation.json",
            resourcesArtifactId,
            "resources/test/catalog.json",
            "mesh/test",
            bounds,
            [mesh],
            world,
            navigation,
            [texture, material]);
        ContentDigest paletteDigest = ContentDigest.Compute("palette"u8);
        NormalizedImportDocument document = new NormalizedImportDocument(
            NormalizedImportDocument.CurrentSchemaVersion,
            new ImportProvenance(
                ImportProvenance.CurrentSchemaVersion,
                "daggerfall-import/test",
                1,
                [new LogicalSourceRecord(LogicalSourceRecord.CurrentSchemaVersion, "arena2/PAL.PAL", paletteDigest, 7, 1)]),
            spatial.ArtifactDescriptors,
            new NormalizedCoordinateConvention(
                NormalizedCoordinateConvention.CurrentSchemaVersion,
                NormalizedHandedness.Right,
                NormalizedVerticalAxis.PositiveY,
                1F),
            bounds,
            [mesh],
            navigation,
            world,
            [texture, material]).Canonicalize();
        return new(document, [], spatial);
    }

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

    private static Arena2ClassicMediaPublication CreateClassicMedia(ContentDigest? sourceDigest = null, string? artifactPath = null)
    {
        ImportPublicationArtifact uiArtifact = new(artifactPath ?? "media/classic/minimal.bin", "classic"u8);
        const string mediaId = "classic.minimal";
        const string weaponMediaId = "weapon.minimal";
        const string fontMediaId = "font.minimal";
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
            .Select((effect, record) => new ClassicEffectManifest(effect, $"effect.minimal.{record}", record, new ClassicSpriteTiming(10F, false)))
            .ToArray();
        List<GeneratedMediaArtifact> generated =
        [
            new(mediaId, NormalizedMediaKind.UserInterface, uiArtifact.RelativePath, uiArtifact.Bytes.ToArray(), 0, 0, null, "application/octet-stream"),
            GeneratedMediaArtifact.FromAtlas(weaponMediaId, NormalizedMediaKind.WeaponSprite, "media/classic/weapon.png", weaponAtlas),
            new(fontMediaId, NormalizedMediaKind.Font, "media/classic/font.bin", "font"u8.ToArray(), 0, 0, null, "application/octet-stream"),
        ];
        List<ImportPublicationArtifact> artifacts =
        [
            uiArtifact,
            new("media/classic/weapon.png", weaponAtlas.PngBytes),
            new("media/classic/font.bin", "font"u8),
        ];
        foreach (ClassicEffectManifest effect in effects)
        {
            NormalizedSpriteAtlas effectAtlas = CreateOneFrameAtlas(effect.MediaId);
            string path = $"media/classic/effect-{effect.SourceRecordOrdinal}.png";
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
            [new LogicalSourceRecord(LogicalSourceRecord.CurrentSchemaVersion, "arena2/PAL.PAL", sourceDigest ?? ContentDigest.Compute("palette"u8), 7, 1),
             new LogicalSourceRecord(LogicalSourceRecord.CurrentSchemaVersion, "arena2/WEAPON02.CIF", ContentDigest.Compute("weapon"u8), 6, 1)],
            null,
            actions,
            effects,
            [],
            [],
            [],
            new ClassicFontManifest(
                fontMediaId,
                1,
                1,
                Enumerable.Range(0, 240).Select(index => new ClassicFontGlyphMetric(index, index, 0, 1, 0)).ToArray()),
            []);
    }

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
}
