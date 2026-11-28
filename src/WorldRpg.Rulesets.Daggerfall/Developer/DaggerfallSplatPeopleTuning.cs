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
/// a character with eyes blinks as <paramref name="Blinks"/> says, on its own seeded clock. Each light's diffuse reaches
/// <paramref name="DiffuseWrap"/> past the terminator, so a torch beside a painted face shades it softly instead of
/// cutting it in half with black. A character with a hybrid body (its splats over a skinned base mesh that fills what
/// they leave open and casts the shadow) draws it as <paramref name="Hybrid"/> says. These looks are the gsplat enemies'
/// too. people.splat-look, people.splat-shadow, people.splat-hybrid, people.splat-pace and people.splat-blinks change them
/// for a run.
/// </summary>
internal sealed record DaggerfallSplatPeopleTuning(uint PixelGrid, float PixelCoverage, bool OwnPalette, float PoseRate, float LightFloor,
    float CrossfadeSeconds, SplatShadowCaster ShadowCaster, bool SkirtsSlow, float GaitJitter, SplatBlinks Blinks, float DiffuseWrap = 0F,
    DaggerfallSplatHybridTuning? Hybrid = null)
{
    internal static DaggerfallSplatPeopleTuning Default { get; } = new(3, .5F, false, 0F, .15F, .25F, SplatShadowCaster.Ellipsoids, SkirtsSlow: true, GaitJitter: .06F,
        Blinks: new(2F, 6F, .04F, .07F, .06F), DiffuseWrap: .6F, Hybrid: DaggerfallSplatHybridTuning.Default);

    /// <summary>How a character's plain splat body is finished.</summary>
    internal DaggerfallSplatLook Look => new(PixelGrid, PixelCoverage, OwnPalette, PoseRate, LightFloor, ShadowCaster, DiffuseWrap);

    /// <summary>
    /// How a character's hybrid body is finished: as the plain look, but on its own grid and light floor, casting as its base
    /// mesh (its splats cast nothing), and drawing over that mesh by its surface and close bias, its Gaussians' material read.
    /// </summary>
    internal DaggerfallSplatLook HybridLook => Look with
    {
        Grid = Hybrid!.PixelGrid, LightFloor = Hybrid.LightFloor, Shadow = SplatShadowCaster.None, SurfaceBias = Hybrid.SurfaceBias,
        CloseNear = Hybrid.CloseNear, CloseFar = Hybrid.CloseFar, CloseBias = Hybrid.CloseBias, Material = Hybrid.Material,
    };

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
            || !(LightFloor is >= 0 and <= 1) || !float.IsFinite(CrossfadeSeconds) || CrossfadeSeconds < 0 || !(GaitJitter is >= 0 and < .5F)
            || !(DiffuseWrap is >= 0 and <= 1) || Hybrid is null)
            throw new ArgumentException($"The gsplat people draw on a pixel grid of 0 to 16, a coverage above 0 to 1, their own palette only on a grid, " +
                $"a pose rate and crossfade of 0 or more, a light floor of 0 to 1, a gait jitter of 0 to under 0.5, a diffuse wrap of 0 to 1 and " +
                $"a hybrid look, not {this}.");
        Blinks.Validate();
        Hybrid.Validate();
        return this;
    }

    internal static DaggerfallSplatPeopleTuning Read(JsonElement e) => new DaggerfallSplatPeopleTuning(
        e.GetProperty("pixelGrid").GetUInt32(), e.GetProperty("pixelCoverage").GetSingle(), e.GetProperty("ownPalette").GetBoolean(),
        e.GetProperty("poseRate").GetSingle(), e.GetProperty("lightFloor").GetSingle(), e.GetProperty("crossfadeSeconds").GetSingle(),
        Caster(e.GetProperty("shadowCaster").GetString()!) ?? throw new ArgumentException(
            $"The gsplat people cast shadows as none, ellipsoids, core or splats, not '{e.GetProperty("shadowCaster").GetString()}'."),
        Slow(e.GetProperty("skirtedPace").GetString()!) ?? throw new ArgumentException(
            $"Long-skirted gsplat people walk slow or clip, not '{e.GetProperty("skirtedPace").GetString()}'."),
        e.GetProperty("gaitJitter").GetSingle(), ReadBlinks(e.GetProperty("blinks")), e.GetProperty("diffuseWrap").GetSingle(),
        DaggerfallSplatHybridTuning.Read(e.GetProperty("hybrid"))).Validate();

    private static SplatBlinks ReadBlinks(JsonElement e)
    {
        JsonElement interval = e.GetProperty("intervalSeconds");
        if (interval.GetArrayLength() != 2) throw new ArgumentException($"The gsplat people's blinks come every [least, most] seconds, not {interval}.");
        return new(interval[0].GetSingle(), interval[1].GetSingle(), e.GetProperty("closingSeconds").GetSingle(), e.GetProperty("closedSeconds").GetSingle(),
            e.GetProperty("openingSeconds").GetSingle());
    }
}

/// <summary>
/// How a character with a hybrid body draws (Patch 2026-11-28: the splats' hands and detail far outshine a mesh, so take the
/// best of both): <paramref name="Bodies"/> draws the hybrid body where the art has one, else the plain one; on a pixel grid
/// of <paramref name="PixelGrid"/> (0 smooth: on a grid the base mesh shows between the splats' blocks); keeping
/// <paramref name="LightFloor"/> of its paint however dark; its splats drawing over their base mesh within
/// <paramref name="SurfaceBias"/> metres, and within <paramref name="CloseBias"/> inside <paramref name="CloseNear"/> metres of
/// the camera, easing back by <paramref name="CloseFar"/> (a hood brim the mesh holds more solid than the paint); and reading
/// each Gaussian's occlusion, roughness and metal when <paramref name="Material"/>.
/// </summary>
internal sealed record DaggerfallSplatHybridTuning(bool Bodies, uint PixelGrid, float LightFloor, float SurfaceBias, float CloseNear, float CloseFar,
    float CloseBias, bool Material)
{
    internal static DaggerfallSplatHybridTuning Default { get; } = new(true, 0, 0F, .03F, 1.2F, 3F, .08F, true);

    internal void Validate()
    {
        // The Engine refuses a bias past half a metre and a close curve whose far end is not past its near end.
        if (PixelGrid > 16 || !(LightFloor is >= 0 and <= 1) || !(SurfaceBias is >= 0 and <= .5F) || !(CloseBias is >= 0 and <= .5F)
            || !(CloseNear >= 0) || !float.IsFinite(CloseFar) || CloseFar <= CloseNear)
            throw new ArgumentException($"A hybrid gsplat body draws on a pixel grid of 0 to 16, a light floor of 0 to 1, a surface and close bias of " +
                $"0 to 0.5 m and a close curve whose far end is past its near end, not {this}.");
    }

    internal static DaggerfallSplatHybridTuning Read(JsonElement e) => new(e.GetProperty("bodies").GetBoolean(), e.GetProperty("pixelGrid").GetUInt32(),
        e.GetProperty("lightFloor").GetSingle(), e.GetProperty("surfaceBias").GetSingle(), e.GetProperty("closeNear").GetSingle(),
        e.GetProperty("closeFar").GetSingle(), e.GetProperty("closeBias").GetSingle(), e.GetProperty("material").GetBoolean());
}

/// <summary>
/// How a gsplat figure is finished, for the Engine's splat appearance: its pixel grid, coverage, own palette and pose rate,
/// its light floor and diffuse wrap, how it casts, its distance fade over a base mesh, its surface and close bias, and
/// whether its Gaussians' material is read. Always lit.
/// </summary>
internal sealed record DaggerfallSplatLook(uint Grid, float Coverage, bool OwnPalette, float PoseRate, float LightFloor, SplatShadowCaster Shadow,
    float DiffuseWrap, float FadeNear = 0F, float FadeFar = 0F, float SurfaceBias = 0F, float CloseNear = 0F, float CloseFar = 0F,
    float CloseBias = 0F, bool Material = false)
{
    internal SplatAppearanceRequest Request(Appearance appearance, System.Numerics.Vector3 multiplier, ReadOnlyMemory<Color> palette) =>
        new(appearance, multiplier, Grid, Coverage, palette, PoseRate, SplatLighting.Lit, LightFloor, Shadow, FadeNear, FadeFar, SurfaceBias,
            CloseNear, CloseFar, CloseBias, Material, DiffuseWrap);
}
