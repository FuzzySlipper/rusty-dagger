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
