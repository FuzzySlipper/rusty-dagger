using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>
/// Our quest tables, taken once from the donor's <c>Quests-*.txt</c> tables at one commit; each table cites the donor
/// file it was read from by path, digest and length.
/// </summary>
public sealed record QuestTablesInput(string Commit, DaggerfallQuestTables Tables);

/// <summary>Our classic quest list, taken once from the donor's <c>QuestList-Classic.txt</c> at one commit.</summary>
public sealed record QuestListInput(string Commit, DaggerfallQuestCatalog Catalog);

/// <summary>
/// One quest document of ours, taken once from a donor quest script: its header, QRC messages and top-level QBN
/// blocks with the script's own lines, or the line and reason the script was refused at. The donor's dash comments
/// outside QRC are not kept; every line the game reads is.
/// </summary>
public sealed record QuestDocumentInput(TakenFrom Source, QuestSourceDocument? Document, QuestRefusal? Refusal);

/// <summary>Where and why a donor quest script could not be read.</summary>
public sealed record QuestRefusal(int Line, string Reason);
