using System.Text.Json;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class SpriteAuthoringTests
{
    [Fact]
    public void ProjectsTypedDungeonAndClassicSpriteFacts()
    {
        Fixture fixture = CreateFixture();
        SpriteInspectionCatalog catalog = SpriteInspectionCatalogBuilder.Create(fixture.Manifest, fixture.Dungeon, fixture.Classic);

        SpriteInspectionEntry actor = catalog.Require("sprite.actor");
        Assert.Equal(SpriteInspectionKind.DungeonActor, actor.Kind);
        Assert.Equal("media/dungeon/actor.png", actor.Closure.RelativePath);
        Assert.Equal(fixture.ActorDigest, actor.Closure.ContentDigest);
        Assert.Equal("arena2/test", Assert.Single(actor.Closure.PublicationSources).SourcePath);
        Assert.Equal(8, actor.Frames.Count);
        Assert.Equal(0, actor.Frames[0].SourceRecord);
        Assert.Equal(0, actor.Frames[0].Orientation);
        SpriteInspectionState state = Assert.Single(actor.States);
        Assert.Equal("Move", state.Name);
        Assert.Equal(6F, state.FramesPerSecond);
        Assert.True(state.IsPreferredRest);
        IReadOnlyList<SpriteSourceSequenceStep> sequence = Assert.Single(actor.Actions).SourceSequence!;
        Assert.Equal(new sbyte[] { 0, -1 }, sequence.Select(step => step.Value));
        Assert.True(sequence[1].IsDamageMarker);

        SpriteInspectionAction billboard = Assert.Single(catalog.Require("sprite.billboard").Actions);
        Assert.Equal(5F, billboard.FramesPerSecond);
        Assert.Equal(12F, billboard.SourceFramesPerSecond);
        Assert.False(billboard.Loops);

        SpriteInspectionEntry weapon = catalog.Require("sprite.weapon");
        Assert.Equal(SpriteInspectionKind.ClassicWeapon, weapon.Kind);
        SpriteInspectionAction action = weapon.Actions.Single(value => value.Name == "Idle");
        Assert.Equal("Idle", action.Name);
        Assert.Equal(new[] { 0 }, action.FrameIndices);
        Assert.Equal(0, action.SourceRecordOrdinal);
        Assert.Equal(0, catalog.Require("sprite.effect").Actions.Single(action => action.Name == "Blood0").SourceRecordOrdinal);
    }

    [Fact]
    public void RejectsStaleUnknownAndGeneratedLayoutOverlayInput()
    {
        Fixture fixture = CreateFixture();
        SpriteInspectionCatalog catalog = SpriteInspectionCatalogBuilder.Create(fixture.Manifest, fixture.Dungeon, fixture.Classic);
        ContentDigest digest = ContentDigest.Compute("publication"u8);

        Assert.Throws<FormatException>(() => SpriteAuthoredOverlayStore.Validate(
            new(SpriteAuthoredOverlayDocument.CurrentSchemaVersion, ContentDigest.Compute("other"u8), []), catalog, digest));
        Assert.Throws<InvalidOperationException>(() => SpriteAuthoredOverlayStore.Validate(
            new(SpriteAuthoredOverlayDocument.CurrentSchemaVersion, digest, [new("sprite.unknown", DisplayName: "Unknown")]), catalog, digest));
        Assert.Throws<FormatException>(() => SpriteAuthoredOverlayStore.Read("""
            { "schemaVersion": 1, "authoringBasisDigest": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "overlays": [], "frames": [] }
            """u8));
        Assert.Throws<FormatException>(() => SpriteAuthoredOverlayStore.Validate(
            new(SpriteAuthoredOverlayDocument.CurrentSchemaVersion, digest, [new("sprite.actor", Sequence: [8])]), catalog, digest));
        Assert.Throws<FormatException>(() => SpriteAuthoredOverlayStore.Validate(
            new(SpriteAuthoredOverlayDocument.CurrentSchemaVersion, digest, [new("sprite.weapon", FramesPerSecond: 7F)]), catalog, digest));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpriteAuthoredOverlayStore.Validate(
            new(SpriteAuthoredOverlayDocument.CurrentSchemaVersion, digest, [new("sprite.actor", DisplaySize: new(float.NaN, 1F))]), catalog, digest));
    }

    [Fact]
    public void RejectsMalformedPersistedSidecarsBeforeSpriteProjection()
    {
        Fixture fixture = CreateFixture();
        DungeonActorMediaManifest actor = Assert.Single(fixture.Dungeon.Actors);
        DungeonMediaManifestSidecar duplicateStates = fixture.Dungeon with
        {
            Actors = [actor with { States = [.. actor.States, actor.States[0]] }],
        };
        Assert.Throws<FormatException>(() => SpriteInspectionCatalogBuilder.Create(fixture.Manifest, duplicateStates, fixture.Classic));

        DungeonMediaManifestSidecar nullResource = fixture.Dungeon with
        {
            Media = new NormalizedMediaManifest([null!]),
        };
        Assert.Throws<FormatException>(() => SpriteInspectionCatalogBuilder.Create(fixture.Manifest, nullResource, fixture.Classic));

        ClassicMediaManifestSidecar invalidTiming = fixture.Classic with
        {
            Effects = fixture.Classic.Effects.Select(effect => effect with { Timing = new ClassicSpriteTiming(5F, false) }).ToArray(),
        };
        Assert.Throws<FormatException>(() => SpriteInspectionCatalogBuilder.Create(fixture.Manifest, fixture.Dungeon, invalidTiming));

        ClassicMediaManifestSidecar reorderedWeaponRanges = fixture.Classic with
        {
            WeaponActions = fixture.Classic.WeaponActions.Reverse().ToArray(),
        };
        Assert.Throws<FormatException>(() => SpriteInspectionCatalogBuilder.Create(fixture.Manifest, fixture.Dungeon, reorderedWeaponRanges));

        ClassicMediaManifestSidecar arbitraryEffectRecord = fixture.Classic with
        {
            Effects = fixture.Classic.Effects.Select(effect => effect.Effect == ClassicEffect.Blood0
                ? effect with { SourceRecordOrdinal = 99 }
                : effect).ToArray(),
        };
        Assert.Throws<FormatException>(() => SpriteInspectionCatalogBuilder.Create(fixture.Manifest, fixture.Dungeon, arbitraryEffectRecord));
    }

    [Fact]
    public void KeepsAuthoringBasisStableAcrossRepeatedOverlayRegeneration()
    {
        Fixture fixture = CreateFixture();
        SpriteInspectionCatalog firstCatalog = SpriteInspectionCatalogBuilder.Create(fixture.Manifest, fixture.Dungeon, fixture.Classic);
        ContentDigest firstBasis = SpriteAuthoringBasis.Compute(fixture.Manifest, firstCatalog);
        SpriteAuthoredOverlayDocument overlay = new(SpriteAuthoredOverlayDocument.CurrentSchemaVersion, firstBasis, [new("sprite.actor", DisplayName: "Authored rat")]);

        NormalizedMediaDescriptor actor = fixture.Dungeon.Media.Resources.Single(resource => resource.Id == "sprite.actor");
        DungeonMediaManifestSidecar regenerated = fixture.Dungeon with
        {
            Media = new(fixture.Dungeon.Media.Resources.Select(resource => resource.Id == actor.Id
                ? resource with { DisplayName = "Authored rat" }
                : resource).ToArray()),
        };
        SpriteInspectionCatalog secondCatalog = SpriteInspectionCatalogBuilder.Create(fixture.Manifest, regenerated, fixture.Classic);
        ContentDigest secondBasis = SpriteAuthoringBasis.Compute(fixture.Manifest, secondCatalog);
        Assert.Equal(firstBasis, secondBasis);
        SpriteAuthoredOverlayStore.Validate(overlay, secondCatalog, secondBasis);
        SpriteAuthoredOverlayStore.Validate(overlay, secondCatalog, secondBasis);

        CanonicalImportManifest changedSources = fixture.Manifest with
        {
            Sources = [new("arena2/test", ContentDigest.Compute("changed"u8), 7)],
        };
        ContentDigest changedBasis = SpriteAuthoringBasis.Compute(changedSources, secondCatalog);
        Assert.NotEqual(firstBasis, changedBasis);
        Assert.Throws<FormatException>(() => SpriteAuthoredOverlayStore.Validate(overlay, secondCatalog, changedBasis));
    }

    [Fact]
    public void IncludesClassicSourceRecordsAndWeaponRangesInAuthoringBasis()
    {
        Fixture fixture = CreateFixture();
        SpriteInspectionCatalog catalog = SpriteInspectionCatalogBuilder.Create(fixture.Manifest, fixture.Dungeon, fixture.Classic);
        ContentDigest baseline = SpriteAuthoringBasis.Compute(fixture.Manifest, catalog);
        SpriteInspectionEntry weapon = catalog.Require("sprite.weapon");
        SpriteInspectionEntry effect = catalog.Require("sprite.effect");
        SpriteInspectionCatalog changedWeaponRange = catalog with
        {
            Entries = catalog.Entries.Select(entry => entry.Id == weapon.Id
                ? entry with { Actions = entry.Actions.Select(action => action.Name == "Idle" ? action with { FrameIndices = [1] } : action).ToArray() }
                : entry).ToArray(),
        };
        SpriteInspectionCatalog changedEffectSource = catalog with
        {
            Entries = catalog.Entries.Select(entry => entry.Id == effect.Id
                ? entry with { Actions = entry.Actions.Select(action => action with { SourceRecordOrdinal = 99 }).ToArray() }
                : entry).ToArray(),
        };

        Assert.NotEqual(baseline, SpriteAuthoringBasis.Compute(fixture.Manifest, changedWeaponRange));
        Assert.NotEqual(baseline, SpriteAuthoringBasis.Compute(fixture.Manifest, changedEffectSource));
    }

    [Fact]
    public void WritesDeterministicRecoverableOverlayThroughASafeRelativePath()
    {
        Fixture fixture = CreateFixture();
        SpriteInspectionCatalog catalog = SpriteInspectionCatalogBuilder.Create(fixture.Manifest, fixture.Dungeon, fixture.Classic);
        ContentDigest digest = ContentDigest.Compute("publication"u8);
        SpriteAuthoredOverlayDocument document = new(
            SpriteAuthoredOverlayDocument.CurrentSchemaVersion,
            digest,
            [new("sprite.actor", DisplayName: "Rat", Pivot: new(0.5F, 0F), FramesPerSecond: 5F, Loop: true, Sequence: [0, 1])]);
        string generatedRoot = Path.Combine(Path.GetTempPath(), $"sprite-generated-{Guid.NewGuid():N}");
        string authoringRoot = Path.Combine(Path.GetTempPath(), $"sprite-authoring-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(generatedRoot);
            SpriteAuthoredOverlayStore.Write(generatedRoot, authoringRoot, "sprites/sprite-overlays.json", document, catalog, digest);
            string target = Path.Combine(authoringRoot, "sprites", "sprite-overlays.json");
            SpriteAuthoredOverlayDocument read = SpriteAuthoredOverlayStore.Read(File.ReadAllBytes(target));
            Assert.Equal(document.SchemaVersion, read.SchemaVersion);
            Assert.Equal(document.AuthoringBasisDigest, read.AuthoringBasisDigest);
            SpriteAuthoredOverlay expected = Assert.Single(document.Overlays);
            SpriteAuthoredOverlay actual = Assert.Single(read.Overlays);
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.Pivot, actual.Pivot);
            Assert.Equal(expected.FramesPerSecond, actual.FramesPerSecond);
            Assert.Equal(expected.Loop, actual.Loop);
            Assert.Equal(expected.Sequence, actual.Sequence);

            Assert.False(File.Exists(Path.Combine(generatedRoot, "sprites", "sprite-overlays.json")));
            Assert.Throws<ArgumentException>(() => SpriteAuthoredOverlayStore.Write(generatedRoot, generatedRoot, "sprites/sprite-overlays.json", document, catalog, digest));
            Assert.Throws<ArgumentException>(() => SpriteAuthoredOverlayStore.ValidateRootSeparation(generatedRoot, Path.Combine(generatedRoot, "authored")));
            Assert.Throws<ArgumentException>(() => SpriteAuthoredOverlayStore.ValidateRootSeparation(Path.Combine(authoringRoot, "generated"), authoringRoot));
            SpriteAuthoredOverlayStore.Write(generatedRoot, authoringRoot, "sprites/sprite-overlays.json", document with { Overlays = [new("sprite.actor", DisplayName: "Rat two")] }, catalog, digest);
            Assert.True(File.Exists(target + ".bak"));
            Assert.Throws<IOException>(() => SpriteAuthoredOverlayStore.Write(generatedRoot, authoringRoot, "sprites/sprite-overlays.json", document with { Overlays = [new("sprite.actor", DisplayName: "Rat three")] }, catalog, digest));
            Assert.Throws<ArgumentException>(() => SpriteAuthoredOverlayStore.ResolveRelativePath(authoringRoot, "../import-manifest.json"));
            Assert.Throws<ArgumentException>(() => SpriteAuthoredOverlayStore.ValidateOverlayRelativePath("sprites/overlay.png"));
            Assert.Throws<ArgumentException>(() => SpriteAuthoredOverlayStore.ValidateOverlayRelativePath("media/dungeon/manifest.json"));
            Assert.True(SpriteAuthoredOverlayStore.Discard(generatedRoot, authoringRoot, "sprites/sprite-overlays.json"));
            Assert.True(File.Exists(target + ".discarded"));
        }
        finally
        {
            if (Directory.Exists(generatedRoot))
            {
                Directory.Delete(generatedRoot, recursive: true);
            }

            if (Directory.Exists(authoringRoot))
            {
                Directory.Delete(authoringRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void RejectsOversizedSerializedWritesAndDiscardsWithoutAPublicationRead()
    {
        Fixture fixture = CreateFixture();
        SpriteInspectionCatalog catalog = SpriteInspectionCatalogBuilder.Create(fixture.Manifest, fixture.Dungeon, fixture.Classic);
        ContentDigest digest = ContentDigest.Compute("publication"u8);
        string generatedRoot = Path.Combine(Path.GetTempPath(), $"sprite-generated-{Guid.NewGuid():N}");
        string authoringRoot = Path.Combine(Path.GetTempPath(), $"sprite-authoring-{Guid.NewGuid():N}");
        try
        {
            SpriteAuthoredOverlayDocument oversized = new(
                SpriteAuthoredOverlayDocument.CurrentSchemaVersion,
                digest,
                [new("sprite.actor", DisplayName: new string('x', 1024 * 1024))]);
            Assert.Throws<FormatException>(() => SpriteAuthoredOverlayStore.Write(generatedRoot, authoringRoot, "sprites/oversized.json", oversized, catalog, digest));

            string discard = Path.Combine(authoringRoot, "sprites", "discard.json");
            Directory.CreateDirectory(Path.GetDirectoryName(discard)!);
            File.WriteAllText(discard, "not a publication input");
            Assert.True(SpriteAuthoredOverlayStore.Discard(generatedRoot, authoringRoot, "sprites/discard.json"));
            Assert.True(File.Exists(discard + ".discarded"));
        }
        finally
        {
            if (Directory.Exists(authoringRoot))
            {
                Directory.Delete(authoringRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ReadsOnlySidecarsThatMatchThePublicationClosure()
    {
        Fixture fixture = CreateFixture();
        string root = Path.Combine(Path.GetTempPath(), $"sprite-publication-{Guid.NewGuid():N}");
        try
        {
            byte[] dungeon = Serialize(fixture.Dungeon);
            byte[] classic = Serialize(fixture.Classic);
            CanonicalImportManifest manifest = fixture.Manifest with
            {
                Artifacts =
                [
                    Artifact("media/dungeon/actor.png", fixture.ActorDigest, 3),
                    Artifact("media/dungeon/billboard.png", fixture.BillboardDigest, 3),
                    Artifact("media/classic/weapon.png", fixture.WeaponDigest, 3),
                    Artifact("media/classic/effect.png", fixture.EffectDigest, 3),
                    Artifact("media/classic/font.bin", fixture.FontDigest, 3),
                    Artifact(Arena2MediaBundlePublication.DungeonMediaManifestRelativePath, ContentDigest.Compute(dungeon), dungeon.Length),
                    Artifact(Arena2MediaBundlePublication.ClassicMediaManifestRelativePath, ContentDigest.Compute(classic), classic.Length),
                ],
            };
            manifest.Validate();
            Write(root, "media/dungeon/actor.png", "act"u8.ToArray());
            Write(root, "media/dungeon/billboard.png", "bb!"u8.ToArray());
            Write(root, "media/classic/weapon.png", "wep"u8.ToArray());
            Write(root, "media/classic/effect.png", "fx!"u8.ToArray());
            Write(root, "media/classic/font.bin", "fnt"u8.ToArray());
            Write(root, Arena2MediaBundlePublication.DungeonMediaManifestRelativePath, dungeon);
            Write(root, Arena2MediaBundlePublication.ClassicMediaManifestRelativePath, classic);
            Write(root, ImportPublicationManifestSerializer.ManifestRelativePath, ImportPublicationManifestSerializer.Serialize(manifest));

            SpritePublicationSnapshot snapshot = SpritePublicationReader.Read(root);
            Assert.Equal(4, snapshot.Catalog.Entries.Count);
            string actorPath = Path.Combine(root, "media", "dungeon", "actor.png");
            File.Delete(actorPath);
            Assert.Throws<FormatException>(() => SpritePublicationReader.Read(root));
            File.WriteAllBytes(actorPath, "bad"u8.ToArray());
            Assert.Throws<FormatException>(() => SpritePublicationReader.Read(root));
            File.WriteAllBytes(actorPath, "act"u8.ToArray());
            File.AppendAllText(Path.Combine(root, "media", "dungeon", "manifest.json"), " ");
            Assert.Throws<FormatException>(() => SpritePublicationReader.Read(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static Fixture CreateFixture()
    {
        ContentDigest actorDigest = ContentDigest.Compute("act"u8);
        ContentDigest billboardDigest = ContentDigest.Compute("bb!"u8);
        ContentDigest weaponDigest = ContentDigest.Compute("wep"u8);
        ContentDigest effectDigest = ContentDigest.Compute("fx!"u8);
        ContentDigest fontDigest = ContentDigest.Compute("fnt"u8);
        NormalizedMediaDescriptor actor = Descriptor("sprite.actor", NormalizedMediaKind.EnemySprite, "media/dungeon/actor.png", actorDigest, 8);
        NormalizedMediaDescriptor billboard = Descriptor("sprite.billboard", NormalizedMediaKind.Billboard, "media/dungeon/billboard.png", billboardDigest, 1) with { Pivot = new(0.5F, 0.5F), FramesPerSecond = 5F, Loop = false };
        NormalizedMediaDescriptor weapon = Descriptor("sprite.weapon", NormalizedMediaKind.WeaponSprite, "media/classic/weapon.png", weaponDigest, 7);
        NormalizedMediaDescriptor effect = Descriptor("sprite.effect", NormalizedMediaKind.EffectSprite, "media/classic/effect.png", effectDigest, 1) with { FramesPerSecond = 10F, Loop = false };
        NormalizedMediaDescriptor font = Descriptor("font.classic", NormalizedMediaKind.Font, "media/classic/font.bin", fontDigest, 1);
        DungeonMediaFrameLayout[] layouts = Enumerable.Range(0, 8)
            .Select(index => new DungeonMediaFrameLayout(index, 0, index, index, false, actor.Frames[index], new(1F, 1F)))
            .ToArray();
        DungeonActorMediaManifest actorManifest = new(
            "actor/rat", 0, "Rat", DungeonActorSpriteState.Move,
            new([0, -1], []), "sprite/rat", new(0.5F, 0F), new(1F, 1F), new(1F, 1F),
            [new(DungeonActorSpriteState.Move, new(6F, true), new(6F, true), 0, 1, layouts)], null, actor.Id);
        DungeonMediaManifestSidecar dungeon = new(
            1,
            new([actor, billboard]),
            [],
            [new("sprite/fixture", 1, 1, new(0.5F, 0.5F), new(1F, 1F), new(12F, true), new(5F, false), [new(0, 0, 0, 0, false, billboard.Frames[0], new(1F, 1F))], billboard.Id)],
            [actorManifest]);
        ClassicMediaManifestSidecar classic = new(
            1,
            new([weapon, effect, font]),
            Enum.GetValues<ClassicDaggerWeaponAction>().Select((action, index) => new ClassicWeaponActionManifest(action, index, index, 1, ClassicWeaponScreenAlignment.Right, 0F, new(10F, true), 0, 0)).ToArray(),
            Enum.GetValues<ClassicEffect>().Select((value, index) => new ClassicEffectManifest(value, effect.Id, index, new(10F, false))).ToArray(),
            [], [], [], new(font.Id, 1, 1, Enumerable.Range(0, 240).Select(index => new ClassicFontGlyphMetric(index, index, 0, 1, checked((ushort)index))).ToArray()), []);
        CanonicalImportManifest manifest = new(
            1,
            "daggerfall-import",
            1,
            [new("arena2/test", ContentDigest.Compute("source"u8), 6)],
            [Artifact(actor.RelativePath, actorDigest, 3), Artifact(billboard.RelativePath, billboardDigest, 3), Artifact(weapon.RelativePath, weaponDigest, 3), Artifact(effect.RelativePath, effectDigest, 3), Artifact(font.RelativePath, fontDigest, 3)]);
        manifest.Validate();
        return new(manifest, dungeon, classic, actorDigest, billboardDigest, weaponDigest, effectDigest, fontDigest);
    }

    private static NormalizedMediaDescriptor Descriptor(string id, NormalizedMediaKind kind, string path, ContentDigest digest, int frameCount) => new(
        id, kind, path, digest, 3, "image/png", 2, 1, frameCount * 2, 1,
        Enumerable.Range(0, frameCount).Select(index => new NormalizedAtlasFrame($"frame.{index}", index, index * 2, 0, 2, 1, 2, 1, false)).ToArray(),
        null, new(0.5F, 0F), new(1F, 1F), 6F, true, null);

    private static ImportPublicationManifestArtifact Artifact(string path, ContentDigest digest, long length) => new(path, digest, length, []);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static byte[] Serialize<T>(T value) => [.. JsonSerializer.SerializeToUtf8Bytes(value, Json), (byte)'\n'];

    private static void Write(string root, string relativePath, byte[] bytes)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private sealed record Fixture(CanonicalImportManifest Manifest, DungeonMediaManifestSidecar Dungeon, ClassicMediaManifestSidecar Classic, ContentDigest ActorDigest, ContentDigest BillboardDigest, ContentDigest WeaponDigest, ContentDigest EffectDigest, ContentDigest FontDigest);
}
