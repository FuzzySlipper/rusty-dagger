namespace Daggerfall.Import.Normalized;

/// <summary>
/// One normalized RDB action node. Raw trigger/action values and source link
/// offsets remain available beside the stable resolved target identity; unknown
/// values are retained for a ruleset diagnostic rather than discarded by import.
/// </summary>
public sealed record NormalizedDungeonAction(
    string Id,
    int SourceOffset,
    uint TriggerFlag,
    byte ActionFlag,
    byte Axis,
    ushort Duration,
    ushort Magnitude,
    int NextObjectOffset,
    string? NextActionId,
    string? DoorId = null,
    bool IsFlat = false,
    byte SoundIndex = 0,
    NormalizedVector3? Position = null,
    byte RawIndex = 0,
    NormalizedDungeonPoisonAction? Poison = null,
    NormalizedVector3? NextObjectPosition = null,
    NormalizedVector3? NextObjectRotationDegrees = null);

/// <summary>
/// Explicit disposition of the original treasure-marker action whose poison variant is unknown.
/// Its raw index is a sound/source byte, never an inferred poison identity.
/// </summary>
public sealed record NormalizedDungeonPoisonAction(string SourceRecord, string Disposition, int? PoisonId = null);
