namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>How a climate cell value is accounted for.</summary>
internal enum DaggerfallClimateDisposition
{
    Named,
    Unresolved,
}

/// <summary>How a politic cell value is accounted for, including the lookup's own answer.</summary>
internal enum DaggerfallPoliticDisposition
{
    Region,
    Ocean,
    Unresolved,
    OutOfBounds,
}

/// <summary>How a climate lookup answers, including the lookup's own answer.</summary>
internal enum DaggerfallClimateCoordinateDisposition
{
    Found,
    OutOfBounds,
}

/// <summary>One distinct climate value with the climate it names.</summary>
/// <param name="Value">The source byte.</param>
/// <param name="Name">The donor's climate name, empty when no table names it.</param>
/// <param name="Disposition">Whether the donor's table names the value.</param>
internal sealed record DaggerfallClimateValueDefinition(int Value, string Name, DaggerfallClimateDisposition Disposition);

/// <summary>One distinct politic value with the region it names.</summary>
/// <param name="Value">The source byte.</param>
/// <param name="Region">The zero-based region index, or -1 when the value names no region.</param>
/// <param name="Disposition">Whether the value names a region, the ocean, or nothing.</param>
internal sealed record DaggerfallPoliticValueDefinition(int Value, int Region, DaggerfallPoliticDisposition Disposition);

/// <summary>One climate lookup answer: the cell's value and what it names.</summary>
/// <param name="Disposition">Whether the coordinates name a cell.</param>
/// <param name="Value">The source byte, or -1 past the grid's edge.</param>
/// <param name="Name">The donor's climate name, empty past the edge or when no table names the value.</param>
internal sealed record DaggerfallClimateCell(DaggerfallClimateCoordinateDisposition Disposition, int Value, string Name);

/// <summary>One politic lookup answer: the cell's value and what it names.</summary>
/// <param name="Disposition">Whether the coordinates name a region, the ocean, nothing, or no cell.</param>
/// <param name="Value">The source byte, or -1 past the grid's edge.</param>
/// <param name="Region">The zero-based region index, or -1 when the value names no region.</param>
internal sealed record DaggerfallPoliticCell(DaggerfallPoliticDisposition Disposition, int Value, int Region);

/// <summary>The normalized climate grid, loaded from the pack alone.</summary>
/// <param name="Width">The grid's column count, sentinel column included.</param>
/// <param name="Height">The grid's row count.</param>
/// <param name="Cells">The row-major source bytes, sentinel column kept.</param>
/// <param name="Values">The distinct cell values with the climate each names.</param>
internal sealed record DaggerfallClimateGridDefinition(int Width, int Height, byte[] Cells, IReadOnlyList<DaggerfallClimateValueDefinition> Values)
{
    /// <summary>Reads one climate cell: its value and what the grid names it, or the edge past it.</summary>
    /// <remarks>
    /// Coordinates past the grid are an explicit answer rather than a refusal: terrain, weather
    /// and social consumers query map pixels the grid does not cover, and a miss they can branch
    /// on is the contract while an exception would be a load-bearing crash for a lookup.
    /// Coordinates name stored columns directly: the donor's map-file accessors add one to the
    /// world-pixel X to line up with the height map, so a consumer porting a donor lookup adds
    /// that one while a consumer following the dungeon normalizer's map-pixel path reads raw.
    /// </remarks>
    internal DaggerfallClimateCell GetCell(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return new DaggerfallClimateCell(DaggerfallClimateCoordinateDisposition.OutOfBounds, -1, string.Empty);
        }

        int value = Cells[(y * Width) + x];
        DaggerfallClimateValueDefinition? named = Values.FirstOrDefault(candidate => candidate.Value == value);
        return new DaggerfallClimateCell(
            DaggerfallClimateCoordinateDisposition.Found,
            value,
            named?.Disposition == DaggerfallClimateDisposition.Named ? named.Name : string.Empty);
    }
}

/// <summary>The normalized politic grid, loaded from the pack alone.</summary>
/// <param name="Width">The grid's column count, sentinel column included.</param>
/// <param name="Height">The grid's row count.</param>
/// <param name="Cells">The row-major source bytes, sentinel column kept.</param>
/// <param name="Values">The distinct cell values with the region each names.</param>
internal sealed record DaggerfallPoliticGridDefinition(int Width, int Height, byte[] Cells, IReadOnlyList<DaggerfallPoliticValueDefinition> Values)
{
    /// <summary>Reads one politic cell: the region it names, the ocean, or the explicit miss.</summary>
    /// <remarks>
    /// The donor reads region index as politic value minus 128 and the ocean as 64; a value that is
    /// neither is unresolved rather than a region, and coordinates past the grid are out of bounds
    /// rather than a region. The donor's bad-value patch for one map pixel is consumer policy, not
    /// a source fact, so it lives with consumers and not in this lookup. Coordinates name stored
    /// columns directly: the donor's map-file accessors add one to the world-pixel X to line up
    /// with the height map, so a consumer porting a donor lookup adds that one while a consumer
    /// following the dungeon normalizer's map-pixel path reads raw.
    /// </remarks>
    internal DaggerfallPoliticCell GetCell(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return new DaggerfallPoliticCell(DaggerfallPoliticDisposition.OutOfBounds, -1, -1);
        }

        int value = Cells[(y * Width) + x];
        DaggerfallPoliticValueDefinition? named = Values.FirstOrDefault(candidate => candidate.Value == value);
        return named switch
        {
            { Disposition: DaggerfallPoliticDisposition.Region } => new DaggerfallPoliticCell(DaggerfallPoliticDisposition.Region, value, named.Region),
            { Disposition: DaggerfallPoliticDisposition.Ocean } => new DaggerfallPoliticCell(DaggerfallPoliticDisposition.Ocean, value, -1),
            _ => new DaggerfallPoliticCell(DaggerfallPoliticDisposition.Unresolved, value, -1),
        };
    }
}

/// <summary>The normalized world grids, loaded from the pack alone.</summary>
/// <param name="Climate">The climate grid.</param>
/// <param name="Politic">The politic grid.</param>
internal sealed record DaggerfallWorldGridsSet(DaggerfallClimateGridDefinition Climate, DaggerfallPoliticGridDefinition Politic);
