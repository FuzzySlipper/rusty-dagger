namespace Daggerfall.Import.Arena2;

/// <summary>
/// Classic dungeon texture-table conversion used while importing Arena2
/// location data. This is source-format behavior, not runtime random policy.
/// </summary>
public static class DungeonTextureTableTransform
{
    private const byte OceanClimate = 223;
    private const byte DesertClimate = 224;
    private const byte SwampClimate = 228;
    private const byte HauntedWoodlandsClimate = 232;
    private const int TextureTableSlots = 6;
    private const int ExteriorTextureSlots = TextureTableSlots - 1;
    private const ushort DoorTextureArchive = 74;

    // The six archive IDs the classic table remaps, in source-table order.
    private static readonly ushort[] SourceTextureArchives = [119, 120, 122, 123, 124, 168];

    // Daggerfall Unity's classic table data and climate-index interpretation.
    private static readonly byte[] ClimateTextureArchiveIndices = [0, 0, 1, 4, 4, 0, 3, 3, 3, 0];
    private static readonly ushort[] ClimateTextureArchives = [19, 119, 319, 419, 119];
    private static readonly byte[] ClimateIndices = [0, 0, 0, 1, 2, 3, 4, 5, 5, 5];

    /// <summary>Gets a copy of the classic default, non-randomized texture table.</summary>
    public static ushort[] CreateDefaultTable()
    {
        return SourceTextureArchives.ToArray();
    }

    /// <summary>
    /// Reproduces the classic per-location dungeon texture table from its
    /// location ID and CLIMATE.PAK climate value.
    /// </summary>
    public static ushort[] CreateClassic(uint locationId, byte worldClimate)
    {
        if (worldClimate is < OceanClimate or > HauntedWoodlandsClimate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldClimate),
                worldClimate,
                $"Classic dungeon texture tables support CLIMATE.PAK values {OceanClimate} through {HauntedWoodlandsClimate}.");
        }

        byte climate = worldClimate == OceanClimate ? SwampClimate : worldClimate;
        int classicIndex = ClimateIndices[climate - OceanClimate];
        int climateTextureArchiveIndex = ClimateTextureArchiveIndices[classicIndex];
        int climateBasedIndex = climate - DesertClimate;

        ClassicDaggerfallRandom random = new(locationId);
        ushort[] table = new ushort[TextureTableSlots];
        for (int slot = 0; slot < ExteriorTextureSlots; slot++)
        {
            int offset = random.NextInclusive(0, 4);
            // Archive offset two is invalid in classic data and resolves to four.
            table[slot] = (ushort)(ClimateTextureArchives[climateTextureArchiveIndex] + (offset == 2 ? 4 : offset));
        }

        table[^1] = (ushort)(68 + (100 * ClimateTextureArchiveIndices[climateBasedIndex]));
        return table;
    }

    /// <summary>
    /// Applies a classic dungeon texture table to one archive. The door archive
    /// follows the climate-base rule; unrelated archives are retained.
    /// </summary>
    public static ushort RemapArchive(ushort archive, ReadOnlySpan<ushort> table, ushort climateBase)
    {
        if (table.Length != TextureTableSlots)
        {
            throw new ArgumentException($"A classic dungeon texture table requires exactly {TextureTableSlots} entries.", nameof(table));
        }

        if (archive == DoorTextureArchive)
        {
            return checked((ushort)(archive + climateBase));
        }

        for (int slot = 0; slot < SourceTextureArchives.Length; slot++)
        {
            if (archive == SourceTextureArchives[slot])
            {
                return table[slot];
            }
        }

        return archive;
    }
}
