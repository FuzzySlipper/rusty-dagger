using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Category of political board-game seat resolved from its faction binding.</summary>
internal enum DaggerfallBoardSeatKind
{
    Ruler = 0,
    MajorSeat = 1,
}

/// <summary>
/// Ruleset binding for one canonical political board-game seat (<c>seatId -&gt; { officeFactionId, title }</c>).
/// Intentionally carries NO holder, seat name, kind, or home region: the base pack's <c>world/iliac-bay.board.json</c>
/// owns seats, holders, and personas, and faction metadata comes from <c>factions.json</c> (no hand-kept copies).
/// </summary>
internal sealed record DaggerfallBoardSeatBinding(
    string SeatId,
    string SeatName,
    DaggerfallBoardSeatKind Kind,
    int OfficeFactionId,
    int? HomeRegion,
    string DefaultTitle);

/// <summary>Admitted ruleset table of political board-game seat bindings loaded from <c>daggerfall.base</c>.</summary>
internal sealed class DaggerfallBoardSeatSet
{
    private readonly Dictionary<string, DaggerfallBoardSeatBinding> _bySeatId;
    private readonly Dictionary<int, DaggerfallBoardSeatBinding> _byFactionId;

    internal static DaggerfallBoardSeatSet Empty { get; } = new([]);

    internal DaggerfallBoardSeatSet(IEnumerable<DaggerfallBoardSeatBinding> seats)
    {
        ArgumentNullException.ThrowIfNull(seats);
        List<DaggerfallBoardSeatBinding> list = [.. seats];
        All = list.AsReadOnly();
        _bySeatId = list.ToDictionary(seat => seat.SeatId, StringComparer.Ordinal);
        _byFactionId = list.ToDictionary(seat => seat.OfficeFactionId);
    }

    internal IReadOnlyList<DaggerfallBoardSeatBinding> All { get; }

    internal bool TryGetBySeatId(string seatId, out DaggerfallBoardSeatBinding? seat)
    {
        if (string.IsNullOrWhiteSpace(seatId))
        {
            seat = null;
            return false;
        }
        return _bySeatId.TryGetValue(seatId, out seat);
    }

    internal bool TryGetByFactionId(int officeFactionId, out DaggerfallBoardSeatBinding? seat) =>
        _byFactionId.TryGetValue(officeFactionId, out seat);
}

internal static partial class DaggerfallBaseContent
{
    private const int ProvinceFactionTypeId = 7;

    private static DaggerfallBoardSeatSet ReadBoardSeats(
        JsonElement root,
        DaggerfallFactionsSet factions,
        DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("boardSeats", out JsonElement section) || section.ValueKind == JsonValueKind.Null)
            return DaggerfallBoardSeatSet.Empty;

        JsonElement obj = Object(section, "boardSeats", diagnostics);
        if (obj.ValueKind != JsonValueKind.Object)
            return DaggerfallBoardSeatSet.Empty;

        List<DaggerfallBoardSeatBinding> seats = [];
        HashSet<string> seenIds = new(StringComparer.Ordinal);
        HashSet<int> seenFactions = [];

        foreach (JsonElement item in Array(obj, "seats", diagnostics))
        {
            JsonElement seatObj = Object(item, "boardSeats.seat", diagnostics);
            if (seatObj.ValueKind != JsonValueKind.Object) continue;
            RejectDuplicateProperties(seatObj, "boardSeats.seat", diagnostics);

            string id = Text(seatObj, "id", diagnostics);
            if (!ValidId(id))
                diagnostics.Add($"Board seat '{id}' has an invalid id.");
            if (seatObj.TryGetProperty("holder", out _) || seatObj.TryGetProperty("defaultCastId", out _))
                diagnostics.Add($"Board seat '{id}' must not declare a holder or defaultCastId; iliac-bay.board.json owns seat holders.");
            if (seatObj.TryGetProperty("name", out _) || seatObj.TryGetProperty("kind", out _) || seatObj.TryGetProperty("homeRegion", out _))
                diagnostics.Add($"Board seat '{id}' should only declare id, officeFactionId and title; iliac-bay.board.json and factions.json own seat names, kinds and regions.");

            int officeFactionId = Integer(seatObj, "officeFactionId", diagnostics);
            string title = Text(seatObj, "title", diagnostics);

            if (string.IsNullOrWhiteSpace(title))
                diagnostics.Add($"Board seat '{id}' requires a non-empty title.");

            DaggerfallFactionDefinition? faction = null;
            if (factions.Factions.Count > 0 && !factions.Factions.TryGetValue(officeFactionId, out faction))
                diagnostics.Add($"Board seat '{id}' references missing faction {officeFactionId}.");

            if (!seenIds.Add(id))
            {
                diagnostics.Add($"Board seats catalog repeats seat '{id}'.");
                continue;
            }
            if (!seenFactions.Add(officeFactionId))
            {
                diagnostics.Add($"Board seats catalog assigns faction {officeFactionId} to multiple seats.");
                continue;
            }

            // Derive baseline display name, kind, and home region from factions.json until B2 supplies BoardSetup.Canon().
            string seatName = faction?.Name ?? id;
            bool isProvince = faction is { Type: ProvinceFactionTypeId };
            DaggerfallBoardSeatKind kind = isProvince && !string.Equals(id, "balfiera", StringComparison.Ordinal)
                ? DaggerfallBoardSeatKind.Ruler
                : DaggerfallBoardSeatKind.MajorSeat;
            int? homeRegion = isProvince && faction is { Region: >= 0 } ? faction.Region : null;

            seats.Add(new DaggerfallBoardSeatBinding(id, seatName, kind, officeFactionId, homeRegion, title));
        }

        return new DaggerfallBoardSeatSet(seats);
    }
}
