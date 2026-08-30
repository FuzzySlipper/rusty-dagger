using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using System.Globalization;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallCatalogContentTests
{
    [Fact]
    public void LoadsTheCompleteDonorCatalogWithTheExplicitHorseGap()
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));

        Assert.Equal(9, definitions.Vocabulary.Attributes.Count);
        Assert.Equal(35, definitions.Vocabulary.Skills.Count);
        Assert.Equal(3, definitions.Vocabulary.Tracks.Count);
        Assert.Equal(7, definitions.Vocabulary.ArmorParts.Count);
        Assert.Equal(2, definitions.Vocabulary.Progression.Count);
        Assert.Equal(42, definitions.Actors.Values.Count(actor => actor.Kind == "monster"));
        Assert.DoesNotContain(definitions.Actors.Values, actor => actor.MobileId == 39);
        Assert.Equal(31, definitions.Items.Count);
        Assert.Equal(25, definitions.EquipmentSlots.Count);
        Assert.Equal(5, definitions.Actions.Count);
        Assert.Equal(22, definitions.LootTables.Count);
        Assert.Equal(12, definitions.ArmorValuesByMaterial.Count);
        Assert.NotEmpty(definitions.RequireActor(new DaggerfallActorId("player")).Loadout);
        string expectedFingerprint = File.ReadAllText(Path.Combine(RepositoryRoot(), "tests/WorldRpg.Rulesets.Daggerfall.Tests/Fixtures/daggerfall.base.semantic.sha256")).Trim();
        Assert.Equal(expectedFingerprint, DaggerfallBaseContent.Fingerprint(definitions));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 40, 41, 42 }, definitions.Actors.Values.Where(actor => actor.Kind == "monster").Select(actor => actor.MobileId!.Value).Order());
        Assert.Equal(new[] { "mobile-39-horse-is-explicitly-absent", "chain2-material-alias-is-not-authored", "bows-retain-donor-both-hands-policy", "loot-matrix-uses-fall-exe-errata" }.Order(), definitions.DonorErrata.Select(erratum => erratum.Id).Order());
        Assert.All(definitions.LootCategoryPools, pool => Assert.Equal("deferred", pool.Status));
        Assert.Equal("both", definitions.Items[new DaggerfallItemId("iron-short-bow")].Weapon!.Handedness);
        Assert.Equal("right-hand", definitions.RequireActor(new DaggerfallActorId("player")).Loadout[0].EquipSlot!.Value.Value);
        Assert.Equal(8, definitions.RequireActor(new DaggerfallActorId("player")).HitPointsPerLevel);
        Assert.Equal(5, definitions.Actions["melee-attack"].StaminaCost);
        Assert.Equal(["melee-attack", "power-attack", "rat-bite", "skeleton-strike", "thief-strike"], definitions.Actions.Values.OrderBy(action => action.Id).Select(action => action.Id));
        Assert.Equal(0.75, definitions.Actions["melee-attack"].CooldownSeconds);
        Assert.Equal(1.2, definitions.Actions["power-attack"].CooldownSeconds);
        Assert.Equal(4, definitions.Actions["power-attack"].DamageBonus);
        Assert.Equal(0, definitions.Actions["skeleton-strike"].AttackRangeIndex);
        Assert.Equal(2, definitions.Actions["thief-strike"].MinimumDamage);
        Assert.Equal(8, definitions.Actions["thief-strike"].MaximumDamage);
        Assert.Equal(80, definitions.LootTables["T"].MaximumGold);
    }

    [Fact]
    public void LootTablesMatchTheCompleteClassicDonorMatrix()
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));
        string actual = string.Join(';', definitions.LootTables.Values.OrderBy(table => table.Key).Select(table => $"{table.Key}:{table.MinimumGold}-{table.MaximumGold}[{string.Join(',', table.Categories.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"))}]"));
        const string expected = "-:0-0[];A:1-10[armor=5,clothing=4,magic=2,misc2=2,weapons=5];B:0-0[plant1=10,plant2=10];C:2-20[armor=5,books=2,creature1=5,creature2=5,creature3=5,magic=3,misc1=5,misc2=2,plant1=10,plant2=10,religious=2,weapons=25];D:1-4[creature1=6,creature2=6,creature3=6,misc1=6,plant1=6,plant2=6,religious=4];E:20-80[armor=10,books=2,clothing=4,magic=3,misc2=1,religious=15,weapons=10];F:4-30[armor=50,creature1=5,creature2=5,creature3=5,magic=1,misc1=2,misc2=3,plant1=2,plant2=2,weapons=50];G:3-15[armor=50,clothing=5,magic=1,misc2=3,weapons=50];H:2-10[clothing=2,magic=1,weapons=100];I:0-0[magic=2,religious=5];J:50-150[armor=5,magic=3,weapons=5];K:1-10[armor=5,books=5,creature1=3,creature2=3,creature3=3,magic=3,misc1=3,misc2=2,plant1=3,plant2=3,religious=100,weapons=5];L:1-20[armor=50,clothing=75,creature1=3,creature2=3,creature3=3,magic=1,misc1=3,misc2=5,religious=3,weapons=50];M:1-15[armor=10,books=2,clothing=15,creature1=1,creature2=1,creature3=1,magic=1,misc1=2,misc2=3,plant1=1,plant2=1,religious=1,weapons=10];N:1-80[armor=5,books=5,clothing=20,creature1=5,creature2=5,creature3=5,magic=1,misc1=5,misc2=2,plant1=5,plant2=5,religious=5,weapons=5];O:5-20[armor=10,creature1=1,creature2=1,creature3=1,magic=2,misc1=1,plant1=1,plant2=1,weapons=15];P:5-20[armor=5,books=10,creature1=5,creature2=5,creature3=5,magic=2,misc1=5,misc2=5,plant1=5,plant2=5,weapons=10];Q:20-80[armor=10,books=5,clothing=35,creature1=8,creature2=8,creature3=8,magic=3,misc1=2,misc2=3,plant1=2,plant2=2,weapons=25];R:5-20[armor=5,creature1=3,creature2=3,creature3=3,magic=2,misc1=5,weapons=15];S:50-125[armor=10,books=5,creature1=5,creature2=5,creature3=5,magic=3,misc1=15,misc2=5,plant1=5,plant2=5,weapons=10];T:20-80[armor=100,magic=1,weapons=100];U:7-30[armor=10,books=2,creature1=5,creature2=5,creature3=5,magic=2,misc1=10,misc2=2,plant1=5,plant2=5,religious=10,weapons=10]";
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LootReceiptsFollowTheCompleteAdoptedCategoryOrder()
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));
        DaggerfallLootResult result = DaggerfallLootPolicy.Generate(definitions, "C", 1, (_, minimum, _) => minimum);

        Assert.Equal(["weapons", "armor", "creature1", "creature2", "creature3", "plant1", "plant2", "misc1", "misc2", "magic", "books", "religious"], result.Categories.Select(category => category.Category));
        Assert.All(result.Categories, category => Assert.Equal(3, category.Rolls.Count));
        Assert.All(result.Categories.Where(category => category.Category is "weapons" or "armor"), category => Assert.All(category.Rolls, roll => Assert.True(roll.Success)));
        Assert.All(result.Categories, category => Assert.All(category.Rolls, roll => Assert.Equal(roll.Roll < roll.Chance, roll.Success)));
    }

    [Fact]
    public void SessionAllocatorIsMonotonicAndCollisionFreeAcrossRetries()
    {
        DaggerfallUniqueItemAllocator allocator = new(100);
        Assert.Equal(100UL, allocator.Allocate());
        Assert.Equal(101UL, allocator.Allocate());
        Assert.Equal(102UL, allocator.Allocate());
    }

    [Fact]
    public void RejectsAUnicodeIdentifierThatIsShortInUtf16ButNotEngineCompatible()
    {
        string payload = File.ReadAllText(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json"));

        Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(payload.Replace("\"id\": \"rat\"", "\"id\": \"rát\"", StringComparison.Ordinal))));
    }

    [Fact]
    public void SemanticFingerprintDoesNotDependOnTheProcessCulture()
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string expectedFingerprint = File.ReadAllText(Path.Combine(RepositoryRoot(), "tests/WorldRpg.Rulesets.Daggerfall.Tests/Fixtures/daggerfall.base.semantic.sha256")).Trim();
            Assert.Equal(expectedFingerprint, DaggerfallBaseContent.Fingerprint(definitions));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("\"strength\": 50", "\"strength\": 50, \"strength\": 51")]
    [InlineData("\"material\": \"iron\"", "\"material\": \"not-a-material\"")]
    [InlineData("\"material\": \"iron\"", "\"material\": \"leather\"")]
    [InlineData("\"material\": \"iron\"", "\"material\": \"chain\"")]
    [InlineData("\"skill\": \"short-blade\"", "\"skill\": \"not-a-skill\"")]
    [InlineData("\"item\": \"iron-longsword\"", "\"item\": \"not-an-item\"")]
    [InlineData("\"action\": \"melee-attack\"", "\"action\": \"not-an-action\"")]
    [InlineData("\"lootTableKey\": \"D\"", "\"lootTableKey\": \"Z\"")]
    [InlineData("\"mobileId\": 0", "\"mobileId\": 39")]
    [InlineData("\"equipSlot\": \"right-hand\"", "\"equipSlot\": \"not-a-slot\"")]
    [InlineData("\"quantity\": 25", "\"quantity\": 0")]
    [InlineData("\"quantity\": 25", "\"quantity\": \"25\"")]
    [InlineData("\"entityId\": 1001", "\"entityId\": 0")]
    [InlineData("\"entityId\": 1001", "\"entityId\": \"1001\"")]
    public void RejectsMalformedCatalogReferencesAndCanonicalLoadoutShapes(string before, string after)
    {
        string payload = File.ReadAllText(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json"));
        Assert.Contains(before, payload, StringComparison.Ordinal);
        Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(payload.Replace(before, after, StringComparison.Ordinal))));
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
