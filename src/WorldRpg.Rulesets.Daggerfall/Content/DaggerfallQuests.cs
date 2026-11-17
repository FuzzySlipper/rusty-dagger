using System.Collections.ObjectModel;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Whether a quest record may run, as the quests reader decides it at load.</summary>
internal enum DaggerfallQuestDisposition
{
    /// <summary>The record is the one source its name resolves to.</summary>
    Compiled,
    /// <summary>The record's diagnostics keep it from running, such as a name another record shares.</summary>
    Diagnosed,
}

/// <summary>One quest record: its identity, messages and blocks.</summary>
/// <param name="Name">The quest name, unique among the selected records.</param>
/// <param name="DisplayName">The display name, empty when the record states none.</param>
/// <param name="SourceFile">The record's identity, which saves name the quest by.</param>
/// <param name="Disposition">Whether the record may run.</param>
/// <param name="Messages">The QRC message blocks.</param>
/// <param name="Blocks">The QBN blocks.</param>
/// <param name="Diagnostics">Why a diagnosed record may not run.</param>
internal sealed record DaggerfallQuestSourceDefinition(
    string Name,
    string DisplayName,
    string SourceFile,
    DaggerfallQuestDisposition Disposition,
    IReadOnlyList<DaggerfallQuestMessageDefinition> Messages,
    IReadOnlyList<DaggerfallQuestBlockDefinition> Blocks,
    IReadOnlyList<DaggerfallQuestDiagnosticDefinition> Diagnostics)
{
    /// <summary>Whether a source is one of the main quests whose finished instances are never tombstoned away.</summary>
    internal static bool IsProtectedMainQuest(string sourceFile)
    {
        string name = Path.GetFileNameWithoutExtension(sourceFile);
        return name.Equals("S0000999", StringComparison.OrdinalIgnoreCase)
            || name.Equals("S0000977", StringComparison.OrdinalIgnoreCase)
            || name.Equals("_BRISIEN", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>One message block: its id and lines.</summary>
internal sealed record DaggerfallQuestMessageDefinition(int Id, IReadOnlyList<string> Lines);

/// <summary>One QBN block: its kind, ordered lines and global link.</summary>
/// <param name="Kind">The block kind.</param>
/// <param name="FirstLine">
/// The label of the block's first line; line <c>n</c> of the block is labelled <c>FirstLine + n</c>. Labels are the
/// quest's own line numbers, not positions in a file: headless and persist tasks, and the state a save keeps for one
/// action (a disease, a foe's spell queue, a keyed draw, a child quest), are named by them, so a save made mid-quest
/// finds its state again only while they stay as they are.
/// </param>
/// <param name="Lines">The ordered lines, compiled by the quest runner.</param>
/// <param name="Global">The linked global key, if any.</param>
internal sealed record DaggerfallQuestBlockDefinition(string Kind, int FirstLine, IReadOnlyList<string> Lines, int? Global = null);

/// <summary>One quest diagnostic: the labelled line, its text and why the quest may not run.</summary>
internal sealed record DaggerfallQuestDiagnosticDefinition(int Line, string Text, string Reason);

internal sealed record DaggerfallQuestResourceDefinition(string Quest, string SourceFile, int SourceLine, string Kind, string SourceSpelling, string CanonicalId, string SourceText, string? TargetSourceSpelling, string? TargetCanonicalId, string? PlaceKind, IReadOnlyList<string> Parameters,
    DaggerfallQuestFoeOptions? Foe = null, DaggerfallQuestItemOptions? Item = null,
    DaggerfallQuestPersonOptions? Person = null, IReadOnlyList<string>? Sites = null);
internal sealed record DaggerfallQuestFoeOptions(int Count);
internal sealed record DaggerfallQuestItemOptions(bool Artifact, int? Class, int? Subclass, int? Template, int? Key, int? RangeLow, int? RangeHigh, string? UsedMessage, string? AnyInfoMessage);
internal sealed record DaggerfallQuestPersonOptions(string? Named, string? Faction, string? FactionType, string? Group, int? Face, string? Gender, string? Scope, bool AtHome);
internal sealed record DaggerfallQuestUnresolvedReferenceDefinition(string Quest, string SourceFile, int SourceLine, string SourceSpelling, string CanonicalId);

/// <summary>
/// The quests the selected quests packs carry: each record by its source file, the tables and catalog they resolve
/// through, the corpora that say how each is offered, and the resource declarations read from the records' lines.
/// </summary>
internal sealed record DaggerfallQuestSourceSet(IReadOnlyDictionary<string, DaggerfallQuestSourceDefinition> Quests, DaggerfallQuestTables Tables, DaggerfallQuestCatalog Catalog, IReadOnlyList<DaggerfallQuestCorpusDefinition> Corpora)
{
    /// <summary>No quests, for a bundle that selects no quests pack.</summary>
    internal static DaggerfallQuestSourceSet None { get; } = new(
        new ReadOnlyDictionary<string, DaggerfallQuestSourceDefinition>(new Dictionary<string, DaggerfallQuestSourceDefinition>(StringComparer.Ordinal)),
        DaggerfallQuestTables.None, new DaggerfallQuestCatalog([]), []);

    private readonly (IReadOnlyList<DaggerfallQuestResourceDefinition> Declarations, IReadOnlyList<DaggerfallQuestUnresolvedReferenceDefinition> Unresolved) _resources =
        DaggerfallQuestResourceDeclarations.Read(Quests.Values);

    /// <summary>Every resource a compiled record declares, ordered by source file and line.</summary>
    internal IReadOnlyList<DaggerfallQuestResourceDefinition> Resources => _resources.Declarations;

    /// <summary>Every placement that names a symbol its record does not declare.</summary>
    internal IReadOnlyList<DaggerfallQuestUnresolvedReferenceDefinition> UnresolvedReferences => _resources.Unresolved;

    internal DaggerfallQuestSourceDefinition Resolve(string sourceFile) => Quests[sourceFile];
}

internal sealed record DaggerfallQuestCatalogRow(string Name, string Group, string? Membership,
    int MinimumRequirement, string RequirementKind, bool Adult, bool OneTime, bool Active);

internal sealed record DaggerfallQuestCatalog(IReadOnlyList<DaggerfallQuestCatalogRow> Rows);

/// <summary>One corpus: a named list of categories.</summary>
internal sealed record DaggerfallQuestCorpusDefinition(string Id, IReadOnlyList<DaggerfallQuestCategoryDefinition> Categories);

/// <summary>
/// One corpus category: how its quests are offered and which they are, in order. A category that names a catalog
/// group holds present rows of that group and activity; one that names none holds quests started by name.
/// </summary>
internal sealed record DaggerfallQuestCategoryDefinition(string Id, string Availability, IReadOnlyList<string> Names, string? CatalogGroup = null, bool Active = true);
