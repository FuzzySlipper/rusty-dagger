using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Normalized;

public enum NormalizedQuestMarkerKind { Spawn, Item }

/// <summary>A source-order quest allocation point, independent of live actor or spatial state.</summary>
public sealed record NormalizedQuestMarker(string Id, NormalizedQuestMarkerKind Kind, NormalizedVector3 Position)
{
    public string SourceKey { get; init; } = string.Empty;
    public int? BuildingIndex { get; init; }
    public int SourceOrdinal { get; init; }
    public int BlockX { get; init; }
    public int BlockZ { get; init; }
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        if (!Enum.IsDefined(Kind)) throw new InvalidOperationException($"Quest marker '{Id}' has an invalid kind.");
        Position.Validate(nameof(Position));
        NormalizedImportDocument.RequireLogicalId(SourceKey, nameof(SourceKey));
        if (BuildingIndex is < 0) throw new InvalidOperationException($"Quest marker '{Id}' has an invalid building ordinal.");
        if (SourceOrdinal < 0) throw new InvalidOperationException($"Quest marker '{Id}' has a negative source ordinal.");
    }
}

internal static class QuestMarkerNormalization
{
    internal static NormalizedQuestMarker? Read(string id, int archive, int record, NormalizedVector3 position,
        string sourceKey, int? buildingIndex, int sourceOrdinal, int blockX = 0, int blockZ = 0)
    {
        return KindOf(archive, record) is { } selected ? new(id, selected, position)
        { SourceKey = sourceKey, BuildingIndex = buildingIndex, SourceOrdinal = sourceOrdinal, BlockX = blockX, BlockZ = blockZ } : null;
    }

    /// <summary>The quest marker an editor flat's texture names, or null when it names none.</summary>
    internal static NormalizedQuestMarkerKind? KindOf(int archive, int record) => archive != RdbSourceClassification.EditorFlatArchive
        ? null
        : record switch
        {
            RdbSourceClassification.QuestSpawnMarkerRecord => NormalizedQuestMarkerKind.Spawn,
            RdbSourceClassification.QuestItemMarkerRecord => NormalizedQuestMarkerKind.Item,
            _ => null,
        };
}
