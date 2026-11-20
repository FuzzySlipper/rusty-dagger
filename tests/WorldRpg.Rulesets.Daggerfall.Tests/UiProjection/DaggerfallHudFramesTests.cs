using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallHudFrames"/> built without the session over <see cref="SocietyStage"/>'s player and effects. The
/// frame itself reads every panel; its publication is exercised end to end by the session's HUD snapshot tests.
/// </summary>
public sealed class DaggerfallHudFramesTests
{
    [Fact]
    public void The_device_pressed_on_is_the_latest_that_moved_past_the_deadzone()
    {
        using SocietyStage stage = new();
        DaggerfallHudFrames frames = Frames(stage);
        Assert.Equal("keyboard", frames.InputDevice);

        frames.ObserveInputDevice([new ProductInputEvent { Kind = InputEventKind.ControllerAxis, X = 0.1f }]);
        Assert.Equal("keyboard", frames.InputDevice);
        frames.ObserveInputDevice([new ProductInputEvent { Kind = InputEventKind.ControllerAxis, X = 0.5f }]);
        Assert.Equal("controller", frames.InputDevice);
        frames.ObserveInputDevice([]);
        Assert.Equal("controller", frames.InputDevice);
        frames.ObserveInputDevice([new ProductInputEvent { Kind = InputEventKind.Key, Edge = InputEdge.Pressed }]);
        Assert.Equal("keyboard", frames.InputDevice);
    }

    [Fact]
    public void A_player_with_no_effects_lists_none()
    {
        using SocietyStage stage = new();
        Assert.Empty(Frames(stage).ReadActiveEffects());
    }

    private static DaggerfallHudFrames Frames(SocietyStage stage) => new(null!, stage.Actors, new PresentationState("Ready", 5d), stage.Player,
        null!, null!, null!, null!, null!, stage.Definitions, null!, null!, null!, null!, stage.Effects, null!, null!, null!, null!, null!,
        null!, null!, null!, null!, null!, null!, null!, null!, null!, stage.Sites, item => $"item {item}", null, null!, stage.Law, null!,
        null!, null!, null!, null!, null!, null!, null, null!, null!, null!, null!, () => null, () => false, () => false, deadzone: 0.25f, null!);
}
