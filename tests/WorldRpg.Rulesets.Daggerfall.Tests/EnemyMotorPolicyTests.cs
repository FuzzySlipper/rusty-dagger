using WorldRpg.Kit.Ai;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class EnemyMotorPolicyTests
{
    [Theory]
    [InlineData(4, PursuitState.Retreat)]
    [InlineData(9, PursuitState.Idle)]
    [InlineData(12, PursuitState.Chase)]
    public void Published_ranged_preference_keeps_usable_distance(double distance, PursuitState expected)
    {
        var policy = DaggerfallEnemyMotorPolicy.Select(new(), new(CanRetreat: true),
            DaggerfallPursuitManeuverTuning.Default, new(1, 1, false, false, false, PrefersRanged: true, MinimumRangedDistance: 6),
            new(true, distance, 10), true, true, true, .1f, (_, _, _) => throw new Exception("No random maneuver needed."));
        Assert.Equal(expected, policy.Maneuver);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void Classic_or_vulnerable_targets_are_approached(bool classic, bool paralyzed, bool backTurned, bool bowOrSheathed)
    {
        var policy = Select(new(), new(1, 1, paralyzed, backTurned, bowOrSheathed), 2,
            DaggerfallPursuitManeuverTuning.Default with { EnhancedCombatAi = !classic }, (_, _, _) => throw new Exception("No random maneuver is needed."));
        Assert.Equal(PursuitState.Chase, policy.Maneuver);
    }

    [Fact]
    public void Retreat_uses_a_timed_level_biased_decision_and_half_speed()
    {
        var memory = new DaggerfallEnemyMotorMemory();
        int draws = 0;
        int Draw(string key, int minimum, int maximum)
        {
            draws++;
            return key == "strafe" ? 1 : minimum;
        }
        var retreat = Select(memory, new(1, 1, false, false, false), 2, DaggerfallPursuitManeuverTuning.Default, Draw);
        Assert.Equal(PursuitState.Retreat, retreat.Maneuver);
        Assert.Equal(.5f, retreat.SpeedMultiplier);
        int before = draws;
        _ = Select(memory, new(1, 1, false, false, false), 2, DaggerfallPursuitManeuverTuning.Default, Draw);
        Assert.Equal(before, draws);
        var advance = Select(new(), new(1, 1, false, false, false), 2, DaggerfallPursuitManeuverTuning.Default, (_, _, max) => max);
        Assert.Equal(PursuitState.Chase, advance.Maneuver);
        var distant = Select(new(), new(1, 1, false, false, false), 10, DaggerfallPursuitManeuverTuning.Default, Draw);
        Assert.Equal(PursuitState.Chase, distant.Maneuver);
    }

    [Fact]
    public void Strafe_uses_quarter_speed_and_the_selected_side()
    {
        var strafe = Select(new(), new(1, 1, false, false, false), 2, DaggerfallPursuitManeuverTuning.Default, (_, min, _) => min);
        Assert.Equal(PursuitState.Strafe, strafe.Maneuver);
        Assert.Equal(.25f, strafe.SpeedMultiplier);
        Assert.Equal(-1, strafe.StrafeDirection);
    }

    private static PursuitPolicy Select(DaggerfallEnemyMotorMemory memory, DaggerfallEnemyMotorContext context,
        double distance, DaggerfallPursuitManeuverTuning tuning, Func<string, int, int, int> draw) =>
        DaggerfallEnemyMotorPolicy.Select(memory, new(CanRetreat: true, CanStrafe: true, RetreatDistance: 2.5f, StrafeDistance: 1.5f),
            tuning, context, new(true, distance, 1.5), true, true, true, .1f, draw);
}
