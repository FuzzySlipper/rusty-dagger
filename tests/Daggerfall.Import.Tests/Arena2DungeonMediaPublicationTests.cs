using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class Arena2DungeonMediaPublicationTests
{
    [Fact]
    public void Human_media_publishes_a_complete_female_atlas_with_the_same_animation_contract()
    {
        var publication = Arena2DungeonMediaPublication.Create(CreateActorRequest(141, 482, 25));
        var actor = Assert.Single(publication.Actors);
        var female = Assert.IsType<DungeonActorSpriteMedia>(actor.Female);
        Assert.Equal("sprite/mobile-141-female", female.SpriteResourceId);
        Assert.Equal(actor.States.Select(state => state.State), female.States.Select(state => state.State));
        Assert.Contains(publication.Artifacts, artifact => artifact.RelativePath == female.Artifact.RelativePath);
        Assert.Contains(publication.MediaManifest.Resources, media => media.Id == female.SpriteResourceId);
    }

    [Fact]
    public void Selected_publication_refuses_absent_or_refused_texture_leaves_with_its_consumer()
    {
        Arena2DungeonMediaSource palette = new("arena2/PAL.PAL", [1]);
        Arena2DungeonMediaSourceSet absent = new([palette, new Arena2DungeonMediaSource("arena2/TEXTURE.021", [1])]);
        InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() =>
            absent.RequireTextureLeaves(TextureLeafInventory.Enumerate([], "fixture"), "selected dungeon media 'fixture'"));
        Assert.Contains("selected dungeon media 'fixture'", missing.Message, StringComparison.Ordinal);
        Assert.Contains("21", missing.Message, StringComparison.Ordinal);

        byte[] refusedBytes = new byte[46];
        TextureLeafInventory refused = TextureLeafInventory.Enumerate([(215, "TEXTURE.215", (ReadOnlyMemory<byte>)refusedBytes)], "fixture");
        Arena2DungeonMediaSourceSet malformed = new([palette, new Arena2DungeonMediaSource("arena2/TEXTURE.215", refusedBytes)]);
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            malformed.RequireTextureLeaves(refused, "selected dungeon media 'fixture'"));
        Assert.Contains("selected dungeon media 'fixture'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("215", failure.Message, StringComparison.Ordinal);
        Assert.Contains("does not parse", failure.Message, StringComparison.Ordinal);
    }

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
    public void Publishes_runtime_terrain_texture_into_the_admitted_media_closure()
    {
        Arena2DungeonMediaPublication publication = Arena2DungeonMediaPublication.Create(CreateRequest() with
        {
            RuntimeTerrainResources = ["terrain/texture-2-0"],
        });

        ImportPublicationArtifact terrain = Assert.Single(publication.Artifacts,
            artifact => artifact.RelativePath == "media/dungeon/terrain/texture-2-0.png");
        Assert.True(terrain.Bytes.Length > 0);
        Assert.Contains(publication.MediaManifest.Resources, resource =>
            resource.Id == "terrain/texture-2-0"
            && resource.Kind == NormalizedMediaKind.Texture
            && resource.RelativePath == terrain.RelativePath);
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
    public void PublishesAnExplicitRuntimeHumanMobileOutsideThePlacedDungeonActors()
    {
        List<Arena2DungeonMediaSource> sources = [.. CreateSources(),
            new("arena2/TEXTURE.380", CreateTextureArchive(20)),
            new("arena2/TEXTURE.482", CreateTextureArchive(25)),
            new("arena2/TEXTURE.481", CreateTextureArchive(25))];
        Arena2DungeonMediaPublication publication = Arena2DungeonMediaPublication.Create(CreateRequest() with
        {
            Sources = new Arena2DungeonMediaSourceSet(sources),
            RuntimeActorResources = ["actor/mobile-141"],
        });

        Assert.Equal(new byte[] { 0, 141 }, publication.Actors.Select(actor => actor.MobileId.Value));
        DungeonActorSpriteMedia archer = publication.Actors.Single(actor => actor.MobileId.Value == 141);
        Assert.Equal("Archer", archer.SourceName);
        Assert.NotNull(archer.Corpse);
        Assert.Contains(archer.States, state => state.State == DungeonActorSpriteState.RangedAttack1);
    }

    [CorpusFact("PAL.PAL", "TEXTURE.002", "TEXTURE.096", "TEXTURE.285")]
    public void RetainsTheDifferentSourceFrameCountsOfEachDirection()
    {
        Arena2DungeonMediaRequest request = CreateActorRequest(30, 285) with
        {
            Sources = new Arena2DungeonMediaSourceSet(
            [
                new("arena2/PAL.PAL", File.ReadAllBytes(TestData.Corpus("PAL.PAL"))),
                new("arena2/TEXTURE.002", File.ReadAllBytes(TestData.Corpus("TEXTURE.002"))),
                new("arena2/TEXTURE.096", File.ReadAllBytes(TestData.Corpus("TEXTURE.096"))),
                new("arena2/TEXTURE.285", File.ReadAllBytes(TestData.Corpus("TEXTURE.285"))),
            ]),
        };

        DungeonActorSpriteStateLayout attack = Assert.Single(Arena2DungeonMediaPublication.Create(request).Actors).States
            .Single(state => state.State == DungeonActorSpriteState.PrimaryAttack);
        int[] sourceFrameCounts = attack.Frames.GroupBy(frame => frame.Orientation)
            .OrderBy(group => group.Key).Select(group => group.Count()).ToArray();
        Assert.Equal(8, sourceFrameCounts.Length);
        Assert.Equal(6, sourceFrameCounts[0]);
        Assert.Equal(5, sourceFrameCounts[1]);
        Assert.Equal(6, attack.FramesPerOrientation);
    }

    [Fact]
    public void FemaleThiefFamilyIdleUsesRecordElevenForBothFrontQuarters()
    {
        ushort[] ordinaryIdle = [15, 16, 17, 18, 19, 18, 17, 16];
        bool[] ordinaryMirroring = [false, false, false, false, false, true, true, true];
        foreach (byte id in new byte[] { 134, 135, 137, 138 })
        {
            DungeonActorSpriteMedia actor = Assert.Single(Arena2DungeonMediaPublication.Create(CreateActorRequest(id, 484, actorRecordCount: 25)).Actors);
            DungeonActorSpriteMedia female = Assert.IsType<DungeonActorSpriteMedia>(actor.Female);
            DungeonActorSpriteStateLayout femaleIdle = female.States.Single(state => state.State == DungeonActorSpriteState.Idle);
            Assert.Equal(new[] { 15, 11, 17, 18, 19, 18, 17, 11 }, femaleIdle.Frames.Select(frame => frame.SourceRecord));
            Assert.Equal(Enumerable.Range(0, 8), femaleIdle.Frames.Select(frame => frame.Orientation));
            Assert.Equal(ordinaryMirroring, femaleIdle.Frames.Select(frame => frame.Mirrored));

            // The male archive keeps the classic idle mapping, and the female archive's other
            // groups keep theirs.
            DungeonActorSpriteStateLayout maleIdle = actor.States.Single(state => state.State == DungeonActorSpriteState.Idle);
            Assert.Equal(ordinaryIdle.Select(record => (int)record), maleIdle.Frames.Select(frame => frame.SourceRecord));
            foreach (DungeonActorSpriteStateLayout state in female.States.Where(state => state.State != DungeonActorSpriteState.Idle))
            {
                DungeonActorSpriteStateLayout male = actor.States.Single(candidate => candidate.State == state.State);
                Assert.Equal(male.Frames.Select(frame => (frame.SourceRecord, frame.Mirrored)), state.Frames.Select(frame => (frame.SourceRecord, frame.Mirrored)));
            }
        }

        DungeonActorSpriteMedia rogue = Assert.Single(Arena2DungeonMediaPublication.Create(CreateActorRequest(136, 480, actorRecordCount: 25)).Actors);
        DungeonActorSpriteStateLayout rogueFemaleIdle = Assert.IsType<DungeonActorSpriteMedia>(rogue.Female).States.Single(state => state.State == DungeonActorSpriteState.Idle);
        Assert.Equal(ordinaryIdle.Select(record => (int)record), rogueFemaleIdle.Frames.Select(frame => frame.SourceRecord));
    }

    [CorpusFact("PAL.PAL", "TEXTURE.002", "TEXTURE.380", "TEXTURE.483", "TEXTURE.484")]
    public void FemaleThiefIdlePublishesTheOriginalRecordElevenPixels()
    {
        Arena2DungeonMediaPublication publication = Arena2DungeonMediaPublication.Create(CreateCorpusActorRequest(137));
        DungeonActorSpriteMedia female = Assert.IsType<DungeonActorSpriteMedia>(Assert.Single(publication.Actors).Female);
        DeterministicPngImage atlas = DeterministicPngReader.ReadRgba8(female.Artifact.Bytes.ToArray(), female.SpriteResourceId);
        DungeonMediaFrameLayout frontQuarter = female.States.Single(state => state.State == DungeonActorSpriteState.Idle)
            .Frames.Single(frame => frame.Orientation == 1 && frame.SourceFrame == 0);

        TextureArchive archive = TextureArchive.Parse(File.ReadAllBytes(TestData.Corpus("TEXTURE.483")), "TEXTURE.483");
        IndexedTextureFrame source = archive.DecodeFrame(11, 0);
        Assert.Equal((64, 109), (frontQuarter.AtlasFrame.Width, frontQuarter.AtlasFrame.Height));
        Assert.Equal(
            source.ToRgba(PaletteDecoder.Decode(File.ReadAllBytes(TestData.Corpus("PAL.PAL")), "PAL.PAL"), PaletteAlphaMode.IndexZeroTransparent),
            Crop(atlas, frontQuarter.AtlasFrame));
    }

    [CorpusTheory("PAL.PAL", "TEXTURE.002", "TEXTURE.273", "TEXTURE.278", "TEXTURE.306")]
    [InlineData(18)]
    [InlineData(23)]
    public void SpectralMobilesPublishTranslucentGreyShadesWithOpaqueEyes(byte mobileId)
    {
        Arena2Palette palette = PaletteDecoder.Decode(File.ReadAllBytes(TestData.Corpus("PAL.PAL")), "PAL.PAL");
        Rgb24 eye = palette.Colors.Span[247];
        DungeonActorSpriteMedia actor = Assert.Single(Arena2DungeonMediaPublication.Create(CreateCorpusActorRequest(mobileId)).Actors);
        DeterministicPngImage atlas = DeterministicPngReader.ReadRgba8(actor.Artifact.Bytes.ToArray(), actor.SpriteResourceId);
        bool[] spectralMirroring = [false, false, true, false, false, true, false, true];
        foreach (DungeonActorSpriteState state in new[] { DungeonActorSpriteState.Move, DungeonActorSpriteState.PrimaryAttack })
        {
            DungeonActorSpriteStateLayout layout = actor.States.Single(candidate => candidate.State == state);
            Assert.Equal(spectralMirroring, layout.Frames.Where(frame => frame.SourceFrame == 0).OrderBy(frame => frame.Orientation).Select(frame => frame.Mirrored));
            HashSet<(byte, byte, byte, byte)> colors = [];
            foreach (DungeonMediaFrameLayout frame in layout.Frames)
            {
                byte[] pixels = Crop(atlas, frame.AtlasFrame);
                for (int offset = 0; offset < pixels.Length; offset += 4)
                {
                    colors.Add((pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]));
                }
            }

            Assert.Contains(colors, color => color.Item4 == 0);
            Assert.All(colors.Where(color => color.Item4 != 0), color =>
            {
                Assert.NotEqual((255, 0, 255), (color.Item1, color.Item2, color.Item3));
                Assert.True(color.Item4 is 180 or 255, $"unexpected spectral alpha {color.Item4}");
            });
            Assert.True(colors.Count(color => color.Item4 == 180 && color.Item1 == color.Item2) >= 3, "the body keeps several grey shades");
            Assert.Contains((eye.Red, eye.Green, eye.Blue, (byte)255), colors);
        }
    }

    [Fact]
    public void PublishesTheDonorRangedGroupOnlyForAMobileThatDeclaresIt()
    {
        DungeonActorSpriteMedia archer = Assert.Single(Arena2DungeonMediaPublication.Create(CreateActorRequest(141, 482, actorRecordCount: 25)).Actors);
        AssertState(archer.States.Single(state => state.State == DungeonActorSpriteState.RangedAttack1), DungeonActorSpriteState.RangedAttack1, 10F, false);
        // The donor's RangedAttack2 group names no adopted mobile, so no source declares it
        // and no ranged state beyond the humanoid bow group is published.
        Assert.DoesNotContain(archer.States, state => state.State == DungeonActorSpriteState.RangedAttack2);
        Assert.Equal(new sbyte[] { 3, 2, 0, 0, 0, -1, 1, 1, 2, 3 }, archer.SourceAttackSequence.RangedFrames);

        DungeonActorSpriteMedia orc = Assert.Single(Arena2DungeonMediaPublication.Create(CreateActorRequest(7, 262)).Actors);
        Assert.DoesNotContain(orc.States, state => state.State is DungeonActorSpriteState.RangedAttack1 or DungeonActorSpriteState.RangedAttack2);
        Assert.Null(orc.SourceAttackSequence.RangedFrames);
    }

    [Fact]
    public void FailsARangedMobileWhoseArchiveLacksTheRangedSourceRecords()
    {
        // Unlike the optional idle groups, a declared ranged group is a combat fact: an
        // archive that cannot play it must fail the publication instead of silently
        // regenerating a ranged mobile with no ranged animation.
        Assert.Throws<InvalidOperationException>(() => Arena2DungeonMediaPublication.Create(CreateActorRequest(141, 482)));
    }

    [Fact]
    public void ActorAndCorpseAtlasesRetainFullVariableSourceCanvasesWithAsymmetricTransparentMargins()
    {
        Arena2DungeonMediaSource[] sources = CreateSources();
        Replace(sources, "TEXTURE.255", CreateAsymmetricTransparentTextureArchive(20));
        Replace(sources, "TEXTURE.401", CreateAsymmetricTransparentTextureArchive(2));

        DungeonActorSpriteMedia actor = Assert.Single(Arena2DungeonMediaPublication.Create(
            Arena2DungeonMediaRequest.Create(CreateDungeon(), new Arena2DungeonMediaSourceSet(sources))).Actors);

        DungeonMediaFrameLayout[] actorFrames = actor.States.SelectMany(state => state.Frames).ToArray();
        Assert.Contains(actorFrames, frame => frame.AtlasFrame.SourceWidth == 3);
        Assert.Contains(actorFrames, frame => frame.AtlasFrame.SourceWidth == 4);
        Assert.All(actorFrames, frame =>
        {
            Assert.Equal(frame.AtlasFrame.SourceWidth, frame.AtlasFrame.Width);
            Assert.Equal(frame.AtlasFrame.SourceHeight, frame.AtlasFrame.Height);
        });
        DungeonMediaFrameLayout narrow = actorFrames.First(frame => frame.AtlasFrame.SourceWidth == 3);
        DungeonMediaFrameLayout wide = actorFrames.First(frame => frame.AtlasFrame.SourceWidth == 4);
        Assert.Equal(narrow.SourceWorldSize.X / narrow.AtlasFrame.SourceWidth, wide.SourceWorldSize.X / wide.AtlasFrame.SourceWidth);
        Assert.NotNull(actor.Corpse);
        Assert.Equal(actor.Corpse!.Frame.AtlasFrame.SourceWidth, actor.Corpse.Frame.AtlasFrame.Width);
        Assert.Equal(actor.Corpse.Frame.AtlasFrame.SourceHeight, actor.Corpse.Frame.AtlasFrame.Height);
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
    public void ReappliesPerFrameAndPerStateAuthoredOverlayDuringDungeonRegeneration()
    {
        Arena2DungeonMediaPublication baseline = Arena2DungeonMediaPublication.Create(CreateRequest());
        DungeonActorSpriteMedia baselineActor = Assert.Single(baseline.Actors);
        AuthoredMediaOverlay overlay = new(
            baselineActor.SpriteResourceId,
            true,
            FrameRects: [new(0, 0, 0, 1, 1)],
            StateTimings: [new(DungeonActorSpriteState.Move.ToString(), 11F, false)]);

        Arena2DungeonMediaPublication regenerated = Arena2DungeonMediaPublication.Create(CreateRequest() with { AuthoredOverlays = [overlay] });
        DungeonActorSpriteMedia actor = Assert.Single(regenerated.Actors);

        Assert.Equal(1, actor.Descriptor.Frames[0].Width);
        Assert.Equal(1, actor.States.SelectMany(state => state.Frames).Single(frame => frame.AtlasFrameIndex == 0).AtlasFrame.Width);
        DungeonActorSpriteStateLayout move = actor.States.Single(state => state.State == DungeonActorSpriteState.Move);
        Assert.Equal(11F, move.Playback.FramesPerSecond);
        Assert.False(move.Playback.Loops);
        Assert.Equal(baselineActor.States.Single(state => state.State == DungeonActorSpriteState.Move).SourcePlayback, move.SourcePlayback);
    }

    [Fact]
    public void RejectsSourceClosureMalformedArchivesAndAtlasQuotaOverflow()
    {
        Arena2DungeonMediaSource[] sources = CreateSources();
        Arena2DungeonMediaSourceSet extra = new([.. sources, new Arena2DungeonMediaSource("TEXTURE.003", CreateTextureArchive(1))]);
        Assert.Throws<MissingDungeonMediaTexturesException>(() => Arena2DungeonMediaPublication.Create(Arena2DungeonMediaRequest.Create(CreateDungeon(), extra)));

        Arena2DungeonMediaSource[] malformedSources = CreateSources();
        Replace(malformedSources, "TEXTURE.255", [1]);
        Arena2DungeonMediaSourceSet malformed = new(malformedSources);
        Assert.Throws<Arena2FormatException>(() => Arena2DungeonMediaPublication.Create(Arena2DungeonMediaRequest.Create(CreateDungeon(), malformed)));

        Arena2DungeonMediaQuotas tinyAtlas = Arena2DungeonMediaQuotas.Default with { MaximumAtlasDimension = 1 };
        Assert.Throws<InvalidOperationException>(() => Arena2DungeonMediaPublication.Create(new(CreateDungeon(), new(CreateSources()), tinyAtlas)));
    }

    [Fact]
    public void Texture_closure_mismatch_reports_typed_missing_and_unneeded_names()
    {
        // The retry coordinator discovers lazy texture sources from these lists, never from the message.
        Arena2DungeonMediaSource[] sources = CreateSources();
        Arena2DungeonMediaSourceSet extra = new([.. sources, new Arena2DungeonMediaSource("TEXTURE.003", CreateTextureArchive(1))]);
        MissingDungeonMediaTexturesException mismatch = Assert.Throws<MissingDungeonMediaTexturesException>(
            () => Arena2DungeonMediaPublication.Create(Arena2DungeonMediaRequest.Create(CreateDungeon(), extra)));
        Assert.Empty(mismatch.MissingTextureNames);
        Assert.Equal(["TEXTURE.003"], mismatch.UnneededTextureNames);

        // Multiple missing archives arrive as one sorted discovery list for a single retry.
        Arena2DungeonMediaSourceSet shortSources = new(CreateSources()
            .Where(source => source.Label is not "arena2/TEXTURE.002" and not "arena2/TEXTURE.401").ToArray());
        MissingDungeonMediaTexturesException shortMismatch = Assert.Throws<MissingDungeonMediaTexturesException>(
            () => Arena2DungeonMediaPublication.Create(Arena2DungeonMediaRequest.Create(CreateDungeon(), shortSources)));
        Assert.Equal(["TEXTURE.002", "TEXTURE.401"], shortMismatch.MissingTextureNames);
        Assert.Empty(shortMismatch.UnneededTextureNames);
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

    private static Arena2DungeonMediaRequest CreateActorRequest(byte mobileId, ushort archive, int actorRecordCount = 20)
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
        // The exact texture closure requires every archive the selected mobile
        // references, its corpse included, so the fixture derives them from the source
        // links rather than listing them: a link that gains a corpse cannot leave this
        // fixture silently incomplete.
        List<Arena2DungeonMediaSource> sources =
        [
            new Arena2DungeonMediaSource("arena2/PAL.PAL", CreatePalette()),
            new Arena2DungeonMediaSource("arena2/TEXTURE.002", CreateTextureArchive(2)),
            new Arena2DungeonMediaSource($"arena2/TEXTURE.{archive:000}", CreateTextureArchive(actorRecordCount)),
        ];
        if (MobileSourceMetadata.TryGet(new Arena2MobileId(mobileId), out Arena2MobileSource? mobile) && mobile.Corpse is Arena2MobileCorpseSource corpse)
        {
            sources.Add(new Arena2DungeonMediaSource($"arena2/TEXTURE.{corpse.TextureArchive.Value:000}", CreateTextureArchive(20)));
        }

        if (mobile?.FemaleTextureArchive is { } female)
            sources.Add(new Arena2DungeonMediaSource($"arena2/TEXTURE.{female.Value:000}", CreateTextureArchive(actorRecordCount)));
        return Arena2DungeonMediaRequest.Create(document, new Arena2DungeonMediaSourceSet(sources));
    }

    private static Arena2DungeonMediaRequest CreateCorpusActorRequest(byte mobileId)
    {
        Assert.True(MobileSourceMetadata.TryGet(new Arena2MobileId(mobileId), out Arena2MobileSource? mobile));
        ushort?[] archives = [2, mobile.TextureArchive.Value, mobile.Corpse?.TextureArchive.Value, mobile.FemaleTextureArchive?.Value];
        return CreateActorRequest(mobileId, mobile.TextureArchive.Value) with
        {
            Sources = new Arena2DungeonMediaSourceSet(
            [
                new("arena2/PAL.PAL", File.ReadAllBytes(TestData.Corpus("PAL.PAL"))),
                .. archives.OfType<ushort>().Distinct().Select(archive =>
                    new Arena2DungeonMediaSource($"arena2/TEXTURE.{archive:000}", File.ReadAllBytes(TestData.Corpus($"TEXTURE.{archive:000}")))),
            ]),
        };
    }

    private static byte[] Crop(DeterministicPngImage atlas, NormalizedAtlasFrame frame)
    {
        byte[] pixels = new byte[frame.Width * frame.Height * 4];
        for (int row = 0; row < frame.Height; row++)
        {
            Array.Copy(atlas.Rgba, (((frame.Y + row) * atlas.Width) + frame.X) * 4, pixels, row * frame.Width * 4, frame.Width * 4);
        }

        return pixels;
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
            artifactId,
            "fixture.json",
            new ContentDigest(new string('0', 64)),
            1,
            []);
        NormalizedResourceCatalogEntry texture = new("texture/2-0", NormalizedResourceKind.Texture, artifactId, [], []);
        NormalizedResourceCatalogEntry material = new("material/texture-2-0", NormalizedResourceKind.Material, artifactId, ["texture/2-0"], []);
        NormalizedResourceCatalogEntry sprite = new("sprite/texture-2-1", NormalizedResourceKind.Sprite, artifactId, [], []);
        NormalizedResourceCatalogEntry actor = new("actor/mobile-0", NormalizedResourceKind.ActorDefinition, artifactId, [], []);
        NormalizedMesh mesh = new(
            "mesh/fixture",
            artifactId,
            [new NormalizedVector3(0, 0, 0), new NormalizedVector3(1, 0, 0), new NormalizedVector3(0, 0, 1)],
            [new NormalizedVector3(0, 1, 0), new NormalizedVector3(0, 1, 0), new NormalizedVector3(0, 1, 0)],
            [new NormalizedVector2(0, 0), new NormalizedVector2(1, 0), new NormalizedVector2(0, 1)],
            [new NormalizedTriangle(0, 1, 2)],
            [new NormalizedMaterialGroup("material/texture-2-0", 0, 1, true)]);
        NormalizedWorld world = new(
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
            new ImportProvenance(
                "daggerfall-import/test",
                "test-revision",
                [new LogicalSourceRecord("arena2/MAPS.BSA", new ContentDigest(new string('0', 64)), 1)]),
            [artifact],
            new NormalizedCoordinateConvention(NormalizedHandedness.Right, NormalizedVerticalAxis.PositiveY, 1F),
            new NormalizedBounds(new NormalizedVector3(0, 0, 0), new NormalizedVector3(1, 1, 1)),
            [mesh],
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


    private static byte[] CreateAsymmetricTransparentTextureArchive(int recordCount)
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
            short width = record % 2 == 0 ? (short)4 : (short)3;
            const short height = 2;
            BitConverter.GetBytes(offset).CopyTo(bytes, headerBytes + (record * tableEntryBytes) + 2);
            BitConverter.GetBytes(width).CopyTo(bytes, offset + 4);
            BitConverter.GetBytes(height).CopyTo(bytes, offset + 6);
            BitConverter.GetBytes((uint)recordBytes).CopyTo(bytes, offset + 14);
            BitConverter.GetBytes((ushort)1).CopyTo(bytes, offset + 20);
            int x = record % width;
            int y = record % height;
            bytes[offset + recordBytes + (y * 256) + x] = checked((byte)(record + 1));
        }

        return bytes;
    }
}
