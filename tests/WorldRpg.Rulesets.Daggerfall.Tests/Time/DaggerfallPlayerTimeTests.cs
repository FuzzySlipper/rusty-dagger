using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Player time's owner on its own, with no session: action-driven play holds the world while nothing needs time,
/// runs it while play moves or acts or a presentation plays, and keeps a world action asked for between steps.
/// </summary>
public sealed class DaggerfallPlayerTimeTests
{
    private static readonly DaggerfallPlayerTimeDemand Idle = new(Presentation: false, Playing: true, Moving: false, Acting: false);

    [Fact]
    public void Action_driven_play_holds_an_idle_world_and_runs_it_while_the_player_moves_or_acts()
    {
        GameplayTimeFake engine = new();
        DaggerfallPlayerTime time = new(engine);

        Assert.True(time.Update(actionDriven: true, Idle, 1d));
        Assert.True(time.Held);
        Assert.False(time.Update(actionDriven: true, Idle, 1d));
        Assert.True(time.Update(actionDriven: true, Idle with { Moving = true }, 1d));
        Assert.False(time.Held);
        Assert.False(time.Update(actionDriven: true, Idle with { Acting = true }, 1d));
        Assert.Equal(new[] { 0d, 1d }, engine.Rates);
    }

    [Fact]
    public void A_presentation_runs_the_world_though_play_is_closed_and_ordinary_play_never_holds_it()
    {
        GameplayTimeFake engine = new();
        DaggerfallPlayerTime time = new(engine);

        _ = time.Update(actionDriven: true, new(Presentation: true, Playing: false, Moving: false, Acting: false), 1d);
        Assert.False(time.Held);
        _ = time.Update(actionDriven: true, new(Presentation: false, Playing: false, Moving: true, Acting: true), 1d);
        Assert.True(time.Held);
        Assert.True(time.Update(actionDriven: false, Idle, .25));
        Assert.False(time.Held);
        Assert.Equal(new[] { 1d, 0d, .25 }, engine.Rates);
    }

    [Fact]
    public void A_world_action_asked_for_between_steps_waits_until_cleared()
    {
        DaggerfallPlayerTime time = new(new GameplayTimeFake());

        time.RequestWorldAction();
        Assert.True(time.WorldActionPending);
        time.ClearWorldAction();
        Assert.False(time.WorldActionPending);
    }
}
