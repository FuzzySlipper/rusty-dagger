namespace Daggerfall.Import.Arena2;

/// <summary>Which top-level QBN item a block is: the donor parser's closed set of line signatures.</summary>
public enum QuestBlockKind
{
    Clock,
    Item,
    Person,
    Foe,
    Place,
    Variable,
    Task,
    Global,
    Headless,
}

/// <summary>One header field: its name and value.</summary>
/// <param name="Name">The field name.</param>
/// <param name="Value">The field value.</param>
public sealed record QuestHeaderField(string Name, string Value);

/// <summary>One QRC message block: its id and lines.</summary>
/// <param name="Id">The message id.</param>
/// <param name="FirstLine">The 1-based first line.</param>
/// <param name="Lines">The message lines.</param>
public sealed record QuestMessageBlock(int Id, int FirstLine, IReadOnlyList<string> Lines);

/// <summary>
/// One QBN block: its top-level kind, ordered source lines and global link. Action-body lines
/// remain source records here; later ruleset-owned action work gives them executable meaning.
/// </summary>
/// <param name="Kind">The block kind.</param>
/// <param name="FirstLine">The 1-based first line.</param>
/// <param name="Lines">The block lines.</param>
/// <param name="Global">The linked global key, if any.</param>
public sealed record QuestBlock(QuestBlockKind Kind, int FirstLine, IReadOnlyList<string> Lines, int? Global);

/// <summary>One parsed quest source: its header, message blocks and QBN blocks.</summary>
/// <param name="FileName">The source file name.</param>
/// <param name="QuestName">The quest name.</param>
/// <param name="QuestNameLine">The 1-based line that states the quest name.</param>
/// <param name="DisplayName">The display name, empty when the source states none.</param>
/// <param name="Header">The header fields.</param>
/// <param name="Messages">The QRC message blocks.</param>
/// <param name="Blocks">The QBN blocks.</param>
public sealed record QuestSourceDocument(
    string FileName,
    string QuestName,
    int QuestNameLine,
    string DisplayName,
    IReadOnlyList<QuestHeaderField> Header,
    IReadOnlyList<QuestMessageBlock> Messages,
    IReadOnlyList<QuestBlock> Blocks);
