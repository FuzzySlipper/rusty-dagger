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
    [DonorFact("Assets/Scripts/Utility/EnemyBasics.cs")]
    public void Published_human_sprite_archives_match_both_donor_genders()
    {
        var donor = Arena2MobileTable.Read(File.ReadAllText(TestData.Donor("Assets/Scripts/Utility/EnemyBasics.cs")));
        foreach (var source in MobileSourceMetadata.All.Where(source => source.Id.Value >= 128))
        {
            var row = Assert.Single(donor, value => value.Id == source.Id.Value);
            Assert.Equal(row.MaleTexture, source.TextureArchive.Value);
            Assert.Equal(row.FemaleTexture, source.FemaleTextureArchive?.Value ?? source.TextureArchive.Value);
        }
    }

    [DonorFact("Assets/Scripts/Utility/EnemyBasics.cs")]
    public void Publishes_soul_glow_shadow_and_ranged_preference_from_source()
    {
        var publication = Arena2MobileCatalogDocument.Build(File.ReadAllText(TestData.Donor("Assets/Scripts/Utility/EnemyBasics.cs")), "donor/EnemyBasics.cs");
        var rows = JsonNode.Parse(publication.Json)!["mobiles"]!.AsArray();
        JsonNode Row(int id) => rows.Single(row => row!["donorId"]!.GetValue<int>() == id)!;
        Assert.Equal(3000, Row(21)["soulPoints"]!.GetValue<int>());
        Assert.True(Row(18)["noShadow"]!.GetValue<bool>());
        Assert.Equal(1.8f, Row(25)["glowColor"]!["red"]!.GetValue<float>(), 5);
        Assert.Equal(6.8f, Row(25)["glowColor"]!["green"]!.GetValue<float>(), 5);
        Assert.Equal(8.8f, Row(25)["glowColor"]!["blue"]!.GetValue<float>(), 5);
        Assert.True(Row(141)["prefersRanged"]!.GetValue<bool>());
        Assert.Null(Row(0)["glowColor"]);
        Assert.False(Row(0)["noShadow"]!.GetValue<bool>());
    }

    [Fact]
    public void PublishesEveryDonorParameterAndIdentityWithoutJoiningTheAuthoredActors()
    {
        Arena2MobileCatalogPublication publication = Arena2MobileCatalogDocument.Build(Donor(), "donor/EnemyBasics.cs");
        JsonObject document = JsonNode.Parse(publication.Json)!.AsObject();

        Assert.Equal(5, publication.Mobiles);
        Assert.Equal("donor/EnemyBasics.cs", document["sources"]!.AsArray()[0]!["path"]!.GetValue<string>());

        JsonObject rat = document["mobiles"]!.AsArray().Single(mobile => mobile!["donorId"]!.GetValue<int>() == 0)!.AsObject();
        Assert.Equal("rat", rat["identity"]!.GetValue<string>());
        // Which authored actor a mobile is, and so its disposition, is joined by the ruleset when it loads.
        Assert.False(rat.ContainsKey("actor"));
        Assert.False(rat.ContainsKey("disposition"));
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

        // A repeated donor name keeps both entries under one identity; the ruleset tells them apart by donor id.
        Assert.Equal(2, document["mobiles"]!.AsArray().Count(mobile => mobile!["identity"]!.GetValue<string>() == "dragonling"));

        // The senses flag is the donor entry's own: stated where the entry says so, false where it is silent.
        Assert.True(document["mobiles"]!.AsArray().Single(mobile => mobile!["donorId"]!.GetValue<int>() == 41)!["seesThroughInvisibility"]!.GetValue<bool>());
        Assert.False(rat["seesThroughInvisibility"]!.GetValue<bool>());
        Assert.False(rat["castsMagic"]!.GetValue<bool>());
        Assert.False(document.ContainsKey("divergences"));

        // A mobile no authored actor carries is published with its parameters all the same.
        JsonObject horse = document["mobiles"]!.AsArray().Single(mobile => mobile!["donorId"]!.GetValue<int>() == 39)!.AsObject();
        Assert.Contains("Horse", horse["donorName"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [DonorFact("Assets/Scripts/Utility/EnemyBasics.cs")]
    public void PublishesTheRealDonorTableWhenItIsAvailable()
    {
        string donorPath = TestData.Donor("Assets/Scripts/Utility/EnemyBasics.cs");

        Arena2MobileCatalogPublication publication = Arena2MobileCatalogDocument.Build(File.ReadAllText(donorPath), "donor/EnemyBasics.cs");
        Assert.Equal(62, publication.Mobiles);
        JsonArray mobiles = JsonNode.Parse(publication.Json)!["mobiles"]!.AsArray();
        // The donor's human-mobile space: 19 records, two of which the authored pack places as actors.
        Assert.Equal(19, mobiles.Count(mobile => mobile!["donorId"]!.GetValue<int>() >= MobileLedgerBuilder.FirstHumanMobileId));
        Assert.Equal(
            "Horse (unused, but can appear in merchant-sold soul traps)",
            mobiles.Single(mobile => mobile!["donorId"]!.GetValue<int>() == 39)!["donorName"]!.GetValue<string>());
        // EnemyBasics states CanOpenDoors per mobile; the behavior caller consumes this source fact
        // instead of granting a blanket product ability.
        Assert.True(mobiles.Single(mobile => mobile!["donorId"]!.GetValue<int>() == 1)!["canOpenDoors"]!.GetValue<bool>());
        Assert.False(mobiles.Single(mobile => mobile!["donorId"]!.GetValue<int>() == 0)!["canOpenDoors"]!.GetValue<bool>());
        // Every monster the donor table carries but the unused horse states a damage range and a behaviour; one with
        // no range is one the donor gives none, which is a fact a policy task must see.
        List<JsonNode> monsters = [.. mobiles.Where(mobile => mobile!["donorId"]!.GetValue<int>() is < MobileLedgerBuilder.FirstHumanMobileId and not 39).Select(mobile => mobile!)];
        Assert.All(monsters, mobile =>
        {
            Assert.NotNull(mobile!["damage"]);
            Assert.False(string.IsNullOrEmpty(mobile["behaviour"]!.GetValue<string>()));
        });
        // The caster flag and the loot table keys are the donor's own: the Monk keeps "T" and the City Watch
        // carries no key, so it generates no table loot.
        Assert.True(mobiles.Single(mobile => mobile!["donorId"]!.GetValue<int>() == 128)!["castsMagic"]!.GetValue<bool>());
        Assert.False(mobiles.Single(mobile => mobile!["donorId"]!.GetValue<int>() == 131)!["castsMagic"]!.GetValue<bool>());
        Assert.Equal("T", mobiles.Single(mobile => mobile!["donorId"]!.GetValue<int>() == 140)!["lootTableKey"]!.GetValue<string>());
        Assert.Null(mobiles.Single(mobile => mobile!["donorId"]!.GetValue<int>() == 146)!["lootTableKey"]);
    }

    [CorpusFact("CLASS00.CFG")]
    public void PublishesEachMobilesEnemyTypeAttackFlagsFromItsEnemyConfiguration()
    {
        // ENEMY???.CFG shares the CLASS??CFG record shape, whose byte 10 names the classic
        // enemy-type attack-modifier flags; a mobile with no configuration carries no flags.
        byte[] configuration = File.ReadAllBytes(TestData.Corpus("CLASS00.CFG"));
        configuration[10] = 0x04; // the humanoid bonus bit
        MonsterArchiveInventory enemyConfigurations = MonsterArchiveInventory.Enumerate(
            EnemyArchive(("ENEMY000.CFG", configuration)), "MONSTER.BSA");

        Arena2MobileCatalogPublication publication = Arena2MobileCatalogDocument.Build(
            Donor(), "donor/EnemyBasics.cs", enemyConfigurations: enemyConfigurations);
        JsonArray mobiles = JsonNode.Parse(publication.Json)!["mobiles"]!.AsArray();

        Assert.Equal(0x04, mobiles.Single(mobile => mobile!["donorId"]!.GetValue<int>() == 0)!["attackModifierFlags"]!.GetValue<int>());
        Assert.All(mobiles.Where(mobile => mobile!["donorId"]!.GetValue<int>() != 0),
            mobile => Assert.Equal(0, mobile!["attackModifierFlags"]!.GetValue<int>()));
    }

    [CorpusAndDonorFact(["MONSTER.BSA"], ["Assets/Scripts/Utility/EnemyBasics.cs"])]
    public void TheRealEnemyConfigurationsCarryTheClassicBonusForTheVampireAndItsKin()
    {
        string donorPath = TestData.Donor("Assets/Scripts/Utility/EnemyBasics.cs");
        string archivePath = TestData.Corpus("MONSTER.BSA");

        Arena2MobileCatalogPublication publication = Arena2MobileCatalogDocument.Build(
            File.ReadAllText(donorPath),
            donorPath,
            enemyConfigurations: MonsterArchiveInventory.Enumerate(File.ReadAllBytes(archivePath), "MONSTER.BSA"));
        JsonArray mobiles = JsonNode.Parse(publication.Json)!["mobiles"]!.AsArray();

        var configurations = MonsterArchiveInventory.Enumerate(File.ReadAllBytes(archivePath), "MONSTER.BSA");
        foreach (var record in configurations.EnemyConfigurations.Where(x => x.Configuration is not null))
        {
            var published = mobiles.Single(x => x!["donorId"]!.GetValue<int>() == record.MobileId)!;
            Assert.Equal((int)record.Configuration!.ResistanceFlags, published["resistanceFlags"]!.GetValue<int>());
            Assert.Equal((int)record.Configuration.ImmunityFlags, published["immunityFlags"]!.GetValue<int>());
            Assert.Equal((int)record.Configuration.LowToleranceFlags, published["lowToleranceFlags"]!.GetValue<int>());
            Assert.Equal((int)record.Configuration.CriticalWeaknessFlags, published["criticalWeaknessFlags"]!.GetValue<int>());
        }
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
                SeesThroughInvisibility = true,
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
}
