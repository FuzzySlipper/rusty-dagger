using System.Buffers.Binary;
using System.Text;

namespace Daggerfall.Import.Arena2;

/// <summary>One spell effect slot as the classic record carries it.</summary>
public sealed record Arena2SpellEffect(
    int Type,
    int SubType,
    int DurationBase,
    int DurationMod,
    int DurationPerLevel,
    int ChanceBase,
    int ChanceMod,
    int ChancePerLevel,
    int MagnitudeBaseLow,
    int MagnitudeBaseHigh,
    int MagnitudeLevelBase,
    int MagnitudeLevelHigh,
    int MagnitudePerLevel);

/// <summary>
/// One classic spell. <paramref name="Index"/> is the record's own identity byte, so a spell keeps the
/// identity the source gives it rather than a position this reader would invent.
/// </summary>
public sealed record Arena2Spell(
    int Index,
    string Name,
    int Element,
    int RangeType,
    int Cost,
    int Icon,
    IReadOnlyList<Arena2SpellEffect> Effects);

/// <summary>A record the catalog states but does not carry as a spell or a template.</summary>
public sealed record Arena2MagicDisposition(int Offset, string Reason);

/// <summary>The decoded classic spell catalog and the records that carried no spell.</summary>
public sealed record Arena2SpellCatalog(IReadOnlyList<Arena2Spell> Spells, IReadOnlyList<Arena2MagicDisposition> Dispositions);

/// <summary>One enchantment slot on a classic magic item; its <c>Param</c> names a spell id when the type does.</summary>
public sealed record Arena2MagicEnchantment(int Type, int Param);

/// <summary>One classic magic item template from MAGIC.DEF.</summary>
public sealed record Arena2MagicItem(
    long Index,
    string Name,
    int Type,
    int Group,
    int GroupIndex,
    IReadOnlyList<Arena2MagicEnchantment> Enchantments,
    int Uses,
    int Value,
    int Material);

/// <summary>Decoded MAGIC.DEF templates and the records that carried no template.</summary>
public sealed record Arena2MagicItemCatalog(IReadOnlyList<Arena2MagicItem> Items, IReadOnlyList<Arena2MagicDisposition> Dispositions);

/// <summary>
/// Reads the classic magical catalogs the product publishes: SPELLS.STD and MAGIC.DEF.
/// </summary>
/// <remarks>
/// Both files are fixed-stride record tables, which is what makes an identity stable: a spell's index
/// byte and a template's byte offset are read from the file rather than derived here. MAGIC.DEF leads
/// with a little-endian record count, so a file whose length disagrees with its own count is refused
/// rather than published short, and a trailing fragment is a refusal naming the offset because a partial
/// catalog would look exactly like a complete one.
/// </remarks>
public static class Arena2MagicReader
{
    /// <summary>SPELLS.STD's record size: 89 bytes, and the file is exactly a whole number of them.</summary>
    public const int SpellRecordSize = 0x59;

    /// <summary>An effect slot of a spell record is always two bytes: type and sub-type.</summary>
    public const int SpellEffectSlotSize = 2;

    /// <summary>MAGIC.DEF's name field width.</summary>
    public const int MagicItemNameLength = 32;

    /// <summary>MAGIC.DEF's record size: 32 name bytes, then the template.</summary>
    public const int MagicItemRecordSize = MagicItemNameLength + 3 + (10 * 2) + 2 + 4 + 1;

    /// <summary>The donor's "this slot is empty" marker.</summary>
    public const int EmptySlot = -1;

    /// <summary>Reads SPELLS.STD.</summary>
    public static Arena2SpellCatalog ReadSpells(byte[] bytes, string label)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length % SpellRecordSize != 0)
        {
            throw new InvalidOperationException($"'{label}' is {bytes.Length} bytes, which is not a whole number of {SpellRecordSize}-byte spell records; the catalog would be published incomplete.");
        }

        List<Arena2Spell> spells = [];
        List<Arena2MagicDisposition> dispositions = [];
        Span<byte> record = stackalloc byte[SpellRecordSize];
        for (int offset = 0; offset < bytes.Length; offset += SpellRecordSize)
        {
            bytes.AsSpan(offset, SpellRecordSize).CopyTo(record);
            int[] types = new int[3];
            int[] subTypes = new int[3];
            int cursor = 0;
            for (int slot = 0; slot < 3; slot++)
            {
                types[slot] = unchecked((sbyte)record[cursor]);
                subTypes[slot] = unchecked((sbyte)record[cursor + 1]);
                cursor += SpellEffectSlotSize;
            }

            if (types[0] == EmptySlot && types[1] == EmptySlot && types[2] == EmptySlot)
            {
                dispositions.Add(new Arena2MagicDisposition(offset, "the record's three effect slots are empty"));
                continue;
            }

            int element = record[cursor++];
            int rangeType = record[cursor++];
            int cost = BinaryPrimitives.ReadUInt16LittleEndian(record[cursor..]);
            cursor += 6;
            (int[] durationBase, int[] durationMod, int[] durationPerLevel) = ReadTriples(record, ref cursor);
            (int[] chanceBase, int[] chanceMod, int[] chancePerLevel) = ReadTriples(record, ref cursor);
            int[] magnitudeBaseLow = new int[3];
            int[] magnitudeBaseHigh = new int[3];
            int[] magnitudeLevelBase = new int[3];
            int[] magnitudeLevelHigh = new int[3];
            int[] magnitudePerLevel = new int[3];
            for (int slot = 0; slot < 3; slot++)
            {
                magnitudeBaseLow[slot] = record[cursor++];
                magnitudeBaseHigh[slot] = record[cursor++];
                magnitudeLevelBase[slot] = record[cursor++];
                magnitudeLevelHigh[slot] = record[cursor++];
                magnitudePerLevel[slot] = record[cursor++];
            }

            string name = ReadCString(record, ref cursor, 25);
            int icon = record[cursor++];
            int index = record[cursor++];
            List<Arena2SpellEffect> effects = [];
            for (int slot = 0; slot < 3; slot++)
            {
                if (types[slot] == EmptySlot) continue;
                effects.Add(new Arena2SpellEffect(
                    types[slot], subTypes[slot],
                    durationBase[slot], durationMod[slot], durationPerLevel[slot],
                    chanceBase[slot], chanceMod[slot], chancePerLevel[slot],
                    magnitudeBaseLow[slot], magnitudeBaseHigh[slot], magnitudeLevelBase[slot], magnitudeLevelHigh[slot], magnitudePerLevel[slot]));
            }

            spells.Add(new Arena2Spell(index, name, element, rangeType, cost, icon, effects));
        }

        return new Arena2SpellCatalog(spells, dispositions);
    }

    /// <summary>Reads MAGIC.DEF.</summary>
    public static Arena2MagicItemCatalog ReadMagicItems(byte[] bytes, string label)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < sizeof(int))
        {
            throw new InvalidOperationException($"'{label}' is shorter than its own record count.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        int expected = sizeof(int) + (count * MagicItemRecordSize);
        if (count < 0 || expected != bytes.Length)
        {
            throw new InvalidOperationException($"'{label}' states {count} magic-item records, which need {expected} bytes, but the file is {bytes.Length} bytes; the catalog would be published incomplete.");
        }

        List<Arena2MagicItem> items = [];
        List<Arena2MagicDisposition> dispositions = [];
        Span<byte> record = stackalloc byte[MagicItemRecordSize];
        for (int offset = sizeof(int); offset < bytes.Length; offset += MagicItemRecordSize)
        {
            bytes.AsSpan(offset, MagicItemRecordSize).CopyTo(record);
            int cursor = 0;
            string name = ReadCString(record, ref cursor, MagicItemNameLength);
            int type = record[cursor++];
            int group = record[cursor++];
            int groupIndex = record[cursor++];
            List<Arena2MagicEnchantment> enchantments = [];
            for (int slot = 0; slot < 10; slot++)
            {
                int enchantmentType = unchecked((sbyte)record[cursor++]);
                int param = unchecked((sbyte)record[cursor++]);
                if (enchantmentType == EmptySlot) continue;
                enchantments.Add(new Arena2MagicEnchantment(enchantmentType, param));
            }

            int uses = BinaryPrimitives.ReadInt16LittleEndian(record[cursor..]);
            cursor += 2;
            int value = BinaryPrimitives.ReadInt32LittleEndian(record[cursor..]);
            cursor += 4;
            int material = record[cursor];
            if (name.Length == 0 && enchantments.Count == 0)
            {
                dispositions.Add(new Arena2MagicDisposition(offset, "the record carries neither a name nor an enchantment"));
                continue;
            }

            items.Add(new Arena2MagicItem(offset, name, type, group, groupIndex, enchantments, uses, value, material));
        }

        return new Arena2MagicItemCatalog(items, dispositions);
    }

    private static (int[] Base, int[] Mod, int[] PerLevel) ReadTriples(ReadOnlySpan<byte> record, ref int cursor)
    {
        int[] baseValues = new int[3];
        int[] modValues = new int[3];
        int[] perLevel = new int[3];
        for (int slot = 0; slot < 3; slot++)
        {
            baseValues[slot] = record[cursor++];
            modValues[slot] = record[cursor++];
            perLevel[slot] = record[cursor++];
        }

        return (baseValues, modValues, perLevel);
    }

    private static string ReadCString(ReadOnlySpan<byte> record, ref int cursor, int width)
    {
        int end = cursor;
        int limit = cursor + width;
        while (end < limit && record[end] != 0) end++;
        string value = Encoding.Latin1.GetString(record[cursor..end]);
        cursor = limit;
        return value;
    }
}
