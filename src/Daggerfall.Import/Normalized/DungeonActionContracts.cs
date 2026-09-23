namespace Daggerfall.Import.Normalized;

/// <summary>
/// One action-bearing RDB model with model-local geometry and the source
/// transform that places it in the initial world pose. Its mesh artifacts are
/// admitted separately from the immutable dungeon mesh and collision closure.
/// </summary>
public sealed record NormalizedActionModelPlacement(
    string ActionId,
    string ModelId,
    string Description,
    ushort ModelIndex,
    byte RawIndex,
    string? DoorId,
    NormalizedVector3 Position,
    NormalizedVector3 RotationDegrees,
    NormalizedBounds LocalBounds,
    IReadOnlyList<string> MeshIds,
    string VisualArtifactId)
{
    public NormalizedActionModelPlacement Canonicalize() => this with
    {
        MeshIds = MeshIds.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
    };

    public void Validate(
        IReadOnlySet<string> actionIds,
        IReadOnlySet<string> doorIds,
        IReadOnlySet<string> meshIds,
        IReadOnlySet<string> artifactIds)
    {
        NormalizedImportDocument.RequireReference(ActionId, actionIds, nameof(ActionId));
        NormalizedImportDocument.RequireLogicalId(ModelId, nameof(ModelId));
        ArgumentNullException.ThrowIfNull(Description);
        NormalizedImportDocument.RequireReference(VisualArtifactId, artifactIds, nameof(VisualArtifactId));
        Position.Validate(nameof(Position));
        RotationDegrees.Validate(nameof(RotationDegrees));
        ArgumentNullException.ThrowIfNull(LocalBounds);
        LocalBounds.Validate();
        ArgumentNullException.ThrowIfNull(MeshIds);
        if (MeshIds.Count == 0)
            throw new InvalidOperationException($"Action model '{ActionId}' requires at least one local mesh.");
        NormalizedImportDocument.ValidateUnique(MeshIds, value => value, "action model mesh");
        foreach (string meshId in MeshIds)
            NormalizedImportDocument.RequireReference(meshId, meshIds, nameof(MeshIds));
        if (DoorId is not null)
            NormalizedImportDocument.RequireReference(DoorId, doorIds, nameof(DoorId));
    }
}

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
    byte RawIndex = 0)
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
