using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The MONSTER.BSA enumeration: every named record is retained with its family and
/// disposition, the enemy configurations decode through the classic record reader, and a
/// record the enumeration cannot resolve says so instead of disappearing.
/// </summary>
public sealed class MonsterArchiveTests
{
    [Fact]
    public void Enumerates_every_named_record_of_the_supplied_archive()
    {
        MonsterArchiveInventory inventory = ReadInventory();

        Assert.Equal(103, inventory.Records.Count);
        Assert.Equal(60, inventory.AnimationScripts.Count());
        Assert.Equal(43, inventory.EnemyConfigurations.Count());
        Assert.DoesNotContain(inventory.Records, record => record.Family == MonsterArchiveRecordFamily.Unrecognized);
        // The named directory is the archive key, so it is unique and complete.
        Assert.Equal(103, inventory.Records.Select(record => record.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enumerable.Range(0, 103), inventory.Records.Select(record => record.Ordinal));
        Assert.All(inventory.Records, record => Assert.True(record.Length > 0));
        Assert.True(inventory.TryGetByName("ENEMY000.CFG", out MonsterArchiveRecord? rat));
        Assert.Equal(0, rat!.MobileId);
    }

    [Fact]
    public void Decodes_every_supplied_enemy_configuration()
    {
        MonsterArchiveInventory inventory = ReadInventory();

        // Every ENEMY record is the classic 74-byte career record, so it decodes through
        // the same reader a class carrier does and carries a real name and hit points.
        Assert.All(inventory.EnemyConfigurations, record =>
        {
            Assert.Equal(ClassCfgDecoder.RecordLength, record.Length);
            Assert.Equal(MonsterArchiveRecordDisposition.Decoded, record.Disposition);
            Assert.NotNull(record.Configuration);
            Assert.False(string.IsNullOrWhiteSpace(record.Configuration!.Name));
            Assert.True(record.Configuration.HitPointsPerLevel > 0);
        });
        Assert.Equal("Rat", inventory.EnemyConfigurations.Single(record => record.Name == "ENEMY000.CFG").Configuration!.Name);
    }

    [Fact]
    public void Reports_a_supplied_configuration_whose_skill_slot_is_past_the_class_space()
    {
        MonsterArchiveInventory inventory = ReadInventory();

        // One supplied configuration (the Sabertooth Tiger) names skill index 40 in a
        // minor slot. The record is otherwise sound, so it decodes and the observed value
        // is reported rather than turning a shipped record into a loss.
        MonsterArchiveRecord tiger = inventory.EnemyConfigurations.Single(record => record.Name == "ENEMY005.CFG");
        Assert.Equal(MonsterArchiveRecordDisposition.Decoded, tiger.Disposition);
        Assert.Equal("Sabertooth Tiger", tiger.Configuration!.Name);
        Assert.Equal([40], tiger.Configuration.SkillIndicesBeyondTerminal);
        Assert.Contains("40", tiger.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Retains_every_animation_script_without_claiming_its_format()
    {
        MonsterArchiveInventory inventory = ReadInventory();

        Assert.All(inventory.AnimationScripts, record =>
        {
            Assert.Equal(MonsterArchiveRecordDisposition.UnresearchedFormat, record.Disposition);
            Assert.Null(record.Configuration);
            Assert.Contains("unknown and unresearched", record.Note, StringComparison.Ordinal);
        });
        // The archive records an id and a size; the enumeration claims nothing beyond that.
        Assert.All(inventory.AnimationScripts, record => Assert.InRange(record.Length, 1, 4096));
    }

    [Fact]
    public void Links_supported_mobiles_and_retains_every_other_record_as_unresolved()
    {
        MonsterArchiveInventory inventory = ReadInventory();

        // The eight mobiles this repository supports are the ones with source media and
        // link facts; every other archive record names the mobile it belongs to and says
        // that nothing supported carries it.
        Assert.Equal(
            MobileSourceMetadata.All.Select(source => source.Id.Value).Order(),
            inventory.Records.Where(record => record.IsLinked).Select(record => record.MobileId).Distinct().Order());
        Assert.Equal(89, inventory.Unlinked.Count());
        Assert.All(inventory.Unlinked, record =>
        {
            Assert.Contains($"Mobile {record.MobileId}", record.Note, StringComparison.Ordinal);
            Assert.Contains("no supported source mobile", record.Note, StringComparison.Ordinal);
        });
        MonsterArchiveRecord rat = inventory.EnemyConfigurations.Single(record => record.Name == "ENEMY000.CFG");
        Assert.Equal(255, rat.Source!.TextureArchive.Value);
        Assert.Equal(401, rat.Source.Corpse!.Value.TextureArchive.Value);
        Assert.Equal(1, rat.Source.Corpse.Value.Record);
        Assert.Equal("EnemyRatBark", rat.Source.Links.BarkSoundCue);
        Assert.Null(rat.Source.Links.LootTableKey);
    }

    [Fact]
    public void Every_loot_link_a_supported_mobile_names_is_a_published_loot_table()
    {
        MonsterArchiveInventory inventory = ReadInventory();
        System.Text.Json.Nodes.JsonArray tables = System.Text.Json.Nodes.JsonNode
            .Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")))!
            ["lootTables"]!.AsArray();
        string[] keys = [.. tables.Select(table => table!.AsObject()["key"]!.GetValue<string>())];

        // A donor loot key is only a link if the content that rolls it exists, so the two
        // sides are compared rather than assumed to agree.
        string[] linked = [.. inventory.Records
            .Where(record => record.Source?.Links.LootTableKey is not null)
            .Select(record => record.Source!.Links.LootTableKey!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        Assert.NotEmpty(linked);
        Assert.All(linked, key => Assert.Contains(key, keys));
    }

    [Fact]
    public void Every_media_reference_a_linked_record_makes_is_supplied()
    {
        MonsterArchiveInventory inventory = ReadInventory();
        HashSet<int> supplied = SuppliedTextureArchives();

        // The corpus supplies 472 texture archives; every archive the linked mobiles point
        // at must be one of them, or the media a later task publishes does not exist.
        Assert.Empty(inventory.MissingMedia(supplied));
        // And the check must have something to check: eight mobiles referencing a live
        // archive and a corpse each, so a link that loses its corpse cannot make this
        // pass by having nothing to compare.
        Assert.All(inventory.Records.Where(record => record.IsLinked).Select(record => record.Source!.Id.Value).Distinct(), mobileId =>
            Assert.NotNull(MobileSourceMetadata.All.Single(source => source.Id.Value == mobileId).Corpse));
    }

    [Fact]
    public void Reports_a_media_reference_the_supplied_sources_do_not_carry()
    {
        MonsterArchiveInventory inventory = ReadInventory();
        HashSet<int> withoutTheRatArchive = SuppliedTextureArchives();
        withoutTheRatArchive.Remove(255);

        // A gap is reported against each record that makes the reference, so the task
        // that publishes that media can see which mobile and which records are affected.
        MonsterArchiveMediaGap[] gaps = [.. inventory.MissingMedia(withoutTheRatArchive)];
        Assert.Equal(["ASCR0000.ANC", "ENEMY000.CFG"], gaps.Select(gap => gap.RecordName).Order(StringComparer.Ordinal));
        Assert.All(gaps, gap =>
        {
            Assert.Equal(0, gap.MobileId);
            Assert.Equal("live", gap.Kind);
            Assert.Equal(255, gap.TextureArchive);
        });
    }

    /// <summary>The texture archives the documented inventory says are supplied.</summary>
    private static HashSet<int> SuppliedTextureArchives() =>
    [
        .. SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")))
            .Where(row => row.RowType == "file" && row.Id.StartsWith("CNT-018.file.TEXTURE", StringComparison.Ordinal))
            .Select(row => int.Parse(Path.GetExtension(row.PathOrPattern).TrimStart('.'), CultureInfo.InvariantCulture)),
    ];

    [Fact]
    public void Retains_a_configuration_that_does_not_fit_the_record_shape()
    {
        // A malformed configuration is a fact about one record, not a reason to lose the
        // other 102: it is retained with the length observed.
        byte[] archive = BuildArchive(
            ("ENEMY000.CFG", new byte[73]),
            ("ENEMY001.CFG", ValidConfig()),
            ("ASCR0000.ANC", new byte[16]));

        MonsterArchiveInventory inventory = MonsterArchiveInventory.Enumerate(archive, "MONSTER.BSA");

        Assert.Equal(3, inventory.Records.Count);
        MonsterArchiveRecord broken = inventory.Records.Single(record => record.Name == "ENEMY000.CFG");
        Assert.Equal(MonsterArchiveRecordDisposition.Malformed, broken.Disposition);
        Assert.Equal(73, broken.Length);
        Assert.Contains("73 bytes", broken.Note, StringComparison.Ordinal);
        Assert.Null(broken.Configuration);
        Assert.Equal(MonsterArchiveRecordDisposition.Decoded, inventory.Records.Single(record => record.Name == "ENEMY001.CFG").Disposition);
        Assert.Equal(MonsterArchiveRecordDisposition.UnresearchedFormat, inventory.Records.Single(record => record.Name == "ASCR0000.ANC").Disposition);
    }

    [Fact]
    public void Retains_a_record_whose_name_is_in_neither_family()
    {
        byte[] archive = BuildArchive(("README.TXT", new byte[8]), ("ASCR0000.ANC", new byte[16]));

        MonsterArchiveInventory inventory = MonsterArchiveInventory.Enumerate(archive, "MONSTER.BSA");

        MonsterArchiveRecord other = inventory.Records.Single(record => record.Name == "README.TXT");
        Assert.Equal(MonsterArchiveRecordFamily.Unrecognized, other.Family);
        Assert.Equal(MonsterArchiveRecordDisposition.Unrecognized, other.Disposition);
        Assert.False(other.IsLinked);
        Assert.Contains("neither", other.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Locates_a_truncated_archive_at_the_directory_entry_that_does_not_fit()
    {
        // Truncating the real archive shifts the directory window, so payload bytes are
        // read as entries. The refusal must point at the entry that does not fit rather
        // than at an offset only the arithmetic could reach.
        byte[] truncated = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/MONSTER.BSA"))[..^10];

        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => MonsterArchiveInventory.Enumerate(truncated, "MONSTER.BSA"));

        Assert.InRange(error.Offset, 0, truncated.Length);
        Assert.Contains("directory", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_the_cue_that_fails_link_validation()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new Arena2MobileForeignLinks(
            null, "  ", "EnemyRatBark", "EnemyRatAttack", ParrySounds: false, BloodIndex: 0, MapChance: 0).Validate());

        Assert.Equal("MoveSoundCue", error.ParamName);
    }

    [Fact]
    public void Refuses_an_archive_whose_directory_repeats_a_name()
    {
        // Two records under one archive key would make the enumeration's lookup ambiguous,
        // so the directory itself is refused.
        byte[] archive = BuildArchive(
            [("ENEMY000.CFG", ValidConfig()), ("ENEMY000.CFG", ValidConfig())],
            allowDuplicateNames: true);

        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => MonsterArchiveInventory.Enumerate(archive, "MONSTER.BSA"));

        Assert.Contains("ENEMY000.CFG", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_mobile_id_the_classic_identity_cannot_carry()
    {
        // The mobile identity is one byte, so an archive key claiming more is not that
        // family's record and must not be read as one.
        byte[] archive = BuildArchive(("ENEMY300.CFG", ValidConfig()));

        MonsterArchiveInventory inventory = MonsterArchiveInventory.Enumerate(archive, "MONSTER.BSA");

        MonsterArchiveRecord record = inventory.Records.Single();
        Assert.Equal(MonsterArchiveRecordFamily.Unrecognized, record.Family);
    }

    private static byte[] ValidConfig()
    {
        // A copy of a supplied configuration, so the fixture exercises the real shape.
        byte[] supplied = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/CLASS00.CFG"));
        return supplied;
    }

    /// <summary>Builds a named BSA: header, sequential payloads, then the directory.</summary>
    private static byte[] BuildArchive(params (string Name, byte[] Payload)[] records) =>
        BuildArchive(records, allowDuplicateNames: false);

    private static byte[] BuildArchive((string Name, byte[] Payload)[] records, bool allowDuplicateNames)
    {
        const int HeaderBytes = 4;
        const int DirectoryEntryBytes = 18;
        if (!allowDuplicateNames && records.Select(record => record.Name).Distinct(StringComparer.Ordinal).Count() != records.Length)
        {
            throw new ArgumentException("fixture records must be distinctly named", nameof(records));
        }

        int payloadBytes = records.Sum(record => record.Payload.Length);
        byte[] archive = new byte[HeaderBytes + payloadBytes + (records.Length * DirectoryEntryBytes)];
        BinaryPrimitives.WriteInt16LittleEndian(archive.AsSpan(0, 2), (short)records.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(2, 2), 0x0100);
        int payloadOffset = HeaderBytes;
        int directoryOffset = HeaderBytes + payloadBytes;
        for (int index = 0; index < records.Length; index++)
        {
            records[index].Payload.CopyTo(archive, payloadOffset);
            Span<byte> entry = archive.AsSpan(directoryOffset + (index * DirectoryEntryBytes), DirectoryEntryBytes);
            entry.Clear();
            Encoding.ASCII.GetBytes(records[index].Name).CopyTo(entry);
            BinaryPrimitives.WriteInt32LittleEndian(entry[14..], records[index].Payload.Length);
            payloadOffset += records[index].Payload.Length;
        }

        return archive;
    }

    private static MonsterArchiveInventory ReadInventory() =>
        MonsterArchiveInventory.Enumerate(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/MONSTER.BSA")), "MONSTER.BSA");

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
