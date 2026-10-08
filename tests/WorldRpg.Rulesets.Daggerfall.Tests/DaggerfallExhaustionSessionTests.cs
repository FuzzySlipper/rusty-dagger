using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The donor's fatigue rules at the session: no passive real-time recovery, the per-minute Swimming
/// roll, and the collapse when the pool empties (PlayerEntity.PlayerEntity_OnExhausted).
/// </summary>
public sealed class DaggerfallExhaustionSessionTests
{
    private static readonly TrackId Health = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private static readonly TrackId Fatigue = TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value);
    private static readonly TrackId Magicka = TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value);

    [Fact]
    public void Idle_real_time_returns_no_fatigue()
    {
        using DaggerfallSession session = FreshSession();
        MoveAwayFromEnemies(session);
        Track fatigue = session.State.Actors.Player.Stats.GetTrack(Fatigue);
        fatigue.SetCurrent(100, clamp: true);

        // Two real seconds of admitted play: a passive regeneration would have returned fatigue by now.
        for (ulong step = 1; step <= 120; step++)
        {
            session.Update(new ProductUpdate(OuterUpdate(step), []));
            Assert.True(fatigue.Current <= 100d, $"idle play returned fatigue: {fatigue.Current}");
        }
    }

    [Fact]
    public void A_safe_collapse_advances_one_hour_recovers_one_rest_hour_tallies_medical_and_fires_once()
    {
        using DaggerfallSession session = FreshSession();
        MoveAwayFromEnemies(session);
        StatsComponent stats = session.State.Actors.Player.Stats;
        Track health = stats.GetTrack(Health), fatigue = stats.GetTrack(Fatigue), magicka = stats.GetTrack(Magicka);
        health.SetCurrent(health.MaximumValue - 10d, clamp: true);
        magicka.SetCurrent(magicka.MaximumValue - 10d, clamp: true);
        fatigue.SetCurrent(0);
        double healthBefore = health.Current, magickaBefore = magicka.Current;
        int medicalBefore = session.State.Progression.SkillUses.TryGetValue("medical", out int medical) ? medical : 0;
        long secondsBefore = CalendarSeconds(session);
        (int healthRate, int fatigueRate, int spellRate) = DaggerfallRestPolicy.RecoveryRates(
            stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).ValueInt,
            stats.GetStat(StatId.Parse("medical")).ValueInt,
            stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value)).ValueInt,
            stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value)).ValueInt,
            stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value)).ValueInt,
            rapidHealing: false, noRegeneration: false);

        session.ResolveExhaustion();

        Assert.Equal(secondsBefore + DaggerfallRestPolicy.SecondsPerRestHour, CalendarSeconds(session));
        Assert.Equal(Math.Min(health.MaximumValue, healthBefore + healthRate), health.Current);
        Assert.Equal(fatigueRate, fatigue.Current);
        Assert.Equal(Math.Min(magicka.MaximumValue, magickaBefore + spellRate), magicka.Current);
        Assert.Equal(medicalBefore + 1, session.State.Progression.SkillUses["medical"]);
        Assert.False(session.State.Actors.Player.IsDefeated);
        Assert.Contains("You awaken one hour later", session.Presentation.LastOutcome, StringComparison.Ordinal);

        // The recovered pool is not empty, so the same collapse does not repeat.
        session.ResolveExhaustion();
        Assert.Equal(secondsBefore + DaggerfallRestPolicy.SecondsPerRestHour, CalendarSeconds(session));
        Assert.Equal(medicalBefore + 1, session.State.Progression.SkillUses["medical"]);
    }

    [Fact]
    public void A_swing_that_empties_the_pool_collapses_through_the_admitted_update()
    {
        using DaggerfallSession session = FreshSession();
        MoveAwayFromEnemies(session);
        Track fatigue = session.State.Actors.Player.Stats.GetTrack(Fatigue);
        fatigue.SetCurrent(0);
        long secondsBefore = CalendarSeconds(session);

        session.Update(new ProductUpdate(OuterUpdate(1), []));

        Assert.True(CalendarSeconds(session) >= secondsBefore + DaggerfallRestPolicy.SecondsPerRestHour);
        Assert.True(fatigue.Current > 0d);
        Assert.Contains("completely exhausted", session.Presentation.LastOutcome, StringComparison.Ordinal);
    }

    [Fact]
    public void Collapsing_with_an_enemy_near_kills_the_player_and_keeps_the_donor_text_on_death()
    {
        // The fixture player starts beside the dungeon's live enemy group, where rest is refused.
        using DaggerfallSession session = FreshSession();
        session.State.Actors.Player.Stats.GetTrack(Fatigue).SetCurrent(0);
        long secondsBefore = CalendarSeconds(session);

        session.ResolveExhaustion();

        Assert.True(session.State.Actors.Player.IsDefeated);
        Assert.Equal(0d, session.State.Actors.Player.Stats.GetTrack(Health).Current);
        Assert.Equal(secondsBefore, CalendarSeconds(session));
        Assert.Equal(ProductMode.Dead, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Dead);
        Assert.Contains("never awaken", session.Presentation.LastOutcome, StringComparison.Ordinal);
    }

    [Fact]
    public void Collapsing_in_water_kills_the_player_with_the_watery_grave_text()
    {
        using DaggerfallSession session = FreshSession();
        MoveAwayFromEnemies(session);
        session.State.Swimming.Restore(new DaggerfallSwimmingSave(Swimming: true, HeadSubmerged: false, CurrentBreath: 10, BreathSeconds: 0d));
        session.State.Actors.Player.Stats.GetTrack(Fatigue).SetCurrent(0);
        long secondsBefore = CalendarSeconds(session);

        session.ResolveExhaustion();

        Assert.True(session.State.Actors.Player.IsDefeated);
        Assert.Equal(0d, session.State.Actors.Player.Stats.GetTrack(Health).Current);
        Assert.Equal(secondsBefore, CalendarSeconds(session));
        Assert.Contains("watery grave", session.Presentation.LastOutcome, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("breton", 0, true)]
    [InlineData("breton", 100, false)]
    [InlineData("argonian", 0, false)]
    public void A_swimming_minute_charges_swimming_fatigue_only_on_a_failed_swimming_roll_and_never_for_an_argonian(
        string race, int swimming, bool charged)
    {
        using DaggerfallSession session = FreshSession();
        session.State.Character.BeginChoices();
        session.State.Character.ReplacePending(session.State.Character.Pending! with { RaceId = race, FaceIndex = 0 });
        session.State.Character.CommitChoices();
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("swimming")).BaseValue = swimming;

        for (long minute = 0; minute < 20; minute++)
            Assert.Equal(charged, session.SwimmingFatigueApplies(minute));
    }

    private static void MoveAwayFromEnemies(DaggerfallSession session) =>
        session.State.PlayerControl.MoveTo(new WorldPoint(1000f, 1f, 1000f).ToVector());

    private static long CalendarSeconds(DaggerfallSession session)
    {
        DaggerfallCalendarSave save = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;
        return new DaggerfallCalendar(save.Year, save.Month, save.Day, save.Hour, save.Minute, save.Second).ToAbsoluteSeconds();
    }
}
