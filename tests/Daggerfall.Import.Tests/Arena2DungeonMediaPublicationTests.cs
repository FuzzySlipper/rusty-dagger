using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class Arena2DungeonMediaPublicationTests
{
    [Fact]
    public void PublishesExactMaterialBillboardActorAndCorpseClosureDeterministically()
    {
        Arena2DungeonMediaRequest request = CreateRequest();

        Arena2DungeonMediaPublication first = Arena2DungeonMediaPublication.Create(request);
        Arena2DungeonMediaPublication second = Arena2DungeonMediaPublication.Create(CreateRequest(reverseSources: true));

        Assert.Equal(first.Artifacts.Select(artifact => artifact.RelativePath), second.Artifacts.Select(artifact => artifact.RelativePath));
        Assert.Equal(first.Artifacts.Select(artifact => artifact.Bytes.ToArray()), second.Artifacts.Select(artifact => artifact.Bytes.ToArray()));
        DungeonMaterialTextureMedia material = Assert.Single(first.MaterialTextures);
        Assert.Equal(("texture/2-0", "material/texture-2-0", (ushort)2, (ushort)0),
            (material.TextureResourceId, material.MaterialResourceId, material.TextureArchive, material.TextureRecord));
        Assert.Equal("media/dungeon/materials/texture-2-0.png", material.Artifact.RelativePath);
        Assert.Equal(NormalizedMediaKind.Texture, material.Descriptor.Kind);
        Assert.Equal(material.Artifact.ContentHash, material.Descriptor.ContentDigest);
        Assert.Equal((long)material.Artifact.Bytes.Length, material.Descriptor.ByteLength);
        Assert.Contains(first.MediaManifest.Resources, descriptor => descriptor.Id == "material/texture-2-0");

        DungeonBillboardSpriteMedia billboard = Assert.Single(first.Billboards);
        Assert.Equal("sprite/texture-2-1", billboard.SpriteResourceId);
        Assert.Null(billboard.Playback);
        Assert.Null(billboard.SourcePlayback);
        DungeonMediaFrameLayout billboardFrame = Assert.Single(billboard.Frames);
        Assert.Equal((0, 1, 0, 0, false), (billboardFrame.AtlasFrameIndex, billboardFrame.SourceRecord, billboardFrame.SourceFrame, billboardFrame.Orientation, billboardFrame.Mirrored));
        Assert.True(billboardFrame.AtlasFrame.Width > 0 && billboardFrame.AtlasFrame.Height > 0);

        DungeonActorSpriteMedia actor = Assert.Single(first.Actors);
        Assert.Equal("actor/mobile-0", actor.ActorResourceId);
        Assert.Equal((byte)0, actor.MobileId.Value);
        Assert.Equal("Rat", actor.SourceName);
        Assert.Equal(DungeonActorSpriteState.RatIdle, actor.PreferredRestState);
        Assert.Collection(actor.States,
            state => AssertState(state, DungeonActorSpriteState.Move, 6F, true),
            state => AssertState(state, DungeonActorSpriteState.RatIdle, 4F, true),
            state => AssertState(state, DungeonActorSpriteState.PrimaryAttack, 10F, false),
            state => AssertState(state, DungeonActorSpriteState.Hurt, 4F, false));
        Assert.Contains((sbyte)-1, actor.SourceAttackSequence.PrimaryFrames);
        Assert.Empty(actor.SourceAttackSequence.Alternates);
        Assert.NotNull(actor.Corpse);
        Assert.Equal((ushort)401, actor.Corpse!.TextureArchive);
        Assert.NotNull(actor.Corpse.Descriptor);
        Assert.All(first.Artifacts, artifact => Assert.DoesNotContain("PAL.PAL", artifact.RelativePath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("encounter", string.Join('|', first.Artifacts.Select(artifact => artifact.RelativePath)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmitsPreferredRestAndEffectiveFlyingPlaybackWithoutChangingSourceCadence()
    {
        DungeonActorSpriteMedia rat = Assert.Single(Arena2DungeonMediaPublication.Create(CreateRequest()).Actors);
        Assert.Equal(DungeonActorSpriteState.RatIdle, rat.PreferredRestState);
        AssertState(rat.States.Single(state => state.State == DungeonActorSpriteState.Move), DungeonActorSpriteState.Move, 6F, true);
        AssertState(rat.States.Single(state => state.State == DungeonActorSpriteState.RatIdle), DungeonActorSpriteState.RatIdle, 4F, true);

        foreach ((byte mobileId, ushort archive) in new[] { ((byte)1, (ushort)256), ((byte)3, (ushort)258) })
        {
            DungeonActorSpriteMedia flying = Assert.Single(Arena2DungeonMediaPublication.Create(CreateActorRequest(mobileId, archive)).Actors);
            Assert.Equal(DungeonActorSpriteState.Move, flying.PreferredRestState);
            AssertPlayback(flying.States.Single(state => state.State == DungeonActorSpriteState.Move), 6F, 10F);
            Assert.DoesNotContain(flying.States, state => state.State == DungeonActorSpriteState.Idle);
        }

        DungeonActorSpriteMedia ordinary = Assert.Single(Arena2DungeonMediaPublication.Create(CreateActorRequest(7, 262)).Actors);
        Assert.Equal(DungeonActorSpriteState.Idle, ordinary.PreferredRestState);
        AssertPlayback(ordinary.States.Single(state => state.State == DungeonActorSpriteState.Move), 6F, 6F);
        AssertPlayback(ordinary.States.Single(state => state.State == DungeonActorSpriteState.Idle), 4F, 4F);
    }

    [Fact]
    public void RejectsAnActorWhosePreferredRestStateWasNotPublished()
    {
        Arena2DungeonMediaSource[] sources = CreateSources();
        Replace(sources, "TEXTURE.255", CreateTextureArchive(15));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Arena2DungeonMediaPublication.Create(
            Arena2DungeonMediaRequest.Create(CreateDungeon(), new Arena2DungeonMediaSourceSet(sources))));

        Assert.Contains("preferred rest state", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectsDescriptorsAndDisplayProfileWithoutChangingRegeneratedAtlasFacts()
    {
        Arena2DungeonMediaPublication generated = Arena2DungeonMediaPublication.Create(CreateRequest());
        Arena2DungeonMediaRequest editedRequest = CreateRequest() with
        {
            AuthoredOverlays =
            [
                new AuthoredMediaOverlay(
                    "sprite/mobile-0",
                    true,
                    DisplayName: "Fixture rat",
                    Pivot: new NormalizedVector2(0.4F, 0F),
                    DisplaySize: new NormalizedVector2(1.2F, 0.8F),
                    FramesPerSecond: 9F,
                    Loop: false,
                    Sequence: [0, 1, 0]),
                new AuthoredMediaOverlay(
                    "sprite/texture-2-1",
                    true,
                    Pivot: new NormalizedVector2(0.2F, 0.8F),
                    DisplaySize: new NormalizedVector2(3F, 2F)),
            ],
            DisplayProfile = DungeonMediaDisplayProfile.DaggerfallDefault with
            {
                ActorWorldScale = 2F,
                MoveFramesPerSecond = 7F,
                ActorPivot = new NormalizedVector2(0.7F, 0.1F),
                BillboardWorldScale = 2F,
            },
        };

        Arena2DungeonMediaPublication edited = Arena2DungeonMediaPublication.Create(editedRequest);

        DungeonActorSpriteMedia original = Assert.Single(generated.Actors);
        DungeonActorSpriteMedia updated = Assert.Single(edited.Actors);
        Assert.Equal(original.Artifact.Bytes.ToArray(), updated.Artifact.Bytes.ToArray());
        Assert.Equal(original.States.SelectMany(state => state.Frames), updated.States.SelectMany(state => state.Frames));
        Assert.Equal("Fixture rat", updated.Descriptor.DisplayName);
        Assert.Equal(new NormalizedVector2(0.4F, 0F), updated.Descriptor.Pivot);
        Assert.Equal(9F, updated.Descriptor.FramesPerSecond);
        Assert.Equal(new NormalizedVector2(0.4F, 0F), updated.Pivot);
        Assert.Equal(new NormalizedVector2(1.2F, 0.8F), updated.WorldSize);
        Assert.All(updated.States, state =>
        {
            Assert.Equal(9F, state.Playback.FramesPerSecond);
            Assert.False(state.Playback.Loops);
        });
        Assert.Equal(6F, updated.States[0].SourcePlayback.FramesPerSecond);
        Assert.True(updated.States[0].SourcePlayback.Loops);
        DungeonBillboardSpriteMedia updatedBillboard = Assert.Single(edited.Billboards);
        Assert.Equal(new NormalizedVector2(0.2F, 0.8F), updatedBillboard.Pivot);
        Assert.Equal(new NormalizedVector2(3F, 2F), updatedBillboard.WorldSize);
    }

    [Fact]
    public void RejectsSourceClosureMalformedArchivesAndAtlasQuotaOverflow()
    {
        Arena2DungeonMediaSource[] sources = CreateSources();
        Arena2DungeonMediaSourceSet extra = new([.. sources, new Arena2DungeonMediaSource("TEXTURE.003", CreateTextureArchive(1))]);
        Assert.Throws<InvalidOperationException>(() => Arena2DungeonMediaPublication.Create(Arena2DungeonMediaRequest.Create(CreateDungeon(), extra)));

        Arena2DungeonMediaSource[] malformedSources = CreateSources();
        Replace(malformedSources, "TEXTURE.255", [1]);
        Arena2DungeonMediaSourceSet malformed = new(malformedSources);
        Assert.Throws<Arena2FormatException>(() => Arena2DungeonMediaPublication.Create(Arena2DungeonMediaRequest.Create(CreateDungeon(), malformed)));

        Arena2DungeonMediaQuotas tinyAtlas = Arena2DungeonMediaQuotas.Default with { MaximumAtlasDimension = 1 };
        Assert.Throws<InvalidOperationException>(() => Arena2DungeonMediaPublication.Create(new(CreateDungeon(), new(CreateSources()), tinyAtlas)));
    }

    [Fact]
    public void RejectsMaterialOverlaysAndInvalidDisplayProfileValues()
    {
        Arena2DungeonMediaRequest materialOverlay = CreateRequest() with
        {
            AuthoredOverlays = [new AuthoredMediaOverlay("material/texture-2-0", true, DisplayName: "not a sprite")],
        };
        Arena2DungeonMediaRequest invalidProfile = CreateRequest() with
        {
            DisplayProfile = DungeonMediaDisplayProfile.DaggerfallDefault with { ActorWorldScale = 0F },
        };

        Assert.Throws<InvalidOperationException>(() => Arena2DungeonMediaPublication.Create(materialOverlay));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena2DungeonMediaPublication.Create(invalidProfile));
    }

    private static void AssertState(DungeonActorSpriteStateLayout layout, DungeonActorSpriteState state, float fps, bool loops)
    {
        Assert.Equal(state, layout.State);
        Assert.Equal(fps, layout.SourcePlayback.FramesPerSecond);
        Assert.Equal(loops, layout.SourcePlayback.Loops);
        Assert.Equal(fps, layout.Playback.FramesPerSecond);
        Assert.Equal(loops, layout.Playback.Loops);
        Assert.Equal(1, layout.FramesPerOrientation);
        Assert.Equal(8, layout.Frames.Count);
        Assert.Equal(Enumerable.Range(0, 8), layout.Frames.Select(frame => frame.Orientation));
        bool[] expectedMirroring = state == DungeonActorSpriteState.RatIdle
            ? [false, true, true, true, false, false, false, false]
            : [false, false, false, false, false, true, true, true];
        Assert.Equal(expectedMirroring, layout.Frames.Select(frame => frame.Mirrored));
        Assert.All(layout.Frames, frame => Assert.True(frame.AtlasFrame.Width > 0 && frame.AtlasFrame.Height > 0));
    }

    private static void AssertPlayback(DungeonActorSpriteStateLayout layout, float sourceFramesPerSecond, float effectiveFramesPerSecond)
    {
        Assert.Equal(sourceFramesPerSecond, layout.SourcePlayback.FramesPerSecond);
        Assert.Equal(effectiveFramesPerSecond, layout.Playback.FramesPerSecond);
    }

    private static Arena2DungeonMediaRequest CreateRequest(bool reverseSources = false) =>
        Arena2DungeonMediaRequest.Create(CreateDungeon(), new Arena2DungeonMediaSourceSet(reverseSources ? CreateSources().Reverse() : CreateSources()));

    private static Arena2DungeonMediaRequest CreateActorRequest(byte mobileId, ushort archive)
    {
        NormalizedImportDocument document = CreateDungeon();
        string actorId = $"actor/mobile-{mobileId}";
        document = document with
        {
            Resources = document.Resources.Select(resource => resource.Id == "actor/mobile-0"
                ? resource with { Id = actorId }
                : resource).ToArray(),
            World = document.World with
            {
                Actors = [new NormalizedActorPlacement("actor/fixture", actorId, new NormalizedVector3(0, 0, 0))],
            },
        };
        return Arena2DungeonMediaRequest.Create(document, new Arena2DungeonMediaSourceSet(
        [
            new Arena2DungeonMediaSource("arena2/PAL.PAL", CreatePalette()),
            new Arena2DungeonMediaSource("arena2/TEXTURE.002", CreateTextureArchive(2)),
            new Arena2DungeonMediaSource($"arena2/TEXTURE.{archive:000}", CreateTextureArchive(20)),
        ]));
    }

    private static Arena2DungeonMediaSource[] CreateSources() =>
    [
        new("arena2/PAL.PAL", CreatePalette()),
        new("arena2/TEXTURE.002", CreateTextureArchive(2)),
        new("arena2/TEXTURE.255", CreateTextureArchive(20)),
        new("arena2/TEXTURE.401", CreateTextureArchive(2)),
    ];

    private static NormalizedImportDocument CreateDungeon()
    {
        const string artifactId = "artifact/fixture";
        NormalizedArtifactDescriptor artifact = new(
            NormalizedArtifactDescriptor.CurrentSchemaVersion,
            artifactId,
            "fixture.json",
            new ContentDigest(new string('0', 64)),
            1,
            []);
        NormalizedResourceCatalogEntry texture = new(NormalizedResourceCatalogEntry.CurrentSchemaVersion, "texture/2-0", NormalizedResourceKind.Texture, artifactId, [], []);
        NormalizedResourceCatalogEntry material = new(NormalizedResourceCatalogEntry.CurrentSchemaVersion, "material/texture-2-0", NormalizedResourceKind.Material, artifactId, ["texture/2-0"], []);
        NormalizedResourceCatalogEntry sprite = new(NormalizedResourceCatalogEntry.CurrentSchemaVersion, "sprite/texture-2-1", NormalizedResourceKind.Sprite, artifactId, [], []);
        NormalizedResourceCatalogEntry actor = new(NormalizedResourceCatalogEntry.CurrentSchemaVersion, "actor/mobile-0", NormalizedResourceKind.ActorDefinition, artifactId, [], []);
        NormalizedMesh mesh = new(
            NormalizedMesh.CurrentSchemaVersion,
            "mesh/fixture",
            artifactId,
            [new NormalizedVector3(0, 0, 0), new NormalizedVector3(1, 0, 0), new NormalizedVector3(0, 0, 1)],
            [new NormalizedVector3(0, 1, 0), new NormalizedVector3(0, 1, 0), new NormalizedVector3(0, 1, 0)],
            [new NormalizedVector2(0, 0), new NormalizedVector2(1, 0), new NormalizedVector2(0, 1)],
            [new NormalizedTriangle(0, 1, 2)],
            [new NormalizedMaterialGroup("material/texture-2-0", 0, 1, true)]);
        NormalizedWorld world = new(
            NormalizedWorld.CurrentSchemaVersion,
            "mesh/fixture",
            ["mesh/fixture"],
            null,
            null,
            null,
            [],
            [new NormalizedBillboardPlacement("billboard/fixture", "sprite/texture-2-1", new NormalizedVector3(0, 0, 0), new NormalizedVector2(1, 1))],
            [new NormalizedActorPlacement("actor/fixture", "actor/mobile-0", new NormalizedVector3(0, 0, 0))],
            [],
            []);
        return new NormalizedImportDocument(
            NormalizedImportDocument.CurrentSchemaVersion,
            new ImportProvenance(
                ImportProvenance.CurrentSchemaVersion,
                "daggerfall-import/test",
                1,
                [new LogicalSourceRecord(LogicalSourceRecord.CurrentSchemaVersion, "arena2/MAPS.BSA", new ContentDigest(new string('0', 64)), 1, 1)]),
            [artifact],
            new NormalizedCoordinateConvention(NormalizedCoordinateConvention.CurrentSchemaVersion, NormalizedHandedness.Right, NormalizedVerticalAxis.PositiveY, 1F),
            new NormalizedBounds(NormalizedBounds.CurrentSchemaVersion, new NormalizedVector3(0, 0, 0), new NormalizedVector3(1, 1, 1)),
            [mesh],
            null,
            world,
            [texture, material, sprite, actor]);
    }

    private static void Replace(Arena2DungeonMediaSource[] sources, string suffix, byte[] bytes)
    {
        int index = Array.FindIndex(sources, source => source.Label.EndsWith(suffix, StringComparison.Ordinal));
        Assert.True(index >= 0);
        sources[index] = new Arena2DungeonMediaSource(sources[index].Label, bytes);
    }

    private static byte[] CreatePalette()
    {
        byte[] palette = new byte[768];
        for (int index = 1; index < 256; index++)
        {
            palette[index * 3] = (byte)(index % 64);
            palette[(index * 3) + 1] = (byte)((index * 3) % 64);
            palette[(index * 3) + 2] = (byte)((index * 7) % 64);
        }

        return palette;
    }

    private static byte[] CreateTextureArchive(int recordCount)
    {
        const int headerBytes = 26;
        const int tableEntryBytes = 20;
        const int recordBytes = 28;
        const int paddedPixelRows = 512;
        int recordsStart = headerBytes + (recordCount * tableEntryBytes);
        byte[] bytes = new byte[recordsStart + (recordCount * (recordBytes + paddedPixelRows))];
        BitConverter.GetBytes(checked((short)recordCount)).CopyTo(bytes, 0);
        for (int record = 0; record < recordCount; record++)
        {
            int offset = recordsStart + (record * (recordBytes + paddedPixelRows));
            BitConverter.GetBytes(offset).CopyTo(bytes, headerBytes + (record * tableEntryBytes) + 2);
            BitConverter.GetBytes((short)2).CopyTo(bytes, offset + 4);
            BitConverter.GetBytes((short)2).CopyTo(bytes, offset + 6);
            BitConverter.GetBytes((uint)recordBytes).CopyTo(bytes, offset + 14);
            BitConverter.GetBytes((ushort)1).CopyTo(bytes, offset + 20);
            byte color = checked((byte)(record + 1));
            bytes[offset + recordBytes] = color;
            bytes[offset + recordBytes + 1] = color;
            bytes[offset + recordBytes + 256] = color;
            bytes[offset + recordBytes + 257] = color;
        }

        return bytes;
    }
}
