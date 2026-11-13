using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The player's attack around its clock, built without the session: the pace it gives the clock and when it retires a press.</summary>
public sealed class DaggerfallPlayerAttackTests
{
    private static DaggerfallPlayerAttack Attack(DaggerCombatFixture f, Func<bool> drawn) => new(DaggerfallAttackTuning.Default, f.Rules, f.Actors,
        new PlayerControlState(new WorldPoint(0f, 0f, 0f), 0f, 0f), () => default, drawn,
        new DaggerfallEffectLifecycle(f.Actors, new DaggerfallEffectCatalog([])), f.Rules.Attacks, null!, null!, null!, () => null, null!, null!,
        f.Definitions, f.Facts, _ => { }, _ => { });

    private static void Stat(DaggerCombatFixture f, string stat, int value)
    {
        StatsComponent stats = f.Actors.Player.Stats;
        if (stats.TryGetStat(StatId.Parse(stat), out Stat? held)) held.BaseValue = value;
        else stats.AddStat(StatId.Parse(stat), new Stat(value));
    }

    [Fact]
    public void Speed_and_skill_quicken_the_clock_and_skill_lengthens_the_chain()
    {
        using DaggerCombatFixture f = new("rat");
        IDaggerfallPlayerAttacks attack = Attack(f, () => true);
        string skill = attack.WeaponSkill;
        Stat(f, DaggerfallMechanicsIds.Speed.Value, 50);
        Stat(f, skill, 0);
        double slow = attack.TimeScale(DaggerfallAttackIds.Fast[0]);
        int shortChain = attack.ChainLength;

        Stat(f, DaggerfallMechanicsIds.Speed.Value, 90);
        Stat(f, skill, 100);
        Assert.True(attack.TimeScale(DaggerfallAttackIds.Fast[0]) < slow);
        Assert.True(attack.ChainLength > shortChain);
    }

    [Fact]
    public void A_press_is_retired_rather_than_started_when_the_weapon_is_sheathed()
    {
        using DaggerCombatFixture f = new("rat");
        bool drawn = false;
        DaggerfallPlayerAttack attack = Attack(f, () => drawn);
        attack.Clock.Press();
        Assert.True(attack.Clock.Busy);

        attack.Step(1d / 60d, 1, 1);

        Assert.False(attack.Clock.Busy);
        Assert.Empty(attack.TakeImpacts());
    }
}
