using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

internal static class DungeonWaterNormalization
{
    // The donor places a water plane across the fixed RDB footprint. Bound its underwater
    // trigger by the dungeon geometry floor; source object-root grid sizes are not world sizes.
    internal static IReadOnlyList<NormalizedWaterVolume> Read(string id, int? nativeLevel,
        float minimumY, int blockX = 0, int blockZ = 0)
    {
        if (nativeLevel is not int level) return [];
        float surface = Arena2SourceTransform.ToImportPoint(0, level, 0).YMetres;
        if (minimumY >= surface) return [];
        const float side = 2048F * Arena2SourceTransform.SourceUnitMetres;
        return [new(id, new(new(blockX * side, minimumY, -(blockZ + 1) * side),
            new((blockX + 1) * side, surface, -blockZ * side)))];
    }
}
