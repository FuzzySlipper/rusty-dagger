namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>How a quest source is accounted for by the offline textual compiler.</summary>
internal enum DaggerfallQuestDisposition
{
    /// <summary>Its sections and top-level blocks compiled to source records without diagnostics.</summary>
    Compiled,
    /// <summary>Its compiler diagnostics keep the source from becoming an admitted record.</summary>
    Diagnosed,
}

/// <summary>One normalized quest source: its identity, spans and blocks.</summary>
/// <param name="Name">The quest name, unique per source file.</param>
/// <param name="DisplayName">The display name, empty when the source states none.</param>
/// <param name="SourceFile">The source file name.</param>
/// <param name="Disposition">Whether the source compiled without diagnostics.</param>
/// <param name="Messages">The QRC message blocks.</param>
/// <param name="Blocks">The QBN blocks.</param>
/// <param name="Diagnostics">The unknown-signature diagnostics with source lines.</param>
internal sealed record DaggerfallQuestSourceDefinition(
    string Name,
    string DisplayName,
    string SourceFile,
    DaggerfallQuestDisposition Disposition,
    IReadOnlyList<DaggerfallQuestMessageDefinition> Messages,
    IReadOnlyList<DaggerfallQuestBlockDefinition> Blocks,
    IReadOnlyList<DaggerfallQuestDiagnosticDefinition> Diagnostics);

/// <summary>One normalized message block: its id, first line and lines.</summary>
/// <param name="Id">The message id.</param>
/// <param name="FirstLine">The 1-based first line.</param>
/// <param name="Lines">The message lines.</param>
internal sealed record DaggerfallQuestMessageDefinition(int Id, int FirstLine, IReadOnlyList<string> Lines);

/// <summary>One normalized QBN block: its kind, ordered source lines and global link.</summary>
/// <param name="Kind">The block kind.</param>
/// <param name="FirstLine">The 1-based first line.</param>
/// <param name="Lines">The ordered source lines retained for later action compilation.</param>
/// <param name="Global">The linked global key, if any.</param>
internal sealed record DaggerfallQuestBlockDefinition(string Kind, int FirstLine, IReadOnlyList<string> Lines, int? Global);

/// <summary>One quest diagnostic: what the source states that no signature claims.</summary>
/// <param name="Line">The 1-based source line.</param>
/// <param name="Text">The offending line.</param>
/// <param name="Reason">Why no signature claims it.</param>
internal sealed record DaggerfallQuestDiagnosticDefinition(int Line, string Text, string Reason);

/// <summary>The normalized quest source pack, loaded from the pack alone.</summary>
/// <param name="Quests">The quests by source file.</param>
internal sealed record DaggerfallQuestResourceDefinition(string Quest, string SourceFile, int SourceLine, string Kind, string SourceSpelling, string CanonicalId, string SourceText, string? TargetSourceSpelling, string? TargetCanonicalId, string? PlaceKind, IReadOnlyList<string> Parameters,
    DaggerfallQuestFoeOptions? Foe = null, DaggerfallQuestItemOptions? Item = null,
    DaggerfallQuestPersonOptions? Person = null, IReadOnlyList<string>? Sites = null);
internal sealed record DaggerfallQuestFoeOptions(int Count);
internal sealed record DaggerfallQuestItemOptions(bool Artifact, int? Class, int? Subclass, int? Template, int? Key, int? RangeLow, int? RangeHigh, string? UsedMessage, string? AnyInfoMessage);
internal sealed record DaggerfallQuestPersonOptions(string? Named, string? Faction, string? FactionType, string? Group, int? Face, string? Gender, string? Scope, bool AtHome);
internal sealed record DaggerfallQuestUnresolvedReferenceDefinition(string Quest, string SourceFile, int SourceLine, string SourceSpelling, string CanonicalId);

internal sealed record DaggerfallQuestSourceSet(IReadOnlyDictionary<string, DaggerfallQuestSourceDefinition> Quests, DaggerfallQuestTables Tables, DaggerfallQuestCatalog Catalog, IReadOnlyList<DaggerfallQuestResourceDefinition>? ResourceDeclarations = null, IReadOnlyList<DaggerfallQuestUnresolvedReferenceDefinition>? UnresolvedReferences = null)
{
    /// <summary>
    /// Resolves a quest source by file: its messages, blocks and compilation disposition. A
    /// diagnosed quest resolves with its diagnostics attached. Quest lifecycle/action execution
    /// belongs to a later owner; this content model only exposes normalized source records.
    /// </summary>
    internal IReadOnlyList<DaggerfallQuestResourceDefinition> Resources { get; } = ResourceDeclarations ?? [];
    internal IReadOnlyList<DaggerfallQuestUnresolvedReferenceDefinition> UnresolvedReferences { get; } = UnresolvedReferences ?? [];
    internal DaggerfallQuestSourceDefinition Resolve(string sourceFile) => Quests[sourceFile];
}

internal sealed record DaggerfallQuestCatalogRow(string Name, string Group, string? Membership,
    int MinimumRequirement, string RequirementKind, bool Adult, bool OneTime, bool Active,
    string SourceDisposition, string Notes, int SourceLine);

internal sealed record DaggerfallQuestCatalog(string SourcePath, IReadOnlyList<DaggerfallQuestCatalogRow> Rows);
