using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// What every location assembled from its blocks draws on: the product-wide world media publication's actor, billboard
/// and classic media with the base payload's classic selection, and the block publication's people and doors.
/// </summary>
public sealed class WorldMediaContentTests
{
    [Fact]
    public void Required_file_accessor_distinguishes_a_missing_file_from_an_empty_file()
    {
        var files = AdmittedFiles.From(TestSessions.Data(new ProductContent(new ProductContentFile[] {
            new(Encoding.UTF8.GetBytes("empty.json"), ReadOnlyMemory<byte>.Empty) })));
        Assert.True(files.GetExactlyOne("empty.json").IsEmpty);
        var error = Assert.Throws<DaggerfallContentException>(() => files.GetExactlyOne("missing.json"));
        Assert.Contains("missing.json", error.Message);
    }

    [Fact]
    public void Admits_selected_interior_building_from_the_checked_normalized_publication()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = Assembled(DaggerfallWorldProfileIds.Interior(Charing, new(1, 1, 0)));
        Assert.Equal(new DaggerfallInteriorBuilding(1, 1, new("RESIAL05.RMB", 0), 18, 0), inputs.InteriorBuilding! with { EntryFrame = null });
        // The publication also carries the exterior frame that matches a building arrival to its interior door.
        Assert.NotEmpty(inputs.InteriorBuilding.EntryFrame!.Doors);
    }

    [Fact]
    public void Every_supported_weapon_and_unarmed_has_complete_normalized_presentation()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        NormalizedClassicPresentation classic = inputs.ClassicPresentation;
        Assert.Equal(17, classic.Weapons.Count);
        Assert.Equal("weapon.unarmed", classic.UnarmedVisual);
        // The two spare archives and the werecreature form keep stable identities no item selects:
        // the donor names no consumer for the spares, and the form is not an item.
        Assert.DoesNotContain("weapon.00", classic.CompatibleItemVisuals.Values);
        Assert.DoesNotContain("weapon.03", classic.CompatibleItemVisuals.Values);
        Assert.DoesNotContain("weapon.werecreature", classic.CompatibleItemVisuals.Values);
        Assert.Equal("center", classic.Weapons["weapon.werecreature"].Actions["idle"].Alignment);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], classic.Weapons["weapon.werecreature"].Actions.Values.Select(action => action.SourceRecordOrdinal).Order());
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
    public void Exposes_the_published_arrow_world_visual_with_its_own_mesh_and_textures()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);

        // The donor draws a flying arrow from ARCH3D mesh 99800; the site publishes that mesh, the
        // textures its planes select, and the record both were decoded from. Loading at all is the
        // evidence that the descriptor's digests are the import manifest's own.
        DaggerfallMissileVisual visual = Assert.Single(inputs.ClassicPresentation.WorldVisuals);
        Assert.Equal("visual.missile.arrow", visual.MediaId);
        Assert.Equal("worldrpg/imports/shared/geometry/mesh-99800.rstatmsh", visual.Path);
        Assert.Equal([0u, 1u], visual.Textures.Select(texture => texture.MeshSlot));
        Assert.All(visual.Textures, texture =>
            Assert.StartsWith("worldrpg/imports/shared/media/world-visuals/", texture.TexturePath, StringComparison.Ordinal));
        Assert.Equal(2, visual.Textures.Select(texture => texture.TextureSha256).Distinct().Count());
        Assert.All(visual.Textures, texture => Assert.NotEqual(default, texture.TextureSha256));

        // The published mesh selects two textures, and the artifact states the same two slots.
        using EngineBinaryContent.StaticMesh artifact = EngineBinaryContent.ReadStaticMesh(
            TestContentFiles.ReadCopy(Path.Combine(root, "content/worldrpg/imports/shared/geometry/mesh-99800.rstatmsh")));
        Assert.Equal(2, artifact.Root.GetProperty("materialSlots").GetArrayLength());

        // The visual's textures travel with the visual rather than through the site's static-mesh
        // material table, whose slots address the combined world mesh and not this one.
        Assert.DoesNotContain(inputs.Materials, material => material.MaterialResourceId is "material/texture-1-121" or "material/texture-0-72");
    }

    [Fact]
    public void Reads_the_donor_treasure_billboards_for_ground_container_projection()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);

        // The donor's random treasure icons, in record order.
        Assert.Equal([0, 1, 3, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 36, 37, 38, 39, 40, 43, 44, 45, 46, 47],
            inputs.GroundContainerSprites!.Select(sprite => int.Parse(Path.GetFileNameWithoutExtension(sprite.TexturePath)["texture-216-".Length..])));
        NormalizedBillboardSprite visual = inputs.GroundContainerSprites![0];
        Assert.Equal("worldrpg/imports/shared/media/dungeon/billboards/texture-216-0.png", visual.TexturePath);
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
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);

        NormalizedActorSprite mobile = inputs.MobileSprites[30];
        Assert.Equal([0, 1, 2, 3, -1, 4, 5], Assert.Single(mobile.AttackSequences).SourceFrames);
        NormalizedSpriteState attack = mobile.States["primaryAttack"];
        Assert.Equal(6, attack.SelectOrientation(0).Count);
        Assert.Equal(5, attack.SelectOrientation(1).Count);
        Assert.Equal(5, attack.SelectOrientation(7).Count);
    }

    [Fact]
    public void Admits_exterior_and_interior_profiles_at_the_same_geographic_site_by_logical_profile_identity()
    {
        DaggerfallSiteId charing = new(17, 4);
        DaggerfallSiteProfile exterior = new(new ProjectFacts(new WorldPoint(0, 0, 0), new Dictionary<long, AuthoredActor>()),
            FixtureGeometry("spatial/charing/exterior.json", default, 1, "mesh/charing/exterior.json", default),
            new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene), new PlayerInitialLook(0, 0), [], new Dictionary<long, NormalizedActorSprite>(),
            site: charing, profileKind: DaggerfallWorldProfileKind.Exterior, logicalProfileId: "charing/exterior");
        DaggerfallSiteProfile interior = new(new ProjectFacts(new WorldPoint(1, 0, 0), new Dictionary<long, AuthoredActor>()),
            FixtureGeometry("spatial/charing/interior-1-1-0.json", default, 2, "mesh/charing/interior-1-1-0.json", default),
            new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene), new PlayerInitialLook(0, 0), [], new Dictionary<long, NormalizedActorSprite>(),
            site: charing, profileKind: DaggerfallWorldProfileKind.Interior, logicalProfileId: "charing/interior-1-1-0");

        DaggerfallSiteProfiles profiles = new([exterior, interior]);

        Assert.Equal(charing, exterior.ProfileKey.Site);
        Assert.Equal(charing, interior.ProfileKey.Site);
        Assert.NotEqual(exterior.ProfileKey, interior.ProfileKey);
        Assert.Same(exterior, profiles.Require(exterior.ProfileKey));
        Assert.Same(interior, profiles.Require(interior.ProfileKey));
    }

    [Fact]
    public void Projects_selected_privateers_hold_RDB_doors_with_real_source_id_pose_and_action_visual_bounds()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);

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


    /// <summary>
    /// Every fixed enemy Privateer's Hold's blocks place is an actor of the assembled Hold at its block position, with
    /// the actor its mobile names and its media: the migration's rat/skeleton-only payload once passed media validation
    /// while silently omitting other fixed enemies.
    /// </summary>
    [Fact]
    public void Every_fixed_enemy_the_blocks_place_is_present_with_its_mobile_and_position()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallWorldBlocks blocks = new(TestSessions.Data(FullContent(TestData.RepositoryRoot)));
        DaggerfallSiteDungeonBlock[] placed = [.. definitions.Locations.Records.Single(record => record.Id == PrivateersHold).DungeonBlocks];
        int count = 0;
        foreach (DaggerfallSiteDungeonBlock block in placed)
        {
            DaggerfallWorldBlockKey key = new(DaggerfallWorldBlockKind.Rdb, block.SourceKey);
            Vector3 origin = new(block.X * DaggerfallWorldGrid.HorizontalSize / 16F, 0F, -(block.Z * DaggerfallWorldGrid.HorizontalSize / 16F));
            foreach (JsonNode? actor in blocks.Read([key])[key].Section("actors"))
            {
                JsonObject position = actor!["position"]!.AsObject();
                Vector3 at = origin + new Vector3(position["x"]!.GetValue<float>(), position["y"]!.GetValue<float>(), position["z"]!.GetValue<float>());
                AuthoredActor placement = Assert.Single(inputs.Project.Actors.Values, value => Vector3.Distance(value.Position.ToVector(), at) < 1e-3F);
                Assert.Equal($"actor/mobile-{definitions.Actors[placement.ActorId].MobileId}", actor["actorResourceId"]!.GetValue<string>());
                Assert.True(inputs.ActorSprites.ContainsKey(placement.EntityId));
                count++;
            }
        }

        Assert.Equal(25, count);
        Assert.Equal(count, inputs.Project.Actors.Count);
        Assert.Equal("imp", inputs.Project.Actors[HoldActor(41)].ActorId.Value);
        Assert.Equal("giant-bat", inputs.Project.Actors[HoldActor(35)].ActorId.Value);
    }

    /// <summary>
    /// The authored actor presentation is over every fixed actor's media: a rat rests in its own idle, and imps and bats
    /// hover in their move state at its authored rate.
    /// </summary>
    [Fact]
    public void Fixed_actors_wear_their_authored_rest_state_and_playback()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        Assert.Equal("ratIdle", SpriteFor(inputs, "rat").PreferredRestState);
        NormalizedActorSprite imp = SpriteFor(inputs, "imp");
        Assert.Equal("move", imp.PreferredRestState);
        Assert.Equal(10F, imp.States["move"].EffectiveFramesPerSecond);
    }

    /// <summary>
    /// The importer marks each burning lamp the place draws, so its glow and its light have one source: the Hold's wall
    /// torches burn, its extinguished lamp does not, and nothing outside the lights archive is a lamp; and each names the
    /// light it sheds, its own or the light record that stands for it, so a swinging lantern carries that light.
    /// </summary>
    [Fact]
    public void The_assembled_hold_knows_its_burning_lamps_from_the_import()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallSiteDecoration[] torches = [.. inputs.Decorations.Where(decoration => decoration.Source == (210, 17))];
        DaggerfallSiteDecoration[] extinguished = [.. inputs.Decorations.Where(decoration => decoration.Source == (210, 12))];
        Assert.NotEmpty(torches);
        Assert.NotEmpty(extinguished);
        Assert.All(torches, decoration => Assert.True(decoration.Lamp));
        Assert.All(extinguished, decoration => Assert.False(decoration.Lamp));
        Assert.DoesNotContain(inputs.Decorations, decoration => decoration.Lamp && decoration.Source.Archive != 210);
        // Each decoration names the light it sheds as its block was placed, among the hold's lights, and no two name one light.
        string[] named = [.. inputs.Decorations.Select(decoration => decoration.Light).OfType<string>()];
        Assert.NotEmpty(named);
        Assert.Subset(inputs.Lights.Select(light => light.Id).ToHashSet(StringComparer.Ordinal), named.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(named.Length, named.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(named, light => light.StartsWith("light/", StringComparison.Ordinal));
        Assert.DoesNotContain(extinguished, decoration => decoration.Light is not null);
    }

    [Fact]
    public void The_assembled_hold_reads_its_lights_materials_sprites_and_classic_presentation()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);

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
        Assert.Equal(["blood0", "blood1", "blood2", "coinGlint", "coldImpact", "coldMissile", "fireImpact", "fireMissile", "magicGlint", "magicImpact", "magicMissile", "magicSparkle",
            "poisonImpact", "poisonMissile", "shockImpact", "shockMissile"], inputs.ClassicPresentation.Effects.Select(effect => effect.Name));
        // The human enemy classes fall as the one corpse TEXTURE.380 record 1 draws, and the content says so.
        Assert.Contains(inputs.ActorSprites.Values.Concat(inputs.MobileSprites.Values), sprite => sprite.Corpse?.SourceTexture == (380, 1));
    }

    /// <summary>
    /// The arrow is the ruleset's own projectile art rather than a location's placement, so every location presents the
    /// same visual; a location that quietly lost it would draw nothing.
    /// </summary>
    [Theory]
    [InlineData("17/179/dungeon")]
    [InlineData("17/4/exterior")]
    [InlineData("17/4/interior-1-1-0")]
    [InlineData("17/9/dungeon")]
    public void Every_assembled_location_carries_the_published_arrow_world_visual(string profile)
    {
        DaggerfallMissileVisual visual = Assert.Single(Assembled(profile).ClassicPresentation.WorldVisuals);
        Assert.Equal("visual.missile.arrow", visual.MediaId);
        Assert.Equal(2, visual.Textures.Count);
    }

    [Fact]
    public void Rejects_a_world_visual_whose_mesh_digest_the_import_manifest_does_not_admit()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent Visual(Action<JsonObject> mutate) =>
            NormalizedMediaValidationTests.MutateClassicMedia(root, media => mutate(((JsonArray)media["worldVisuals"]!)[0]!.AsObject()));

        Assert.Throws<DaggerfallContentException>(() => NormalizedMediaValidationTests.Read(Visual(visual => visual["contentDigest"] = new string('0', 64)), definitions));
        // A descriptor whose texture identity is not the one published cannot be drawn either: the missing media is
        // named rather than silently left untextured.
        Assert.Throws<DaggerfallContentException>(() => NormalizedMediaValidationTests.Read(Visual(visual =>
            ((JsonArray)visual["materials"]!)[0]!["contentDigest"] = new string('1', 64)), definitions));
        // A second texture stated for a slot is a sidecar that no longer describes the published mesh; it is reported
        // rather than dropped. The extra entry keeps valid bytes and its own material identity, so the repeated slot is
        // the only failure.
        Assert.Throws<DaggerfallContentException>(() => NormalizedMediaValidationTests.Read(Visual(visual =>
        {
            JsonArray materials = (JsonArray)visual["materials"]!;
            JsonObject surplus = (JsonObject)materials[0]!.DeepClone();
            surplus["materialResourceId"] = "material/texture-1-121-surplus";
            materials.Add(surplus);
        }), definitions));
        // The same for a texture the descriptor names but the import manifest does not admit: no missile is drawn with
        // bytes nobody published.
        Assert.Throws<DaggerfallContentException>(() => NormalizedMediaValidationTests.Read(Visual(visual =>
            ((JsonArray)visual["materials"]!)[0]!["relativePath"] = "media/world-visuals/texture-9-9.png"), definitions));
    }

    /// <summary>
    /// Every published classic sidecar states its world visuals, so their absence means a sidecar that predates the
    /// visual every location is supposed to carry. Reporting it beats presenting locations that draw no arrow.
    /// </summary>
    [Fact]
    public void Rejects_a_classic_manifest_that_states_no_world_visuals_at_all()
    {
        Assert.Throws<DaggerfallContentException>(() => NormalizedMediaValidationTests.Read(
            NormalizedMediaValidationTests.MutateClassicMedia(TestData.RepositoryRoot, media => media.Remove("worldVisuals")), TestPayload.Definitions));
    }

    [Fact]
    public void Admits_castle_necromoghan_as_a_distinct_assembled_destination_profile()
    {
        DaggerfallSiteProfile destination = Assembled(DaggerfallWorldProfileIds.Dungeon(CastleNecromoghan));
        DaggerfallSiteProfiles profiles = new([destination]);

        Assert.Equal(new DaggerfallSiteId(17, 9), destination.Site);
        Assert.Equal(TestPayload.Definitions.Locations.Records.Single(record => record.Id == CastleNecromoghan).DungeonBlocks.Count, destination.Geometry.Spatial.Count);
        Assert.Empty(destination.Project.Actors);
        Assert.Equal(125, destination.Doors.Count);
        Assert.All(destination.Doors, door => Assert.Equal(DaggerfallLockInteractionSurface.Interior, door.LockSurface));
        Assert.Same(destination, profiles.Require(destination.ProfileKey));
    }

    [Fact]
    public void Preserves_each_actor_crop_at_its_scaled_world_geometry()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        using JsonDocument media = JsonDocument.Parse(TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content",
            DaggerfallWorldMedia.Root, DaggerfallWorldMedia.DungeonSidecarRelativePath)));

        JsonElement actor = media.RootElement.GetProperty("actors").EnumerateArray().Single(value => value.GetProperty("mobileId").GetInt32() == 0);
        Vector2 worldSize = Vector(actor.GetProperty("worldSize"));
        Vector2 sourceWorldSize = Vector(actor.GetProperty("sourceWorldSize"));
        NormalizedActorSprite sprite = inputs.ActorSprites[HoldActor(39)];
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

    private static Vector2 Vector(JsonElement value) => new(value.GetProperty("x").GetSingle(), value.GetProperty("y").GetSingle());
}
