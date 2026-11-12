using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>Source parameters remain intact even when an alias resolves to a corrected place.</summary>
public sealed record DaggerfallQuestPlace(string Name, string CanonicalName, int P1, int P2, int P3,
    uint? LocationKey, byte? TeleportTransfer, int SourceLine);

public sealed record DaggerfallQuestPlaces(PublishedSource Source, IReadOnlyList<DaggerfallQuestPlace> Rows);
