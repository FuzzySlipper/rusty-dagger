using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>
/// A load's last step waits until the resource batches the Engine was preparing when the wait began are taken in,
/// measured by the resources still pending, and is not held by batches opened after it began.
/// </summary>
public sealed class DrawnSceneTests
{
    [Fact]
    public void The_wait_holds_until_the_batches_open_when_it_began_are_drawn_and_ignores_later_ones()
    {
        ResourcePreparationReadout readout = new(CompletedBatch: 6, LastBatch: 7, PendingResources: 188, FailedResources: 0);
        DrawnScene scene = new(() => readout);

        Assert.Equal(LoadingProgress.Measured(0d), scene.Await());
        readout = new(6, 7, 47, 0);
        Assert.Equal(LoadingProgress.Measured(.75d), scene.Await());
        // Ground streaming opens batch 8 meanwhile; the load waits only for 7.
        readout = new(7, 8, 30, 0);
        Assert.Equal(LoadingProgress.Finished, scene.Await());

        // The next load's wait fixes its own batches.
        Assert.False(scene.Await().Done);
        readout = new(8, 8, 0, 0);
        Assert.Equal(LoadingProgress.Finished, scene.Await());
    }

    [Fact]
    public void Nothing_being_prepared_or_a_failed_batch_does_not_hold_the_load()
    {
        Assert.Equal(LoadingProgress.Finished, new DrawnScene(() => default).Await());
        // CompletedBatch counts failed batches as done.
        Assert.Equal(LoadingProgress.Finished, new DrawnScene(() => new(3, 3, 0, 2)).Await());
    }
}
