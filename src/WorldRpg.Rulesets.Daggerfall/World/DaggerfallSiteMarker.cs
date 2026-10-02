using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

internal enum DaggerfallSiteMarkerKind
{
    Entrance,
    Portal,
    QuestSpawn,
    QuestItem,
}

/// <summary>A stable source marker admitted with one selected world profile.</summary>
internal sealed record DaggerfallSiteMarker(
    string Id,
    DaggerfallSiteMarkerKind Kind,
    WorldPoint Position,
    string? DestinationLogicalProfile = null)
{
    internal int? SourceOrdinal { get; init; }
    internal int BlockX { get; init; }
    internal int BlockZ { get; init; }
    internal DaggerfallSiteMarker Validate()
    {
        if (!DaggerfallBaseContent.ValidId(Id))
            throw new ArgumentException("Site markers must use stable source ids.", nameof(Id));
        if (!Enum.IsDefined(Kind)) throw new ArgumentOutOfRangeException(nameof(Kind));
        if (Kind is DaggerfallSiteMarkerKind.QuestSpawn or DaggerfallSiteMarkerKind.QuestItem && SourceOrdinal is null or < 0)
            throw new ArgumentException("A quest marker requires its source flat ordinal.");
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z))
            throw new ArgumentOutOfRangeException(nameof(Position));

        if (Kind != DaggerfallSiteMarkerKind.Portal)
        {
            if (DestinationLogicalProfile is not null)
                throw new ArgumentException("A non-portal marker cannot name a destination profile.", nameof(DestinationLogicalProfile));
        }
        else if (string.IsNullOrWhiteSpace(DestinationLogicalProfile)
            || !DaggerfallBaseContent.ValidId(DestinationLogicalProfile.Replace('/', '-')))
        {
            throw new ArgumentException("A portal marker must name its stable destination profile.", nameof(DestinationLogicalProfile));
        }

        return this;
    }
}

