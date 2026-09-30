using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
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
        byte[] scenario = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        ProductContent withImportedPreference = MutateDungeonMedia(root, media => media["actors"]!.AsArray()
            .Single(value => value!["mobileId"]!.GetValue<int>() == 15)!["preferredRestState"] = "idle");
        DaggerfallSiteProfile inputs = DaggerfallSiteContent.Read(withImportedPreference, scenario, definitions);
        Assert.Equal("idle", SpriteFor(inputs, "skeletal-warrior").PreferredRestState);

        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(
            MutateDungeonMedia(root, media => media["actors"]!.AsArray().Single(value => value!["mobileId"]!.GetValue<int>() == 15)!["preferredRestState"] = "missingState"),
            scenario,
            definitions));

        DaggerfallDefinitions unknownAuthoredState = DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(TestPayload.CombinedText.Replace("\"preferredRestState\": \"ratIdle\"", "\"preferredRestState\": \"missingState\"", StringComparison.Ordinal)));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(ImportContent(root), scenario, unknownAuthoredState));
    }

    [Fact]
    public void Normalized_media_rejects_invalid_sector_loop_and_per_sector_attack_index()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateDungeonMedia(root, media => FirstActorState(media, "idle")["frames"]!.AsArray()[0]!["orientation"] = 8), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateDungeonMedia(root, media => FirstActorState(media, "idle")["playback"]!["loops"] = "true"), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateDungeonMedia(root, media =>
        {
            JsonArray frames = FirstActorState(media, "primaryAttack")["frames"]!.AsArray();
            JsonNode frame = frames.Last(value => value!["orientation"]!.GetValue<int>() == 7)!;
            frames.Remove(frame);
        }), payload, definitions));
    }

    [Fact]
    public void Generated_media_declares_ranged_states_only_for_mobiles_the_donor_makes_ranged()
    {
        string root = TestData.RepositoryRoot;
        JsonObject media = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/imports/privateers-hold/media/dungeon/manifest.json")))!.AsObject();
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
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media =>
        {
            JsonArray audio = media["audio"]!.AsArray();
            audio.Single(value => value!["clip"]!.GetValue<string>() == "hit2")!.AsObject()["clip"] = "hit3";
        }), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media =>
        {
            JsonArray audio = media["audio"]!.AsArray();
            audio.Remove(audio.Single(value => value!["clip"]!.GetValue<string>() == "hit2"));
        }), payload, definitions));
    }

    [Fact]
    public void Normalized_classic_sidecar_rejects_noncanonical_source_records_ranges_frames_effects_and_descriptors()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media => media["weaponMedia"]!.AsArray()[0]!["actions"]!.AsArray()[0]!["frameStart"] = -1), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media => media["weaponMedia"]!.AsArray()[0]!["actions"]!.AsArray()[1]!["frameStart"] = -1), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media => media["weaponMedia"]!.AsArray()[0]!["actions"]!.AsArray()[0]!["frameCount"] = int.MaxValue), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["frames"]!.AsArray()[0]!["frameIndex"] = 4), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media =>
        {
            JsonObject frame = WeaponResource(media)["frames"]!.AsArray()[0]!.AsObject();
            frame["x"] = 1;
            frame["width"] = int.MaxValue;
        }), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media => media["effects"]!.AsArray()[0]!["sourceRecordOrdinal"] = 3), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media => media["effects"]!.AsArray()[0]!["timing"]!["framesPerSecond"] = 12), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media => EffectResource(media, "effect.blood.0")["frames"]!.AsArray()[0]!["frameIndex"] = 1), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["byteLength"] = 1), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["mimeType"] = ""), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["sourceWidth"] = -1), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["relativePath"] = "../weapon.png"), payload, definitions));
    }

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

    private static ProductContent MutateDungeonMedia(string repositoryRoot, Action<JsonObject> mutate)
    {
        string contentRoot = Path.Combine(repositoryRoot, "content");
        ProductContentFile[] files = Directory.GetFiles(Path.Combine(contentRoot, "worldrpg/imports/privateers-hold"), "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')), File.ReadAllBytes(path)))
            .ToArray();
        int mediaIndex = Array.FindIndex(files, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("media/dungeon/manifest.json", StringComparison.Ordinal));
        int importsIndex = Array.FindIndex(files, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("import-manifest.json", StringComparison.Ordinal));
        Assert.True(mediaIndex >= 0 && importsIndex >= 0);

        JsonObject media = JsonNode.Parse(files[mediaIndex].Bytes.Span)!.AsObject();
        mutate(media);
        byte[] mediaBytes = Encoding.UTF8.GetBytes(media.ToJsonString());
        files[mediaIndex] = new ProductContentFile(files[mediaIndex].Path, mediaBytes);

        JsonObject imports = JsonNode.Parse(files[importsIndex].Bytes.Span)!.AsObject();
        JsonObject artifact = imports["artifacts"]!.AsArray().Select(value => value!.AsObject())
            .Single(value => value["relativePath"]!.GetValue<string>() == "media/dungeon/manifest.json");
        artifact["contentHash"] = Convert.ToHexString(SHA256.HashData(mediaBytes));
        files[importsIndex] = new ProductContentFile(files[importsIndex].Path, Encoding.UTF8.GetBytes(imports.ToJsonString()));
        return new ProductContent(files);
    }

    private static ProductContent MutateClassicMedia(string repositoryRoot, Action<JsonObject> mutate)
    {
        string contentRoot = Path.Combine(repositoryRoot, "content");
        ProductContentFile[] files = Directory.GetFiles(Path.Combine(contentRoot, "worldrpg/imports/privateers-hold"), "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')), File.ReadAllBytes(path)))
            .ToArray();
        int mediaIndex = Array.FindIndex(files, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("media/classic/manifest.json", StringComparison.Ordinal));
        int importsIndex = Array.FindIndex(files, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("import-manifest.json", StringComparison.Ordinal));
        Assert.True(mediaIndex >= 0 && importsIndex >= 0);

        JsonObject media = JsonNode.Parse(files[mediaIndex].Bytes.Span)!.AsObject();
        mutate(media);
        byte[] mediaBytes = Encoding.UTF8.GetBytes(media.ToJsonString());
        files[mediaIndex] = new ProductContentFile(files[mediaIndex].Path, mediaBytes);

        JsonObject imports = JsonNode.Parse(files[importsIndex].Bytes.Span)!.AsObject();
        JsonObject artifact = imports["artifacts"]!.AsArray().Select(value => value!.AsObject())
            .Single(value => value["relativePath"]!.GetValue<string>() == "media/classic/manifest.json");
        artifact["contentHash"] = Convert.ToHexString(SHA256.HashData(mediaBytes));
        files[importsIndex] = new ProductContentFile(files[importsIndex].Path, Encoding.UTF8.GetBytes(imports.ToJsonString()));
        return new ProductContent(files);
    }
}
