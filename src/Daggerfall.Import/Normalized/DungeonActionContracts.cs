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
    NormalizedVector3? Position = null)
{
    public NormalizedDungeonAction Canonicalize() => this;

    public void Validate(IReadOnlySet<string> actionIds, IReadOnlySet<string> doorIds)
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        if (SourceOffset <= 0) throw new ArgumentOutOfRangeException(nameof(SourceOffset));
        Position?.Validate(nameof(Position));
        // Retain the RDB's raw link sentinel exactly; only positive absolute offsets can resolve to a node.
        if (NextActionId is not null)
        {
            NormalizedImportDocument.RequireLogicalId(NextActionId, nameof(NextActionId));
            if (!actionIds.Contains(NextActionId))
                throw new InvalidOperationException($"Normalized dungeon action '{Id}' links to unknown action '{NextActionId}'.");
            if (NextObjectOffset <= 0)
                throw new InvalidOperationException($"Normalized dungeon action '{Id}' resolves a target while preserving non-link source offset {NextObjectOffset}.");
        }

        if (DoorId is not null)
        {
            NormalizedImportDocument.RequireLogicalId(DoorId, nameof(DoorId));
            if (!doorIds.Contains(DoorId))
                throw new InvalidOperationException($"Normalized dungeon action '{Id}' refers to unknown door '{DoorId}'.");
        }
    }
}
