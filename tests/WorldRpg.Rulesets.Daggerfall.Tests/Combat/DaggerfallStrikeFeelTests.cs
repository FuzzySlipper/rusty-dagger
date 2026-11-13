using System.Reflection;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The blow's weight on screen, built without the session.</summary>
public sealed class DaggerfallStrikeFeelTests
{
    private class CameraView : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(ICameraViewService.CreateCamera) => new Camera(new CameraHandle(1), () => { }),
            _ => null,
        };
    }

    private static readonly DaggerfallStrikeTuning Tuning = DaggerfallStrikeTuning.Default;

    private static DaggerfallStrikeFeel Feel(Func<double> rate) => new(Tuning,
        new FirstPersonCameraSystem(DispatchProxy.Create<ICameraViewService, CameraView>(), new PlayerControlState(new WorldPoint(0f, 0f, 0f), 0f, 0f),
            DaggerfallTuning.Defaults.Camera), rate);

    private static ActorStruckFact Struck(bool heavy) => new(2, default, heavy, 1, 1);

    [Fact]
    public void A_blow_catches_world_time_for_its_steps_a_heavy_one_longer()
    {
        DaggerfallStrikeFeel feel = Feel(() => 1d);
        Assert.Equal(1d, feel.TimeRate);

        feel.Feel(Struck(heavy: true));
        int caught = 0;
        while (feel.HitStopped) { Assert.Equal(Tuning.HitStopRate, feel.TimeRate); feel.Settle(1d / 60d); caught++; }
        Assert.Equal(Tuning.HeavyHitStopSteps, caught);

        feel.Feel(Struck(heavy: false));
        caught = 0;
        while (feel.HitStopped) { feel.Settle(1d / 60d); caught++; }
        Assert.Equal(Tuning.HitStopSteps, caught);
        Assert.Equal(1d, feel.TimeRate);
    }

    [Fact]
    public void A_world_another_owner_slowed_is_left_alone()
    {
        DaggerfallStrikeFeel feel = Feel(() => .5d);
        feel.Feel(Struck(heavy: true));
        Assert.False(feel.HitStopped);
    }
}
