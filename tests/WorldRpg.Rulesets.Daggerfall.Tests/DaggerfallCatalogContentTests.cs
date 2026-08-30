using WorldRpg.Rulesets.Daggerfall.Content;
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
        Assert.Equal(0, definitions.Actions["skeleton-strike"].AttackRangeIndex);
        Assert.Equal(2, definitions.Actions["thief-strike"].MinimumDamage);
        Assert.Equal(8, definitions.Actions["thief-strike"].MaximumDamage);
        Assert.Equal(80, definitions.LootTables["T"].MaximumGold);
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
