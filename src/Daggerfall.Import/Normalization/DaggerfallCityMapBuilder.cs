using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>A horizontal run of equal source automap meaning in normalized right-handed metres.</summary>
public sealed record DaggerfallMapFootprint(float MinX, float MinZ, float MaxX, float MaxZ, int Kind);
public sealed record DaggerfallBlockMap(string Block, float BlockSize, IReadOnlyList<DaggerfallMapFootprint> Footprints);

/// <summary>Publishes the existing source automap without a second geometry or navigation interpretation.</summary>
public static class DaggerfallCityMapBuilder
{
    private const int Dimension = 64;
    private const float CellSize = 64 * Arena2SourceTransform.SourceUnitMetres;
    public static DaggerfallBlockMap Build(string sourceKey, IReadOnlyList<byte> cells)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Count != Dimension * Dimension)
            throw new ArgumentException($"Block '{sourceKey}' must publish its complete 64-by-64 source automap.", nameof(cells));
        List<DaggerfallMapFootprint> runs = [];
        for (int row = 0; row < Dimension; row++)
        {
            int column = 0;
            while (column < Dimension)
            {
                int first = column;
                byte kind = cells[row * Dimension + column++];
                while (column < Dimension && cells[row * Dimension + column] == kind) column++;
                // BlocksFile.GetBlockAutoMap removes the ground-flat speckles; empty streets and
                // its show-all-only special values are not ordinary building footprints.
                if (kind is 0 or 25 or 117 or 224 or 250 or 251) continue;
                runs.Add(new(first * CellSize, -(Dimension - row) * CellSize,
                    column * CellSize, -(Dimension - row - 1) * CellSize, kind));
            }
        }
        return new(sourceKey, Dimension * CellSize, runs);
    }
}
