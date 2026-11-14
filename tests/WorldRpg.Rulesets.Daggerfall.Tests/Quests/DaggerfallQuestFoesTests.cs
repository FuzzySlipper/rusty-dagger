using WorldRpg.Kit.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A quest's foes built without the session over the fixture's actors, roster and placement; the kill's facts, their
/// delivery, a detached actor's retirement and a site delta's capture and restore are delegates the test watches.
/// </summary>
public sealed class DaggerfallQuestFoesTests
{
    private sealed class Watched
    {
        internal readonly List<DamageResult> Kills = [];
        internal readonly List<long> Retired = [];
        internal int Deliveries;
    }

    private static DaggerfallQuestFoes Foes(DaggerfallSession session, Watched watched) => new(session.State.Quests, session.State.Actors,
        session.State.PlayerControl, session.State.Travel, session.Roster, session.ActorPlacement, session.EnemyBehavior, session.Vitality, session.Sites,
        DaggerfallTuning.Defaults.QuestSpawning, DaggerfallTuning.Defaults.EnemyBehavior, watched.Kills.Add, () => watched.Deliveries++,
        id => { watched.Retired.Add(id); return false; },
        _ => throw new InvalidOperationException("No foe is kept in an unloaded site."),
        (_, _) => throw new InvalidOperationException("No foe is kept in an unloaded site."));

    [Fact]
    public void A_quests_kill_of_a_live_foe_is_harm_handed_back_and_a_foe_not_yet_placed_keeps_its_command()
    {
        using SanguineRoseSessionTests.Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        Watched watched = new();
        DaggerfallQuestFoes foes = Foes(session, watched);

        Assert.Equal(session.DefinitionsByActor[fixture.Enemy].Team, foes.Team(fixture.Enemy));
        Assert.Null(foes.Team(987654321));
        Assert.False(foes.Restrained(fixture.Enemy));

        Assert.True(foes.Command(fixture.Enemy, remove: false));
        DamageResult kill = Assert.Single(watched.Kills);
        Assert.True(kill.Defeated);
        Assert.Equal(session.State.Actors.Get(fixture.Enemy).Actor.Entity, kill.Target.Entity);

        Assert.False(foes.Command(987654321, remove: false));
        Assert.False(foes.Command(987654321, remove: true));
        Assert.Equal([987654321L], watched.Retired);
        Assert.Equal(0, watched.Deliveries);
    }

    [Fact]
    public void Clearing_all_enemies_removes_the_monsters()
    {
        using SanguineRoseSessionTests.Fixture fixture = new();
        DaggerfallQuestFoes foes = Foes(fixture.Session, new());
        Assert.True(fixture.Session.State.Actors.TryGet(fixture.Enemy, out _));

        foes.CommandAllEnemies(clear: true);

        Assert.False(fixture.Session.State.Actors.TryGet(fixture.Enemy, out _));
    }
}
