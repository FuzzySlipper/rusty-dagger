using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallEnemySplatSwitch"/> built without the session: shipped human enemy splats, all four geometry
/// variants (<c>gaussians</c>, <c>core</c>, <c>cards</c>, <c>cards-core</c>), combat clip playback, livery tinting,
/// and classic sprite fallback.
/// </summary>
public sealed class DaggerfallEnemySplatSwitchTests
{
    [Fact]
    public void Every_human_enemy_mobile_id_maps_to_a_shipped_splat_with_all_five_variants_and_combat_clips()
    {
        DaggerfallSplatEnemies enemies = Art().SplatEnemies!;
        Assert.Equal(15, enemies.Characters.Count);

        // Every Daggerfall human enemy mobile ID (128..146) maps cleanly for both Male and Female.
        for (int mobileId = 128; mobileId <= 146; mobileId++)
        {
            string? male = enemies.Resolve(mobileId, "Male");
            string? female = enemies.Resolve(mobileId, "Female");
            Assert.NotNull(male);
            Assert.NotNull(female);
            Assert.True(enemies.Characters.ContainsKey(male), $"Mobile {mobileId} Male -> {male}");
            Assert.True(enemies.Characters.ContainsKey(female), $"Mobile {mobileId} Female -> {female}");
        }

        // Every shipped character carries all five geometry variants (the hybrid: its splats over a skinned base mesh) and
        // the core combat clip fits.
        string[] requiredVariants = ["gaussians", "core", "cards", "cards-core", "hybrid"];
        string[] requiredClips = ["combat-idle", "mage-idle", "run", "run-sword", "attack-sword", "attack-sword-2", "attack-melee", "attack-melee-2", "attack-cast", "attack-bow", "hurt", "death"];
        foreach ((string id, DaggerfallSplatEnemy enemy) in enemies.Characters)
        {
            Assert.All(requiredVariants, variant => Assert.True(enemy.Bodies.ContainsKey(variant), $"{id} missing variant {variant}"));
            Assert.All(requiredClips, clip => Assert.True(enemy.Fits.ContainsKey(clip), $"{id} missing fit for {clip}"));
        }
    }

    [Fact]
    public void Live_variant_switching_opens_each_body_and_off_releases_appearances_for_classic_sprites()
    {
        DaggerfallSplatEnemies enemies = Art().SplatEnemies!;
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = Clips(enemies);
        using DaggerfallEnemySplatSwitch sw = Switch(enemies, animation);

        // Mobile 135 (Warrior Male -> npc-466) draws his hybrid body by default, in the tuning's hybrid look: smooth, casting
        // as its base mesh, his splats drawing over it by the surface and close bias, their material read, light wrapped.
        Appearance? hybrid = sw.Show(200, 10, 135, "Male", null, "idle", 0, false, null, null);
        Assert.NotNull(hybrid);
        Assert.Equal([enemies.Characters["npc-466"].Bodies["hybrid"].Path], animation.OpenedPaths);
        SplatAppearanceRequest look = animation.Splats[^1];
        Assert.Equal((0U, 0F, SplatShadowCaster.None, .03F, .08F, true, .6F),
            (look.PixelGrid, look.LightFloor, look.ShadowCaster, look.SurfaceBias, look.CloseBias, look.Material, look.DiffuseWrap));
        sw.Publishing();
        sw.Settle();

        // His gaussians draw in the plain look.
        Assert.True(sw.EnemiesSplatVariant("gaussians").Succeeded);
        Appearance? gaussians = sw.Show(200, 10, 135, "Male", null, "idle", 0, false, null, null);
        Assert.NotSame(hybrid, gaussians);
        Assert.Equal(enemies.Characters["npc-466"].Bodies["gaussians"].Path, animation.OpenedPaths[^1]);
        look = animation.Splats[^1];
        Assert.Equal((3U, .15F, SplatShadowCaster.Ellipsoids, 0F, false, .6F),
            (look.PixelGrid, look.LightFloor, look.ShadowCaster, look.SurfaceBias, look.Material, look.DiffuseWrap));
        sw.Publishing();
        sw.Settle();
        Assert.Equal(1, animation.ReleasedAppearances);

        // Switching to cards-core opens the cards-core GLB and disposes the superseded gaussians appearance after Settle.
        Assert.True(sw.EnemiesSplatVariant("cards-core").Succeeded);
        Appearance? cardsCore = sw.Show(200, 10, 135, "Male", null, "idle", 0, false, null, null);
        Assert.NotNull(cardsCore);
        Assert.NotSame(gaussians, cardsCore);
        Assert.Equal(enemies.Characters["npc-466"].Bodies["cards-core"].Path, animation.OpenedPaths[^1]);
        sw.Publishing();
        sw.Settle();
        Assert.Equal(2, animation.ReleasedAppearances);

        // Invalid variant is refused without disturbing the active variant.
        Assert.False(sw.EnemiesSplatVariant("voxels").Succeeded);

        // enemies.splat off drops back to classic sprites (null) and releases the active instance (2 total including ReplaceInstance) & appearance.
        Assert.True(sw.EnemiesSplat("off").Succeeded);
        Assert.Null(sw.Show(200, 10, 135, "Male", null, "idle", 0, false, null, null));
        sw.Publishing();
        Assert.Equal(3, animation.ReleasedInstances);
        sw.Settle();
        Assert.Equal(3, animation.ReleasedAppearances);
    }

    [Fact]
    public void Combat_states_and_clip_overrides_drive_expected_playback_and_one_shot_restarts()
    {
        DaggerfallSplatEnemies enemies = Art().SplatEnemies!;
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = Clips(enemies);
        using DaggerfallEnemySplatSwitch sw = Switch(enemies, animation);

        // Melee warrior (npc-466) starts in combat-idle, transitions through run-sword, attack-sword / attack-sword-2, bow, cast, hurt, death.
        Step(200, 10, 135, "idle", 0, false);
        Assert.Equal(("combat-idle", AnimationLoopMode.Repeat), (animation.Playbacks[^1].Clip, animation.Playbacks[^1].LoopMode));

        Step(200, 10, 135, "move", 0, false);
        Assert.Equal(("run-sword", AnimationLoopMode.Repeat), (animation.Playbacks[^1].Clip, animation.Playbacks[^1].LoopMode));

        Step(200, 10, 135, "primaryAttack", 1, false);
        Assert.Equal(("attack-sword-2", AnimationLoopMode.Once), (animation.Playbacks[^1].Clip, animation.Playbacks[^1].LoopMode));

        Step(200, 10, 135, "primaryAttack", 2, false);
        Assert.Equal(("attack-sword", AnimationLoopMode.Once), (animation.Playbacks[^1].Clip, animation.Playbacks[^1].LoopMode));

        // Staff caster (mobile 128 Female -> npc-467) uses mage-idle and 2H staff attack-melee.
        Assert.NotNull(sw.Show(201, 11, 128, "Female", null, "idle", 0, false, null, null));
        sw.Publishing(); sw.Settle();
        Assert.Equal(("mage-idle", AnimationLoopMode.Repeat), (animation.Playbacks[^1].Clip, animation.Playbacks[^1].LoopMode));
        Assert.NotNull(sw.Show(201, 11, 128, "Female", null, "primaryAttack", 1, false, null, null));
        sw.Publishing(); sw.Settle();
        Assert.Equal(("attack-melee", AnimationLoopMode.Once), (animation.Playbacks[^1].Clip, animation.Playbacks[^1].LoopMode));

        Step(200, 10, 135, "rangedAttack1", 3, false);
        Assert.Equal(("attack-bow", AnimationLoopMode.Once), (animation.Playbacks[^1].Clip, animation.Playbacks[^1].LoopMode));

        Step(200, 10, 135, "cast", 4, false);
        Assert.Equal(("attack-cast", AnimationLoopMode.Once), (animation.Playbacks[^1].Clip, animation.Playbacks[^1].LoopMode));

        Step(200, 10, 135, "hurt", 5, false);
        Assert.Equal(("hurt", AnimationLoopMode.Once), (animation.Playbacks[^1].Clip, animation.Playbacks[^1].LoopMode));

        Step(200, 10, 135, "idle", 5, defeated: true);
        Assert.Equal(("death", AnimationLoopMode.Once), (animation.Playbacks[^1].Clip, animation.Playbacks[^1].LoopMode));

        // Developer clip override forces a clip regardless of state, and auto restores live state playback.
        Assert.False(sw.EnemiesSplatClip("backflip").Succeeded);
        Assert.True(sw.EnemiesSplatClip("attack-bow").Succeeded);
        Step(200, 10, 135, "idle", 0, false);
        Assert.Equal(("attack-bow", AnimationLoopMode.Once), (animation.Playbacks[^1].Clip, animation.Playbacks[^1].LoopMode));
        Assert.True(sw.EnemiesSplatClip("auto").Succeeded);
        Step(200, 10, 135, "idle", 0, false);
        Assert.Equal(("combat-idle", AnimationLoopMode.Repeat), (animation.Playbacks[^1].Clip, animation.Playbacks[^1].LoopMode));

        void Step(ulong id, long actor, int mobileId, string state, uint seq, bool defeated)
        {
            Assert.NotNull(sw.Show(id, actor, mobileId, "Male", null, state, seq, defeated, null, null));
            sw.Publishing();
            sw.Settle();
        }
    }

    [Fact]
    public void Look_shadow_and_livery_tinting_apply_to_enemy_appearances()
    {
        DaggerfallSplatEnemies enemies = Art().SplatEnemies!;
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = Clips(enemies);
        using DaggerfallEnemySplatSwitch sw = Switch(enemies, animation);

        // City Watch (mobile 146 -> npc-399) with two distinct liege livery tints creates two distinct appearances.
        Color blue = new(.2F, .4F, .9F, 1F);
        Color red = new(.9F, .2F, .2F, 1F);
        Appearance? blueGuard = sw.Show(300, 20, 146, "Male", null, "idle", 0, false, blue, "daggerfall");
        Appearance? redGuard = sw.Show(301, 21, 146, "Male", null, "idle", 0, false, red, "wayrest");
        Assert.NotNull(blueGuard);
        Assert.NotNull(redGuard);
        Assert.NotSame(blueGuard, redGuard);
        Assert.Single(animation.OpenedPaths); // Same body GLB shared across liveries.
        Assert.Equal(new Vector3(blue.R, blue.G, blue.B), animation.Splats[0].Multiplier);
        Assert.Equal(new Vector3(red.R, red.G, red.B), animation.Splats[1].Multiplier);

        // enemies.splat-shadow core, enemies.splat-look and enemies.splat-fade update active appearances.
        Assert.True(sw.EnemiesSplatShadow("core").Succeeded);
        Assert.Equal(SplatShadowCaster.Core, animation.Splats[^1].ShadowCaster);
        Assert.False(sw.EnemiesSplatShadow("blobs").Succeeded);
        Assert.True(sw.EnemiesSplatLook(0, .5, "none", 0).Succeeded);
        Assert.Equal(0U, animation.Splats[^1].PixelGrid);
        Assert.False(sw.EnemiesSplatLook(0, .5, "own", 0).Succeeded);
        Assert.True(sw.EnemiesSplatFade(4, 12, .02).Succeeded);
        Assert.Equal((4F, 12F, .02F), (animation.Splats[^1].FadeNear, animation.Splats[^1].FadeFar, animation.Splats[^1].SurfaceBias));
        Assert.False(sw.EnemiesSplatFade(12, 4, 0).Succeeded);
        Assert.False(sw.EnemiesSplatFade(0, 0, -.01).Succeeded);
    }

    /// <summary>
    /// Den #11224: a look that names a splat character draws as it, whatever its mobile binds; one the art set lacks draws
    /// the classic sprite and is said once.
    /// </summary>
    [Fact]
    public void A_character_the_look_names_draws_in_place_of_the_mobiles_and_a_missing_one_falls_back_with_a_warning()
    {
        DaggerfallSplatEnemies enemies = Art().SplatEnemies!;
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = Clips(enemies);
        List<string> warnings = [];
        using DaggerfallEnemySplatSwitch sw = Switch(enemies, animation, warnings.Add);

        Assert.NotNull(sw.Show(400, 30, 143, "female", "npc-469", "idle", 0, false, null, null));
        Assert.NotNull(sw.Show(401, 31, 146, "male", "npc-470", "idle", 0, false, null, null));
        Assert.Equal(2, animation.OpenedPaths.Count);
        Assert.All(animation.OpenedPaths, path => Assert.Matches("npc-4(69|70)", path));

        Assert.Null(sw.Show(402, 32, 143, "male", "npc-orc", "idle", 0, false, null, null));
        Assert.Null(sw.Show(403, 33, 143, "male", "npc-orc", "move", 0, false, null, null));
        Assert.Single(warnings, warning => warning.Contains("npc-orc", StringComparison.Ordinal));
    }

    private static DaggerfallEnemySplatSwitch Switch(DaggerfallSplatEnemies enemies, AnimationRecorder animation, Action<string>? warn = null) =>
        new(enemies, Shipped(enemies), () => animation.Service, () => new ContentFake([]),
            DaggerfallSplatPeopleTuning.Default, () => true, warn ?? (_ => { }));

    private static DaggerfallAnimations Shipped(DaggerfallSplatEnemies enemies)
    {
        BundleContentFake service = new();
        service.Container(DaggerfallAnimations.ContainerName);
        foreach ((string _, DaggerfallArtFile clip) in enemies.Clips)
            service.AddEntry(DaggerfallAnimations.ContainerName, clip.Path, clip.Sha256, 1,
                () => throw new InvalidOperationException("The recording animation service reads no clip pack's bytes."));
        return new(new ProductContent(Array.Empty<ProductContentFile>(), service).OpenContainer(DaggerfallAnimations.ContainerName));
    }

    private static AnimationClipInfo[] Clips(DaggerfallSplatEnemies enemies) =>
        [.. enemies.Clips.Keys.Select((clip, at) => new AnimationClipInfo(clip, clip, 2F, true, (uint)at + 1))];

    private static DaggerfallArtReplacements Art()
    {
        JsonNode section = JsonNode.Parse(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot,
            "content/worldrpg/packs/daggerfall.art-replacements/payload.json")))!["artReplacements"]!;
        return DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes(
            $"{{\"artReplacements\": {{\"images\": [], \"splatEnemies\": {section["splatEnemies"]!.ToJsonString()}}}}}"),
            "daggerfall.art-replacements");
    }
}
