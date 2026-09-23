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
    internal int Region => Id.Region;

    internal int Index => Id.Index;

    /// <summary>Classic 1000 by 500 wilderness map pixel containing this site.</summary>
    internal int MapPixelX => Longitude / 128;
    internal int MapPixelY => 499 - (Latitude / 128);
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
    int MaxY);

internal static class DaggerfallSiteKinds
{
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
