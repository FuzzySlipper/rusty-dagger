using System.Globalization;
using System.Text.RegularExpressions;
using Daggerfall.Import.Arena2;

namespace Daggerfall.Source.Tool.Donor;

/// <summary>
/// Reads the donor's static mobile table. The table is the authority for what a classic mobile is: its
/// behaviour, its affinity, the textures and corpse it uses, the sounds it makes and the numbers a
/// consumer would otherwise have to re-derive.
/// </summary>
/// <remarks>
/// This is a source read, not a normalized record: the values keep the donor's own names, and a field the
/// entry does not state is left at its default rather than invented. Nothing here decides behavior.
/// </remarks>
internal static class DonorMobileTable
{
    private static readonly Regex EntryPattern = new(
        @"//[ \t]*(?<name>[^\n]*?)[ \t]*\r?\n[ \t]*new MobileEnemy\(\)[ \t]*\r?\n[ \t]*\{[ \t]*\r?\n(?<body>.*?)\r?\n[ \t]*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex FieldPattern = new(
        @"(?<field>[A-Za-z][A-Za-z0-9_]*)[ \t]*=[ \t]*(?<value>[^,\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Reads every entry the donor's table defines, in file order.</summary>
    public static IReadOnlyList<Arena2MobileTableEntry> Read(string donorEnemyBasics)
    {
        ArgumentNullException.ThrowIfNull(donorEnemyBasics);
        int table = donorEnemyBasics.IndexOf("MobileEnemy[] Enemies", StringComparison.Ordinal);
        if (table < 0)
        {
            throw new InvalidOperationException("The donor's static mobile table was not found; the catalog cannot publish parameters it cannot read.");
        }

        List<Arena2MobileTableEntry> entries = [];
        foreach (Match match in EntryPattern.Matches(donorEnemyBasics[table..]))
        {
            Dictionary<string, string> fields = new(StringComparer.Ordinal);
            foreach (Match field in FieldPattern.Matches(match.Groups["body"].Value))
            {
                fields[field.Groups["field"].Value] = field.Groups["value"].Value.Trim();
            }

            if (!fields.TryGetValue("ID", out string? id) || !int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int donorId))
            {
                continue;
            }

            (int corpseArchive, int corpseRecord) = Corpse(match.Groups["body"].Value);
            entries.Add(new Arena2MobileTableEntry(
                donorId,
                match.Groups["name"].Value.Trim(),
                Last(fields, "Behaviour"),
                Last(fields, "Affinity"),
                Number(fields, "MaleTexture"),
                Number(fields, "FemaleTexture"),
                corpseArchive,
                corpseRecord,
                Flag(fields, "HasIdle"),
                Flag(fields, "HasRangedAttack1"),
                Flag(fields, "HasRangedAttack2"),
                Last(fields, "MoveSound"),
                Last(fields, "BarkSound"),
                Last(fields, "AttackSound"),
                TextLiteral(fields, "LootTableKey"),
                Last(fields, "MinMetalToHit"),
                Number(fields, "MinDamage"),
                Number(fields, "MaxDamage"),
                Number(fields, "MinHealth"),
                Number(fields, "MaxHealth"),
                Number(fields, "Level"),
                Number(fields, "ArmorValue"),
                Flag(fields, "ParrySounds"),
                Number(fields, "MapChance"),
                Number(fields, "Weight"),
                Last(fields, "Team"),
                Flag(fields, "SeesThroughInvisibility"),
                Flag(fields, "CastsMagic"),
                Flag(fields, "CanOpenDoors"))
            {
                SoulPoints = Number(fields, "SoulPts"),
                NoShadow = Flag(fields, "NoShadow"),
                PrefersRanged = Flag(fields, "PrefersRanged"),
                GlowColor = Glow(match.Groups["body"].Value),
            });
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException("The donor's static mobile table defines no entry this reader understands.");
        }

        return entries;
    }

    private static Arena2MobileGlow? Glow(string body)
    {
        if (!body.Contains("GlowColor", StringComparison.Ordinal)) return null;
        Match color = Regex.Match(body, @"GlowColor\s*=\s*new Color\(\s*(?<r>[0-9.]+)f?\s*,\s*(?<g>[0-9.]+)f?\s*,\s*(?<b>[0-9.]+)f?\s*\)\s*\*\s*(?<scale>[0-9.]+)f?", RegexOptions.CultureInvariant);
        if (!color.Success) throw new InvalidOperationException("The mobile glow color cannot be read from the donor table.");
        float Read(string name) => float.Parse(color.Groups[name].Value, CultureInfo.InvariantCulture);
        float scale = Read("scale");
        return new(Read("r") * scale, Read("g") * scale, Read("b") * scale);
    }

    /// <summary>Reads an entry's field as an integer, or zero when the entry does not state it.</summary>
    private static int Number(Dictionary<string, string> fields, string field) =>
        fields.TryGetValue(field, out string? value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

    private static bool Flag(Dictionary<string, string> fields, string field) =>
        fields.TryGetValue(field, out string? value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static string? TextLiteral(Dictionary<string, string> fields, string field) =>
        fields.TryGetValue(field, out string? value) ? value.Trim().Trim('"') : null;

    /// <summary>
    /// Reads the donor's named constant, keeping the name rather than its numeric value: a behaviour or a
    /// sound is what the source says it is, and the number is an implementation detail of the donor's enum.
    /// </summary>
    private static string Last(Dictionary<string, string> fields, string field)
    {
        if (!fields.TryGetValue(field, out string? value)) return string.Empty;
        int dot = value.LastIndexOf('.');
        return dot >= 0 ? value[(dot + 1)..].Trim() : value.Trim();
    }

    /// <summary>
    /// Reads the corpse the entry names. It is parsed from the entry text rather than from the field map,
    /// because the donor writes it as a call and a field value stops at the call's own comma.
    /// </summary>
    private static (int Archive, int Record) Corpse(string body)
    {
        Match match = Regex.Match(body, @"CorpseTexture\(\s*(?<archive>\d+)\s*,\s*(?<record>\d+)\s*\)", RegexOptions.CultureInvariant);
        return match.Success
            ? (int.Parse(match.Groups["archive"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["record"].Value, CultureInfo.InvariantCulture))
            : (0, 0);
    }
}

