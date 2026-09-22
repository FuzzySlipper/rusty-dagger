namespace Daggerfall.Import.Arena2;

/// <summary>Which QBN item a block is: the donor's closed set of line signatures.</summary>
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

/// <summary>One QBN block: its kind, lines and global link.</summary>
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

/// <summary>
/// Reads donor-shaped quest text into source records. Offsets on refusals are 1-based source
/// line numbers, because a quest diagnostic names lines rather than bytes. The grammar follows
/// the donor's first
/// pass exactly: quest/displayname/qrc/qbn markers, dash comments skipped outside QRC, empty
/// QRC or QBN refused, and a QBN line no branch claims refused rather than carried. Message
/// ids follow the donor's bracket rule; QBN blocks follow its branch order with the headless
/// entry point taken once.
/// </summary>
public static class QuestSourceReader
{
    /// <summary>Reads one quest text source.</summary>
    /// <param name="globalKeys">The quest global names to keys a global-link line starts with.</param>
    public static QuestSourceDocument Read(string text, string fileName, IReadOnlyList<string> messageNames, IReadOnlyDictionary<string, int> globalKeys)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(messageNames);
        ArgumentNullException.ThrowIfNull(globalKeys);
        string[] lines = text.Split('\n');
        string questName = string.Empty;
        int questNameLine = 0;
        string displayName = string.Empty;
        List<QuestHeaderField> header = [];
        List<string> qrcLines = [];
        List<string> qbnLines = [];
        List<int> qbnNumbers = [];
        bool inQrc = false;
        bool inQbn = false;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');
            string trimmed = line.Trim();
            if (trimmed.StartsWith("quest:", StringComparison.OrdinalIgnoreCase))
            {
                questName = FieldValue(trimmed);
                questNameLine = index + 1;
                header.Add(new QuestHeaderField("quest", questName));
            }
            else if (trimmed.StartsWith("displayname:", StringComparison.OrdinalIgnoreCase))
            {
                displayName = FieldValue(trimmed);
                header.Add(new QuestHeaderField("displayname", displayName));
            }
            else if (trimmed.StartsWith("qrc:", StringComparison.OrdinalIgnoreCase))
            {
                inQrc = true;
                inQbn = false;
                continue;
            }
            else if (trimmed.StartsWith("qbn:", StringComparison.OrdinalIgnoreCase))
            {
                inQrc = false;
                inQbn = true;
                continue;
            }

            if (inQrc)
            {
                qrcLines.Add(line);
            }
            else if (inQbn)
            {
                if (trimmed.StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }

                qbnLines.Add(line);
                qbnNumbers.Add(index + 1);
            }
            else if (trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }
        }

        if (questName.Length == 0)
        {
            throw new Arena2FormatException(fileName, 0, "Quest source states no quest name.");
        }

        if (qrcLines.Count == 0)
        {
            throw new Arena2FormatException(fileName, 0, "Quest source states no QRC section.");
        }

        if (qbnLines.Count == 0)
        {
            throw new Arena2FormatException(fileName, 0, "Quest source states no QBN section.");
        }

        return new QuestSourceDocument(fileName, questName, questNameLine, displayName, header, ReadMessages(qrcLines, fileName, messageNames), ReadBlocks(qbnLines, qbnNumbers, fileName, globalKeys));
    }

    private static string FieldValue(string line)
    {
        int colon = line.IndexOf(':');
        return colon < 0 ? string.Empty : line[(colon + 1)..].Trim();
    }

    private static IReadOnlyList<QuestMessageBlock> ReadMessages(List<string> lines, string fileName, IReadOnlyList<string> messageNames)
    {
        List<QuestMessageBlock> messages = [];
        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index].TrimEnd('\r');
            if (line.StartsWith("-", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // A message header names a known static field; anything else is content of the
            // open block, because content may stand past blank lines.
            if (!IsMessageHeader(line, messageNames))
            {
                throw new Arena2FormatException(fileName, index + 1, $"Quest QRC line opens no message: '{line.Trim()}'.");
            }

            int colon = line.IndexOf(':');
            string idText = line[(colon + 1)..].Trim();
            // Fixed message types carry their table id in brackets; the rest state it bare.
            if (idText.StartsWith("[", StringComparison.Ordinal) && idText.EndsWith("]", StringComparison.Ordinal))
            {
                idText = idText[1..^1].Trim();
            }

            if (!int.TryParse(idText, out int id))
            {
                throw new Arena2FormatException(fileName, index + 1, $"Quest QRC line states no message id: '{line.Trim()}'.");
            }

            int firstLine = index + 1;
            List<string> message = [];
            while (index + 1 < lines.Count)
            {
                string next = lines[index + 1].TrimEnd('\r');
                // Only a truly empty line is a block boundary candidate: a whitespace-only line is
                // content the donor keeps verbatim, which is why letters survive their spacing.
                if (next.Length == 0)
                {
                    // A blank ends the block only when the peek says so: end of stream, another
                    // blank, a comment, or a line carrying a colon. Otherwise it is a line break.
                    if (PeekMessageEnd(lines, index + 1))
                    {
                        break;
                    }

                    message.Add(" ");
                    index++;
                    continue;
                }

                if (IsMessageHeader(next, messageNames))
                {
                    break;
                }

                // The inner loop skips no comment lines: a dash line abutting content is content.
                message.Add(next);
                index++;
            }

            messages.Add(new QuestMessageBlock(id, firstLine, message));
        }

        return messages;
    }

    private static bool PeekMessageEnd(List<string> lines, int blank)
    {
        if (blank + 1 >= lines.Count)
        {
            return true;
        }

        string next = lines[blank + 1].TrimEnd('\r');
        return next.Contains(':') || next.StartsWith("-", StringComparison.Ordinal) || next.Length == 0;
    }

    private static bool IsMessageHeader(string line, IReadOnlyList<string> messageNames)
    {
        int colon = line.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        string field = line[..colon].Trim();
        return messageNames.Any(name => string.Equals(name, field, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<QuestBlock> ReadBlocks(List<string> lines, List<int> numbers, string fileName, IReadOnlyDictionary<string, int> globalKeys)
    {
        List<QuestBlock> blocks = [];
        bool headless = false;
        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index].TrimEnd('\r');
            if (line.Trim().Length == 0)
            {
                continue;
            }

            QuestBlockKind kind;
            int? global = null;
            if (Starts(line, "clock"))
            {
                kind = QuestBlockKind.Clock;
            }
            else if (Starts(line, "item"))
            {
                kind = QuestBlockKind.Item;
            }
            else if (Starts(line, "person"))
            {
                kind = QuestBlockKind.Person;
            }
            else if (Starts(line, "foe"))
            {
                kind = QuestBlockKind.Foe;
            }
            else if (Starts(line, "place"))
            {
                kind = QuestBlockKind.Place;
            }
            else if (Starts(line, "variable"))
            {
                kind = QuestBlockKind.Variable;
            }
            else if (line.Contains("task:", StringComparison.Ordinal)
                || (Starts(line, "until") && line.Contains("performed:", StringComparison.Ordinal)))
            {
                kind = QuestBlockKind.Task;
            }
            else if (IsGlobalReference(line, globalKeys, out int linked))
            {
                kind = QuestBlockKind.Global;
                global = linked;
            }
            else if (!headless)
            {
                kind = QuestBlockKind.Headless;
                headless = true;
            }
            else
            {
                throw new Arena2FormatException(fileName, numbers[index], $"Quest QBN line matches no block signature: '{line.Trim()}'.");
            }

            // Resource and variable declarations are single lines; only tasks, globals with a
            // body and the headless entry consume a block to the blank line.
            List<string> block;
            if (kind is QuestBlockKind.Task or QuestBlockKind.Headless
                || (kind == QuestBlockKind.Global && HasBody(lines, index, globalKeys)))
            {
                block = [line.Trim('\t')];
                while (index + 1 < lines.Count && lines[index + 1].Trim().Length != 0)
                {
                    block.Add(lines[++index].Trim('\t'));
                }
            }
            else
            {
                block = [line.Trim('\t')];
            }

            blocks.Add(new QuestBlock(kind, numbers[index - block.Count + 1], block, global));
        }

        return blocks;
    }

    private static bool HasBody(List<string> lines, int index, IReadOnlyDictionary<string, int> globalKeys)
    {
        if (index + 1 >= lines.Count)
        {
            return false;
        }

        return !IsNewTaskOrLineBreak(lines[index + 1], globalKeys);
    }

    private static bool IsNewTaskOrLineBreak(string line, IReadOnlyDictionary<string, int> globalKeys)
    {
        if (Starts(line, "variable")
            || line.Contains("task:", StringComparison.Ordinal)
            || (Starts(line, "until") && line.Contains("performed:", StringComparison.Ordinal))
            || string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        return IsGlobalReference(line, globalKeys, out _);
    }

    private static bool Starts(string line, string word) =>
        line.TrimStart().StartsWith(word, StringComparison.InvariantCultureIgnoreCase);

    private static bool IsGlobalReference(string line, IReadOnlyDictionary<string, int> globalKeys, out int key)
    {
        key = -1;
        string[] parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach ((string name, int linked) in globalKeys)
        {
            if (string.Equals(parts[0], name, StringComparison.OrdinalIgnoreCase))
            {
                key = linked;
                return true;
            }
        }

        return false;
    }
}
