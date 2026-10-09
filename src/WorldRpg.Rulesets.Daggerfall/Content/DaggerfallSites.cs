namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The kinds of site the map table names, in the donor's own numbering
/// (<c>DFRegion.LocationTypes</c>, <c>Assets/Scripts/API/DFRegion.cs</c>).
/// </summary>
/// <remarks>
/// The map table packs this into a five-bit field (<c>MapsFile.cs</c> reads it as
/// <c>(4 * bitfield) >> 27</c>), so the field carries <c>0..31</c>. Every member below is representable,
/// and so are <c>15..31</c> - values that name no kind and are refused against the record carrying them
/// rather than defaulted. The donor's <c>None = 0xffff</c> member is deliberately not modelled: it does
/// not fit the field at all, and it marks a location the map table has no entry for. That is a question
/// of whether a site record exists, which is answered by the record being absent, rather than a kind a
/// published record can carry.
/// </remarks>
internal enum DaggerfallSiteKind
{
    /// <summary>Large settlement.</summary>
    TownCity = 0,

    /// <summary>Medium settlement.</summary>
    TownHamlet = 1,

    /// <summary>Small settlement.</summary>
    TownVillage = 2,

    /// <summary>Farmhouse.</summary>
    HomeFarms = 3,

    /// <summary>Large dungeon.</summary>
    DungeonLabyrinth = 4,

    /// <summary>Temple.</summary>
    ReligionTemple = 5,

    /// <summary>Tavern.</summary>
    Tavern = 6,

    /// <summary>Medium dungeon.</summary>
    DungeonKeep = 7,

    /// <summary>Wealthy home.</summary>
    HomeWealthy = 8,

    /// <summary>Cult.</summary>
    ReligionCult = 9,

    /// <summary>Small dungeon.</summary>
    DungeonRuin = 10,

    /// <summary>Poor home.</summary>
    HomePoor = 11,

    /// <summary>Graveyard.</summary>
    Graveyard = 12,

    /// <summary>Coven.</summary>
    Coven = 13,

    /// <summary>The player's ship, which the corpus publishes as the location named "Your Ship".</summary>
    HomeYourShips = 14,
}

/// <summary>
/// A location's durable identity: the region it belongs to and its index within that region.
/// </summary>
/// <remarks>
/// This pair, and not the display name, is what identifies a site. The published corpus carries 15,251
/// locations under 12,672 distinct names: 1,467 names appear in more than one place, and within a single
/// region 129 (region, name) pairs - across 44 distinct names - share their name with another location,
/// so a name is not a key and a lookup that treats it as one silently resolves to whichever record it
/// happened to see first.
/// </remarks>
/// <param name="Region">The source region index.</param>
/// <param name="Index">The location's ordinal in the region's names table.</param>
internal readonly record struct DaggerfallSiteId(int Region, int Index)
{
    public override string ToString() => $"{Region}/{Index}";
}

/// <summary>
/// One location the published pack carries: its identity, its name, and the source facts a site
/// consumer reads.
/// </summary>
/// <param name="Id">The region and index that identify it.</param>
/// <param name="Name">The exact name the source names table carries, which is not unique.</param>
/// <param name="MapId">The map the location draws, which a map consumer keys its own pixel identity from.</param>
/// <param name="DungeonType">The location's dungeon type byte, zero when it has none.</param>
/// <param name="Kind">The site kind the map table's type field names.</param>
/// <param name="Discovered">Whether the source marks the location discovered before play begins.</param>
internal sealed record DaggerfallSiteRecord(
    DaggerfallSiteId Id,
    string Name,
    int MapId,
    int Longitude,
    int Latitude,
    int DungeonType,
    DaggerfallSiteKind Kind,
    bool Discovered,
    DaggerfallSiteExterior? Exterior = null)
{
    internal IReadOnlyList<DaggerfallSiteDungeonBlock> DungeonBlocks { get; init; } = [];

    /// <summary>
    /// The CLIMATE.PAK value at the location's own map pixel (DEC-11.ground-archive): its climate texture
    /// swaps follow it. Null when the published location states none.
    /// </summary>
    internal int? Climate { get; init; }

    /// <summary>
    /// The archives the dungeon's classic texture table redraws (DEC-11.dungeon-textures), each with the
    /// archive this dungeon draws it from; empty for a location without a dungeon.
    /// </summary>
    internal IReadOnlyDictionary<int, int> DungeonTextureRemaps { get; init; } = new Dictionary<int, int>();
    internal int Region => Id.Region;

    internal int Index => Id.Index;

    /// <summary>Classic 1000 by 500 wilderness map pixel containing this site.</summary>
    internal int MapPixelX => Longitude / 128;
    internal int MapPixelY => 499 - (Latitude / 128);
}

internal sealed record DaggerfallSiteDungeonBlock(string SourceKey, int X, int Z)
{
    /// <summary>Whether this is the block the dungeon's start and enter markers are taken from.</summary>
    internal bool Start { get; init; }
}

/// <summary>Normalized terrain footprint of one exterior location, in its map pixel's terrain tiles.</summary>
internal sealed record DaggerfallSiteExterior(
    int MapPixelX,
    int MapPixelY,
    int Width,
    int Height,
    int TileOriginX,
    int TileOriginY,
    bool UsesCustomLocationPosition,
    int BlendClearance,
    int MinX,
    int MaxX,
    int MinY,
    int MaxY)
{
    internal int? SourceLocationId { get; init; }
    internal IReadOnlyList<DaggerfallSiteBlock> Blocks { get; init; } = [];
    /// <summary>
    /// Compact row-major FLD facts. The reader keeps only non-sentinel source tiles in this typed
    /// grid, including authored record zero, so the full location catalog does not retain one
    /// 16-KiB array (or thousands of tile objects) per location. Contains distinguishes an absent
    /// generated terrain tile from an authored record-zero tile.
    /// </summary>
    internal DaggerfallGroundTileGrid GroundTiles { get; init; } = DaggerfallGroundTileGrid.Empty;
    internal IReadOnlyList<DaggerfallSiteBuildingReference> BuildingReferences { get; init; } = [];
    internal int PortTownAndUnknown { get; init; }
    internal IReadOnlyDictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource> Buildings { get; init; } = new Dictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource>();
}

/// <summary>
/// A source location's 128-by-128 FLD frame in a compact typed representation. The published
/// importer contract remains a complete base64 byte grid; this runtime value indexes the same
/// row-major bytes without retaining a heap object for every terrain tile.
/// </summary>
internal sealed class DaggerfallGroundTileGrid
{
    internal const int Dimension = 128;
    internal const int CellCount = Dimension * Dimension;
    // Matches Daggerfall.Import's normalized logical grid sentinel. Record zero remains a
    // source-authored tile and therefore must remain present in the sparse index.
    internal const byte GeneratedTerrainBitfield = 0xFE;
    private readonly ushort[] _indices;
    private readonly byte[] _values;

    private DaggerfallGroundTileGrid(ushort[] indices, byte[] values)
    {
        _indices = indices;
        _values = values;
    }

    internal static DaggerfallGroundTileGrid Empty { get; } = new([], []);

    internal bool IsEmpty => _indices.Length == 0;

    internal static DaggerfallGroundTileGrid FromBytes(ReadOnlySpan<byte> source, string owner)
    {
        if (source.Length != 0 && source.Length != CellCount)
            throw new InvalidOperationException($"Location {owner} carries {source.Length} source ground bytes instead of the donor {Dimension}-by-{Dimension} frame.");

        int nonZero = 0;
        foreach (byte bitfield in source)
        {
            if (bitfield != GeneratedTerrainBitfield && (bitfield & 0x3F) >= 56)
                throw new InvalidOperationException($"Location {owner} carries unsupported ground texture record {bitfield & 0x3F}.");
            if (bitfield != GeneratedTerrainBitfield) nonZero++;
        }

        if (nonZero == 0) return Empty;
        ushort[] indices = new ushort[nonZero];
        byte[] values = new byte[nonZero];
        int cursor = 0;
        for (int index = 0; index < source.Length; index++)
        {
            byte bitfield = source[index];
            if (bitfield == GeneratedTerrainBitfield) continue;
            indices[cursor] = checked((ushort)index);
            values[cursor++] = bitfield;
        }
        return new(indices, values);
    }

    internal byte At(int x, int y)
    {
        if ((uint)x >= Dimension || (uint)y >= Dimension)
            throw new ArgumentOutOfRangeException($"({x},{y})");
        return At(checked((y * Dimension) + x));
    }

    internal byte At(int index)
    {
        if ((uint)index >= CellCount) throw new ArgumentOutOfRangeException(nameof(index));
        return TryAt(index, out byte value) ? value : (byte)0;
    }

    /// <summary>Returns whether the normalized source frame authored this tile, including record zero.</summary>
    internal bool Contains(int index)
    {
        if ((uint)index >= CellCount) throw new ArgumentOutOfRangeException(nameof(index));
        return TryAt(index, out _);
    }

    private bool TryAt(int index, out byte value)
    {
        int low = 0;
        int high = _indices.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int candidate = _indices[middle];
            if (candidate == index)
            {
                value = _values[middle];
                return true;
            }
            if (candidate < index) low = middle + 1;
            else high = middle - 1;
        }
        value = 0;
        return false;
    }
}

internal sealed record DaggerfallSiteBlock(string SourceName, int X, int Y);
internal sealed record DaggerfallSiteGroundTile(int X, int Y, byte TextureRecord, bool Rotated, bool Flipped);
internal sealed record DaggerfallSiteBuildingReference(int LocationId, int Sector, int? BuildingType = null);

/// <summary>A placement identity is local to a site; an RMB source slot can repeat in its grid.</summary>
internal readonly record struct DaggerfallSiteBuildingId(int BlockX, int BlockY, int Index)
{
    public override string ToString() => $"{BlockX}/{BlockY}/{Index}";
}

internal sealed record DaggerfallSiteBuildingSource(DaggerfallSiteBuildingId Id,
    DaggerfallRmbBuildingSource Source, int Quality)
{
    internal int? SourceLocationId { get; init; }
    internal string? ModelId { get; init; }
    internal float? ModelRadius { get; init; }
}

internal static class DaggerfallSiteKinds
{
    /// <summary>The player label for a site kind, as the travel map names its destinations.</summary>
    internal static string Label(DaggerfallSiteKind kind) => kind switch
    {
        DaggerfallSiteKind.TownCity => "City",
        DaggerfallSiteKind.TownHamlet => "Town",
        DaggerfallSiteKind.TownVillage => "Village",
        DaggerfallSiteKind.HomeFarms => "Farmhouse",
        DaggerfallSiteKind.DungeonLabyrinth => "Large dungeon",
        DaggerfallSiteKind.ReligionTemple => "Temple",
        DaggerfallSiteKind.Tavern => "Tavern",
        DaggerfallSiteKind.DungeonKeep => "Dungeon",
        DaggerfallSiteKind.HomeWealthy => "Wealthy home",
        DaggerfallSiteKind.ReligionCult => "Cult",
        DaggerfallSiteKind.DungeonRuin => "Small dungeon",
        DaggerfallSiteKind.HomePoor => "Poor home",
        DaggerfallSiteKind.Graveyard => "Graveyard",
        DaggerfallSiteKind.Coven => "Coven",
        DaggerfallSiteKind.HomeYourShips => "Your ship",
        _ => "Location",
    };

    /// <summary>
    /// The travel map's dot family for a site kind, as the donor's travel window colours and filters
    /// them: dungeons (labyrinths, keeps, ruins, graveyards, covens), homes, temples (including cults)
    /// and towns (including taverns).
    /// </summary>
    internal static string MapCategory(DaggerfallSiteKind kind) => kind switch
    {
        DaggerfallSiteKind.DungeonLabyrinth or DaggerfallSiteKind.DungeonKeep or DaggerfallSiteKind.DungeonRuin
            or DaggerfallSiteKind.Graveyard or DaggerfallSiteKind.Coven => "dungeon",
        DaggerfallSiteKind.HomeFarms or DaggerfallSiteKind.HomeWealthy or DaggerfallSiteKind.HomePoor => "home",
        DaggerfallSiteKind.ReligionTemple or DaggerfallSiteKind.ReligionCult => "temple",
        DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet or DaggerfallSiteKind.TownVillage
            or DaggerfallSiteKind.Tavern => "town",
        _ => "other",
    };

    /// <summary>The kinds the map table's five-bit type field can name.</summary>
    internal const int PublishedCount = (int)DaggerfallSiteKind.HomeYourShips + 1;

    /// <summary>
    /// Reads a published <c>locationType</c> into the kind it names, reporting whether the field's
    /// vocabulary covers it.
    /// </summary>
    /// <param name="published">The record's <c>locationType</c>.</param>
    /// <param name="kind">The kind it names, when it names one.</param>
    internal static bool TryResolve(int published, out DaggerfallSiteKind kind)
    {
        if (published >= 0 && published < PublishedCount)
        {
            kind = (DaggerfallSiteKind)published;
            return true;
        }

        kind = default;
        return false;
    }

    /// <summary>
    /// Why a published <c>locationType</c> names no kind, against the record that carries it.
    /// </summary>
    /// <remarks>
    /// A value outside the field's vocabulary is refused rather than defaulted. The pack and this
    /// ruleset would disagree about what the site is, and every consumer that branches on a kind - the
    /// holiday a region observes, the service a building offers, whether a map draws it - would answer
    /// from a kind nobody published. Naming the record and the value is what makes that recoverable.
    /// </remarks>
    /// <param name="published">The record's <c>locationType</c>.</param>
    /// <param name="region">The record's region, for the message.</param>
    /// <param name="index">The record's index, for the message.</param>
    internal static string UnnameableMessage(int published, int region, int index) =>
        $"Published location {index} of region {region} carries locationType {published}, which is not one of the {PublishedCount} site kinds the map table's type field names (0..{PublishedCount - 1}).";

    /// <summary>What a JSON value actually was, for a diagnostic that has to name it.</summary>
    /// <param name="value">The value the record carried.</param>
    internal static string Describe(System.Text.Json.JsonElement value) => value.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Undefined => "nothing at all",
        System.Text.Json.JsonValueKind.Null => "null",
        System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => $"the boolean {value.GetRawText()}",
        System.Text.Json.JsonValueKind.Number => $"the number {value.GetRawText()}, which is not a 32-bit integer",
        System.Text.Json.JsonValueKind.String => $"the string {value.GetRawText()}",
        System.Text.Json.JsonValueKind.Array => "an array",
        _ => "an object",
    };

    /// <summary>Reads a published <c>locationType</c> into the kind it names, or refuses the value.</summary>
    /// <param name="published">The record's <c>locationType</c>.</param>
    /// <param name="region">The record's region, for the message.</param>
    /// <param name="index">The record's index, for the message.</param>
    internal static DaggerfallSiteKind Resolve(int published, int region, int index) =>
        TryResolve(published, out DaggerfallSiteKind kind)
            ? kind
            : throw new InvalidOperationException(UnnameableMessage(published, region, index));
}
