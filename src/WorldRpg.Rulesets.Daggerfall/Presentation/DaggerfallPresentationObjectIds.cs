namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The object identities of the one complete appearance snapshot the session publishes. Engine Graphics refuses an
/// object identity above <see cref="Maximum"/> (it must survive the JSON frame exactly), so every owner publishing
/// into the snapshot draws from its own band below it, and no band can reach another's:
/// <list type="table">
/// <item><term>[1, 2^48)</term><description>gameplay identities published as their own visuals: actors, NPCs and ground containers.</description></item>
/// <item><term>[2^49, 2^50)</term><description>exterior nature sprite batches, by map pixel and sprite record.</description></item>
/// <item><term>[2^50, 2^52)</term><description>drawn locations' visuals. Each location slot (the active location is slot 0, each
/// resident neighbour its own) owns a 2^32 range: its static meshes by index, then its door, city gate, action model and resident actor visuals by ordinal.</description></item>
/// <item><term>[2^52, 2^53)</term><description>transient visuals (effects, viewmodel, missiles, the magic candle), counted down.</description></item>
/// </list>
/// A band that runs out refuses rather than spilling into its neighbour.
/// </summary>
internal static class DaggerfallPresentationObjectIds
{
    /// <summary>The largest object identity Engine Graphics publishes, <c>2^53 - 1</c>.</summary>
    internal const ulong Maximum = (1UL << 53) - 1;

    private const ulong GameplayEnd = 1UL << 48;
    /// <summary>Marks a map pixel's river water, above any coordinate the sea's sheets reach.</summary>
    private const ulong RiverBit = 1UL << 40;
    /// <summary>How many map pixels past the world's north and west edges a sheet of sea can stand.</summary>
    private const int SeaPastEdge = 16;
    private const ulong NatureBase = 1UL << 49;
    private const ulong LocationBase = 1UL << 50;
    internal const ulong TransientVisualFloor = 3UL << 51;
    /// <summary>
    /// The held arms and what they carry, numbered once for the whole product rather than by each site's appearance: the
    /// Engine keeps one animation instance per object, and one site's or session's arms are still retiring when the next's
    /// are drawn.
    /// </summary>
    internal const ulong HeldVisualFloor = 1UL << 52;
    private static ulong nextHeld = TransientVisualFloor - 1;
    internal const ulong TransientVisualFirst = Maximum;

    /// <summary>Map pixel coordinates, wider than the world map, that nature identities carry.</summary>
    private const int CellBits = 12;
    /// <summary>A nature sprite record within its climate archive; normalized billboards address records 0 to 127.</summary>
    private const int SpriteRecordBits = 7;

    /// <summary>Each location slot's range: meshes in the lower half; door, gate, action model and resident actor visuals in the upper.</summary>
    private const int LocationSlotBits = 32;
    private const ulong LocationPartLimit = 1UL << (LocationSlotBits - 1);

    /// <summary>The location slots the location band holds (2^16 ranges of 2^32 stay below 2^51).</summary>
    internal const int LocationSlots = 1 << 16;

    /// <summary>The slot the active site's location draws under; resident neighbours take the others.</summary>
    internal const int ActiveLocationSlot = 0;

    /// <summary>A gameplay identity drawn under its own value.</summary>
    internal static ulong Gameplay(long id, string owner)
    {
        if (id <= 0 || (ulong)id >= GameplayEnd)
            throw new InvalidOperationException($"{owner} {id} is outside the gameplay object identities [1, {GameplayEnd}).");
        return (ulong)id;
    }

    /// <summary>One cell's batch of the nature sprites that draw one sprite record.</summary>
    internal static ulong NatureBatch(int x, int y, int record)
    {
        RequireCell(x, y, "Exterior nature");
        if ((uint)record >= 1U << SpriteRecordBits)
            throw new ArgumentOutOfRangeException(nameof(record), "Exterior nature sprite record exceeds its identity range.");
        return NatureBase | ((ulong)(uint)x << (CellBits + SpriteRecordBits)) | ((ulong)(uint)y << SpriteRecordBits) | (uint)record;
    }

    /// <summary>The sea's sheet on a map pixel. The sea runs on past the map's edge, so a pixel may lie a little short of it.</summary>
    internal static ulong Sea(int x, int y)
    {
        (int cellX, int cellY) = (x + SeaPastEdge, y + SeaPastEdge);
        RequireCell(cellX, cellY, "Sea");
        return GameplayEnd | ((ulong)(uint)cellX << CellBits) | (uint)cellY;
    }

    /// <summary>The ground cover's shells about the player, past every identity the sea and rivers take.</summary>
    internal const ulong GroundCover = GameplayEnd | (1UL << 41);

    /// <summary>A map pixel's river water, in the half of the sea's band no map pixel reaches.</summary>
    internal static ulong River(int x, int y)
    {
        RequireCell(x, y, "River");
        return GameplayEnd | ((ulong)(uint)x << CellBits) | (uint)y | RiverBit;
    }

    /// <summary>A drawn location's static mesh by its index in the location's geometry.</summary>
    internal static ulong WorldMesh(int slot, int index)
    {
        if (index < 0 || (ulong)index >= LocationPartLimit)
            throw new ArgumentOutOfRangeException(nameof(index), "A location has more static meshes than its identity range.");
        return LocationSlotBase(slot) + (ulong)index;
    }

    /// <summary>A drawn location's door, city gate, action model or resident actor visual, by its appearance ordinal.</summary>
    internal static ulong LocationVisual(int slot, ulong ordinal)
    {
        if (ordinal >= LocationPartLimit)
            throw new InvalidOperationException("Presentation location visual identities are exhausted.");
        return LocationSlotBase(slot) + LocationPartLimit + ordinal;
    }

    /// <summary>The next identity counted down from <paramref name="next"/> without passing <paramref name="floor"/>.</summary>
    /// <summary>The next held object.</summary>
    internal static ulong TakeHeld() => Take(ref nextHeld, HeldVisualFloor, "held visual");

    internal static ulong Take(ref ulong next, ulong floor, string band)
    {
        if (next < floor) throw new InvalidOperationException($"Presentation {band} identities are exhausted.");
        return next--;
    }

    private static ulong LocationSlotBase(int slot)
    {
        if ((uint)slot >= LocationSlots)
            throw new ArgumentOutOfRangeException(nameof(slot), $"Location slot {slot} is outside the {LocationSlots} location identity slots.");
        return LocationBase + ((ulong)(uint)slot << LocationSlotBits);
    }

    private static void RequireCell(int x, int y, string owner)
    {
        if ((uint)x >= 1U << CellBits || (uint)y >= 1U << CellBits)
            throw new ArgumentOutOfRangeException(nameof(x), $"{owner} cell ({x}, {y}) exceeds its identity range.");
    }
}
