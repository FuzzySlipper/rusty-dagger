using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// How the gsplat townspeople draw: lit by the world's lights, keeping <paramref name="LightFloor"/> of their paint
/// however dark the light, on a screen-pixel grid of <paramref name="PixelGrid"/> pixels fixed to the figure (0 draws
/// them smooth), a block drawn where they cover <paramref name="PixelCoverage"/> of it, snapped to their own palette or
/// not (<paramref name="OwnPalette"/>, which needs a grid), posed <paramref name="PoseRate"/> times a second (0 every
/// frame), crossfading <paramref name="CrossfadeSeconds"/> between walk and idle, and casting into the sun's shadow as
/// <paramref name="ShadowCaster"/>: an ellipsoid a joint, their own Gaussians (their silhouette), or nothing. A walker
/// drawn in a long skirt walks a skirt walk: <paramref name="SkirtsSlow"/> walks it at that clip's own pace, slower
/// than the other walkers ("slow"), and otherwise plays the clip faster to keep the walkers' pace ("clip"). Each walker's
/// clips play, and it walks, at a rate up to <paramref name="GaitJitter"/> either side of 1, by its seed. A walker drawn as
/// a character with eyes blinks as <paramref name="Blinks"/> says, on its own seeded clock.
/// people.splat-look, people.splat-shadow, people.splat-pace and people.splat-blinks change them for a run.
/// </summary>
internal sealed record DaggerfallSplatPeopleTuning(uint PixelGrid, float PixelCoverage, bool OwnPalette, float PoseRate, float LightFloor,
    float CrossfadeSeconds, SplatShadowCaster ShadowCaster, bool SkirtsSlow, float GaitJitter, SplatBlinks Blinks)
{
    internal static DaggerfallSplatPeopleTuning Default { get; } = new(3, .5F, false, 0F, .15F, .25F, SplatShadowCaster.Ellipsoids, SkirtsSlow: true, GaitJitter: .06F,
        Blinks: new(2F, 6F, .04F, .07F, .06F));

    /// <summary>Whether long skirts walk slow, by the tuning and command name: slow or clip.</summary>
    internal static bool? Slow(string pace) => pace switch
    {
        "slow" => true,
        "clip" => false,
        _ => null,
    };

    /// <summary>A shadow caster by its tuning and command name: none, ellipsoids, core or splats.</summary>
    internal static SplatShadowCaster? Caster(string name) => name switch
    {
        "none" => SplatShadowCaster.None,
        "ellipsoids" => SplatShadowCaster.Ellipsoids,
        "core" => SplatShadowCaster.Core,
        "splats" => SplatShadowCaster.Splats,
        _ => null,
    };

    internal DaggerfallSplatPeopleTuning Validate()
    {
        if (PixelGrid > 16 || !(PixelCoverage is > 0 and <= 1) || OwnPalette && PixelGrid == 0 || !float.IsFinite(PoseRate) || PoseRate < 0
            || !(LightFloor is >= 0 and <= 1) || !float.IsFinite(CrossfadeSeconds) || CrossfadeSeconds < 0 || !(GaitJitter is >= 0 and < .5F))
            throw new ArgumentException($"The gsplat people draw on a pixel grid of 0 to 16, a coverage above 0 to 1, their own palette only on a grid, " +
                $"a pose rate and crossfade of 0 or more, a light floor of 0 to 1 and a gait jitter of 0 to under 0.5, not {this}.");
        Blinks.Validate();
        return this;
    }

    internal static DaggerfallSplatPeopleTuning Read(JsonElement e) => new DaggerfallSplatPeopleTuning(
        e.GetProperty("pixelGrid").GetUInt32(), e.GetProperty("pixelCoverage").GetSingle(), e.GetProperty("ownPalette").GetBoolean(),
        e.GetProperty("poseRate").GetSingle(), e.GetProperty("lightFloor").GetSingle(), e.GetProperty("crossfadeSeconds").GetSingle(),
        Caster(e.GetProperty("shadowCaster").GetString()!) ?? throw new ArgumentException(
            $"The gsplat people cast shadows as none, ellipsoids, core or splats, not '{e.GetProperty("shadowCaster").GetString()}'."),
        Slow(e.GetProperty("skirtedPace").GetString()!) ?? throw new ArgumentException(
            $"Long-skirted gsplat people walk slow or clip, not '{e.GetProperty("skirtedPace").GetString()}'."),
        e.GetProperty("gaitJitter").GetSingle(), ReadBlinks(e.GetProperty("blinks"))).Validate();

    private static SplatBlinks ReadBlinks(JsonElement e)
    {
        JsonElement interval = e.GetProperty("intervalSeconds");
        if (interval.GetArrayLength() != 2) throw new ArgumentException($"The gsplat people's blinks come every [least, most] seconds, not {interval}.");
        return new(interval[0].GetSingle(), interval[1].GetSingle(), e.GetProperty("closingSeconds").GetSingle(), e.GetProperty("closedSeconds").GetSingle(),
            e.GetProperty("openingSeconds").GetSingle());
    }
}
