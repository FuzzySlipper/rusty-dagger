using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published mobile catalog is what an actor-construction consumer resolves donor parameters
/// through, so the round-trip through the pack and the disagreements it must refuse are pinned here.
/// </summary>
public sealed class DaggerfallMobileCatalogTests
{
    [Fact]
    public void ResolvesAMobilesParametersFromThePublishedPackAlone()
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(Payload());

        Assert.Contains("CNT-007", definitions.Mobiles.SourceRecords);
        DaggerfallMobileDefinition rat = Assert.Contains(0, definitions.Mobiles.Mobiles);
        Assert.Equal("rat", rat.Identity);
        Assert.Equal("rat", rat.Actor);
        Assert.Equal("published", rat.Disposition);
        Assert.True(rat.IsPublished);
        Assert.Equal("General", rat.Behaviour);
        Assert.Equal("Animal", rat.Affinity);
        Assert.Equal("None", rat.MinMetalToHit);
        Assert.Equal((1, 4), rat.DamageRange);
        Assert.Equal((9, 16), rat.HealthRange);
        Assert.Equal(1, rat.Level);
        Assert.Equal(6, rat.ArmorValue);
        Assert.Equal(2, rat.Weight);
        Assert.Equal("Vermin", rat.Team);
        Assert.Equal(401, rat.CorpseArchive);
        Assert.Equal(1, rat.CorpseRecord);
        Assert.Equal("EnemyRatMove", rat.MoveSound);
        Assert.True(rat.HasIdle);
        Assert.False(rat.HasRangedAttack1);

        // The catalog resolves an actor to its mobile record, which is how a policy consumer reaches the
        // donor's numbers.
        DaggerfallMobileDefinition byActor = definitions.Mobiles.ForActor("rat") ?? throw new InvalidOperationException("the actor did not resolve");
        Assert.Equal(rat.DonorId, byActor.DonorId);

        // A mobile the product does not publish keeps its parameters and its gap.
        DaggerfallMobileDefinition horse = Assert.Single(definitions.Mobiles.Unpublished);
        Assert.Equal(39, horse.DonorId);
        Assert.Contains("Horse", horse.DonorName, StringComparison.Ordinal);
        Assert.Null(horse.Actor);
        Assert.Equal("unpublished", horse.Disposition);
        // Every published actor the donor table names resolves back through the catalog.
        Assert.All(definitions.Mobiles.Mobiles.Values.Where(mobile => mobile.IsPublished), mobile =>
        {
            Assert.NotNull(mobile.Actor);
            Assert.True(definitions.Actors.ContainsKey(new DaggerfallActorId(mobile.Actor!)), $"actor '{mobile.Actor}' is not in the pack");
        });
    }

    [Fact]
    public void RefusesACatalogThatDisagreesWithThePack()
    {
        // A mobile that names an actor the pack does not define, and a published mobile with no actor at
        // all: both would leave a consumer resolving a record that points nowhere.
        JsonObject payload = JsonNode.Parse(PayloadJson())!.AsObject();
        JsonObject named = payload["mobiles"]!["mobiles"]!.AsArray().First(mobile => mobile!["disposition"]!.GetValue<string>() == "published")!.AsObject();
        named["actor"] = "no-such-actor";
        JsonObject stripped = payload["mobiles"]!["mobiles"]!.AsArray().Last(mobile => mobile!["disposition"]!.GetValue<string>() == "published")!.AsObject();
        stripped["actor"] = null;

        DaggerfallContentException failure = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString())));

        Assert.True(
            failure.Diagnostics.Any(message => message.Contains("'no-such-actor'", StringComparison.Ordinal) && message.Contains("does not define", StringComparison.Ordinal)),
            $"the unknown actor was not named: {string.Join(" | ", failure.Diagnostics)}");
        Assert.True(
            failure.Diagnostics.Any(message => message.Contains("carries no actor identity", StringComparison.Ordinal)),
            $"the actorless published mobile was not named: {string.Join(" | ", failure.Diagnostics)}");
    }

    [Fact]
    public void RefusesADuplicateDonorIdAndAPayloadWithNoMobileCatalog()
    {
        JsonObject payload = JsonNode.Parse(PayloadJson())!.AsObject();
        JsonObject rat = payload["mobiles"]!["mobiles"]!.AsArray().First(mobile => mobile!["donorId"]!.GetValue<int>() == 0)!.AsObject();
        payload["mobiles"]!["mobiles"]!.AsArray().Add(rat.DeepClone());
        DaggerfallContentException duplicate = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString())));
        Assert.True(
            duplicate.Diagnostics.Any(message => message.Contains("donor id 0 twice", StringComparison.Ordinal)),
            $"the duplicate donor id was not named: {string.Join(" | ", duplicate.Diagnostics)}");

        JsonObject withoutMobiles = JsonNode.Parse(PayloadJson())!.AsObject();
        Assert.True(withoutMobiles.Remove("mobiles"), "the published payload carries no mobile section to remove");
        DaggerfallContentException missing = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(withoutMobiles.ToJsonString())));
        Assert.True(
            missing.Diagnostics.Any(message => message.Contains("no mobile catalog section", StringComparison.Ordinal)),
            $"the missing catalog was not named: {string.Join(" | ", missing.Diagnostics)}");
    }

    private static byte[] Payload() => System.Text.Encoding.UTF8.GetBytes(PayloadJson());

    private static string PayloadJson() => File.ReadAllText(Path.Combine(
        RepositoryRoot(), "content", "worldrpg", "payloads", "daggerfall.base.json"));

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test output.");
    }
}
