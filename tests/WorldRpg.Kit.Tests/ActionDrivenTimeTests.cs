using Rusty.Engine;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class ActionDrivenTimeTests
{
    private sealed class Time : IGameplayTimeService
    {
        internal List<double> Rates { get; } = [];
        private GameplayTimeReadout _readout = new(false, 1, 0, 60);
        public GameplayTimeReadout SelectRate(GameplayTimeRateRequest request) { Rates.Add(request.Rate); return _readout = new(true, request.Rate, 0, 60); }
        public GameplayTimeReadout Advance(GameplayTimeAdvanceRequest request) => _readout;
        public GameplayTimeReadout Read() => _readout;
    }

    [Fact]
    public void A_held_world_admits_no_step_whether_or_not_action_driven_play_is_chosen()
    {
        Time time = new();
        ActionDrivenTime selection = new(time);

        selection.Update(active: false);
        selection.Update(active: true, worldHeld: true);
        Assert.False(selection.Held); // Action-driven play is not what holds it.
        selection.Update(active: false);
        selection.Select(true);
        selection.Update(active: true, slowedTo: .5d, worldHeld: true);
        Assert.True(selection.Held);
        selection.Update(active: true, slowedTo: .5d);

        Assert.Equal([1d, 0d, 1d, 0d, .5d], time.Rates);
    }
}
