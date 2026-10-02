using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Normalized;

public enum NormalizedQuestMarkerKind { Spawn, Item }

/// <summary>A source-order quest allocation point, independent of live actor or spatial state.</summary>
public sealed record NormalizedQuestMarker(string Id, NormalizedQuestMarkerKind Kind, NormalizedVector3 Position)
{
    public int SourceOrdinal { get; init; }
    public int BlockX { get; init; }
    public int BlockZ { get; init; }
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(Id, nameof(Id));
        if (!Enum.IsDefined(Kind)) throw new InvalidOperationException($"Quest marker '{Id}' has an invalid kind.");
        Position.Validate(nameof(Position));
        if (SourceOrdinal < 0) throw new InvalidOperationException($"Quest marker '{Id}' has a negative source ordinal.");
    }
}

internal static class QuestMarkerNormalization
{
    internal static NormalizedQuestMarker? Read(string id, int archive, int record, NormalizedVector3 position)
    {
        if (archive != RdbSourceClassification.EditorFlatArchive) return null;
        return record switch
        {
            11 => new(id, NormalizedQuestMarkerKind.Spawn, position),
            18 => new(id, NormalizedQuestMarkerKind.Item, position),
            _ => null,
        };
    }
}
