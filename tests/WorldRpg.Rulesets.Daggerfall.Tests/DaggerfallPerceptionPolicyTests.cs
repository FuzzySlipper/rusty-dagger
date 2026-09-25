using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallPerceptionPolicyTests
{
    [Fact]
    public void Classic_sense_formulas_keep_source_units_and_integer_rounding()
    {
        Assert.Equal(156, DaggerfallPerceptionPolicy.CalculateStealthChance(25d, 80));
        Assert.Equal(26, DaggerfallPerceptionPolicy.CalculateEnemyPacificationChance(
            DaggerfallSkills.Etiquette, 60, 50, weaponSheathed: true));
        Assert.Equal(40, DaggerfallPerceptionPolicy.CalculateEnemyPacificationChance(
            DaggerfallSkills.Orcish, 60, 50, weaponSheathed: false));
        Assert.Equal(31, DaggerfallPerceptionPolicy.CalculateEnemyPacificationChance(
            DaggerfallSkills.Etiquette, 60, 50, weaponSheathed: true, comprehendLanguagesBonus: 5));
    }

    [Fact]
    public void Illusion_effect_blocks_visibility_until_the_effect_flag_changes()
    {
        DaggerfallEnemyPerceptionMemory memory = new();
        DaggerfallEnemyPerceptionSource source = new();
        DaggerfallEnemyPerceptionContext hidden = Context() with { TargetInvisible = true };

        DaggerfallEnemyPerceptionDecision blocked = DaggerfallPerceptionPolicy.Evaluate(
            memory, Pair(PerceptionPairKind.Visible), source, hidden, _ => 0);

        Assert.True(blocked.BlockedByIllusion);
        Assert.False(blocked.InSight);
        Assert.False(blocked.Detected);

        DaggerfallEnemyPerceptionDecision visible = DaggerfallPerceptionPolicy.Evaluate(
            memory, Pair(PerceptionPairKind.Visible), source, Context(), _ => 0);

        Assert.True(visible.InSight);
        Assert.True(visible.Detected);
        Assert.False(visible.BlockedByIllusion);

        DaggerfallEnemyPerceptionDecision blockedAfterEncounter = DaggerfallPerceptionPolicy.Evaluate(
            memory,
            Pair(PerceptionPairKind.Visible),
            source,
            hidden with { GameMinute = 1, Noise = 1d },
            _ => 0);

        Assert.True(blockedAfterEncounter.BlockedByIllusion);
        Assert.False(blockedAfterEncounter.InEarshot);
        Assert.False(blockedAfterEncounter.Detected);

        DaggerfallEnemyPerceptionDecision blockedRear = DaggerfallPerceptionPolicy.Evaluate(
            memory,
            Pair(PerceptionPairKind.FacingRejected),
            source,
            hidden with { GameMinute = 2, Noise = 1d },
            _ => 0);

        Assert.True(blockedRear.BlockedByIllusion);
        Assert.False(blockedRear.InEarshot);
        Assert.False(blockedRear.Detected);
    }

    [Fact]
    public void Illusion_breakthrough_uses_the_donor_chances_and_enemy_override()
    {
        DaggerfallEnemyPerceptionSource source = new();

        DaggerfallEnemyPerceptionDecision blendingBreakthrough = DaggerfallPerceptionPolicy.Evaluate(
            new DaggerfallEnemyPerceptionMemory(),
            Pair(PerceptionPairKind.Visible),
            source,
            Context() with { TargetBlending = true },
            _ => 7);
        Assert.True(blendingBreakthrough.InSight);

        DaggerfallEnemyPerceptionDecision blendingBlocked = DaggerfallPerceptionPolicy.Evaluate(
            new DaggerfallEnemyPerceptionMemory(),
            Pair(PerceptionPairKind.Visible),
            source,
            Context() with { TargetBlending = true },
            _ => 8);
        Assert.True(blendingBlocked.BlockedByIllusion);
        Assert.False(blendingBlocked.Detected);

        DaggerfallEnemyPerceptionDecision shadeBreakthrough = DaggerfallPerceptionPolicy.Evaluate(
            new DaggerfallEnemyPerceptionMemory(),
            Pair(PerceptionPairKind.Visible),
            source,
            Context() with { TargetShade = true },
            _ => 3);
        Assert.True(shadeBreakthrough.InSight);

        DaggerfallEnemyPerceptionDecision shadeBlocked = DaggerfallPerceptionPolicy.Evaluate(
            new DaggerfallEnemyPerceptionMemory(),
            Pair(PerceptionPairKind.Visible),
            source,
            Context() with { TargetShade = true },
            _ => 4);
        Assert.True(shadeBlocked.BlockedByIllusion);

        DaggerfallEnemyPerceptionDecision trueSight = DaggerfallPerceptionPolicy.Evaluate(
            new DaggerfallEnemyPerceptionMemory(),
            Pair(PerceptionPairKind.Visible),
            source,
            Context() with { TargetInvisible = true, EnemySeesThroughInvisibility = true },
            _ => 99);
        Assert.True(trueSight.InSight);
    }

    [Fact]
    public void Source_defaults_preserve_classic_sight_hearing_fov_and_stealth_bounds()
    {
        DaggerfallEnemyPerceptionSource source = new();

        Assert.Equal(102.4d, source.SightRadius, precision: 10);
        Assert.Equal(25d, source.HearingRadius);
        Assert.Equal(180d, DaggerfallPerceptionQueryDefaults.FieldOfViewDegrees);
        Assert.Equal(0d, DaggerfallPerceptionQueryDefaults.MinimumFacingCosine);
        Assert.Equal(25.6d, DaggerfallPerceptionQueryDefaults.StealthMaximumDistance, precision: 10);
    }

    [Fact]
    public void Remembered_detection_hears_rear_noise_but_not_silent_or_occluded_targets()
    {
        DaggerfallEnemyPerceptionMemory memory = new();
        DaggerfallEnemyPerceptionSource source = new();

        DaggerfallEnemyPerceptionDecision first = DaggerfallPerceptionPolicy.Evaluate(
            memory, Pair(PerceptionPairKind.Visible), source, Context(), _ => 0);
        Assert.True(first.Detected);

        DaggerfallEnemyPerceptionDecision rearNoise = DaggerfallPerceptionPolicy.Evaluate(
            memory, Pair(PerceptionPairKind.FacingRejected, distance: 10d), source,
            Context() with { GameMinute = 1, Noise = 1d }, _ => 0);
        Assert.True(rearNoise.InEarshot);
        Assert.True(rearNoise.Detected);

        DaggerfallEnemyPerceptionDecision silent = DaggerfallPerceptionPolicy.Evaluate(
            memory, Pair(PerceptionPairKind.FacingRejected, distance: 10d), source,
            Context() with
            {
                GameMinute = 2,
                Noise = 0d,
                TargetInsideDungeonCastle = true,
                EnemyHostile = false,
            }, _ => 0);
        Assert.False(silent.InEarshot);
        Assert.False(silent.Detected);

        DaggerfallEnemyPerceptionDecision occluded = DaggerfallPerceptionPolicy.Evaluate(
            memory, Pair(PerceptionPairKind.Occluded, distance: 10d), source,
            Context() with
            {
                GameMinute = 3,
                TargetInsideDungeonCastle = true,
                EnemyHostile = false,
            }, _ => 0);
        Assert.False(occluded.InSight);
        Assert.False(occluded.InEarshot);
        Assert.False(occluded.Detected);
    }

    [Fact]
    public void Stealth_use_is_attempted_once_per_game_minute_and_respects_slow_movement_cadence()
    {
        DaggerfallEnemyPerceptionMemory memory = new();
        DaggerfallEnemyPerceptionSource source = new();
        DaggerfallEnemyPerceptionContext minuteZero = Context() with
        {
            GameMinute = 0,
            StealthSkill = 100,
            TargetMovingLessThanHalfSpeed = true,
        };

        DaggerfallEnemyPerceptionDecision first = DaggerfallPerceptionPolicy.Evaluate(
            memory, Pair(PerceptionPairKind.FacingRejected, distance: 25d), source, minuteZero, _ => 99);
        Assert.True(first.StealthCheckAttempted);
        Assert.False(first.Detected);
        Assert.Equal(DaggerfallSkillUseReason.StealthCheck, Assert.Single(first.SkillUses).Reason);

        DaggerfallEnemyPerceptionDecision sameMinute = DaggerfallPerceptionPolicy.Evaluate(
            memory, Pair(PerceptionPairKind.FacingRejected, distance: 25d), source, minuteZero, _ => 0);
        Assert.False(sameMinute.StealthCheckAttempted);
        Assert.Empty(sameMinute.SkillUses);
        Assert.False(sameMinute.Detected);

        DaggerfallEnemyPerceptionDecision nextEvenMinute = DaggerfallPerceptionPolicy.Evaluate(
            memory, Pair(PerceptionPairKind.FacingRejected, distance: 25d), source,
            minuteZero with { GameMinute = 2 }, _ => 99);
        Assert.True(nextEvenMinute.StealthCheckAttempted);
        Assert.Single(nextEvenMinute.SkillUses);
    }

    [Fact]
    public void Slow_player_still_rolls_stealth_on_even_minutes_after_contact()
    {
        DaggerfallEnemyPerceptionMemory memory = new();
        DaggerfallEnemyPerceptionSource source = new();

        DaggerfallEnemyPerceptionDecision first = DaggerfallPerceptionPolicy.Evaluate(
            memory, Pair(PerceptionPairKind.Visible), source, Context(), _ => 0);
        Assert.True(first.Detected);

        DaggerfallEnemyPerceptionContext slowEven = Context() with
        {
            GameMinute = 2,
            StealthSkill = 0,
            TargetMovingLessThanHalfSpeed = true,
            Noise = 0d,
        };
        DaggerfallEnemyPerceptionDecision even = DaggerfallPerceptionPolicy.Evaluate(
            memory, Pair(PerceptionPairKind.FacingRejected, distance: 25d), source, slowEven, _ => 99);
        Assert.True(even.StealthCheckAttempted);
        Assert.Single(even.SkillUses);
        Assert.True(even.Detected);

        DaggerfallEnemyPerceptionDecision odd = DaggerfallPerceptionPolicy.Evaluate(
            memory, Pair(PerceptionPairKind.FacingRejected, distance: 25d), source,
            slowEven with { GameMinute = 3 }, _ => 99);
        Assert.False(odd.StealthCheckAttempted);
        Assert.Empty(odd.SkillUses);
        Assert.True(odd.Detected);
    }

    [Fact]
    public void Pacification_changes_hostility_projection_and_records_only_required_language_uses()
    {
        DaggerfallEnemyPerceptionSource source = new();
        DaggerfallEnemyPerceptionMemory successMemory = new();
        DaggerfallEnemyPerceptionDecision success = DaggerfallPerceptionPolicy.Evaluate(
            successMemory,
            Pair(PerceptionPairKind.Visible),
            source,
            Context() with
            {
                LanguageSkill = DaggerfallSkills.Orcish,
                LanguageSkillValue = 100,
                Personality = 100,
            },
            _ => 0);

        Assert.True(success.Pacified);
        Assert.False(success.PursuitVisible);
        DaggerfallSkillUse successUse = Assert.Single(success.SkillUses);
        Assert.Equal(DaggerfallSkills.Orcish, successUse.Skill);
        Assert.Equal(DaggerfallSkillUseReason.PacificationSucceeded, successUse.Reason);
        Assert.Equal(DaggerfallSkillUseOutcome.Succeeded, successUse.Outcome);

        DaggerfallEnemyPerceptionMemory etiquetteMemory = new();
        DaggerfallEnemyPerceptionDecision etiquetteFailure = DaggerfallPerceptionPolicy.Evaluate(
            etiquetteMemory,
            Pair(PerceptionPairKind.Visible),
            source,
            Context() with
            {
                LanguageSkill = DaggerfallSkills.Etiquette,
                TargetWeaponSheathed = false,
            },
            _ => 199);
        Assert.False(etiquetteFailure.Pacified);
        Assert.DoesNotContain(etiquetteFailure.SkillUses, use => use.Reason == DaggerfallSkillUseReason.PacificationFailed);

        DaggerfallEnemyPerceptionMemory languageMemory = new();
        DaggerfallEnemyPerceptionDecision languageFailure = DaggerfallPerceptionPolicy.Evaluate(
            languageMemory,
            Pair(PerceptionPairKind.Visible),
            source,
            Context() with
            {
                LanguageSkill = DaggerfallSkills.Orcish,
                TargetWeaponSheathed = false,
            },
            _ => 199);
        DaggerfallSkillUse failureUse = Assert.Single(languageFailure.SkillUses);
        Assert.Equal(DaggerfallSkillUseReason.PacificationFailed, failureUse.Reason);
        Assert.Equal(DaggerfallSkillUseOutcome.Attempted, failureUse.Outcome);
    }

    [Fact]
    public void Clearing_perception_memory_removes_detection_stealth_and_pacification_state()
    {
        DaggerfallEnemyPerceptionMemory memory = new()
        {
            Detected = true,
            HasEncounteredPlayer = true,
            Pacified = true,
            LastStealthCheckMinute = 12,
        };

        memory.Clear();

        Assert.False(memory.Detected);
        Assert.False(memory.HasEncounteredPlayer);
        Assert.False(memory.Pacified);
        Assert.Null(memory.LastStealthCheckMinute);
        Assert.Null(memory.LastDirectSightMinute);
    }

    [Fact]
    public void Typed_effect_projection_supplies_perception_meaning_until_effect_end()
    {
        using ActorsState actors = new();
        actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), Stats(), "health");
        DaggerfallEffectCatalog catalog = new(
        [
            new DaggerfallEffectDefinition(
                "typed-concealment",
                "typed-concealment",
                DaggerfallEffectStacking.Stack,
                1,
                1,
                Perception: new DaggerfallPerceptionEffectState(
                    Invisible: true,
                    ComprehendLanguagesBonus: 7)),
        ]);
        using DaggerfallEffectLifecycle effects = new(actors, catalog);
        using JsonDocument state = JsonDocument.Parse("{}");
        DaggerfallEffectRequest request = new(
            "typed-concealment-instance",
            "typed-concealment",
            "test-source",
            null,
            DaggerfallActorIdentity.PlayerEntityId,
            "test",
            null,
            null,
            1,
            2,
            state.RootElement);

        _ = effects.Start(request);
        DaggerfallPerceptionEffectState active = effects.PerceptionFor(DaggerfallActorIdentity.PlayerEntityId);
        Assert.True(active.Invisible);
        Assert.False(active.Blending);
        Assert.Equal(7, active.ComprehendLanguagesBonus);

        Assert.True(effects.Cancel(EffectInstanceId.Parse("typed-concealment-instance")));
        Assert.Equal(default, effects.PerceptionFor(DaggerfallActorIdentity.PlayerEntityId));
    }

    [Fact]
    public void Mobile_catalog_publishes_enemy_basics_true_sight_flags()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        int[] trueSightMobiles = [1, 15, 18, 19, 23, 25, 26, 27, 28, 29, 30, 31, 32];

        foreach (int mobileId in trueSightMobiles)
            Assert.True(definitions.Mobiles.Mobiles[mobileId].SeesThroughInvisibility, $"mobile {mobileId} should see through concealment");
        Assert.False(definitions.Mobiles.Mobiles[0].SeesThroughInvisibility);
    }

    private static DaggerfallEnemyPerceptionContext Context() =>
        DaggerfallEnemyPerceptionContext.Default(gameMinute: 0);

    private static PerceptionPair Pair(PerceptionPairKind kind, double distance = 1d) =>
        new(2000, (ulong)DaggerfallActorIdentity.PlayerEntityId, distance,
            kind == PerceptionPairKind.Visible ? 1d : 0d,
            kind,
            kind == PerceptionPairKind.Occluded ? 0d : 1d);

    private static StatsComponent Stats()
    {
        Stat maximum = new(100);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value), maximum);
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value), new Track(maximum, 100, 0));
        return stats;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }
}
