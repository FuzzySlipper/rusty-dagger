using System.Text;
using System.Text.Json;
using WorldRpg.Kit.Controls;
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
        var loaded = DaggerfallTuning.Read(File.ReadAllBytes(Path.Combine(root,
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
    public void Music_playlist_tuning_defaults_to_standard_and_requires_a_boolean()
    {
        string root = TestData.RepositoryRoot;
        Assert.False(DaggerfallTuning.Read(File.ReadAllBytes(Path.Combine(root,
            "content/worldrpg/tuning-payloads/daggerfall.defaults.json"))).Music.AlternatePlaylists);
        Assert.True(DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["music"]!["alternatePlaylists"] = true)).Music.AlternatePlaylists);
        Assert.Throws<InvalidOperationException>(() => DaggerfallTuning.Read(MutatedTuning(root,
            tuning => tuning["music"]!["alternatePlaylists"] = "true")));
    }

    [Fact]
    public void Daggerfall_tuning_exposes_its_controller_values_in_loaded_payloads()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallTuning tuning = DaggerfallTuning.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/tuning-payloads/daggerfall.defaults.json")));
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
        using JsonDocument profile = JsonDocument.Parse(File.ReadAllBytes(path));
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
        Assert.DoesNotContain("attackReach", File.ReadAllText(path), StringComparison.Ordinal);
    }

    /// <summary>
    /// Property prices, ship anchors, transport values and the experimental XP step are payload tuning:
    /// the shipped profile must load to exactly the ruleset defaults, and an out-of-range value is refused
    /// at admission rather than reaching the owner that reads it.
    /// </summary>
    [Fact]
    public void The_default_payload_carries_property_transport_and_progression_tuning()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallTuning loaded = DaggerfallTuning.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/tuning-payloads/daggerfall.defaults.json")));

        Assert.Equal(DaggerfallTuning.Defaults.Progression, loaded.Progression);
        Assert.Equal(DaggerfallTuning.Defaults.Property, loaded.Property);
        Assert.Equal(DaggerfallTuning.Defaults.Transport, loaded.Transport);

        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["progression"]!["experiencePerLevel"] = 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["property"]!["salePercent"] = 101)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["property"]!["largeShipArrival"]!["mapPixelX"] = 1000)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["transport"]!["wagonCapacityClassicUnits"] = 0)));
    }
}
