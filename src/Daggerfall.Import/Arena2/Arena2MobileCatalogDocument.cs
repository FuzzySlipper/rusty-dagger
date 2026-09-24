using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Daggerfall.Import.Arena2;

/// <summary>One mobile as the donor's static table defines it, with the parameters that table carries.</summary>
public sealed record Arena2MobileTableEntry(
    int Id,
    string Name,
    string Behaviour,
    string Affinity,
    int MaleTexture,
    int FemaleTexture,
    int CorpseArchive,
    int CorpseRecord,
    bool HasIdle,
    bool HasRangedAttack1,
    bool HasRangedAttack2,
    string MoveSound,
    string BarkSound,
    string AttackSound,
    string? LootTableKey,
    string MinMetalToHit,
    int MinDamage,
    int MaxDamage,
    int MinHealth,
    int MaxHealth,
    int Level,
    int ArmorValue,
    bool ParrySounds,
    int MapChance,
    int Weight,
    string Team);

/// <summary>
/// Reads the donor's static mobile table. The table is the authority for what a classic mobile is: its
/// behaviour, its affinity, the textures and corpse it uses, the sounds it makes and the numbers a
/// consumer would otherwise have to re-derive.
/// </summary>
/// <remarks>
/// This is a source read, not a normalized record: the values keep the donor's own names, and a field the
/// entry does not state is left at its default rather than invented. Nothing here decides behavior.
/// </remarks>
public static class Arena2MobileTable
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
                Last(fields, "Team")));
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException("The donor's static mobile table defines no entry this reader understands.");
        }

        return entries;
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

/// <summary>The published mobile catalog and what it carried.</summary>
public sealed record Arena2MobileCatalogPublication(string Json, int Mobiles, int Published, int Unpublished, int HumanMobiles);

/// <summary>
/// Publishes the donor's static mobile table as one normalized record per mobile: the parameters a later
/// actor-construction task consumes, each reconciled with the identity this product publishes.
/// </summary>
/// <remarks>
/// The donor's table is the parameter authority and the pack is the identity authority, so both are read
/// here and every entry states its disposition — published, published as a numbered variant where the
/// donor names a mobile twice, human mobile, or unpublished. A mobile the pack does not publish is
/// recorded with the donor's parameters and its gap named rather than dropped, because the coverage
/// record is the point of publishing the table at all.
/// </remarks>
public static class Arena2MobileCatalogDocument
{
    /// <summary>The documented inventory record that owns the monster archive.</summary>
    public const string SourceRecordId = "CNT-007";

    /// <summary>
    /// Builds the document's JSON from the donor table and the published pack. The mobile table is the
    /// parameter authority; a supplied MONSTER.BSA inventory additionally supplies each mobile's career
    /// attack-modifier byte, which lives in its <c>ENEMY###.CFG</c> record and not in the table.
    /// </summary>
    public static Arena2MobileCatalogPublication Build(
        string donorEnemyBasics, string packJson, string donorPath, MonsterArchiveInventory? enemyConfigurations = null)
    {
        IReadOnlyList<Arena2MobileTableEntry> table = Arena2MobileTable.Read(donorEnemyBasics);
        JsonNode pack = JsonNode.Parse(packJson) ?? throw new InvalidOperationException("The published pack is not JSON.");
        string[] actors = [.. (pack["actors"]?.AsArray() ?? throw new InvalidOperationException("The published pack carries no actors."))
            .Select(actor => actor!["id"]?.GetValue<string>() ?? throw new InvalidOperationException("A published actor carries no id."))];
        HashSet<string> actorIds = new(actors, StringComparer.Ordinal);

        Dictionary<string, int> nameCounts = [];
        foreach (Arena2MobileTableEntry entry in table)
        {
            string identity = MobileLedgerBuilder.Identity(entry.Name);
            nameCounts[identity] = nameCounts.TryGetValue(identity, out int seen) ? seen + 1 : 1;
        }

        JsonArray mobiles = [];
        int published = 0;
        int unpublished = 0;
        int human = 0;
        foreach (Arena2MobileTableEntry entry in table)
        {
            string identity = MobileLedgerBuilder.Identity(entry.Name);
            bool repeated = nameCounts[identity] > 1;
            string numbered = $"{identity}-{entry.Id}";
            string? actor = actorIds.Contains(numbered) ? numbered : actorIds.Contains(identity) ? identity : null;
            string disposition = actor is not null ? (actor == numbered && repeated ? "published-variant" : "published")
                : entry.Id >= MobileLedgerBuilder.FirstHumanMobileId ? "human-mobile"
                : "unpublished";
            if (disposition is "published" or "published-variant") published++;
            else if (disposition == "human-mobile") human++;
            else unpublished++;

            mobiles.Add(new JsonObject
            {
                ["donorId"] = entry.Id,
                ["donorName"] = entry.Name,
                ["identity"] = identity,
                ["actor"] = actor,
                ["disposition"] = disposition,
                ["behaviour"] = entry.Behaviour,
                ["affinity"] = entry.Affinity,
                ["maleTexture"] = entry.MaleTexture,
                ["femaleTexture"] = entry.FemaleTexture,
                ["corpse"] = new JsonObject { ["archive"] = entry.CorpseArchive, ["record"] = entry.CorpseRecord },
                ["hasIdle"] = entry.HasIdle,
                ["hasRangedAttack1"] = entry.HasRangedAttack1,
                ["hasRangedAttack2"] = entry.HasRangedAttack2,
                ["sounds"] = new JsonObject
                {
                    ["move"] = entry.MoveSound,
                    ["bark"] = entry.BarkSound,
                    ["attack"] = entry.AttackSound,
                },
                ["lootTableKey"] = entry.LootTableKey,
                ["minMetalToHit"] = entry.MinMetalToHit,
                ["damage"] = new JsonObject { ["minimum"] = entry.MinDamage, ["maximum"] = entry.MaxDamage },
                ["health"] = new JsonObject { ["minimum"] = entry.MinHealth, ["maximum"] = entry.MaxHealth },
                ["level"] = entry.Level,
                ["armorValue"] = entry.ArmorValue,
                ["parrySounds"] = entry.ParrySounds,
                ["mapChance"] = entry.MapChance,
                ["weight"] = entry.Weight,
                ["team"] = entry.Team,
                ["attackModifierFlags"] = AttackModifierFlags(enemyConfigurations, entry.Id),
            });
        }

        JsonObject document = new()
        {
            ["schemaVersion"] = 1,
            ["sources"] = new JsonArray(new JsonObject { ["recordId"] = SourceRecordId, ["path"] = donorPath }),
            ["mobiles"] = mobiles,
        };

        return new Arena2MobileCatalogPublication(
            document.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            table.Count, published, unpublished, human);
    }

    /// <summary>
    /// The career attack-modifier byte the classic <c>ENEMY###.CFG</c> record carries for one mobile:
    /// the bonus and phobia bits its career applies against each enemy group. A mobile the supplied
    /// archive carries no decoded configuration for — every human mobile among them — carries none.
    /// </summary>
    private static int AttackModifierFlags(MonsterArchiveInventory? enemyConfigurations, int mobileId) =>
        enemyConfigurations is null ? 0 : enemyConfigurations.EnemyConfigurations
            .Where(record => record.MobileId == mobileId && record.Configuration is not null)
            .Select(record => (int)record.Configuration!.AttackModifierFlags)
            .DefaultIfEmpty(0)
            .Max();
}
