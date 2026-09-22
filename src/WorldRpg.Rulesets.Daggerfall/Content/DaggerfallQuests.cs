namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>How a quest source is accounted for.</summary>
internal enum DaggerfallQuestDisposition
{
    Runnable,
    Diagnosed,
}

/// <summary>One normalized quest source: its identity, spans and blocks.</summary>
/// <param name="Name">The quest name, unique per source file.</param>
/// <param name="DisplayName">The display name, empty when the source states none.</param>
/// <param name="SourceFile">The source file name.</param>
/// <param name="Disposition">Whether the quest may run.</param>
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

/// <summary>One normalized QBN block: its kind, lines and global link.</summary>
/// <param name="Kind">The block kind.</param>
/// <param name="FirstLine">The 1-based first line.</param>
/// <param name="Lines">The block lines, verbatim.</param>
/// <param name="Global">The linked global key, if any.</param>
internal sealed record DaggerfallQuestBlockDefinition(string Kind, int FirstLine, IReadOnlyList<string> Lines, int? Global);

/// <summary>One quest diagnostic: what the source states that no signature claims.</summary>
/// <param name="Line">The 1-based source line.</param>
/// <param name="Text">The offending line.</param>
/// <param name="Reason">Why no signature claims it.</param>
internal sealed record DaggerfallQuestDiagnosticDefinition(int Line, string Text, string Reason);

/// <summary>The normalized quest source pack, loaded from the pack alone.</summary>
/// <param name="Quests">The quests by source file.</param>
internal sealed record DaggerfallQuestSourceSet(IReadOnlyDictionary<string, DaggerfallQuestSourceDefinition> Quests)
{
    /// <summary>
    /// Resolves a quest source by file: its messages, blocks and whether it may run. A
    /// diagnosed quest resolves with its diagnostics attached; running it belongs to the
    /// quest lifecycle owner, which refuses diagnosed quests.
    /// </summary>
    internal DaggerfallQuestSourceDefinition Resolve(string sourceFile) => Quests[sourceFile];
}
