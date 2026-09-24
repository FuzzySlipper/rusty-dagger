using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The mobile catalog is the parameter record a later actor-construction task consumes, so the donor's
/// values, the published identity each mobile reconciles to, and the gap a mobile leaves are all pinned.
/// </summary>
public sealed class Arena2MobileCatalogDocumentTests
{
    [Fact]
    public void PublishesEveryDonorParameterAndNamesEachMobileDisposition()
    {
        Arena2MobileCatalogPublication publication = Arena2MobileCatalogDocument.Build(Donor(), Pack(), "donor/EnemyBasics.cs");
        JsonObject document = JsonNode.Parse(publication.Json)!.AsObject();

        Assert.Equal(5, publication.Mobiles);
        // The repeated donor name publishes twice: once under the plain identity and once numbered.
        Assert.Equal(3, publication.Published);
        Assert.Equal(1, publication.Unpublished);
        Assert.Equal(1, publication.HumanMobiles);
        Assert.Equal("CNT-007", document["sources"]!.AsArray()[0]!["recordId"]!.GetValue<string>());

        JsonObject rat = document["mobiles"]!.AsArray().Single(mobile => mobile!["donorId"]!.GetValue<int>() == 0)!.AsObject();
        Assert.Equal("rat", rat["actor"]!.GetValue<string>());
        Assert.Equal("published", rat["disposition"]!.GetValue<string>());
        Assert.Equal("General", rat["behaviour"]!.GetValue<string>());
        Assert.Equal("Animal", rat["affinity"]!.GetValue<string>());
        Assert.Equal(255, rat["maleTexture"]!.GetValue<int>());
        Assert.Equal(401, rat["corpse"]!["archive"]!.GetValue<int>());
        Assert.Equal(1, rat["corpse"]!["record"]!.GetValue<int>());
        Assert.Equal("EnemyRatMove", rat["sounds"]!["move"]!.GetValue<string>());
        Assert.Equal("None", rat["minMetalToHit"]!.GetValue<string>());
        Assert.Equal(1, rat["damage"]!["minimum"]!.GetValue<int>());
        Assert.Equal(4, rat["damage"]!["maximum"]!.GetValue<int>());
        Assert.Equal((9, 16), (rat["health"]!["minimum"]!.GetValue<int>(), rat["health"]!["maximum"]!.GetValue<int>()));
        Assert.Equal(1, rat["level"]!.GetValue<int>());
        Assert.Equal(6, rat["armorValue"]!.GetValue<int>());
        Assert.True(rat["hasIdle"]!.GetValue<bool>());
        Assert.False(rat["hasRangedAttack1"]!.GetValue<bool>());
        Assert.Equal(2, rat["weight"]!.GetValue<int>());
        Assert.Equal("Vermin", rat["team"]!.GetValue<string>());

        // A repeated donor name keeps both entries, the second addressed by the numbered identity.
        JsonObject variant = document["mobiles"]!.AsArray().Single(mobile => mobile!["disposition"]!.GetValue<string>() == "published-variant")!.AsObject();
        Assert.Equal("dragonling-40", variant["actor"]!.GetValue<string>());
        Assert.Equal("Dragonling", variant["donorName"]!.GetValue<string>());

        // The human mobile is a career-space fact, not a missing actor.
        JsonObject human = document["mobiles"]!.AsArray().Single(mobile => mobile!["disposition"]!.GetValue<string>() == "human-mobile")!.AsObject();
        Assert.Null(human["actor"]);
        Assert.Equal(130, human["donorId"]!.GetValue<int>());

        // A mobile nothing publishes still carries its parameters; the gap is stated, not dropped.
        JsonObject unpublished = document["mobiles"]!.AsArray().Single(mobile => mobile!["disposition"]!.GetValue<string>() == "unpublished")!.AsObject();
        Assert.Equal(39, unpublished["donorId"]!.GetValue<int>());
        Assert.Null(unpublished["actor"]);
        Assert.Contains("Horse", unpublished["donorName"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void PublishesTheRealDonorTableWhenItIsAvailable()
    {
        string donorPath = "/home/research/daggerfall-unity/Assets/Scripts/Utility/EnemyBasics.cs";
        if (!File.Exists(donorPath)) return;
        string payload = Path.Combine(RepositoryRoot(), "content", "worldrpg", "payloads", "daggerfall.base.json");

        Arena2MobileCatalogPublication publication = Arena2MobileCatalogDocument.Build(File.ReadAllText(donorPath), File.ReadAllText(payload), "donor/EnemyBasics.cs");
        // The measured reconciliation: 62 donor entries, one variant, one unpublished mobile.
        Assert.Equal(62, publication.Mobiles);
        Assert.Equal(1, publication.Unpublished);
        Assert.Equal(17, publication.HumanMobiles);
        JsonArray mobiles = JsonNode.Parse(publication.Json)!["mobiles"]!.AsArray();
        Assert.Equal(
            "Horse (unused, but can appear in merchant-sold soul traps)",
            mobiles.Single(mobile => mobile!["disposition"]!.GetValue<string>() == "unpublished")!["donorName"]!.GetValue<string>());
        // Every published actor that came from the donor table carries a damage range; an actor with no
        // range here is one the donor gives none, which is a fact a policy task must see.
        List<JsonNode> ranged = [.. mobiles.Where(mobile => mobile!["disposition"]!.GetValue<string>() == "published").Select(mobile => mobile!)];
        Assert.All(ranged, mobile =>
        {
            Assert.NotNull(mobile!["damage"]);
            Assert.False(string.IsNullOrEmpty(mobile["behaviour"]!.GetValue<string>()));
        });
    }

    [Fact]
    public void PublishesEachMobilesEnemyTypeAttackFlagsFromItsEnemyConfiguration()
    {
        // ENEMY???.CFG shares the CLASS??CFG record shape, whose byte 10 names the classic
        // enemy-type attack-modifier flags; a mobile with no configuration carries no flags.
        byte[] configuration = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/CLASS00.CFG"));
        configuration[10] = 0x04; // the humanoid bonus bit
        MonsterArchiveInventory enemyConfigurations = MonsterArchiveInventory.Enumerate(
            EnemyArchive(("ENEMY000.CFG", configuration)), "MONSTER.BSA");

        Arena2MobileCatalogPublication publication = Arena2MobileCatalogDocument.Build(
            Donor(), Pack(), "donor/EnemyBasics.cs", enemyConfigurations: enemyConfigurations);
        JsonArray mobiles = JsonNode.Parse(publication.Json)!["mobiles"]!.AsArray();

        Assert.Equal(0x04, mobiles.Single(mobile => mobile!["donorId"]!.GetValue<int>() == 0)!["attackModifierFlags"]!.GetValue<int>());
        Assert.All(mobiles.Where(mobile => mobile!["donorId"]!.GetValue<int>() != 0),
            mobile => Assert.Equal(0, mobile!["attackModifierFlags"]!.GetValue<int>()));
    }

    [Fact]
    public void TheRealEnemyConfigurationsCarryTheClassicBonusForTheVampireAndItsKin()
    {
        string donorPath = "/home/research/daggerfall-unity/Assets/Scripts/Utility/EnemyBasics.cs";
        string archivePath = Path.Combine(RepositoryRoot(), "local/arena2/MONSTER.BSA");
        if (!File.Exists(donorPath) || !File.Exists(archivePath)) return;

        Arena2MobileCatalogPublication publication = Arena2MobileCatalogDocument.Build(
            File.ReadAllText(donorPath),
            File.ReadAllText(Path.Combine(RepositoryRoot(), "content", "worldrpg", "payloads", "daggerfall.base.json")),
            donorPath,
            enemyConfigurations: MonsterArchiveInventory.Enumerate(File.ReadAllBytes(archivePath), "MONSTER.BSA"));
        JsonArray mobiles = JsonNode.Parse(publication.Json)!["mobiles"]!.AsArray();

        // The supplied classic corpus pays the humanoid bonus for the vampire (28) and the
        // zombie (30) families and nothing for the rat (0).
        Assert.Equal(0x04, mobiles.Single(mobile => mobile!["donorId"]!.GetValue<int>() == 28)!["attackModifierFlags"]!.GetValue<int>());
        Assert.Equal(0, mobiles.Single(mobile => mobile!["donorId"]!.GetValue<int>() == 0)!["attackModifierFlags"]!.GetValue<int>());
    }

    /// <summary>Builds a named BSA in the layout <see cref="MonsterArchiveInventory"/> reads.</summary>
    private static byte[] EnemyArchive(params (string Name, byte[] Payload)[] records)
    {
        const int HeaderBytes = 4;
        const int DirectoryEntryBytes = 18;
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

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }

    private static string Donor() => """
        public static MobileEnemy[] Enemies = new MobileEnemy[]
        {
            // Rat
            new MobileEnemy()
            {
                ID = 0,
                Behaviour = MobileBehaviour.General,
                Affinity = MobileAffinity.Animal,
                MaleTexture = 255,
                FemaleTexture = 255,
                CorpseTexture = CorpseTexture(401, 1),
                HasIdle = true,
                HasRangedAttack1 = false,
                HasRangedAttack2 = false,
                MoveSound = (int)SoundClips.EnemyRatMove,
                BarkSound = (int)SoundClips.EnemyRatBark,
                AttackSound = (int)SoundClips.EnemyRatAttack,
                MinMetalToHit = WeaponMaterialTypes.None,
                MinDamage = 1,
                MaxDamage = 4,
                MinHealth = 9,
                MaxHealth = 16,
                Level = 1,
                ArmorValue = 6,
                ParrySounds = false,
                MapChance = 0,
                Weight = 2,
                Team = MobileTeams.Vermin,
            },
            // Horse (unused, but can appear in merchant-sold soul traps)
            new MobileEnemy()
            {
                ID = 39,
                Behaviour = MobileBehaviour.General,
                MinDamage = 1,
                MaxDamage = 2,
                MinHealth = 4,
                MaxHealth = 8,
            },
            // Dragonling
            new MobileEnemy()
            {
                ID = 41,
                Behaviour = MobileBehaviour.General,
                MinDamage = 4,
                MaxDamage = 12,
            },
            // Dragonling
            new MobileEnemy()
            {
                ID = 40,
                Behaviour = MobileBehaviour.General,
                MinDamage = 4,
                MaxDamage = 12,
            },
            // Mage
            new MobileEnemy()
            {
                ID = 130,
                Behaviour = MobileBehaviour.Humanoid,
                MinDamage = 2,
                MaxDamage = 8,
            },
        };

        """;

    private static string Pack() => """
        {
          "actors": [
            { "id": "rat" },
            { "id": "dragonling" },
            { "id": "dragonling-40" }
          ]
        }
        """;
}
