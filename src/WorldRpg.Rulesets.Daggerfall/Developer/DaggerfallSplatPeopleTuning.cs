using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// How the gsplat townspeople draw: lit by the world's lights, keeping <paramref name="LightFloor"/> of their paint
/// however dark the light, on a screen-pixel grid of <paramref name="PixelGrid"/> pixels fixed to the figure (0 draws
/// them smooth), a block drawn where they cover <paramref name="PixelCoverage"/> of it, snapped to their own palette or
/// not (<paramref name="OwnPalette"/>, which needs a grid), posed <paramref name="PoseRate"/> times a second (0 every
/// frame), crossfading <paramref name="CrossfadeSeconds"/> between walk and idle, and casting into the sun's shadow as
/// <paramref name="ShadowCaster"/>: an ellipsoid a joint, their own Gaussians (their silhouette), or nothing. A walker
/// drawn in a long skirt walks the gentle clip: <paramref name="SkirtsSlow"/> walks it at that clip's own pace, slower
/// than the other walkers ("slow"), and otherwise plays the clip faster to keep the walkers' pace ("clip").
/// people.splat-look, people.splat-shadow and people.splat-pace change them for a run.
/// </summary>
internal sealed record DaggerfallSplatPeopleTuning(uint PixelGrid, float PixelCoverage, bool OwnPalette, float PoseRate, float LightFloor,
    float CrossfadeSeconds, SplatShadowCaster ShadowCaster, bool SkirtsSlow)
{
    internal static DaggerfallSplatPeopleTuning Default { get; } = new(3, .5F, false, 0F, .15F, .25F, SplatShadowCaster.Ellipsoids, SkirtsSlow: true);

    /// <summary>Whether long skirts walk slow, by the tuning and command name: slow or clip.</summary>
    internal static bool? Slow(string pace) => pace switch
    {
        "slow" => true,
        "clip" => false,
        _ => null,
    };

    /// <summary>A shadow caster by its tuning and command name: none, ellipsoids or splats.</summary>
    internal static SplatShadowCaster? Caster(string name) => name switch
    {
        "none" => SplatShadowCaster.None,
        "ellipsoids" => SplatShadowCaster.Ellipsoids,
        "splats" => SplatShadowCaster.Splats,
        _ => null,
    };

    internal DaggerfallSplatPeopleTuning Validate()
    {
        if (PixelGrid > 16 || !(PixelCoverage is > 0 and <= 1) || OwnPalette && PixelGrid == 0 || !float.IsFinite(PoseRate) || PoseRate < 0
            || !(LightFloor is >= 0 and <= 1) || !float.IsFinite(CrossfadeSeconds) || CrossfadeSeconds < 0)
            throw new ArgumentException($"The gsplat people draw on a pixel grid of 0 to 16, a coverage above 0 to 1, their own palette only on a grid, " +
                $"a pose rate and crossfade of 0 or more and a light floor of 0 to 1, not {this}.");
        return this;
    }

    internal static DaggerfallSplatPeopleTuning Read(JsonElement e) => new DaggerfallSplatPeopleTuning(
        e.GetProperty("pixelGrid").GetUInt32(), e.GetProperty("pixelCoverage").GetSingle(), e.GetProperty("ownPalette").GetBoolean(),
        e.GetProperty("poseRate").GetSingle(), e.GetProperty("lightFloor").GetSingle(), e.GetProperty("crossfadeSeconds").GetSingle(),
        Caster(e.GetProperty("shadowCaster").GetString()!) ?? throw new ArgumentException(
            $"The gsplat people cast shadows as none, ellipsoids or splats, not '{e.GetProperty("shadowCaster").GetString()}'."),
        Slow(e.GetProperty("skirtedPace").GetString()!) ?? throw new ArgumentException(
            $"Long-skirted gsplat people walk slow or clip, not '{e.GetProperty("skirtedPace").GetString()}'.")).Validate();
}
