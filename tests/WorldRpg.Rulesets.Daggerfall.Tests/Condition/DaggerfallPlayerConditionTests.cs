using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Condition's owner on its own, with no session: the donor's Swimming roll, when the pool empties, how a
/// collapse ends and the lines it keeps (PlayerEntity.PlayerEntity_OnExhausted).
/// </summary>
public sealed class DaggerfallPlayerConditionTests
{
    private static readonly TrackId Health = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private static readonly TrackId Fatigue = TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value);
    private static readonly TrackId Magicka = TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value);
    private static readonly DaggerfallTextKey InWater = new(DaggerfallTextKind.Internal, "exhaustedInWater");
    private static readonly DaggerfallTextKey NearEnemies = new(DaggerfallTextKind.Resource, "1072");
    private static readonly DaggerfallTextKey Safely = new(DaggerfallTextKind.Resource, "1071");

    [Theory]
    [InlineData("breton", 0, 1, true)]
    [InlineData("breton", 50, 51, true)]
    [InlineData("breton", 50, 50, false)]
    [InlineData("breton", 100, 100, false)]
    public void A_swimming_minute_charges_fatigue_on_a_failed_swimming_roll_keyed_by_the_minute(string race, int swimming, long roll, bool charged)
    {
        FixedRandom random = FixedRandom.Create(roll);
        StatsComponent stats = Stats(health: 50d, fatigue: 50d, swimming: swimming);

        Assert.Equal(charged, Condition(random, seed: 7).SwimmingFatigueApplies(race, stats, minute: 12));
        Assert.Equal([new KeyedRngRequest(7, "daggerfall.swimming-fatigue.v1", "minute:12", 1, 100)], random.Requests);
    }

    [Fact]
    public void An_argonian_never_pays_swimming_fatigue_and_draws_nothing()
    {
        FixedRandom random = FixedRandom.Create(1);

        Assert.False(Condition(random).SwimmingFatigueApplies("argonian", Stats(health: 50d, fatigue: 50d, swimming: 0), minute: 12));
        Assert.Empty(random.Requests);
    }

    [Theory]
    [InlineData(10d, 0d, true)]
    [InlineData(10d, 1d, false)]
    [InlineData(0d, 0d, false)]
    public void Only_a_living_player_with_an_empty_pool_is_exhausted(double health, double fatigue, bool exhausted) =>
        Assert.Equal(exhausted, Condition(FixedRandom.Create(1)).Exhausted(Stats(health, fatigue, swimming: 0)));

    [Theory]
    [InlineData(true, "Into a watery grave.")]
    [InlineData(false, "Struck down where you fell.")]
    public void A_fatal_collapse_kills_through_the_health_boundary_and_keeps_the_donors_line(bool inWater, string expected)
    {
        using ActorsState actors = new();
        StatsComponent stats = Stats(health: 40d, fatigue: 0d, swimming: 0);
        PlayerActorState player = actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health", DaggerActorFactory.PlayerCapabilities);
        DaggerfallPlayerCondition condition = Condition(FixedRandom.Create(1));

        (DamageResult death, string line) = condition.CollapseFatally(player.Actor,
            inWater, Text((InWater, "Into a watery grave."), (NearEnemies, "Struck down where you fell.")));

        Assert.True(death.Defeated);
        Assert.Equal(0d, stats.GetTrack(Health).Current);
        Assert.Equal(expected, line);
        Assert.Equal(expected, condition.ExhaustionDeathLine);
        condition.ClearExhaustionDeathLine();
        Assert.Null(condition.ExhaustionDeathLine);
    }

    [Fact]
    public void A_collapse_line_the_pack_does_not_carry_is_a_plain_report()
    {
        using ActorsState actors = new();
        PlayerActorState player = actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"),
            Stats(health: 40d, fatigue: 0d, swimming: 0), "health", DaggerActorFactory.PlayerCapabilities);
        DaggerfallPlayerCondition condition = Condition(FixedRandom.Create(1));

        Assert.Equal("You drop to the ground, completely exhausted.", condition.CollapseFatally(player.Actor, inWater: true, Text()).Line);
        Assert.Equal("You drop to the ground, completely exhausted.", condition.AwakeningLine(Text()));
        Assert.Equal("You awaken.", condition.AwakeningLine(Text((Safely, "You awaken."))));
    }

    [Fact]
    public void A_safe_collapse_recovers_one_rest_hour_with_fatigue_at_a_camps_share()
    {
        StatsComponent stats = Stats(health: 40d, fatigue: 0d, swimming: 0, magicka: 30d);
        DaggerfallRestTuning rest = new(InnFatigueMultiplier: 4);
        (int healthRate, int fatigueRate, int spellRate) = DaggerfallRestPolicy.RecoveryRates(50, 20, 100, 100, 100,
            rapidHealing: false, noRegeneration: false, fatigueDivisor: rest.InnFatigueMultiplier);

        Assert.True(Condition(FixedRandom.Create(1), rest).RecoverFromCollapse(stats, rapidHealing: false, noRegeneration: false));

        Assert.Equal((40d + healthRate, (double)fatigueRate, 30d + spellRate),
            (stats.GetTrack(Health).Current, stats.GetTrack(Fatigue).Current, stats.GetTrack(Magicka).Current));
    }

    [Fact]
    public void Recovery_never_resurrects_a_player_the_hour_ended()
    {
        StatsComponent stats = Stats(health: 0d, fatigue: 0d, swimming: 0, magicka: 30d);

        Assert.False(Condition(FixedRandom.Create(1)).RecoverFromCollapse(stats, rapidHealing: false, noRegeneration: false));

        Assert.Equal((0d, 0d, 30d), (stats.GetTrack(Health).Current, stats.GetTrack(Fatigue).Current, stats.GetTrack(Magicka).Current));
    }

    private static DaggerfallPlayerCondition Condition(FixedRandom random, DaggerfallRestTuning? rest = null, ulong seed = 0) =>
        new(new DaggerfallVitalityConsequences(new CombatResolution()), DaggerfallStaminaTuning.Default, rest ?? DaggerfallRestTuning.Default,
            (IRandomService)(object)random, seed);

    private static DaggerfallTextResolver Text(params (DaggerfallTextKey Key, string Text)[] lines) => new(new DaggerfallTextSet(
        lines.ToDictionary(line => line.Key, line => new DaggerfallTextValue(line.Key, "test", "en", 0, 0, line.Text.Length, 1,
            DaggerfallTextState.Read, string.Empty, [], [new(DaggerfallTextCode.Text, line.Text, null, null)])), []));

    private static StatsComponent Stats(double health, double fatigue, int swimming, double magicka = 100d)
    {
        StatsComponent stats = new();
        Track(stats, DaggerfallMechanicsIds.HealthMaximum.Value, DaggerfallMechanicsIds.Health.Value, health);
        Track(stats, DaggerfallMechanicsIds.FatigueMaximum.Value, DaggerfallMechanicsIds.Fatigue.Value, fatigue);
        Track(stats, DaggerfallMechanicsIds.MagickaMaximum.Value, DaggerfallMechanicsIds.Magicka.Value, magicka);
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value), new Stat(50d));
        stats.AddStat(StatId.Parse("medical"), new Stat(20d));
        stats.AddStat(StatId.Parse("swimming"), new Stat(swimming));
        return stats;
    }

    private static void Track(StatsComponent stats, string maximum, string track, double current)
    {
        Stat max = new(100d);
        stats.AddStat(StatId.Parse(maximum), max);
        stats.AddTrack(TrackId.Parse(track), new Track(max, current, 0d));
    }

    /// <summary>The Engine's random service answering every keyed draw with one value, recording what it was asked.</summary>
    private class FixedRandom : DispatchProxy
    {
        private long _roll;
        internal List<KeyedRngRequest> Requests { get; } = [];

        internal static FixedRandom Create(long roll)
        {
            var random = (FixedRandom)(object)Create<IRandomService, FixedRandom>();
            random._roll = roll;
            return random;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            Requests.Add((KeyedRngRequest)arguments![0]!);
            return new KeyedRngReceipt(_roll);
        }
    }
}
