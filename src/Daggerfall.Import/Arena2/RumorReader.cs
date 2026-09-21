namespace Daggerfall.Import.Arena2;

/// <summary>One classic rumor: who it concerns, where it circulates, and the text it carries.</summary>
/// <param name="Index">The record's ordinal in the file, which is the source's order.</param>
/// <param name="Offset">The byte the record starts at.</param>
/// <param name="Faction1">The first faction the rumor concerns.</param>
/// <param name="Faction2">The second faction the rumor concerns.</param>
/// <param name="Type">The rumor type, in the donor's rumor-type numbering.</param>
/// <param name="Region">The region the rumor circulates in.</param>
/// <param name="Flags">The donor's import flags: quest rumor, sign message, and the rest.</param>
/// <param name="QuestId">The quest the rumor belongs to, zero for none.</param>
/// <param name="QuestName">The quest's name field, empty for none.</param>
/// <param name="Unknown">The unexplained field the record carries, retained rather than dropped.</param>
/// <param name="NpcId">The NPC a post-quest greeting addresses, zero for none.</param>
/// <param name="TextOffset">The byte the rumor's text starts at.</param>
/// <param name="TextLength">The bytes the rumor's text spans.</param>
/// <param name="TimeLimit">The days the rumor stays current.</param>
/// <param name="Tokens">The rumor text's token stream, in the classic text grammar.</param>
public sealed record RumorRecord(
    int Index,
    int Offset,
    int Faction1,
    int Faction2,
    int Type,
    int Region,
    int Flags,
    int QuestId,
    string QuestName,
    int Unknown,
    int NpcId,
    int TextOffset,
    int TextLength,
    int TimeLimit,
    IReadOnlyList<Arena2TextToken> Tokens);

/// <summary>Every rumor a rumor file declares, in file order.</summary>
/// <param name="Records">The records, in file order.</param>
public sealed record RumorCatalog(IReadOnlyList<RumorRecord> Records);

/// <summary>
/// Reads the classic rumor file exactly as the donor does: a sequence of fixed-shape records
/// closed by a length-delimited text, each text tokenized with the classic text grammar the
/// donor's own rumor import reads it through. Quest names ride a fixed nine-byte field that may
/// or may not be NUL-terminated.
/// </summary>
public static class RumorReader
{
    public const string FileName = "RUMOR.DAT";

    /// <summary>How many bytes the fixed part of one rumor record spans.</summary>
    public const int RecordHeaderBytes = 2 + 2 + 4 + 1 + 1 + 1 + 9 + 2 + 4 + 4 + 4;

    /// <summary>How many bytes the quest-name field spans.</summary>
    public const int QuestNameBytes = 9;

    /// <summary>A rumor type the donor's rumor-type table names.</summary>
    public static bool IsKnownType(int type) => type is 4 or 7 or 10 or 11 or 12 or 18 or 26 or 27 or 28 or 100;

    /// <summary>Reads every rumor, or reports the first byte that is not one.</summary>
    public static RumorCatalog Read(ReadOnlySpan<byte> bytes, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        // The byte array backs the token runs the records publish, so the reader copies what a
        // length prefix cannot prove is there rather than slicing a span that outlives the call.
        byte[] owned = bytes.ToArray();
        CheckedLittleEndianReader cursor = new(owned, label);
        List<RumorRecord> records = [];
        int index = 0;
        while (cursor.Position < owned.Length)
        {
            records.Add(ReadRecord(ref cursor, owned, label, index));
            index++;
        }

        return new RumorCatalog(records);
    }

    private static RumorRecord ReadRecord(ref CheckedLittleEndianReader cursor, byte[] owned, string label, int index)
    {
        int offset = cursor.Position;
        int faction1 = cursor.ReadUInt16();
        int faction2 = cursor.ReadUInt16();
        int type = checked((int)cursor.ReadUInt32());
        int region = cursor.ReadByte();
        int flags = cursor.ReadByte();
        int questId = cursor.ReadByte();
        string questName = ReadQuestName(ref cursor, label);
        int unknown = cursor.ReadUInt16();
        int npcId = checked((int)cursor.ReadUInt32());
        uint declaredLength = cursor.ReadUInt32();
        int timeLimit = checked((int)cursor.ReadUInt32());
        int textOffset = cursor.Position;
        if (declaredLength > (uint)(owned.Length - textOffset))
        {
            throw new Arena2FormatException(label, textOffset, $"rumor {index} runs {declaredLength - (uint)(owned.Length - textOffset)} bytes past the file's {owned.Length} bytes");
        }

        int textLength = (int)declaredLength;
        IReadOnlyList<Arena2TextToken> tokens = TextResourceReader.TokenizeRange(owned, textOffset, textOffset + textLength);
        cursor.Seek(textOffset + textLength);
        return new RumorRecord(
            index, offset, faction1, faction2, type, region, flags, questId, questName,
            unknown, npcId, textOffset, textLength, timeLimit, tokens);
    }

    private static string ReadQuestName(ref CheckedLittleEndianReader cursor, string label)
    {
        int offset = cursor.Position;
        ReadOnlySpan<byte> field = cursor.ReadBytes(QuestNameBytes);
        int terminator = field.IndexOf((byte)0);
        ReadOnlySpan<byte> text = terminator >= 0 ? field[..terminator] : field;
        foreach (byte value in text)
        {
            if (value > 0x7F)
            {
                throw new Arena2FormatException(label, offset, $"non-ASCII byte {value} in rumor quest name");
            }
        }

        return System.Text.Encoding.ASCII.GetString(text);
    }
}
