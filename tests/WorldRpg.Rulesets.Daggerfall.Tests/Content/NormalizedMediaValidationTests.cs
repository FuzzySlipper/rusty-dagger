using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Validation of the normalized actor, classic audio and sidecar media the importer publishes.</summary>
public sealed class NormalizedMediaValidationTests
{
    [Fact]
    public void Normalized_media_parses_an_imported_preferred_rest_state_and_rejects_unknown_presentation_states()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        DaggerfallProductMedia withImportedPreference = Read(MutateDungeonMedia(root, media => media["actors"]!.AsArray()
            .Single(value => value!["mobileId"]!.GetValue<int>() == 15)!["preferredRestState"] = "idle"), definitions);
        Assert.Equal("idle", withImportedPreference.MobileSprites[15].PreferredRestState);

        Assert.Throws<DaggerfallContentException>(() => Read(
            MutateDungeonMedia(root, media => media["actors"]!.AsArray().Single(value => value!["mobileId"]!.GetValue<int>() == 15)!["preferredRestState"] = "missingState"),
            definitions));

        // An authored actor's rest state is refused where its location places it and the media does not publish the state.
        DaggerfallDefinitions unknownAuthoredState = DaggerfallBaseContent.Read(TestPayload.Replaced(("\"preferredRestState\": \"ratIdle\"", "\"preferredRestState\": \"missingState\"")));
        ProductContent content = FullContent(root);
        DaggerfallData data = TestSessions.Data(content);
        DaggerfallWorldMedia world = DaggerfallWorldMedia.Read(data);
        DaggerfallProductMedia product = DaggerfallSiteContent.ReadProductMedia(data, world, unknownAuthoredState);
        DaggerfallLocationAssembly assembly = new(unknownAuthoredState, new DaggerfallWorldBlocks(data), () => product,
            () => DaggerfallLocationAssembly.ReadMeshIndex(data, world));
        DaggerfallContentException refused = Assert.Throws<DaggerfallContentException>(() => assembly.Assemble(DaggerfallWorldProfileIds.Dungeon(PrivateersHold)));
        Assert.Contains(refused.Diagnostics, message => message.Contains("'rat' preferredRestState 'missingState'", StringComparison.Ordinal));
    }

    [Fact]
    public void Normalized_media_rejects_invalid_sector_loop_and_per_sector_attack_index()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        Assert.Throws<DaggerfallContentException>(() => Read(MutateDungeonMedia(root, media => FirstActorState(media, "idle")["frames"]!.AsArray()[0]!["orientation"] = 8), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateDungeonMedia(root, media => FirstActorState(media, "idle")["playback"]!["loops"] = "true"), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateDungeonMedia(root, media =>
        {
            JsonArray frames = FirstActorState(media, "primaryAttack")["frames"]!.AsArray();
            JsonNode frame = frames.Last(value => value!["orientation"]!.GetValue<int>() == 7)!;
            frames.Remove(frame);
        }), definitions));
    }

    [Fact]
    public void Generated_media_declares_ranged_states_only_for_mobiles_the_donor_makes_ranged()
    {
        string root = TestData.RepositoryRoot;
        JsonObject media = JsonNode.Parse(TestContentFiles.ReadCopy(Path.Combine(root, "content", DaggerfallWorldMedia.Root, DaggerfallWorldMedia.DungeonSidecarRelativePath)))!.AsObject();
        Dictionary<int, JsonObject> actors = media["actors"]!.AsArray()
            .Select(value => value!.AsObject())
            .ToDictionary(actor => actor["mobileId"]!.GetValue<int>(), actor => actor);

        foreach (int rangedMobile in new[] { 138, 141 })
        {
            JsonObject actor = actors[rangedMobile];
            JsonObject ranged = actor["states"]!.AsArray().Select(value => value!.AsObject())
                .Single(state => state["state"]!.GetValue<string>() == "rangedAttack1");
            Assert.Equal(10F, ranged["sourcePlayback"]!["framesPerSecond"]!.GetValue<float>());
            Assert.False(ranged["sourcePlayback"]!["loops"]!.GetValue<bool>());
            Assert.Equal(new[] { 3, 2, 0, 0, 0, -1, 1, 1, 2, 3 },
                actor["sourceAttackSequence"]!["rangedFrames"]!.AsArray().Select(value => value!.GetValue<int>()).ToArray());
            Assert.Equal(20, ranged["frames"]!.AsArray().First(frame => frame!["orientation"]!.GetValue<int>() == 0)!["sourceRecord"]!.GetValue<int>());
        }

        foreach (int meleeMobile in new[] { 0, 1, 3, 7, 15 })
        {
            JsonObject actor = actors[meleeMobile];
            Assert.DoesNotContain(actor["states"]!.AsArray(), value => value!["state"]!.GetValue<string>() == "rangedAttack1");
            // JSON null is published explicitly: the mobile declares no ranged sequence.
            Assert.True(actor["sourceAttackSequence"]!.AsObject().TryGetPropertyValue("rangedFrames", out JsonNode? rangedFrames) && rangedFrames is null);
        }
    }

    [Fact]
    public void Normalized_classic_audio_requires_a_contiguous_canonical_hit_cue_family()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media =>
        {
            JsonArray audio = media["audio"]!.AsArray();
            audio.Single(value => value!["clip"]!.GetValue<string>() == "hit2")!.AsObject()["clip"] = "hit3";
        }), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media =>
        {
            JsonArray audio = media["audio"]!.AsArray();
            audio.Remove(audio.Single(value => value!["clip"]!.GetValue<string>() == "hit2"));
        }), definitions));
    }

    [Fact]
    public void Normalized_classic_sidecar_rejects_noncanonical_source_records_ranges_frames_effects_and_descriptors()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media => media["weaponMedia"]!.AsArray()[0]!["actions"]!.AsArray()[0]!["frameStart"] = -1), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media => media["weaponMedia"]!.AsArray()[0]!["actions"]!.AsArray()[1]!["frameStart"] = -1), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media => media["weaponMedia"]!.AsArray()[0]!["actions"]!.AsArray()[0]!["frameCount"] = int.MaxValue), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media => WeaponResource(media)["frames"]!.AsArray()[0]!["frameIndex"] = 4), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media =>
        {
            JsonObject frame = WeaponResource(media)["frames"]!.AsArray()[0]!.AsObject();
            frame["x"] = 1;
            frame["width"] = int.MaxValue;
        }), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media => media["effects"]!.AsArray()[0]!["sourceRecordOrdinal"] = 3), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media => media["effects"]!.AsArray()[0]!["timing"]!["framesPerSecond"] = 12), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media => EffectResource(media, "effect.blood.0")["frames"]!.AsArray()[0]!["frameIndex"] = 1), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media => WeaponResource(media)["byteLength"] = 1), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media => WeaponResource(media)["mimeType"] = ""), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media => WeaponResource(media)["sourceWidth"] = -1), definitions));
        Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media => WeaponResource(media)["relativePath"] = "../weapon.png"), definitions));
    }

    [Fact]
    public void Classic_weapon_admission_refuses_a_strike_that_ends_before_its_hit_frame()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        // A melee strike shorter than the melee hit frame: every strike can play a melee swing.
        DaggerfallContentException melee = Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media =>
            WeaponAction(media, "weapon.dagger.steel", "strikeUp")["sequence"] = new JsonArray(26, 27)), definitions));
        Assert.Contains(melee.Diagnostics, message => message.Contains("'weapon.dagger.steel' strike 'strikeUp' plays 2 frames", StringComparison.Ordinal)
            && message.Contains($"melee hit frame {NormalizedClassicWeapon.MeleeWeaponHitFrame}", StringComparison.Ordinal));

        // The bow's loosing strike shorter than the bow hit frame: the arrow would never leave.
        DaggerfallContentException bow = Assert.Throws<DaggerfallContentException>(() => Read(MutateClassicMedia(root, media =>
            WeaponAction(media, "weapon.bow", "strikeDown")["sequence"] = new JsonArray(0, 1, 2, 3, 4)), definitions));
        Assert.Contains(bow.Diagnostics, message => message.Contains("Bow 'iron-short-bow' selects classic weapon 'weapon.bow', whose 'strikeDown' plays 5 frames", StringComparison.Ordinal)
            && message.Contains($"bow hit frame {NormalizedClassicWeapon.BowWeaponHitFrame}", StringComparison.Ordinal));

        // The bow's up strike is shorter than the bow hit frame, as published, and is admitted: a bow
        // never plays it.
        Assert.Equal(4, WeaponAction(ClassicMedia(root), "weapon.bow", "strikeUp")["frameCount"]!.GetValue<int>());
        _ = Read(MutateClassicMedia(root, _ => { }), definitions);
    }

    private static JsonObject WeaponAction(JsonObject media, string resource, string action) => media["weaponMedia"]!.AsArray()
        .Select(value => value!.AsObject())
        .Single(weapon => weapon["resourceId"]!.GetValue<string>() == resource)["actions"]!.AsArray()
        .Select(value => value!.AsObject())
        .Single(value => value["action"]!.GetValue<string>() == action);

    private static JsonObject ClassicMedia(string repositoryRoot) => JsonNode.Parse(TestContentFiles.ReadCopy(
        Path.Combine(repositoryRoot, "content", DaggerfallWorldMedia.Root, "media/classic/manifest.json")))!.AsObject();

    private static JsonObject FirstActorState(JsonObject media, string name) => media["actors"]!.AsArray()
        .Select(value => value!.AsObject())
        .SelectMany(actor => actor["states"]!.AsArray())
        .Select(value => value!.AsObject())
        .First(state => state["state"]!.GetValue<string>() == name);

    private static JsonObject WeaponResource(JsonObject media) => media["media"]!["resources"]!.AsArray()
        .Select(value => value!.AsObject())
        .Single(resource => resource["id"]!.GetValue<string>() == "weapon.dagger.steel");

    private static JsonObject EffectResource(JsonObject media, string id) => media["media"]!["resources"]!.AsArray()
        .Select(value => value!.AsObject())
        .Single(resource => resource["id"]!.GetValue<string>() == id);

    /// <summary>The product media the world media publication in this content presents.</summary>
    internal static DaggerfallProductMedia Read(ProductContent content, DaggerfallDefinitions definitions)
    {
        DaggerfallData data = TestSessions.Data(content);
        using DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(data);
        return DaggerfallSiteContent.ReadProductMedia(data, media, definitions);
    }

    /// <summary>The world media publication with its dungeon sidecar mutated: the reader validates each entry it carries.</summary>
    internal static ProductContent MutateDungeonMedia(string repositoryRoot, Action<JsonObject> mutate) =>
        WithSidecar(repositoryRoot, DaggerfallWorldMedia.DungeonSidecarRelativePath, mutate);

    /// <summary>The world media publication with its classic sidecar mutated.</summary>
    internal static ProductContent MutateClassicMedia(string repositoryRoot, Action<JsonObject> mutate) =>
        WithSidecar(repositoryRoot, DaggerfallWorldMedia.ClassicSidecarRelativePath, mutate);

    /// <summary>The eager product media with one world media sidecar replaced and its manifest digest updated to match.</summary>
    private static ProductContent WithSidecar(string repositoryRoot, string relativePath, Action<JsonObject> mutate)
    {
        string media = DaggerfallWorldMedia.Root;
        string contentRoot = Path.Combine(repositoryRoot, "content");
        string sidecarPath = $"{media}/{relativePath}", manifestPath = $"{media}/import-manifest.json";
        List<ProductContentFile> files = [.. ProductMediaFiles(repositoryRoot)
            .Where(file => Encoding.UTF8.GetString(file.Path.Span) is var path && path != sidecarPath && path != manifestPath)];
        JsonObject sidecar = JsonNode.Parse(TestContentFiles.ReadCopy(Path.Combine(contentRoot, sidecarPath)))!.AsObject();
        mutate(sidecar);
        byte[] sidecarBytes = Encoding.UTF8.GetBytes(sidecar.ToJsonString());
        files.Add(new ProductContentFile(Encoding.UTF8.GetBytes(sidecarPath), sidecarBytes));
        JsonObject manifest = JsonNode.Parse(TestContentFiles.ReadCopy(Path.Combine(contentRoot, manifestPath)))!.AsObject();
        JsonObject artifact = manifest["artifacts"]!.AsArray().Select(value => value!.AsObject())
            .Single(value => value["relativePath"]!.GetValue<string>() == relativePath);
        artifact["contentHash"] = Convert.ToHexString(SHA256.HashData(sidecarBytes)).ToLowerInvariant();
        files.Add(new ProductContentFile(Encoding.UTF8.GetBytes(manifestPath), Encoding.UTF8.GetBytes(manifest.ToJsonString())));
        return new ProductContent(files.ToArray());
    }
}
