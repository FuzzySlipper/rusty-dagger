using WorldRpg.Rulesets.Daggerfall.Facts;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallPlaytestState"/> built without the session: what it keeps of combat and use, with the update's
/// generation and step as plain inputs. The owners its observation reads are not reached on these paths and are passed
/// as null; the observation and target JSON in a real place are <c>PlaytestInspectionTests</c>' end-to-end cases.
/// </summary>
public sealed class DaggerfallPlaytestStateTests
{
    private ulong? _generation, _step;

    [Fact]
    public void Combat_facts_become_the_last_combat_event_numbered_in_order_and_other_facts_do_not()
    {
        DaggerfallPlaytestState playtest = Playtest();
        playtest.ObserveFact(new FatigueAppliedFact(1, 2, 3, 3, 1, 1));
        Assert.Null(playtest.LastCombatEvent);

        // Before any admitted update the step and generation read as 0.
        playtest.ObserveFact(new PlayerAttackStartedFact(0, 0));
        Assert.Equal(new DaggerfallPlaytestCombatEvent(1, "PlayerAttackStarted", 0, 0), playtest.LastCombatEvent);

        (_generation, _step) = (4, 37);
        playtest.ObserveFact(new EnemyAttackTelegraphFact(2, 1, 0.45, 4, 37));
        Assert.Equal(new DaggerfallPlaytestCombatEvent(2, "EnemyAttackTelegraph:0.45", 37, 4), playtest.LastCombatEvent);
        playtest.ObserveFact(new AttackWhiffedFact(1, null, false, 4, 37));
        Assert.Equal(new DaggerfallPlaytestCombatEvent(3, "AttackWhiffed", 37, 4), playtest.LastCombatEvent);
        playtest.ObserveFact(new FatigueAppliedFact(1, 2, 3, 3, 4, 37));
        Assert.Equal(3UL, playtest.LastCombatEvent!.Value.Sequence);
    }

    [Fact]
    public void A_use_notes_its_step_and_then_its_refusal_and_the_next_use_clears_it()
    {
        DaggerfallPlaytestState playtest = Playtest();
        Assert.Null(playtest.LastUseStep);
        playtest.ObserveUse(12);
        playtest.ObserveUseRefusal("locked");
        Assert.Equal((12UL, "locked"), (playtest.LastUseStep!.Value, playtest.LastUseRefusal));
        playtest.ObserveUse(20);
        Assert.Equal(20UL, playtest.LastUseStep);
        Assert.Null(playtest.LastUseRefusal);
    }

    [Fact]
    public void An_unknown_action_is_answered_without_reading_the_game()
    {
        DaggerfallPlaytestState playtest = Playtest();
        Assert.Equal(["forward", "back", "left", "right", "attack", "use", "jump", "toggle-weapon", "inventory", "character", "menu"], playtest.Actions);
        var unknown = playtest.InspectAction("dance");
        Assert.False(unknown.Available);
        Assert.Equal("unknown-action", unknown.Reason);
    }

    private DaggerfallPlaytestState Playtest() => new(null!, null!, null!, null!, null!, null!, null!, null!, null!,
        () => _generation, () => _step, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, 2.5, () => null);
}
