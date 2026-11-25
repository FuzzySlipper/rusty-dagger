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
    NormalizedQuaternion? NextObjectRotation = null)
{
    /// <summary>How the action moves its model, when it is a motion action; the runtime reads no source axis.</summary>
    public NormalizedDungeonMotion? Motion { get; init; }
}

public enum NormalizedDungeonMotionKind
{
    Translation,
    Rotation,
}

/// <summary>
/// One action's motion in the published right-handed frame: a translation in the block's frame, or a turn about an
/// axis in the model's own frame by a signed angle (a turn can exceed half a turn, which a quaternion cannot hold),
/// over a duration. A rotation whose source axis names none turns by nothing but still runs.
/// </summary>
public sealed record NormalizedDungeonMotion(
    NormalizedDungeonMotionKind Kind,
    NormalizedVector3 Translation,
    NormalizedVector3 RotationAxis,
    float RotationDegrees,
    float DurationSeconds);

/// <summary>
/// Explicit disposition of the original treasure-marker action whose poison variant is unknown.
/// Its raw index is a sound/source byte, never an inferred poison identity.
/// </summary>
public sealed record NormalizedDungeonPoisonAction(string SourceRecord, string Disposition, int? PoisonId = null);
