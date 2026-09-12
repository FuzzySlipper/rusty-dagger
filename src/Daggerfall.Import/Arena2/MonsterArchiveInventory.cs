namespace Daggerfall.Import.Arena2;

/// <summary>The two named families MONSTER.BSA carries.</summary>
public enum MonsterArchiveRecordFamily
{
    /// <summary>An <c>ASCR####.ANC</c> animation-script record.</summary>
    AnimationScript,

    /// <summary>An <c>ENEMY###.CFG</c> configuration record in the classic career shape.</summary>
    EnemyConfiguration,

    /// <summary>A record whose name is in neither family.</summary>
    Unrecognized,
}

/// <summary>What the enumeration could establish about one archive record.</summary>
public enum MonsterArchiveRecordDisposition
{
    /// <summary>The record's payload was decoded.</summary>
    Decoded,

    /// <summary>The record is named and sized, but its format is not documented.</summary>
    UnresearchedFormat,

    /// <summary>The record belongs to a known family and its payload does not fit it.</summary>
    Malformed,

    /// <summary>The record's name is in neither family.</summary>
    Unrecognized,
}

/// <summary>
/// One enumerated MONSTER.BSA record: its archive facts, its family, what the
/// enumeration could establish about it, and the source links it participates in. An
/// unlinked record is retained with the reason it could not be linked rather than
/// dropped or given a meaning no source supports.
/// </summary>
public sealed record MonsterArchiveRecord(
    int Ordinal,
    string Name,
    MonsterArchiveRecordFamily Family,
    byte MobileId,
    int Length,
    MonsterArchiveRecordDisposition Disposition,
    string Note,
    ClassCfgRecord? Configuration,
    Arena2MobileSource? Source)
{
    /// <summary>Whether this record resolved to a supported source mobile.</summary>
    public bool IsLinked => Source is not null;
}

/// <summary>
/// One media reference a linked record makes that the supplied source set does not
/// carry: the live texture archive or the corpse texture archive a mobile needs.
/// </summary>
public sealed record MonsterArchiveMediaGap(string RecordName, byte MobileId, string Kind, int TextureArchive);

/// <summary>
/// The enumerated contents of one MONSTER.BSA. The task is enumeration: this type
/// records what the archive holds and which source mobile each record belongs to. It
/// assigns no actor policy, no audio selection and no animation playback; those belong
/// to the ruleset and the runtime services that consume these links.
/// </summary>
public sealed class MonsterArchiveInventory
{
    /// <summary>The animation-script family prefix.</summary>
    public const string AnimationScriptPrefix = "ASCR";

    /// <summary>The enemy-configuration family prefix.</summary>
    public const string EnemyConfigurationPrefix = "ENEMY";

    /// <summary>The suffix the animation-script family carries.</summary>
    public const string AnimationScriptSuffix = ".ANC";

    /// <summary>The suffix the enemy-configuration family carries.</summary>
    public const string EnemyConfigurationSuffix = ".CFG";

    private readonly Dictionary<string, MonsterArchiveRecord> byName;

    private MonsterArchiveInventory(string source, IReadOnlyList<MonsterArchiveRecord> records)
    {
        Source = source;
        Records = records;
        byName = records.ToDictionary(record => record.Name, StringComparer.Ordinal);
    }

    /// <summary>Logical source identity supplied to <see cref="Enumerate"/>.</summary>
    public string Source { get; }

    /// <summary>Directory-order records; every archive record is retained.</summary>
    public IReadOnlyList<MonsterArchiveRecord> Records { get; }

    /// <summary>The records in the animation-script family.</summary>
    public IEnumerable<MonsterArchiveRecord> AnimationScripts => Records.Where(record => record.Family == MonsterArchiveRecordFamily.AnimationScript);

    /// <summary>The records in the enemy-configuration family.</summary>
    public IEnumerable<MonsterArchiveRecord> EnemyConfigurations => Records.Where(record => record.Family == MonsterArchiveRecordFamily.EnemyConfiguration);

    /// <summary>
    /// The records that did not resolve to a supported source mobile, each carrying the
    /// reason. An empty sequence means every record linked.
    /// </summary>
    public IEnumerable<MonsterArchiveRecord> Unlinked => Records.Where(record => !record.IsLinked);

    /// <summary>Enumerates every record in a supplied MONSTER.BSA.</summary>
    public static MonsterArchiveInventory Enumerate(ReadOnlySpan<byte> bytes, string source)
    {
        BsaArchive archive = BsaArchive.Parse(bytes, source);
        List<MonsterArchiveRecord> records = new(archive.Records.Count);
        foreach (BsaRecord record in archive.Records)
        {
            // The named directory guarantees a name; a numeric directory cannot describe
            // this archive's families, and saying so is better than guessing.
            string name = record.Name ?? throw new Arena2FormatException(source, 0, $"MONSTER.BSA record {record.Ordinal} has a numeric directory entry where this archive names its records");
            records.Add(Enumerate(archive, record, name, source));
        }

        return new MonsterArchiveInventory(source, records);
    }

    /// <summary>Gets a record by its exact stored archive key.</summary>
    public bool TryGetByName(string name, out MonsterArchiveRecord? record)
    {
        ArgumentNullException.ThrowIfNull(name);
        return byName.TryGetValue(name, out record);
    }

    /// <summary>Gets every record that belongs to one classic mobile.</summary>
    public IEnumerable<MonsterArchiveRecord> ForMobile(Arena2MobileId id) => Records.Where(record => record.MobileId == id.Value);

    /// <summary>
    /// The media a linked record references that the supplied source set does not carry.
    /// The caller supplies the archives it has, so this stays a source question rather
    /// than a second inventory: a link to media that is not supplied is a gap the task
    /// that publishes that media needs to see.
    /// </summary>
    /// <remarks>
    /// This is not the publication's texture closure. That check
    /// (<c>Arena2DungeonMediaPublication.EnforceExactTextureClosure</c>) refuses a
    /// publication whose supplied archives are not exactly the set one dungeon selection
    /// requires. This one asks, per archive record, whether the mobile it names has its
    /// media in the supplied sources at all, and reports the records that do not rather
    /// than refusing: an enumeration is allowed to find the corpus incomplete.
    /// </remarks>
    public IEnumerable<MonsterArchiveMediaGap> MissingMedia(IReadOnlySet<int> suppliedTextureArchives)
    {
        ArgumentNullException.ThrowIfNull(suppliedTextureArchives);
        foreach (MonsterArchiveRecord record in Records.Where(record => record.Source is not null))
        {
            Arena2MobileSource source = record.Source!;
            if (!suppliedTextureArchives.Contains(source.TextureArchive.Value))
            {
                yield return new MonsterArchiveMediaGap(record.Name, record.MobileId, "live", source.TextureArchive.Value);
            }

            if (source.Corpse is Arena2MobileCorpseSource corpse && !suppliedTextureArchives.Contains(corpse.TextureArchive.Value))
            {
                yield return new MonsterArchiveMediaGap(record.Name, record.MobileId, "corpse", corpse.TextureArchive.Value);
            }
        }
    }

    private static MonsterArchiveRecord Enumerate(BsaArchive archive, BsaRecord record, string name, string source)
    {
        if (TryMobileId(name, AnimationScriptPrefix, AnimationScriptSuffix, out byte scriptId))
        {
            // The donor states this format is unknown and unresearched, so the enumeration
            // records the archive fact and claims nothing about the payload.
            return new MonsterArchiveRecord(
                record.Ordinal,
                name,
                MonsterArchiveRecordFamily.AnimationScript,
                scriptId,
                record.Length,
                MonsterArchiveRecordDisposition.UnresearchedFormat,
                LinkNote(scriptId, "its format is documented as unknown and unresearched"),
                Configuration: null,
                Source: Link(scriptId));
        }

        if (TryMobileId(name, EnemyConfigurationPrefix, EnemyConfigurationSuffix, out byte enemyId))
        {
            return EnumerateEnemyConfiguration(archive, record, name, enemyId, source);
        }

        return new MonsterArchiveRecord(
            record.Ordinal,
            name,
            MonsterArchiveRecordFamily.Unrecognized,
            MobileId: 0,
            record.Length,
            MonsterArchiveRecordDisposition.Unrecognized,
            $"'{name}' is in neither the {AnimationScriptPrefix}*{AnimationScriptSuffix} nor the {EnemyConfigurationPrefix}*{EnemyConfigurationSuffix} family.",
            Configuration: null,
            Source: null);
    }

    private static MonsterArchiveRecord EnumerateEnemyConfiguration(BsaArchive archive, BsaRecord record, string name, byte mobileId, string source)
    {
        // Enemy configuration records are the classic career record, so the same decoder
        // reads them; a record that does not fit is retained as malformed with the length
        // observed rather than refused, because the rest of the archive is still usable.
        if (record.Length != ClassCfgDecoder.RecordLength)
        {
            return new MonsterArchiveRecord(
                record.Ordinal,
                name,
                MonsterArchiveRecordFamily.EnemyConfiguration,
                mobileId,
                record.Length,
                MonsterArchiveRecordDisposition.Malformed,
                LinkNote(mobileId, $"its payload is {record.Length} bytes where an enemy configuration is exactly {ClassCfgDecoder.RecordLength}"),
                Configuration: null,
                Source: Link(mobileId));
        }

        try
        {
            ClassCfgRecord configuration = ClassCfgDecoder.Decode(archive.GetPayload(record).Span, name);
            // The enemy family tolerates a skill slot outside the class index space: one
            // supplied configuration carries it, the record is otherwise sound, and the
            // observed value is reported rather than turning the record into a loss.
            string drift = configuration.SkillIndicesBeyondTerminal.Length == 0
                ? string.Empty
                : $" and names skill indices [{string.Join(", ", configuration.SkillIndicesBeyondTerminal)}] past the classic class skill space";
            return new MonsterArchiveRecord(
                record.Ordinal,
                name,
                MonsterArchiveRecordFamily.EnemyConfiguration,
                mobileId,
                record.Length,
                MonsterArchiveRecordDisposition.Decoded,
                LinkNote(mobileId, $"decoded as '{configuration.Name}'{drift}"),
                configuration,
                Link(mobileId));
        }
        catch (Arena2FormatException failure)
        {
            return new MonsterArchiveRecord(
                record.Ordinal,
                name,
                MonsterArchiveRecordFamily.EnemyConfiguration,
                mobileId,
                record.Length,
                MonsterArchiveRecordDisposition.Malformed,
                LinkNote(mobileId, failure.Message),
                Configuration: null,
                Source: Link(mobileId));
        }
    }

    /// <summary>Whether a supported source mobile explains this record's id.</summary>
    private static Arena2MobileSource? Link(byte mobileId) =>
        MobileSourceMetadata.TryGet(new Arena2MobileId(mobileId), out Arena2MobileSource? source) ? source : null;

    private static string LinkNote(byte mobileId, string detail) =>
        Link(mobileId) is null
            ? $"Mobile {mobileId}: {detail}; no supported source mobile carries this id, so the record stays unlinked."
            : $"Mobile {mobileId}: {detail}.";

    /// <summary>
    /// Reads a family's mobile id from its archive key. The id is the classic mobile
    /// identity, so a name that is not the family's exact shape with a byte-sized id is
    /// not that family.
    /// </summary>
    private static bool TryMobileId(string name, string prefix, string suffix, out byte mobileId)
    {
        mobileId = 0;
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        string digits = name[prefix.Length..^suffix.Length];
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        return byte.TryParse(digits, out mobileId);
    }
}
