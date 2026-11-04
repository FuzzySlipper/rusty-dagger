using System.Globalization;
using System.Text;
using System.Text.Json;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Tuning payloads the session loads: controller, enemy reach, property, transport and progression values.</summary>
public sealed class TuningPayloadSessionTests
{
    [Fact]
    public void Strike_enchantment_values_load_from_tuning_and_reject_invalid_ranges()
    {
        string root = TestData.RepositoryRoot;
        var loaded = DaggerfallTuning.Read(TestContentFiles.ReadCopy(Path.Combine(root,
            "content/worldrpg/tuning-payloads/daggerfall.defaults.json")));
        Assert.Equal(new DaggerfallStrikeEnchantmentTuning(5, 2.25), loaded.StrikeEnchantments);
        var adjusted = DaggerfallTuning.Read(MutatedTuning(root, tuning =>
        {
            tuning["strikeEnchantments"]!["damageAdjustment"] = 7;
            tuning["strikeEnchantments"]!["vampiricRange"] = 3;
        }));
        Assert.Equal(new DaggerfallStrikeEnchantmentTuning(7, 3), adjusted.StrikeEnchantments);
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallTuning.Read(MutatedTuning(root,
            tuning => tuning["strikeEnchantments"]!["vampiricRange"] = 0)));
    }

    [Fact]
    public void The_attack_tunings_code_default_agrees_with_the_payload_including_its_paths_and_reach()
    {
        DaggerfallAttackTuning expected = DaggerfallAttackTuning.Default;
        DaggerfallAttackTuning loaded = DaggerfallTuning.Read(TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot,
            "content/worldrpg/tuning-payloads/daggerfall.defaults.json"))).Attacks;

        Assert.Equal((expected.PowerThresholdSeconds, expected.PowerDamageMultiplier, expected.PowerStaminaMultiplier),
            (loaded.PowerThresholdSeconds, loaded.PowerDamageMultiplier, loaded.PowerStaminaMultiplier));
        Assert.Equal((expected.Kick, expected.KickStaminaCost, expected.KickDamage, expected.KickReach),
            (loaded.Kick, loaded.KickStaminaCost, loaded.KickDamage, loaded.KickReach));
        Assert.Equal(expected.Stamina, loaded.Stamina);
        Assert.Equal(expected.Classes.Keys.Order(), loaded.Classes.Keys.Order());
        foreach (var (skill, attacks) in expected.Classes) Assert.Equal(attacks, loaded.Classes[skill]);
        Assert.Equal(expected.Paths.Keys.Order(), loaded.Paths.Keys.Order());
        foreach (var (attack, path) in expected.Paths) Assert.Equal(path, loaded.Paths[attack]);
        Assert.Equal(expected.PathGroups.Count, loaded.PathGroups.Count);
        foreach (var (authored, read) in expected.PathGroups.Zip(loaded.PathGroups))
        {
            Assert.Equal(authored.Templates.Order(), read.Templates.Order());
            Assert.Equal(authored.Paths.OrderBy(path => path.Key), read.Paths.OrderBy(path => path.Key));
        }
    }

    [Fact]
    public void Every_delivering_melee_attack_has_a_path_with_every_weapon_class_and_nothing_else_has_one()
    {
        DaggerfallAttackTuning tuning = DaggerfallTuning.Defaults.Attacks;
        string[] melee = [.. DaggerfallAttackIds.Fast, DaggerfallAttackIds.PowerOut, DaggerfallAttackIds.Kick];

        Assert.Equal(melee.Order(), tuning.Paths.Keys.Order());
        foreach (string skill in tuning.Classes.Keys)
            foreach (string attack in melee)
                Assert.True(tuning.Arc(attack, skill, null).Range > 0f, $"{attack} with {skill} reaches nothing.");
        Assert.Equal(tuning.KickReach, tuning.Arc(DaggerfallAttackIds.Kick, "long-blade", null).Range);
        Assert.Equal(tuning.For("axe").Reach, tuning.Arc("fastA", "axe", null).Range);
        Assert.Throws<InvalidOperationException>(() => tuning.Arc(DaggerfallAttackIds.PowerHold, "long-blade", null));
    }

    [Fact]
    public void Music_playlist_tuning_defaults_to_standard_and_requires_a_boolean()
    {
        string root = TestData.RepositoryRoot;
        Assert.False(DaggerfallTuning.Read(TestContentFiles.ReadCopy(Path.Combine(root,
            "content/worldrpg/tuning-payloads/daggerfall.defaults.json"))).Music.AlternatePlaylists);
        Assert.True(DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["music"]!["alternatePlaylists"] = true)).Music.AlternatePlaylists);
        Assert.Throws<InvalidOperationException>(() => DaggerfallTuning.Read(MutatedTuning(root,
            tuning => tuning["music"]!["alternatePlaylists"] = "true")));
    }

    [Fact]
    public void Daggerfall_tuning_exposes_its_controller_values_in_loaded_payloads()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallTuning tuning = DaggerfallTuning.Read(TestContentFiles.ReadCopy(Path.Combine(root, "content/worldrpg/tuning-payloads/daggerfall.defaults.json")));
        CharacterControllerTuning controller = Assert.IsType<CharacterControllerTuning>(tuning.Spatial.CharacterController);

        Assert.Equal(3.5f, controller.ForwardSpeed);
        Assert.Equal(3.5f, controller.BackwardSpeed);
        Assert.Equal(3.5f, controller.StrafeSpeed);
        Assert.Equal(1.8f, controller.StandingHeight);
        Assert.Equal(1.1f, controller.CrouchedHeight);
        Assert.Equal(.25f, controller.Radius);
        Assert.Equal(7f, controller.JumpSpeed);
        Assert.Equal(.12f, controller.JumpBufferSeconds);
        Assert.Equal(.1f, controller.JumpCoyoteSeconds);
        Assert.Equal(.1f, controller.JumpLandingLockoutSeconds);
        Assert.False(controller.JumpHeldInputRetriggers);
        Assert.Equal(1f, controller.RecoveryMaximumDistance);
        Assert.Equal(.75f, controller.MaximumStepHeight);
        Assert.Equal(39.5f, tuning.Locomotion.ClassicToEngineSpeedRatio);
        Assert.Equal(11, tuning.Locomotion.IdleFatiguePerGameMinute);
        Assert.Equal(88, tuning.Locomotion.RunningFatiguePerGameMinute);
        Assert.Equal(2.25d, tuning.MeleeTargeting.MaximumDistance);
        Assert.Equal(.5d, tuning.MeleeTargeting.MinimumFacingCosine);
    }

    /// <summary>
    /// The tuning profile shape the ruleset reads is the one it ships, and a profile still carrying the
    /// key that used to tune enemy reach is refused rather than loaded with its intent dropped.
    /// </summary>
    /// <remarks>
    /// Reach is authored on the action that carries it now, so the key is no longer read. Loading a profile
    /// that still names it would apply every other value and silently discard the tuning the operator set,
    /// which is the kind of difference no diagnostic would ever surface.
    /// </remarks>
    [Fact]
    public void An_obsolete_enemy_reach_tuning_key_is_refused_rather_than_ignored()
    {
        string root = TestData.RepositoryRoot;
        string path = Path.Combine(root, "content/worldrpg/tuning-payloads/daggerfall.defaults.json");
        using JsonDocument profile = JsonDocument.Parse(TestContentFiles.ReadCopy(path));
        Dictionary<string, object?> mutated = [];
        foreach (JsonProperty property in profile.RootElement.EnumerateObject())
        {
            if (property.NameEquals("enemyBehavior"))
            {
                Dictionary<string, object?> behavior = [];
                foreach (JsonProperty entry in property.Value.EnumerateObject()) behavior[entry.Name] = entry.Value.Clone();
                behavior["attackReach"] = 9.99;
                mutated[property.Name] = behavior;
                continue;
            }

            mutated[property.Name] = property.Value.Clone();
        }

        string obsolete = JsonSerializer.Serialize(mutated);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => DaggerfallTuning.Read(Encoding.UTF8.GetBytes(obsolete)));
        Assert.Contains("attackReach", error.Message, StringComparison.Ordinal);
        // The shipped profile does not carry it, so the refusal is the shape's and not the payload's.
        Assert.DoesNotContain("attackReach", TestContentFiles.ReadText(path), StringComparison.Ordinal);
    }

    /// <summary>
    /// Property prices, ship anchors, transport values and the experimental XP step are payload tuning:
    /// the shipped profile must load to exactly the ruleset defaults, and an out-of-range value is refused
    /// at admission rather than reaching the owner that reads it.
    /// </summary>
    [Fact]
    public void A_weapons_path_group_replaces_only_the_attacks_it_names()
    {
        DaggerfallAttackTuning tuning = DaggerfallTuning.Defaults.Attacks;
        SwingArc chop = tuning.Arc("fastC", DaggerfallSkills.LongBlade, null);

        Assert.True(chop.TiltDegrees > 60f, "a weapon in no group chops overhead");
        Assert.True(tuning.Arc("fastC", DaggerfallSkills.LongBlade, 120).TiltDegrees < 60f, "the sword-and-shield set's fastC rises across the view");
        Assert.Equal(tuning.Arc("fastA", DaggerfallSkills.LongBlade, null), tuning.Arc("fastA", DaggerfallSkills.LongBlade, 120));
        Assert.True(tuning.Arc("powerOut", DaggerfallSkills.BluntWeapon, 115).TiltDegrees < 30f, "the staff's power attack sweeps sideways");
        Assert.True(tuning.Arc("powerOut", DaggerfallSkills.BluntWeapon, 122).TiltDegrees > 60f, "a two-handed sword's power attack chops");
    }

    [Fact]
    public void The_path_groups_stay_in_step_with_the_clips_each_weapon_plays()
    {
        using JsonDocument held = JsonDocument.Parse(TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/first-person/daggerfall.held.json")));
        int[] rising = [.. held.RootElement.GetProperty("items").EnumerateObject()
            .Where(item => item.Value.GetProperty("family").GetString() == "sns").Select(item => int.Parse(item.Name, CultureInfo.InvariantCulture)).Order()];

        Assert.Equal(rising, DaggerfallTuning.Defaults.Attacks.PathGroups[0].Templates.Order());
    }
    [Fact]
    public void The_default_payload_carries_property_transport_and_progression_tuning()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallTuning loaded = DaggerfallTuning.Read(TestContentFiles.ReadCopy(Path.Combine(root, "content/worldrpg/tuning-payloads/daggerfall.defaults.json")));

        Assert.Equal(DaggerfallTuning.Defaults.Progression, loaded.Progression);
        Assert.Equal(DaggerfallTuning.Defaults.Property, loaded.Property);
        Assert.Equal(DaggerfallTuning.Defaults.Transport, loaded.Transport);

        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["progression"]!["experiencePerLevel"] = 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["property"]!["salePercent"] = 101)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["property"]!["largeShipArrival"]!["mapPixelX"] = 1000)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["transport"]!["wagonCapacityClassicUnits"] = 0)));
    }
}
