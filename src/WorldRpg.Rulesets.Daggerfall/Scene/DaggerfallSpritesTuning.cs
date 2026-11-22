using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// How the world's sprites stand and sway. Each kind of sprite turns about world up toward the camera's position
/// (<c>position</c>, so walking past one turns it) or takes the camera's heading (<c>heading</c>, so it sits parallel to
/// the screen and turns only as the view turns, as the original game's sprites do).
/// </summary>
/// <param name="Nature">How wild nature turns: trees, plants and rocks on open ground.</param>
/// <param name="Decorations">How a place's placed sprites turn: town trees and plants, signs, statues, lamps and the rest.</param>
/// <param name="Actors">How people and creatures turn. Which of their views draws is chosen apart from this.</param>
/// <param name="Items">How treasure piles and dropped items turn.</param>
/// <param name="FloraWindBend">How far a tree or plant leans in the wind, in metres per metre of its height at full wind: wild nature and placed sprites from the nature archives. 0 holds them still.</param>
/// <param name="FloraWindWholeTexels">Whether flora leans by whole texels, so pixel art moves in hard steps rather than sliding.</param>
internal sealed record DaggerfallSpritesTuning(BillboardMode Nature, BillboardMode Decorations, BillboardMode Actors, BillboardMode Items,
    float FloraWindBend, bool FloraWindWholeTexels)
{
    internal static DaggerfallSpritesTuning Default { get; } = new(BillboardMode.CylindricalView, BillboardMode.CylindricalView,
        BillboardMode.CylindricalView, BillboardMode.CylindricalView, .05f, true);

    internal DaggerfallSpritesTuning Validate()
    {
        if (!float.IsFinite(FloraWindBend) || FloraWindBend < 0) throw new ArgumentException("Flora leans in the wind by a finite amount, 0 or more.");
        return this;
    }

    /// <summary>The flora's material: <paramref name="material"/> leaning in the wind as these values say.</summary>
    internal SpriteMaterialDescriptor Flora(SpriteMaterialDescriptor material) =>
        material with { WindBend = FloraWindBend, WindWholeTexels = FloraWindWholeTexels };

    internal static DaggerfallSpritesTuning Read(JsonElement e) => new DaggerfallSpritesTuning(
        Facing(e, "nature"), Facing(e, "decorations"), Facing(e, "actors"), Facing(e, "items"),
        e.GetProperty("floraWindBend").GetSingle(), e.GetProperty("floraWindWholeTexels").GetBoolean()).Validate();

    private static BillboardMode Facing(JsonElement e, string kind) => e.GetProperty(kind).GetString() switch
    {
        "position" => BillboardMode.Cylindrical,
        "heading" => BillboardMode.CylindricalView,
        var other => throw new ArgumentException($"Sprites of kind '{kind}' face by 'position' or 'heading', not '{other}'."),
    };
}
