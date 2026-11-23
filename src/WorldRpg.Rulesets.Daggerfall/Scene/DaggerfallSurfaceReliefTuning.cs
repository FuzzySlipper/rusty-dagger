using System.Numerics;
using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// How a building's walls, doors and roofs draw their record's relief near the camera (the art set's surface shader,
/// <c>relief.wgsl</c>): each texel stands as a small column under the classic face, as deep as the record's relief
/// states times the player's strength, and the relief shallows to the flat face between the fade's start and end, beyond
/// which nothing is marched. The strength, and a share both fade distances scale by, are the player's video choices; the
/// tuned fade is the reach at a share of 1.
/// </summary>
/// <param name="FadeStart">How far from the camera the relief starts to shallow, in metres.</param>
/// <param name="FadeEnd">How far from the camera it is gone, in metres.</param>
/// <param name="Steps">The most texels a view ray walks through before it takes the texel it is in.</param>
/// <param name="Cavity">How dark the deepest hollow goes, from 0 (unshaded) to 1 (black).</param>
/// <param name="Slope">How far a texel top's slope turns its normal, from 0 (flat tops) to 1 (the height's own slope).</param>
internal sealed record DaggerfallSurfaceReliefTuning(float FadeStart, float FadeEnd, int Steps, float Cavity, float Slope)
{
    internal static DaggerfallSurfaceReliefTuning Default { get; } = new(8, 14, 24, .5f, 1);

    internal DaggerfallSurfaceReliefTuning Validate()
    {
        if (!float.IsFinite(FadeStart) || !float.IsFinite(FadeEnd) || FadeStart < 0 || FadeEnd <= FadeStart)
            throw new ArgumentException("Surface relief fades from 0 metres or more to a farther end.");
        if (Steps is < 1 or > 128) throw new ArgumentException("A surface relief ray walks 1 to 128 texels.");
        if (!float.IsFinite(Cavity) || Cavity is < 0 or > 1 || !float.IsFinite(Slope) || Slope is < 0 or > 1)
            throw new ArgumentException("Surface relief's cavity and slope are from 0 to 1.");
        return this;
    }

    /// <summary>
    /// The surface shader drawing one record's relief: <paramref name="relief"/> its image, whose alpha is each texel's
    /// height, 1 at the face, or none; <paramref name="depth"/> its floor's depth under the face in metres, after the player's
    /// strength, 0 for none; <paramref name="distance"/> the player's share of the tuned fade, both ends scaled alike so the
    /// edge keeps fading; and lit glass flickering by a share at a rate (<paramref name="flicker"/>).
    /// </summary>
    internal MaterialShader Shader(RenderResource shader, RenderResource? relief, float depth, float distance, Vector2 flicker) => new(new RenderResourceReference(shader),
        new Vector4(depth, Steps, FadeStart * distance, FadeEnd * distance), new Vector4(Cavity, Slope, 0, 0), new Vector4(flicker, 0, 0), default,
        relief is null ? default : new RenderResourceReference(relief), default);

    internal static DaggerfallSurfaceReliefTuning Read(JsonElement e) => new DaggerfallSurfaceReliefTuning(
        e.GetProperty("fadeStart").GetSingle(), e.GetProperty("fadeEnd").GetSingle(), e.GetProperty("steps").GetInt32(),
        e.GetProperty("cavity").GetSingle(), e.GetProperty("slope").GetSingle()).Validate();
}
