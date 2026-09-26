using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class NormalizedPrivateersHoldContentTests
{
    [Fact]
    public void Admits_selected_interior_building_from_the_checked_normalized_publication()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(GeneratedContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.charing-interior-1-1-0.json")), definitions);
        Assert.Equal(new DaggerfallInteriorBuilding(1, 1, new("RESIAL05.RMB", 0), 18, 0), inputs.InteriorBuilding);
    }

    [Fact]
    public void Every_extracted_fixed_enemy_is_present_with_its_original_mobile_and_position()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(GeneratedContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), definitions);
        using JsonDocument extracted = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/imports/privateers-hold/normalized.json")));

        // Compare against independent import output: the migration's rat/skeleton-only
        // payload passed media validation while silently omitting other fixed enemies.
        foreach (JsonElement actor in extracted.RootElement.GetProperty("world").GetProperty("actors").EnumerateArray())
        {
            JsonElement position = actor.GetProperty("position");
            var placement = Assert.Single(inputs.Project.Actors.Values, value =>
                value.Position.X == position.GetProperty("x").GetSingle()
                && value.Position.Y == position.GetProperty("y").GetSingle()
                && value.Position.Z == position.GetProperty("z").GetSingle());
            Assert.Equal($"actor/mobile-{definitions.Actors[placement.ActorId].MobileId}", actor.GetProperty("actorResourceId").GetString());
            Assert.True(inputs.ActorSprites.ContainsKey(placement.EntityId));
        }
        Assert.Equal("imp", inputs.Project.Actors[2009].ActorId.Value);
        Assert.Equal("giant-bat", inputs.Project.Actors[2006].ActorId.Value);
    }

    [Fact]
    public void Every_supported_weapon_and_unarmed_has_complete_normalized_presentation()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(GeneratedContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), definitions);
        NormalizedClassicPresentation classic = inputs.ClassicPresentation;
        Assert.Equal(12, classic.Weapons.Count);
        Assert.Equal("weapon.longblade", classic.CompatibleItemVisuals["iron-longsword"]);
        Assert.Equal("weapon.dagger.steel", classic.CompatibleItemVisuals["iron-dagger"]);
        Assert.Equal("weapon.unarmed", classic.UnarmedVisual);
        // The two spare archives and the werecreature form keep stable identities no item selects:
        // the donor names no consumer for the spares, and the form is not an item.
        Assert.DoesNotContain("weapon.00", classic.CompatibleItemVisuals.Values);
        Assert.DoesNotContain("weapon.03", classic.CompatibleItemVisuals.Values);
        Assert.DoesNotContain("weapon.werecreature", classic.CompatibleItemVisuals.Values);
        Assert.Equal("center", classic.Weapons["weapon.werecreature"].Actions["idle"].Alignment);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], classic.Weapons["weapon.werecreature"].Actions.Values.Select(action => action.SourceRecordOrdinal).Order());
        Assert.All(definitions.Items.Values.Where(item => item.Weapon is not null), item =>
            Assert.True(classic.Weapons.ContainsKey(classic.CompatibleItemVisuals[item.Id.Value])));
        Assert.All(classic.Weapons.Values, weapon =>
        {
            Assert.True(weapon.Actions["idle"].Loops);
            Assert.All(weapon.Actions.Values.Where(action => action.Name != "idle"), action => Assert.False(action.Loops));
            Assert.All(weapon.Frames, frame => { Assert.Equal(320, frame.Width); Assert.Equal(200, frame.Height); });
        });
        NormalizedClassicWeaponAction left = classic.Weapons["weapon.unarmed"].Actions["strikeLeft"];
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 2, 1, 0 }, left.Sequence!.Select(frame => frame - left.FrameStart));
    }

    [Fact]
    public void ReadsTheGeneratedClosureWithoutSourceShapedSpatialOrSpriteFields()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(GeneratedContent(root), payload, definitions);

        Assert.Equal("worldrpg/imports/privateers-hold/spatial/privateer-s-hold/collision-navigation.json", inputs.SpatialArtifact.Path);
        Assert.Equal("worldrpg/imports/privateers-hold/spatial/privateer-s-hold/static-mesh.json", inputs.StaticMesh.Path);
        Assert.NotEmpty(inputs.Materials);
        Assert.Equal(inputs.Materials.Count, inputs.Materials.Select(material => material.Slot).Distinct().Count());
        Assert.NotEmpty(inputs.ActorSprites);
        DaggerfallSiteLight firstLight = Assert.Single(inputs.Lights, light => light.Id == "light/b0000003-rdb/1/0/0");
        Assert.Equal(new WorldPoint(54.4F, 34.4F, -17.2F), firstLight.Position);
        Assert.Equal(7.5F, firstLight.Range);
        Assert.Equal(1F, firstLight.Intensity);
        Assert.All(inputs.ActorSprites.Values, sprite => Assert.InRange(sprite.Frames.Count, 1, 4096));
        Assert.Equal("weapon.dagger.steel", Assert.IsType<NormalizedClassicWeapon>(inputs.ClassicPresentation.Weapons["weapon.dagger.steel"]).ResourceId);
        Assert.Equal("weapon.dagger.steel", inputs.ClassicPresentation.CompatibleItemVisuals["iron-dagger"]);
        Assert.Equal(["blood0", "blood1", "blood2", "magicSparkle"], inputs.ClassicPresentation.Effects.Select(effect => effect.Name));
    }

    [Fact]
    public void Exposes_the_published_arrow_world_visual_with_its_own_mesh_and_textures()
    {
        string root = RepositoryRoot();
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(GeneratedContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), TestPayload.Definitions);

        // The donor draws a flying arrow from ARCH3D mesh 99800; the site publishes that mesh, the
        // textures its planes select, and the record both were decoded from. Loading at all is the
        // evidence that the descriptor's digests are the import manifest's own.
        DaggerfallMissileVisual visual = Assert.Single(inputs.ClassicPresentation.WorldVisuals);
        Assert.Equal("visual.missile.arrow", visual.MediaId);
        Assert.Equal("worldrpg/imports/privateers-hold/geometry/mesh-99800.json", visual.Path);
        Assert.Equal([0u, 1u], visual.Textures.Select(texture => texture.MeshSlot));
        Assert.All(visual.Textures, texture =>
            Assert.StartsWith("worldrpg/imports/privateers-hold/media/world-visuals/", texture.TexturePath, StringComparison.Ordinal));
        Assert.Equal(2, visual.Textures.Select(texture => texture.TextureSha256).Distinct().Count());
        Assert.All(visual.Textures, texture => Assert.NotEqual(default, texture.TextureSha256));

        // The published mesh selects two textures, and the artifact states the same two slots.
        using JsonDocument artifact = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/imports/privateers-hold/geometry/mesh-99800.json")));
        Assert.Equal(2, artifact.RootElement.GetProperty("materialSlots").GetArrayLength());

        // The visual's textures travel with the visual rather than through the site's static-mesh
        // material table, whose slots address the combined world mesh and not this one.
        Assert.DoesNotContain(inputs.Materials, material => material.MaterialResourceId is "material/texture-1-121" or "material/texture-0-72");
    }

    [Theory]
    [InlineData("daggerfall.privateers-hold.json")]
    [InlineData("daggerfall.charing-exterior.json")]
    [InlineData("daggerfall.charing-interior-1-1-0.json")]
    [InlineData("daggerfall.castle-necromoghan.json")]
    public void Every_published_site_carries_the_published_arrow_world_visual(string payload)
    {
        string root = RepositoryRoot();
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(GeneratedContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads", payload)), TestPayload.Definitions);

        // The arrow is the ruleset's own projectile art rather than a site's placement, so every site
        // the bundle admits publishes the same visual; a site that quietly lost it would draw nothing.
        DaggerfallMissileVisual visual = Assert.Single(inputs.ClassicPresentation.WorldVisuals);
        Assert.Equal("visual.missile.arrow", visual.MediaId);
        Assert.Equal(2, visual.Textures.Count);
    }

    [Fact]
    public void Rejects_a_world_visual_whose_mesh_digest_the_import_manifest_does_not_admit()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(
            ContentWithMutatedClassicVisual(root, visual => visual["contentDigest"] = new string('0', 64)),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")),
            definitions));

        // A descriptor whose texture identity is not the one the site published cannot be drawn either:
        // the missing media is named rather than silently left untextured.
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(
            ContentWithMutatedClassicVisual(root, visual =>
                ((JsonArray)visual["materials"]!)[0]!["contentDigest"] = new string('1', 64)),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")),
            definitions));

        // A stated texture no mesh slot draws with is a sidecar that no longer describes the published
        // mesh; it is reported rather than dropped. The extra entry keeps valid bytes and its own
        // material identity, so every slot still resolves and the surplus branch is the only failure.
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(
            ContentWithMutatedClassicVisual(root, visual =>
            {
                JsonArray materials = (JsonArray)visual["materials"]!;
                JsonObject surplus = (JsonObject)materials[0]!.DeepClone();
                surplus["materialResourceId"] = "material/texture-1-121-surplus";
                materials.Add(surplus);
            }),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")),
            definitions));

        // The same for a texture the descriptor names but the import manifest does not admit: the site
        // must not draw a missile with bytes nobody published.
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(
            ContentWithMutatedClassicVisual(root, visual =>
                ((JsonArray)visual["materials"]!)[0]!["relativePath"] = "media/world-visuals/texture-9-9.png"),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")),
            definitions));
    }

    [Fact]
    public void Rejects_a_classic_manifest_that_states_no_world_visuals_at_all()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        const string classicRelativePath = "worldrpg/imports/privateers-hold/media/classic/manifest.json";
        string contentRoot = Path.Combine(root, "content");
        ProductContentFile[] files = Directory.GetFiles(contentRoot, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                string relative = Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                byte[] bytes = File.ReadAllBytes(path);
                if (StringComparer.Ordinal.Equals(relative, classicRelativePath))
                {
                    JsonObject manifest = JsonNode.Parse(bytes)!.AsObject();
                    manifest.Remove("worldVisuals");
                    bytes = Encoding.UTF8.GetBytes(manifest.ToJsonString());
                }

                return new ProductContentFile(Encoding.UTF8.GetBytes(relative), bytes);
            })
            .ToArray();

        // Every published sidecar states the section, so its absence means the sidecar predates the
        // visual the site is supposed to carry. Reporting it beats loading a site that draws nothing.
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(
            new ProductContent(files),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")),
            definitions));
    }

    /// <summary>The committed site content with one mutation applied to its one world visual.</summary>
    private static ProductContent ContentWithMutatedClassicVisual(string root, Action<JsonObject> mutate)
    {
        string contentRoot = Path.Combine(root, "content");
        const string classicRelativePath = "worldrpg/imports/privateers-hold/media/classic/manifest.json";
        ProductContentFile[] files = Directory.GetFiles(Path.Combine(contentRoot, "worldrpg/imports"), "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                string relative = Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                byte[] bytes = File.ReadAllBytes(path);
                if (StringComparer.Ordinal.Equals(relative, classicRelativePath))
                {
                    JsonObject manifest = JsonNode.Parse(bytes)!.AsObject();
                    mutate(((JsonArray)manifest["worldVisuals"]!)[0]!.AsObject());
                    bytes = Encoding.UTF8.GetBytes(manifest.ToJsonString());
                }

                return new ProductContentFile(Encoding.UTF8.GetBytes(relative), bytes);
            })
            .ToArray();
        return new ProductContent(files);
    }

    [Fact]
    public void Reads_the_donor_treasure_billboard_for_ground_container_projection()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(GeneratedContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), definitions);

        Assert.NotNull(inputs.GroundContainerSprite);
        NormalizedGroundContainerSprite visual = inputs.GroundContainerSprite!;
        Assert.Equal("worldrpg/imports/privateers-hold/media/dungeon/billboards/texture-216-0.png", visual.TexturePath);
        Assert.Equal(new Vector2(.5F, .5F), visual.Pivot);
        Assert.Equal(new Vector2(.975F, .65F), visual.Size);
        Assert.Equal(0u, visual.InitialFrameId);
        Assert.Single(visual.Frames);
        Assert.Equal(39, visual.Frames[0].Width);
        Assert.Equal(26, visual.Frames[0].Height);
    }

    [Fact]
    public void Admits_a_shared_attack_script_when_a_real_direction_has_fewer_source_frames()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(GeneratedContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), definitions);

        NormalizedActorSprite mobile = inputs.MobileSprites[30];
        Assert.Equal([0, 1, 2, 3, -1, 4, 5], Assert.Single(mobile.AttackSequences).SourceFrames);
        NormalizedSpriteState attack = mobile.States["primaryAttack"];
        Assert.Equal(6, attack.SelectOrientation(0).Count);
        Assert.Equal(5, attack.SelectOrientation(1).Count);
        Assert.Equal(5, attack.SelectOrientation(7).Count);
    }

    [Fact]
    public void Admits_castle_necromoghan_as_a_distinct_normalized_destination_profile()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        PrivateersHoldInputs destination = PrivateersHoldContent.Read(GeneratedContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        DaggerfallSiteProfiles profiles = new([destination]);

        Assert.Equal(new DaggerfallSiteId(17, 9), destination.Site);
        Assert.Equal("worldrpg/imports/castle-necromoghan/spatial/castle-necromoghan/collision-navigation.json", destination.SpatialArtifact.Path);
        Assert.Empty(destination.Project.Actors);
        Assert.Equal(125, destination.Doors.Count);
        Assert.Same(destination, profiles.Require(destination.ProfileKey));
    }

    [Fact]
    public void Admits_exterior_and_interior_profiles_at_the_same_geographic_site_by_logical_profile_identity()
    {
        DaggerfallSiteId charing = new(17, 4);
        PrivateersHoldInputs exterior = new(new ProjectFacts(new WorldPoint(0, 0, 0), new Dictionary<long, AuthoredActor>()),
            new SpatialContentArtifact("spatial/charing/exterior.json", default, 1), new ContentArtifact("mesh/charing/exterior.json", default),
            new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene), new PlayerInitialLook(0, 0), [], new Dictionary<long, NormalizedActorSprite>(),
            site: charing, profileKind: DaggerfallWorldProfileKind.Exterior, logicalProfileId: "worldrpg/imports/charing/exterior");
        PrivateersHoldInputs interior = new(new ProjectFacts(new WorldPoint(1, 0, 0), new Dictionary<long, AuthoredActor>()),
            new SpatialContentArtifact("spatial/charing/interior-1-1-0.json", default, 2), new ContentArtifact("mesh/charing/interior-1-1-0.json", default),
            new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene), new PlayerInitialLook(0, 0), [], new Dictionary<long, NormalizedActorSprite>(),
            site: charing, profileKind: DaggerfallWorldProfileKind.Interior, logicalProfileId: "worldrpg/imports/charing/interior-1-1-0");

        DaggerfallSiteProfiles profiles = new([exterior, interior]);

        Assert.Equal(charing, exterior.ProfileKey.Site);
        Assert.Equal(charing, interior.ProfileKey.Site);
        Assert.NotEqual(exterior.ProfileKey, interior.ProfileKey);
        Assert.Same(exterior, profiles.Require(exterior.ProfileKey));
        Assert.Same(interior, profiles.Require(interior.ProfileKey));
        Assert.Throws<InvalidOperationException>(() => profiles.RequireUniqueSite(charing));
    }

    [Fact]
    public void Projects_selected_privateers_hold_RDB_doors_with_real_source_id_pose_and_action_visual_bounds()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(GeneratedContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), definitions);

        Assert.Equal(58, inputs.Doors.Count);
        DaggerfallRdbDoorDefinition first = Assert.Single(inputs.Doors, door => door.Id == new DaggerfallRdbDoorId("B0000003.RDB", 1, 0, 0));
        Assert.Equal(new Vector3(67.8F, 32F, 0F), first.Position);
        Assert.Equal(new Vector3(0F, 540F, 0F), first.RotationDegrees);
        Assert.True(first.BoundsMin.X < first.BoundsMax.X);
        Assert.True(first.BoundsMin.Y < first.BoundsMax.Y);
        Assert.True(first.BoundsMin.Z < first.BoundsMax.Z);
        DaggerfallRdbDoorDefinition locked = Assert.Single(inputs.Doors, door => door.Id == new DaggerfallRdbDoorId("S0000999.RDB", 0, 0, 153));
        Assert.Equal(2, locked.StartingLockValue);
    }

    [Fact]
    public void Preserves_each_actor_crop_at_its_scaled_world_geometry()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(GeneratedContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), definitions);
        using JsonDocument media = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/imports/privateers-hold/media/dungeon/manifest.json")));

        JsonElement actor = media.RootElement.GetProperty("actors").EnumerateArray().Single(value => value.GetProperty("mobileId").GetInt32() == 0);
        Vector2 worldSize = Vector(actor.GetProperty("worldSize"));
        Vector2 sourceWorldSize = Vector(actor.GetProperty("sourceWorldSize"));
        NormalizedActorSprite sprite = inputs.ActorSprites[2007];
        foreach (uint frameId in new uint[] { 0, 8 })
        {
            JsonElement sourceFrame = actor.GetProperty("states").EnumerateArray()
                .SelectMany(state => state.GetProperty("frames").EnumerateArray())
                .Single(frame => frame.GetProperty("atlasFrameIndex").GetUInt32() == frameId);
            Vector2 sourceSize = Vector(sourceFrame.GetProperty("sourceWorldSize"));
            Vector2 expected = new(sourceSize.X * worldSize.X / sourceWorldSize.X, sourceSize.Y * worldSize.Y / sourceWorldSize.Y);
            Assert.Equal(expected, Assert.Single(sprite.Frames, frame => frame.Id == frameId).DisplaySize);
        }
    }

    [Fact]
    public void AdmitsANamedSpatialArtifactWithoutRehashingTheImportDigest()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));
        ProductContent content = GeneratedContent(root);
        ProductContentFile[] changed = content.Files.ToArray();
        int index = Array.FindIndex(changed, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("collision-navigation.json", StringComparison.Ordinal));
        Assert.True(index >= 0);
        byte[] bytes = changed[index].Bytes.ToArray();
        bytes[0] ^= 1;
        changed[index] = new ProductContentFile(changed[index].Path, bytes);

        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(new ProductContent(changed), payload, definitions);
        Assert.Equal("worldrpg/imports/privateers-hold/spatial/privateer-s-hold/collision-navigation.json", inputs.SpatialArtifact.Path);
    }

    [Fact]
    public void RejectsAnUnknownPlacementActorAsContentDiagnostics()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        string payload = File.ReadAllText(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        DaggerfallContentException exception = Assert.Throws<DaggerfallContentException>(() =>
            PrivateersHoldContent.Read(GeneratedContent(root), Encoding.UTF8.GetBytes(payload.Replace("\"actor\": \"skeletal-warrior\"", "\"actor\": \"missing-actor\"", StringComparison.Ordinal)), definitions));

        Assert.Contains("missing actor 'missing-actor'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateScenarioProperties()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        string payload = File.ReadAllText(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        DaggerfallContentException exception = Assert.Throws<DaggerfallContentException>(() =>
            PrivateersHoldContent.Read(GeneratedContent(root), Encoding.UTF8.GetBytes(payload.Replace("\"ruleset\": \"daggerfall\"", "\"ruleset\": \"daggerfall\", \"ruleset\": \"daggerfall\"", StringComparison.Ordinal)), definitions));

        Assert.Contains("repeats property 'ruleset'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsClassicPresentationThatSelectsANonWeaponSpriteOrMalformedEffectTiming()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        string payload = File.ReadAllText(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"))
            .Replace("\"weapon.dagger.steel\"", "\"effect.blood.0\"", StringComparison.Ordinal);

        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(GeneratedContent(root), Encoding.UTF8.GetBytes(payload), definitions));
    }

    [Fact]
    public void RejectsDuplicateWeaponMappingsAndMissingUnarmedMedia()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        string payload = File.ReadAllText(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(
            GeneratedContent(root), Encoding.UTF8.GetBytes(payload.Replace("\"itemId\": \"iron-dagger\"", "\"itemId\": \"iron-longsword\"", StringComparison.Ordinal)), definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(
            GeneratedContent(root), Encoding.UTF8.GetBytes(payload.Replace("weapon.unarmed", "weapon.missing", StringComparison.Ordinal)), definitions));
    }

    private static ProductContent GeneratedContent(string root)
    {
        string contentRoot = Path.Combine(root, "content");
        ProductContentFile[] files = Directory.GetFiles(Path.Combine(contentRoot, "worldrpg/imports"), "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')), File.ReadAllBytes(path)))
            .ToArray();
        return new ProductContent(files);
    }

    private static Vector2 Vector(JsonElement value) => new(value.GetProperty("x").GetSingle(), value.GetProperty("y").GetSingle());

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
