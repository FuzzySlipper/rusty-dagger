using System.Numerics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The city guards a quest calls, built without the session over the guard fixture's actors, people, crime and law.</summary>
public sealed class DaggerfallQuestGuardsTests
{
    private static DaggerfallQuestGuards Guards(DaggerfallSession session) => new(session.State.Actors, session.State.Npcs, session.State.PlayerControl,
        session.State.Crime, session.DefinitionsByActor, session.Sites, new DaggerfallWorldTime(DaggerfallCalendar.Start, 0d, 12d),
        session.Crimes, session.Law, session.Civilians, DaggerfallTuning.Defaults.QuestSpawning, new(5, 5, 2, 2, 12.8f, 51.2f, 3.2));

    [Fact]
    public void An_immediate_call_converts_a_watchman_standing_by_and_completes()
    {
        using QuestGuardSpawnTests.Fixture fixture = new(true);
        long watchman = fixture.Civilian("guard");
        DaggerfallQuestGuardSpawnState? state = Guards(fixture.Session).Spawn("guards/owner", immediate: true, 0, null);

        Assert.Equal(DaggerfallQuestGuardSpawnOutcome.Complete, state?.Outcome);
        Assert.Equal(1, state!.SpawnedCount);
        Assert.False(fixture.Session.State.Actors.TryGet(watchman, out _));
        Assert.Single(fixture.Session.DefinitionsByActor, pair => pair.Value.MobileId == 146);
    }

    [Fact]
    public void A_report_nobody_saw_brings_no_guards_and_the_wilderness_has_none_to_call()
    {
        using QuestGuardSpawnTests.Fixture fixture = new(false);
        Assert.Equal(DaggerfallQuestGuardSpawnOutcome.NoWitness, Guards(fixture.Session).Spawn("guards/unseen", immediate: false, 0, null)?.Outcome);

        fixture.Session.State.PlayerControl.MoveTo(new Vector3(10000, 2, 10000));
        Assert.Contains("wilderness", Assert.Throws<NotSupportedException>(() =>
            Guards(fixture.Session).Spawn("guards/wilderness", immediate: true, 0, null)).Message);
        Assert.DoesNotContain(fixture.Session.DefinitionsByActor, pair => pair.Value.MobileId == 146);
    }
}
