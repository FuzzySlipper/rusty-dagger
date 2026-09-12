using System.Text;

namespace Daggerfall.Import.Arena2;

/// <summary>
/// One decoded CLASS*.CFG career record. The field order is the classic 74-byte
/// record: resistance/immunity flag bytes, ability and spell-point bitfield, healing
/// and absorption bytes, the enemy-group attack modifier, forbidden materials, the
/// weapon/armor/shields bitfield, nine skill indices, the career name, eight unknown
/// bytes, hit points per level, the advancement multiplier and eight attribute values.
/// </summary>
public sealed record ClassCfgRecord(
    string Name,
    int PrimarySkill1,
    int PrimarySkill2,
    int PrimarySkill3,
    int MajorSkill1,
    int MajorSkill2,
    int MajorSkill3,
    int MinorSkill1,
    int MinorSkill2,
    int MinorSkill3,
    int MinorSkill4,
    int MinorSkill5,
    int MinorSkill6,
    int HitPointsPerLevel,
    float AdvancementMultiplier,
    ushort[] Attributes,
    byte ResistanceFlags,
    byte ImmunityFlags,
    byte LowToleranceFlags,
    byte CriticalWeaknessFlags,
    ushort AbilityFlagsAndSpellPoints,
    byte RapidHealing,
    byte Regeneration,
    byte SpellAbsorptionFlags,
    byte AttackModifierFlags,
    ushort ForbiddenMaterials,
    uint WeaponArmorShields,
    byte[] UnknownBytes)
{
    /// <summary>
    /// Skill-slot values past the terminal no-skill value, which name nothing in the
    /// classic index space at all. The terminal value itself is legal in a slot and is
    /// not reported here. A supplied enemy configuration carries 40 in one minor slot, so
    /// the carrier is well formed and the index space is what the value exceeds; callers
    /// decide whether that is a defect for their family.
    /// </summary>
    public int[] SkillIndicesBeyondTerminal => [.. SkillIndices.Where(index => index > ClassCfgDecoder.NoSkillIndex)];

    /// <summary>Every skill index the record names, in record order.</summary>
    public int[] SkillIndices =>
    [
        PrimarySkill1, PrimarySkill2, PrimarySkill3,
        MajorSkill1, MajorSkill2, MajorSkill3,
        MinorSkill1, MinorSkill2, MinorSkill3, MinorSkill4, MinorSkill5, MinorSkill6,
    ];
}

/// <summary>
/// Decodes the classic 74-byte career record. Source-format knowledge only: it assigns
/// no gameplay meaning and allocates nothing.
/// </summary>
public static class ClassCfgDecoder
{
    /// <summary>The exact record length the classic carrier uses.</summary>
    public const int RecordLength = 74;

    /// <summary>The number of skill indices in a record: three primary, three major, six minor.</summary>
    public const int SkillIndexCount = 12;

    /// <summary>The number of attribute values the record carries.</summary>
    public const int AttributeCount = 8;

    /// <summary>The number of classic skills a record's indices may name.</summary>
    public const int SkillCount = 35;

    /// <summary>
    /// The terminal value a slot carries when it names no skill. It is a legal classic
    /// value, not a malformed one: the supplied Knight record holds it in a major slot.
    /// </summary>
    public const int NoSkillIndex = SkillCount;

    public static ClassCfgRecord Decode(ReadOnlySpan<byte> bytes, string source)
    {
        if (bytes.Length != RecordLength)
        {
            throw new Arena2FormatException(source, 0, $"a career record is {bytes.Length} bytes where the classic carrier is exactly {RecordLength}");
        }

        // The advancement multiplier is a 16.16 fixed-point value, and the name is a
        // NUL-terminated ASCII field, which is how the original and the donor read both.
        uint advancementRaw = ReadUInt32(bytes, 54);
        float advancementMultiplier = (advancementRaw >> 16) + (advancementRaw & 0xffff) / 65536f;
        ushort[] attributes = new ushort[AttributeCount];
        for (int index = 0; index < AttributeCount; index++)
        {
            attributes[index] = ReadUInt16(bytes, 58 + (2 * index));
        }

        ClassCfgRecord record = new(
            ReadName(bytes, 28, source),
            bytes[16], bytes[17], bytes[18],
            bytes[19], bytes[20], bytes[21],
            bytes[22], bytes[23], bytes[24], bytes[25], bytes[26], bytes[27],
            ReadUInt16(bytes, 52),
            advancementMultiplier,
            attributes,
            bytes[0], bytes[1], bytes[2], bytes[3],
            ReadUInt16(bytes, 4),
            bytes[6], bytes[7], bytes[9], bytes[10],
            ReadUInt16(bytes, 11),
            (uint)((bytes[13] << 16) | (bytes[15] << 8) | bytes[14]),
            bytes.Slice(44, 8).ToArray());

        // The skill bytes are bytes: the record shape is what this decoder owns, and
        // whether an index names a skill in a given family is the family's question.
        // OutOfSpaceSkillIndices reports them for a caller that must care.
        return record;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));

    private static string ReadName(ReadOnlySpan<byte> bytes, int offset, string source)
    {
        ReadOnlySpan<byte> field = bytes.Slice(offset, 16);
        int terminator = field.IndexOf((byte)0);
        ReadOnlySpan<byte> text = terminator >= 0 ? field[..terminator] : field;
        if (text.Length == 0)
        {
            throw new Arena2FormatException(source, offset, "the career name field is empty");
        }

        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] < 0x20 || text[index] > 0x7e)
            {
                throw new Arena2FormatException(source, offset + index, "the career name is not printable ASCII");
            }
        }

        return Encoding.ASCII.GetString(text);
    }
}
