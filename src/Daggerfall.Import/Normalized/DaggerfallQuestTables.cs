using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>A source row, retaining aliases and its original one-based line.</summary>
public sealed record DaggerfallQuestTableRow(int Id, string Name, int SourceLine);

/// <summary>Named numeric identities with source provenance; aliases share numeric identity.</summary>
public sealed record DaggerfallQuestTable(PublishedSource Source, IReadOnlyList<DaggerfallQuestTableRow> Rows)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, int> Lookup => Rows.GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
}

public sealed record DaggerfallQuestTables(DaggerfallQuestTable Globals, DaggerfallQuestTable StaticMessages,
    DaggerfallQuestPlaces Places, DaggerfallQuestTable Sounds,
    DaggerfallQuestTable Diseases, DaggerfallQuestTable Spells, DaggerfallQuestActorItemTables ActorItemTables);
