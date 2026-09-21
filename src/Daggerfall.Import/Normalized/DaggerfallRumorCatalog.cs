using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>How a rumor record's type is accounted for.</summary>
public enum DaggerfallRumorTypeDisposition
{
    /// <summary>The donor's rumor-type table names the type.</summary>
    Known,
    /// <summary>The donor's table names no such type; the record keeps its number.</summary>
    Unknown,
}

/// <summary>One normalized rumor: who it concerns, where it circulates, and the text key it reads through.</summary>
/// <param name="Index">The record's ordinal in the file.</param>
/// <param name="Type">The rumor type, in the donor's rumor-type numbering.</param>
/// <param name="TypeName">The donor's name for the type, empty when the table names none.</param>
/// <param name="TypeDisposition">Whether the donor's table names the type.</param>
/// <param name="Region">The region the rumor circulates in.</param>
/// <param name="Flags">The donor's import flags, verbatim.</param>
/// <param name="IsQuestRumor">Whether the quest-rumor bit is set, which the donor does not import.</param>
/// <param name="IsSignMessage">Whether the sign-message bit is set, which the donor does not import.</param>
/// <param name="Faction1">The first faction the rumor concerns; no faction catalog resolves it yet.</param>
/// <param name="Faction2">The second faction the rumor concerns; no faction catalog resolves it yet.</param>
/// <param name="QuestId">The quest the rumor belongs to, zero for none.</param>
/// <param name="QuestName">The quest's name field, empty for none.</param>
/// <param name="NpcId">The NPC a post-quest greeting addresses, zero for none.</param>
/// <param name="TimeLimit">The days the rumor stays current.</param>
/// <param name="TextKey">The text key the rumor's text resolves through.</param>
public sealed record DaggerfallRumorEntry(
    int Index,
    int Type,
    string TypeName,
    DaggerfallRumorTypeDisposition TypeDisposition,
    int Region,
    int Flags,
    bool IsQuestRumor,
    bool IsSignMessage,
    int Faction1,
    int Faction2,
    int QuestId,
    string QuestName,
    int NpcId,
    int TimeLimit,
    string TextKey)
{
    public void Validate()
    {
        if (Index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Index), Index, "A published rumor carries a negative ordinal.");
        }

        if (TypeDisposition == DaggerfallRumorTypeDisposition.Known && string.IsNullOrWhiteSpace(TypeName))
        {
            throw new ArgumentException($"Rumor {Index} names a known type with no name.", nameof(TypeName));
        }

        if (TypeDisposition == DaggerfallRumorTypeDisposition.Unknown && TypeName.Length != 0)
        {
            throw new ArgumentException($"Rumor {Index} names an unknown type '{TypeName}'.", nameof(TypeName));
        }

        if (Region is < 0 or > 61)
        {
            throw new ArgumentOutOfRangeException(nameof(Region), Region, $"Rumor {Index} circulates in region {Region} outside the 62 classic regions.");
        }

        NormalizedImportDocument.RequireLogicalId(TextKey, nameof(TextKey));
    }
}

/// <summary>The normalized rumor catalog: every rumor the source states, with text keys per record.</summary>
/// <param name="Source">The source the catalog was read from.</param>
/// <param name="Entries">The entries, in file order.</param>
public sealed record DaggerfallRumorCatalog(DaggerfallTextSource Source, IReadOnlyList<DaggerfallRumorEntry> Entries)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Source);
        Source.Validate();
        if (Source.Kind != DaggerfallTextKind.Rumor)
        {
            throw new InvalidOperationException($"Rumor catalog cites source '{Source.Path}' of family '{Source.Kind}'.");
        }

        NormalizedImportDocument.ValidateUnique(Entries, entry => entry.Index.ToString(), "rumor entries");
        foreach (DaggerfallRumorEntry entry in Entries)
        {
            entry.Validate();
        }

        if (Entries.Count != Source.Records)
        {
            throw new InvalidOperationException($"Rumor source '{Source.Path}' declares {Source.Records} records and publishes {Entries.Count}.");
        }
    }
}

/// <summary>
/// Builds the normalized rumor catalog from the classic rumor file. Text values join the shared
/// text section through the returned records; the catalog keeps the region, type, faction and
/// quest references dialogue, faction and quest consumers resolve through.
/// </summary>
public static class DaggerfallRumorCatalogBuilder
{
    public const string FamilyId = "CNT-014";

    /// <summary>The donor's rumor-type names by number.</summary>
    public static readonly IReadOnlyDictionary<int, string> TypeNames = new Dictionary<int, string>
    {
        [4] = "Plague",
        [7] = "Famine",
        [10] = "WitchBurnings",
        [11] = "CrimeWave",
        [12] = "NewRuler",
        [18] = "PersecutedTemple",
        [26] = "AllianceSignMessage",
        [27] = "EnemySignMessage",
        [28] = "WarSignMessage",
        [100] = "FactionRumor",
    };

    /// <summary>The quest-rumor bit, which the donor does not import.</summary>
    public const int QuestRumorFlag = 4;

    /// <summary>The sign-message bit, which the donor does not import.</summary>
    public const int SignMessageFlag = 1;

    /// <summary>Builds the catalog and the text records the rumor texts resolve through.</summary>
    public static (DaggerfallRumorCatalog Catalog, IReadOnlyList<DaggerfallTextRecord> Records) Build(
        ReadOnlySpan<byte> bytes,
        string label,
        IReadOnlyList<SourceInventoryRow> inventory,
        string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        string recordId = RequireFile(inventory, label);
        RumorCatalog catalog = RumorReader.Read(bytes, label);
        List<DaggerfallRumorEntry> entries = [];
        List<DaggerfallTextRecord> records = [];
        foreach (RumorRecord rumor in catalog.Records)
        {
            string id = $"{rumor.Index:D2}-{rumor.Region:D2}";
            bool known = TypeNames.TryGetValue(rumor.Type, out string? typeName);
            entries.Add(new DaggerfallRumorEntry(
                rumor.Index,
                rumor.Type,
                known ? typeName! : string.Empty,
                known ? DaggerfallRumorTypeDisposition.Known : DaggerfallRumorTypeDisposition.Unknown,
                rumor.Region,
                rumor.Flags,
                (rumor.Flags & QuestRumorFlag) != 0,
                (rumor.Flags & SignMessageFlag) != 0,
                rumor.Faction1,
                rumor.Faction2,
                rumor.QuestId,
                rumor.QuestName,
                rumor.NpcId,
                rumor.TimeLimit,
                new DaggerfallTextKey(DaggerfallTextKind.Rumor, id).ToString()));
            records.Add(FamilyTextRecords.Tokens(
                DaggerfallTextKind.Rumor, id, label, rumor.Index, rumor.TextOffset, rumor.TextLength, rumor.Tokens));
        }

        DaggerfallRumorCatalog published = new(
            new DaggerfallTextSource(DaggerfallTextKind.Rumor, recordId, label, language, bytes.Length, 0, records.Count),
            entries);
        published.Validate();
        foreach (DaggerfallTextRecord record in records)
        {
            record.Validate();
        }

        return (published, records);
    }

    private static string RequireFile(IReadOnlyList<SourceInventoryRow> inventory, string label)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return inventory.FirstOrDefault(row => row.FamilyId == FamilyId && StringComparer.Ordinal.Equals(row.PathOrPattern, label))?.Id
            ?? throw new InvalidOperationException($"The documented inventory does not carry '{label}', so the rumor catalog cites no provenance.");
    }
}
