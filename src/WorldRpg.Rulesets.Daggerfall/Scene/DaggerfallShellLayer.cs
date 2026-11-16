using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// One layer of a shell stack drawn with the ground cover's shader (<c>shells.wgsl</c>): the ground's grass and relief
/// (<see cref="DaggerfallGroundCover"/>) and a remodel's shell stack (<see cref="DaggerfallSiteAppearance"/>) both make
/// their layers here, so the shader's parameters and the stack's looks have one home. A layer shows the texels of a
/// texture whose alpha (each texel's height, as a share of the stack) reaches its cutoff.
/// </summary>
internal static class DaggerfallShellLayer
{
    /// <summary>
    /// A layer material's own alpha cut: only above zero, so the material keeps every texel but the empty ones and the
    /// shader makes the layer's cut, raggedly where the ground thins.
    /// </summary>
    internal const float AlphaCutoff = 1f / 512;

    /// <summary>
    /// The shader a layer is drawn with: <paramref name="cutoff"/> the height its texels must reach to show, as a share of
    /// the stack; <paramref name="height"/> how high it stands in its stack, from 0 at the lowest layer to 1 at the top,
    /// which the wind leans; <paramref name="texels"/> its texture's texels a side, the grid its clumps and glints fall on;
    /// <paramref name="glints"/> the share of its texels that glint as the viewer moves (<see cref="Glints"/>);
    /// <paramref name="ragged"/> how raggedly a thinning ground gives way, from 0 (evenly) to 1 (in clumps); and its wind:
    /// how far the top layer slides at full wind in metres, by whole texels of <paramref name="texelsPerMetre"/>, and how
    /// fast its gusts rise and fall. A layer that stands still slides nothing.
    /// </summary>
    internal static MaterialShader Shader(RenderResource shader, float cutoff, float height, int texels, float glints, float ragged = 0,
        float sway = 0, float texelsPerMetre = 1, float gustRate = 0) =>
        new(new RenderResourceReference(shader), new Vector4(texelsPerMetre, sway, gustRate, height), new Vector4(cutoff, ragged, glints, texels),
            default, default, default, default, sway);

    /// <summary>The share of a layer's texels that glint: only a stack's top half glints, where light catches a crest.</summary>
    internal static float Glints(float glints, int layer, int layers) => layer >= layers / 2 ? glints : 0;

    /// <summary>A layer's colour: shaded from the stack's root colour at its lowest layer up to its own at the top.</summary>
    internal static Vector3 Shade(Vector3 roots, float height) => Vector3.Lerp(roots, Vector3.One, height);
}
