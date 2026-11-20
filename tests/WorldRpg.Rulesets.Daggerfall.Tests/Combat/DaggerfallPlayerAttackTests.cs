using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The player's attack around its clock, built without the session: the pace it gives the clock and when it retires a press.</summary>
public sealed class DaggerfallPlayerAttackTests
{
    private static DaggerfallPlayerAttack Attack(DaggerfallCombatFixture f, Func<bool> drawn, DaggerfallInputRefusals? refusals = null) => new(DaggerfallAttackTuning.Default, f.Rules, f.Actors,
        new PlayerControlState(new WorldPoint(0f, 0f, 0f), 0f, 0f), () => default, drawn,
        new DaggerfallEffectLifecycle(f.Actors, new DaggerfallEffectCatalog([])), f.Rules.Attacks, null!, null!, null!, () => null, null!, null!,
        f.Definitions, f.Facts, _ => { }, _ => { }, refusals ?? new());

    private static void Stat(DaggerfallCombatFixture f, string stat, int value)
    {
        StatsComponent stats = f.Actors.Player.Stats;
        if (stats.TryGetStat(StatId.Parse(stat), out Stat? held)) held.BaseValue = value;
        else stats.AddStat(StatId.Parse(stat), new Stat(value));
    }

    [Fact]
    public void Speed_and_skill_quicken_the_clock_and_skill_lengthens_the_chain()
    {
        using DaggerfallCombatFixture f = new("rat");
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
    public void A_press_is_retired_rather_than_started_when_the_weapon_is_sheathed_and_says_so()
    {
        using DaggerfallCombatFixture f = new("rat");
        bool drawn = false;
        DaggerfallInputRefusals refusals = new();
        DaggerfallPlayerAttack attack = Attack(f, () => drawn, refusals);
        attack.Clock.Press();
        Assert.True(attack.Clock.Busy);

        attack.Step(1d / 60d, 1, 1);

        Assert.False(attack.Clock.Busy);
        Assert.Empty(attack.TakeImpacts());
        Assert.Equal("Attack dropped: weapon sheathed", refusals.Latest.ToString());
    }

    [Fact]
    public void Admissions_refusal_of_the_players_attack_is_named_and_anothers_or_a_material_turn_is_not()
    {
        using DaggerfallCombatFixture f = new("rat");
        DaggerfallInputRefusals refusals = new();
        DaggerfallPlayerAttack attack = Attack(f, () => true, refusals);
        attack.ObserveRejection(new AttackRejectedFact(AttackRejection.Cooldown, 99));
        attack.ObserveRejection(new AttackRejectedFact(AttackRejection.InsufficientWeaponMaterial));
        Assert.Null(refusals.Latest);
        attack.ObserveRejection(new AttackRejectedFact(AttackRejection.Winded));
        Assert.Equal("Attack refused: winded", refusals.Latest.ToString());
        attack.ObserveRejection(new AttackRejectedFact(AttackRejection.EmptyQuiver, DaggerfallActorIdentity.PlayerEntityId));
        Assert.Equal("Attack refused: no arrows", refusals.Latest.ToString());
        attack.ObserveRejection(new AttackRejectedFact(AttackRejection.Cooldown, DaggerfallActorIdentity.PlayerEntityId));
        Assert.Equal("Attack refused: recovering, 0.0 s left", refusals.Latest.ToString());
    }
}
