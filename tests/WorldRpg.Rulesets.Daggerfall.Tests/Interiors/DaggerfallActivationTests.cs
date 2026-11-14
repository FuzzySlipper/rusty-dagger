using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Targeting;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallActivation"/> built without the session, over the dungeon actions' stage: the module and door
/// owner composition hands it, the mode a new site resets, and a use with nothing in reach.
/// </summary>
public sealed class DaggerfallActivationTests
{
    private static readonly LookReceipt Look = new(default, default, Quaternion.Identity, -Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);

    [Fact]
    public void Before_a_module_is_composed_nothing_is_used_and_the_mode_is_grab()
    {
        using DaggerfallDungeonActionsTests.Stage stage = new([]);
        DaggerfallActivation activation = Activation(stage);

        Assert.Null(activation.Module);
        Assert.Equal(DaggerfallActivationMode.Grab, activation.Mode);
        Assert.Null(activation.Activate(Look, out string? refusal));
        Assert.Null(refusal);
        Assert.Null(activation.BashWithSwing(Look, creatureSelected: false));
        Assert.Throws<InvalidOperationException>(() => activation.ChangeMode(DaggerfallActivationMode.Steal));
    }

    [Fact]
    public void Each_sites_module_starts_in_grab_and_a_use_with_nothing_in_reach_says_so()
    {
        using DaggerfallDungeonActionsTests.Stage stage = new([]);
        DaggerfallActivation activation = Activation(stage);
        activation.Use(Module(stage), Door(stage));

        Assert.True(activation.ChangeMode(DaggerfallActivationMode.Steal));
        Assert.False(activation.ChangeMode(DaggerfallActivationMode.Steal));
        Assert.Equal(DaggerfallActivationMode.Steal, activation.Mode);
        activation.Use(Module(stage), Door(stage));
        Assert.Equal(DaggerfallActivationMode.Grab, activation.Mode);

        DaggerfallActivationOutcome outcome = Assert.IsType<DaggerfallActivationOutcome>(activation.Activate(Look, out string? refusal));
        Assert.Equal(new DaggerfallActivationOutcome(false, "No eligible target within reach."), outcome);
        Assert.Null(refusal);
        // A creature the swing selected is struck, not the door behind it; with no door in reach nothing is bashed.
        Assert.Null(activation.BashWithSwing(Look, creatureSelected: true));
        Assert.Null(activation.BashWithSwing(Look, creatureSelected: false));
    }

    private static DaggerfallActivation Activation(DaggerfallDungeonActionsTests.Stage stage) =>
        new(stage.Owner, stage.Actors, stage.Player, () => stage.Doors, useReach: 2.5d, swingReach: 2.5d);

    private static DaggerfallActivationModule Module(DaggerfallDungeonActionsTests.Stage stage) => new(
        new InteractionTargetingService(PerceptionFake.Create().Service, stage.Movement, stage.Entities),
        DaggerfallTuning.Defaults.LootInteraction,
        new DaggerfallActivationContributions(new NoCorpses(), Door(stage)));

    private static DaggerfallDoorActivationOwner Door(DaggerfallDungeonActionsTests.Stage stage) => new(stage.Doors, _ => { },
        (_, _) => throw new InvalidOperationException("No lock is forced here."), _ => null, (_, _) => null, _ => null, _ => null,
        _ => "lock", _ => { });

    private sealed class NoCorpses : IDaggerfallCorpseActivationOwner
    {
        public IEnumerable<DaggerfallActivationTarget> CorpseTargets() => [];
        public DaggerfallActivationOutcome ActivateCorpse(DaggerfallActivationSelection selection) =>
            throw new InvalidOperationException("No body lies here.");
    }
}
