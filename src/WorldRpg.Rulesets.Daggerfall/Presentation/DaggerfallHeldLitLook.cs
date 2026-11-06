using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// One first-person mesh's tint: each material slot's own colour (white, or a metal's) under the view's light. The Engine
/// replaces an appearance's whole set of factors, so every slot is written each time.
/// </summary>
internal sealed class DaggerfallHeldLitLook
{
    private static readonly Color White = new(1, 1, 1, 1);

    private readonly IEngineContext engine;
    private readonly Appearance look;
    private readonly Color[] slots;
    private Color light = White;

    internal DaggerfallHeldLitLook(IEngineContext engine, Appearance look, Color[] slots)
    {
        this.engine = engine;
        this.look = look;
        this.slots = slots;
        if (slots.Any(slot => slot != White)) Draw();
    }

    /// <summary>Draws the look under <paramref name="level"/>, if that's new.</summary>
    internal void Light(Color level)
    {
        if (level == light) return;
        light = level;
        Draw();
    }

    private void Draw() => engine.Animation.UpdateAnimatedMeshMaterialFactors(new(look,
        slots.Select((slot, index) => MeshMaterialFactors.Tint((uint)index, Under(slot, light))).ToArray()));

    /// <summary>A slot's colour under the view's light.</summary>
    internal static Color Under(Color slot, Color light) => new(slot.R * light.R, slot.G * light.G, slot.B * light.B, slot.A);
}
