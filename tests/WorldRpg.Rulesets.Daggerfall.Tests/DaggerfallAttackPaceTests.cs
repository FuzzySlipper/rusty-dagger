using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>What Speed, weapon skill, Strength and stamina do to how long the player's attacks take.</summary>
public sealed class DaggerfallAttackPaceTests
{
    private static DaggerfallCombatantTuning Tuning => DaggerfallAttackTuning.Default.Combatant;

    private static void Stat(DaggerfallSession session, string stat, int value) =>
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(stat)).BaseValue = value;

    private static void Average(DaggerfallSession session)
    {
        Stat(session, DaggerfallMechanicsIds.Speed.Value, 50);
        Stat(session, DaggerfallMechanicsIds.Strength.Value, 50);
        Stat(session, ((IDaggerfallPlayerAttacks)session).WeaponSkill, 0);
    }

    [Fact]
    public void Every_attack_of_a_mashed_chain_is_admitted()
    {
        using DaggerfallSession session = FreshSession();
        IDaggerfallPlayerAttacks attacks = session;
        session.State.WeaponDrawn = true;
        HashSet<ulong> admitted = [];
        for (ulong step = 1; step <= 180; step++)
        {
            session.Update(new ProductUpdate(OuterUpdate(step), AttackTap));
            if (session.State.Actors.Player.Actor.Get<AttackState>().Pending is { } pending) admitted.Add(pending.Request.SimulationStep);
        }

        DaggerfallAttackTiming fast = DaggerfallAttackTuning.Default.For(attacks.WeaponSkill).Fast;
        double between = (fast.Windup + fast.Commit) * attacks.TimeScale("fastA") + 2d / 60d;
        Assert.True(admitted.Count >= (int)(3d / between) - 1, $"{admitted.Count} attacks admitted in three seconds of {between} each");
    }

    [Fact]
    public void Speed_quickens_an_attack_above_average_and_slows_it_below()
    {
        using DaggerfallSession session = FreshSession();
        IDaggerfallPlayerAttacks attacks = session;
        Average(session);
        Assert.Equal(1d, attacks.TimeScale("fastA"), 6);

        Stat(session, DaggerfallMechanicsIds.Speed.Value, 75);
        Assert.Equal(1d - 25 * Tuning.SpeedPerPointAbove50, attacks.TimeScale("fastA"), 6);

        Stat(session, DaggerfallMechanicsIds.Speed.Value, 25);
        Assert.Equal(1d + 25 * Tuning.SpeedPerPointAbove50, attacks.TimeScale("fastA"), 6);
    }

    [Fact]
    public void Speed_quickens_a_shot_as_it_does_any_attack()
    {
        using DaggerfallSession session = FreshSession();
        IDaggerfallPlayerAttacks attacks = session;
        Average(session);
        double average = attacks.TimeScale(DaggerfallAttackIds.ShotDraw);
        Stat(session, DaggerfallMechanicsIds.Speed.Value, 100);
        Assert.True(attacks.TimeScale(DaggerfallAttackIds.ShotDraw) < average);
    }

    [Fact]
    public void Weapon_skill_shortens_an_attack_and_strength_only_a_power_charge()
    {
        using DaggerfallSession session = FreshSession();
        IDaggerfallPlayerAttacks attacks = session;
        Average(session);

        Stat(session, attacks.WeaponSkill, 100);
        Assert.Equal(1d - Tuning.SkillAt100, attacks.TimeScale("fastA"), 6);

        Stat(session, DaggerfallMechanicsIds.Strength.Value, 75);
        Assert.Equal(1d - Tuning.SkillAt100, attacks.TimeScale("fastA"), 6);
        Assert.Equal(1d - Tuning.SkillAt100 - 25 * Tuning.StrengthPerPointAbove50, attacks.TimeScale(DaggerfallAttackIds.PowerIn), 6);
    }

    [Fact]
    public void A_winded_character_is_slower_and_no_attack_runs_below_the_minimum()
    {
        using DaggerfallSession session = FreshSession();
        IDaggerfallPlayerAttacks attacks = session;
        Average(session);
        Track stamina = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value));

        stamina.SetCurrent(Tuning.WindedStamina);
        Assert.True(attacks.Winded);
        Assert.Equal(Tuning.WindedTimeScale, attacks.TimeScale("fastA"), 6);

        stamina.SetCurrent(stamina.MaximumValue);
        Stat(session, DaggerfallMechanicsIds.Speed.Value, 100);
        Stat(session, attacks.WeaponSkill, 100);
        Stat(session, DaggerfallMechanicsIds.Strength.Value, 100);
        Assert.Equal(Math.Max(Tuning.MinimumTimeScale, 1d - .2d - .15d - .2d), attacks.TimeScale(DaggerfallAttackIds.PowerIn), 6);
    }
}
