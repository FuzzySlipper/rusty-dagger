using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>DFU's interior-light flat policy, expressed as normalized source facts without Unity prefabs.</summary>
internal static class DaggerfallInteriorLightFacts
{
    internal const ushort Archive = 210;

    internal static bool TryProject(string id, RdbFlatSource flat, NormalizedVector3 position, float billboardHeight, out NormalizedLightPlacement light)
    {
        light = default!;
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (flat.TextureArchive != Archive) return false;
        float offset = flat.TextureRecord switch
        {
            0 => -.1F, 2 or 3 => .1F, 5 => .15F, 6 or 20 => .6F, 9 => .4F, 11 => -.4F,
            13 => -.35F, 14 or 15 => billboardHeight / 2F, 17 => .2F, 21 => billboardHeight / 2.4F,
            22 => -.5F, 24 => -1.85F, 25 => -1F, 27 => -.02F, _ => 0F,
        };
        (float range, float intensity, NormalizedVector3 color) = ((float Range, float Intensity, NormalizedVector3 Color))(flat.TextureRecord switch
        {
            0 => (20F, 1.1F, new(.95F, .91F, .63F)),
            2 => (10F / 3F, .6F, new(1F, .99F, .82F)),
            3 or 4 => (10F / 3F, 1F, new(1F, 1F, 1F)),
            5 => (7.5F, .33F, new(1F, .89F, .61F)),
            6 => (15F, .75F, new(1F, .93F, .62F)),
            8 => (10F, 1F, new(.68F, 1F, .94F)),
            9 => (15F, .65F, new(1F, .92F, .6F)),
            11 => (5F, .5F, new(1F, 1F, 1F)),
            13 => (12F, 1.1F, new(.93F, .84F, .49F)),
            17 => (10F, .8F, new(1F, .97F, .87F)),
            20 => (12F, .75F, new(1F, .92F, .72F)),
            21 => (10F / 3F, .5F, new(1F, .95F, .67F)),
            22 => (10F, 1.5F, new(1F, .95F, .78F)),
            24 or 25 or 26 or 27 => (10F, 1.4F, new(1F, .98F, .64F)),
            _ => (10F, 1F, new(1F, 1F, 1F)),
        });
        light = new(id, new(position.X, position.Y + offset, position.Z), range, intensity, color);
        return true;
    }
}
