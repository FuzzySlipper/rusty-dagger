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
        byte[] scenario = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        ProductContent withImportedPreference = MutateDungeonMedia(root, media => media["actors"]!.AsArray()
            .Single(value => value!["mobileId"]!.GetValue<int>() == 15)!["preferredRestState"] = "idle");
        DaggerfallSiteProfile inputs = DaggerfallSiteContent.Read(withImportedPreference, scenario, definitions);
        Assert.Equal("idle", SpriteFor(inputs, "skeletal-warrior").PreferredRestState);

        Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(
            MutateDungeonMedia(root, media => media["actors"]!.AsArray().Single(value => value!["mobileId"]!.GetValue<int>() == 15)!["preferredRestState"] = "missingState"),
            scenario,
            definitions));

        DaggerfallDefinitions unknownAuthoredState = DaggerfallBaseContent.Read(TestPayload.Replaced(("\"preferredRestState\": \"ratIdle\"", "\"preferredRestState\": \"missingState\"")));
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
        JsonObject media = CompleteDungeonSidecar(root, "worldrpg/imports/privateers-hold");
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

    [Fact]
    public void Classic_weapon_admission_refuses_a_strike_that_ends_before_its_hit_frame()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        // A melee strike shorter than the melee hit frame: every strike can play a melee swing.
        DaggerfallContentException melee = Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media =>
            WeaponAction(media, "weapon.dagger.steel", "strikeUp")["sequence"] = new JsonArray(26, 27)), payload, definitions));
        Assert.Contains(melee.Diagnostics, message => message.Contains("'weapon.dagger.steel' strike 'strikeUp' plays 2 frames", StringComparison.Ordinal)
            && message.Contains($"melee hit frame {DaggerfallFormulaPolicy.MeleeWeaponHitFrame}", StringComparison.Ordinal));

        // The bow's loosing strike shorter than the bow hit frame: the arrow would never leave.
        DaggerfallContentException bow = Assert.Throws<DaggerfallContentException>(() => DaggerfallSiteContent.Read(MutateClassicMedia(root, media =>
            WeaponAction(media, "weapon.bow", "strikeDown")["sequence"] = new JsonArray(0, 1, 2, 3, 4)), payload, definitions));
        Assert.Contains(bow.Diagnostics, message => message.Contains("Bow 'iron-short-bow' selects classic weapon 'weapon.bow', whose 'strikeDown' plays 5 frames", StringComparison.Ordinal)
            && message.Contains($"bow hit frame {DaggerfallFormulaPolicy.BowWeaponHitFrame}", StringComparison.Ordinal));

        // The bow's up strike is shorter than the bow hit frame, as published, and is admitted: a bow
        // never plays it.
        Assert.Equal(4, WeaponAction(ClassicMedia(root), "weapon.bow", "strikeUp")["frameCount"]!.GetValue<int>());
        _ = DaggerfallSiteContent.Read(MutateClassicMedia(root, _ => { }), payload, definitions);
    }

    private static JsonObject WeaponAction(JsonObject media, string resource, string action) => media["weaponMedia"]!.AsArray()
        .Select(value => value!.AsObject())
        .Single(weapon => weapon["resourceId"]!.GetValue<string>() == resource)["actions"]!.AsArray()
        .Select(value => value!.AsObject())
        .Single(value => value["action"]!.GetValue<string>() == action);

    private static JsonObject ClassicMedia(string repositoryRoot) => JsonNode.Parse(File.ReadAllBytes(
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

    /// <summary>
    /// Privateer's Hold with a complete dungeon sidecar of its own, as a closure carries one that overrides
    /// every product-wide entry, mutated: the reader validates each entry whichever publication carries it.
    /// </summary>
    private static ProductContent MutateDungeonMedia(string repositoryRoot, Action<JsonObject> mutate) =>
        WithOwnSidecar(repositoryRoot, "media/dungeon/manifest.json", CompleteDungeonSidecar(repositoryRoot, "worldrpg/imports/privateers-hold"), mutate);

    /// <summary>Privateer's Hold carrying its own copy of the product-wide classic sidecar, mutated.</summary>
    private static ProductContent MutateClassicMedia(string repositoryRoot, Action<JsonObject> mutate) =>
        WithOwnSidecar(repositoryRoot, "media/classic/manifest.json",
            JsonNode.Parse(File.ReadAllBytes(Path.Combine(repositoryRoot, "content", DaggerfallWorldMedia.Root, "media/classic/manifest.json")))!.AsObject(), mutate);

    private static ProductContent WithOwnSidecar(string repositoryRoot, string relativePath, JsonObject sidecar, Action<JsonObject> mutate)
    {
        const string site = "worldrpg/imports/privateers-hold";
        string contentRoot = Path.Combine(repositoryRoot, "content");
        List<ProductContentFile> files = [.. Directory.GetFiles(Path.Combine(contentRoot, site), "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => path != $"{site}/{relativePath}" && path != $"{site}/import-manifest.json")
            .Select(path => new ProductContentFile(Encoding.UTF8.GetBytes(path), File.ReadAllBytes(Path.Combine(contentRoot, path))))];
        mutate(sidecar);
        byte[] sidecarBytes = Encoding.UTF8.GetBytes(sidecar.ToJsonString());
        files.Add(new ProductContentFile(Encoding.UTF8.GetBytes($"{site}/{relativePath}"), sidecarBytes));

        JsonObject imports = JsonNode.Parse(File.ReadAllBytes(Path.Combine(contentRoot, site, "import-manifest.json")))!.AsObject();
        JsonArray artifacts = imports["artifacts"]!.AsArray();
        JsonObject? artifact = artifacts.Select(value => value!.AsObject()).SingleOrDefault(value => value["relativePath"]!.GetValue<string>() == relativePath);
        if (artifact is null)
        {
            artifact = new JsonObject { ["relativePath"] = relativePath, ["byteLen"] = sidecarBytes.Length, ["dependsOnPaths"] = new JsonArray() };
            artifacts.Add(artifact);
        }

        artifact["contentHash"] = Convert.ToHexString(SHA256.HashData(sidecarBytes)).ToLowerInvariant();
        files.Add(new ProductContentFile(Encoding.UTF8.GetBytes($"{site}/import-manifest.json"), Encoding.UTF8.GetBytes(imports.ToJsonString())));
        return new ProductContent(files.Concat(ProductMediaFiles(repositoryRoot)).ToArray());
    }
}
