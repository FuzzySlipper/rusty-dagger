namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The object identities of the one complete appearance snapshot the session publishes. Engine Graphics refuses an
/// object identity above <see cref="Maximum"/> (it must survive the JSON frame exactly), so every owner publishing
/// into the snapshot draws from its own band below it, and no band can reach another's:
/// <list type="table">
/// <item><term>[1, 2^48)</term><description>gameplay identities published as their own visuals: actors, NPCs and ground containers.</description></item>
/// <item><term>[2^48, 2^49)</term><description>exterior terrain cells, by map pixel.</description></item>
/// <item><term>[2^49, 2^50)</term><description>exterior nature sprites, by map pixel and terrain tile.</description></item>
/// <item><term>[2^50, 2^51)</term><description>a location's static meshes, by index.</description></item>
/// <item><term>[2^51, 2^52)</term><description>a location's door, city gate and action model visuals, counted down.</description></item>
/// <item><term>[2^52, 2^53)</term><description>transient visuals (effects, viewmodel, missiles, the magic candle), counted down.</description></item>
/// </list>
/// A band that runs out refuses rather than spilling into its neighbour.
/// </summary>
internal static class DaggerfallPresentationObjectIds
{
    /// <summary>The largest object identity Engine Graphics publishes, <c>2^53 - 1</c>.</summary>
    internal const ulong Maximum = (1UL << 53) - 1;

    private const ulong GameplayEnd = 1UL << 48;
    private const ulong TerrainBase = 1UL << 48;
    private const ulong NatureBase = 1UL << 49;
    private const ulong WorldMeshBase = 1UL << 50;
    internal const ulong LocationVisualFloor = 1UL << 51;
    internal const ulong LocationVisualFirst = (1UL << 52) - 1;
    internal const ulong TransientVisualFloor = 1UL << 52;
    internal const ulong TransientVisualFirst = Maximum;

    /// <summary>Map pixel coordinates, wider than the world map, that terrain and nature identities carry.</summary>
    private const int CellBits = 12;
    private const int TileBits = 14;

    /// <summary>A gameplay identity drawn under its own value.</summary>
    internal static ulong Gameplay(long id, string owner)
    {
        if (id <= 0 || (ulong)id >= GameplayEnd)
            throw new InvalidOperationException($"{owner} {id} is outside the gameplay object identities [1, {GameplayEnd}).");
        return (ulong)id;
    }

    /// <summary>One exterior terrain cell's visual.</summary>
    internal static ulong TerrainCell(int x, int y)
    {
        RequireCell(x, y, "Exterior terrain");
        return TerrainBase | ((ulong)(uint)x << CellBits) | (uint)y;
    }

    /// <summary>One nature sprite, by its cell and its tile ordinal within that cell.</summary>
    internal static ulong Nature(int x, int y, uint tile)
    {
        RequireCell(x, y, "Exterior nature");
        if (tile >= 1U << TileBits) throw new ArgumentOutOfRangeException(nameof(tile), "Exterior nature tile exceeds its identity range.");
        return NatureBase | ((ulong)(uint)x << (CellBits + TileBits)) | ((ulong)(uint)y << TileBits) | tile;
    }

    /// <summary>A location's static mesh by its index in the location's geometry.</summary>
    internal static ulong WorldMesh(int index)
    {
        if (index < 0 || (ulong)index >= WorldMeshBase) throw new ArgumentOutOfRangeException(nameof(index), "A location has more static meshes than its identity band.");
        return WorldMeshBase + (ulong)index;
    }

    /// <summary>The next identity counted down from <paramref name="next"/> without passing <paramref name="floor"/>.</summary>
    internal static ulong Take(ref ulong next, ulong floor, string band)
    {
        if (next < floor) throw new InvalidOperationException($"Presentation {band} identities are exhausted.");
        return next--;
    }

    private static void RequireCell(int x, int y, string owner)
    {
        if ((uint)x >= 1U << CellBits || (uint)y >= 1U << CellBits)
            throw new ArgumentOutOfRangeException(nameof(x), $"{owner} cell ({x}, {y}) exceeds its identity range.");
    }
}
