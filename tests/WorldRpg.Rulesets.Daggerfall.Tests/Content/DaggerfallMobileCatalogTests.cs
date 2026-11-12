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

        Assert.Contains("daggerfall-unity/Assets/Scripts/Utility/EnemyBasics.cs", definitions.Mobiles.SourcePaths);
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
        Assert.False(rat.CanOpenDoors);
        Assert.True(Assert.Contains(1, definitions.Mobiles.Mobiles).CanOpenDoors);

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
    public void JoinsEachMobileToTheAuthoredActorsWhenItLoads()
    {
        DaggerfallMobileCatalogSet mobiles = TestPayload.Definitions.Mobiles;
        // The donor names some mobiles twice; the authored actor numbered after the donor id is the variant.
        DaggerfallMobileDefinition variant = Assert.Single(mobiles.Mobiles.Values, mobile => mobile.Disposition == "published-variant");
        Assert.Equal($"{variant.Identity}-{variant.DonorId}", variant.Actor);
        // The human mobiles are a career-space fact, not missing actors.
        Assert.Equal(17, mobiles.HumanMobiles.Count());
        Assert.All(mobiles.HumanMobiles, mobile => Assert.Null(mobile.Actor));

        // An authored actor added for a mobile nothing published reaches the catalog with no new import.
        JsonObject payload = TestPayload.Sections("mobiles", "actors");
        JsonObject horse = payload["actors"]!.AsArray().First(actor => actor!["id"]!.GetValue<string>() == "rat")!.DeepClone().AsObject();
        horse["id"] = "horse";
        horse["mobileId"] = 39;
        payload["actors"]!.AsArray().Add(horse);

        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(TestPayload.Splice(payload));

        DaggerfallMobileDefinition published = Assert.Contains(39, definitions.Mobiles.Mobiles);
        Assert.Equal("horse", published.Actor);
        Assert.Equal("published", published.Disposition);
        Assert.Empty(definitions.Mobiles.Unpublished);
        Assert.Equal(39, definitions.Mobiles.ForActor("horse")?.DonorId);
    }

    [Fact]
    public void RefusesTwoMobilesThatJoinOneActor()
    {
        // Two donor records whose identities both name one authored actor would leave a consumer resolving two
        // mobiles for it.
        JsonObject payload = TestPayload.Sections("mobiles", "actors");
        payload["mobiles"]!["mobiles"]!.AsArray().First(mobile => mobile!["donorId"]!.GetValue<int>() == 1)!["identity"] = "rat";

        DaggerfallContentException failure = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(payload)));

        Assert.True(
            failure.Diagnostics.Any(message => message.Contains("names actor 'rat' twice", StringComparison.Ordinal)),
            $"the doubly joined actor was not named: {string.Join(" | ", failure.Diagnostics)}");
    }

    [Fact]
    public void RefusesADuplicateDonorIdAndAPayloadWithNoMobileCatalog()
    {
        JsonObject payload = TestPayload.Sections("mobiles", "actors");
        JsonObject rat = payload["mobiles"]!["mobiles"]!.AsArray().First(mobile => mobile!["donorId"]!.GetValue<int>() == 0)!.AsObject();
        payload["mobiles"]!["mobiles"]!.AsArray().Add(rat.DeepClone());
        DaggerfallContentException duplicate = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(payload)));
        Assert.True(
            duplicate.Diagnostics.Any(message => message.Contains("donor id 0 twice", StringComparison.Ordinal)),
            $"the duplicate donor id was not named: {string.Join(" | ", duplicate.Diagnostics)}");

        byte[] withoutMobiles = TestPayload.Without("mobiles");
        Assert.Null(TestSessions.TopLevelSection(withoutMobiles, "mobiles"));
        DaggerfallContentException missing = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(withoutMobiles));
        Assert.True(
            missing.Diagnostics.Any(message => message.Contains("no mobile catalog section", StringComparison.Ordinal)),
            $"the missing catalog was not named: {string.Join(" | ", missing.Diagnostics)}");
    }

    [Fact]
    public void RefusesAnActorWhoseMobileReferenceDangles()
    {
        // An actor's mobile id is a media reference, so a reference the catalog does not carry is refused
        // with the actor and the id named rather than leaving the actor without a source record.
        JsonObject payload = TestPayload.Sections("mobiles", "actors");
        JsonObject rat = payload["actors"]!.AsArray().First(actor => actor!["id"]!.GetValue<string>() == "rat")!.AsObject();
        rat["mobileId"] = 999;
        DaggerfallContentException dangling = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(payload)));
        Assert.True(
            dangling.Diagnostics.Any(message => message.Contains("'rat'", StringComparison.Ordinal) && message.Contains("mobile 999", StringComparison.Ordinal) && message.Contains("does not carry", StringComparison.Ordinal)),
            $"the dangling mobile reference was not named: {string.Join(" | ", dangling.Diagnostics)}");

        // A reference that resolves to another actor would give this actor that mobile's media.
        JsonObject swapped = TestPayload.Sections("mobiles", "actors");
        JsonObject imp = swapped["actors"]!.AsArray().First(actor => actor!["id"]!.GetValue<string>() == "imp")!.AsObject();
        imp["mobileId"] = 0;
        DaggerfallContentException mismatch = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(swapped)));
        Assert.True(
            mismatch.Diagnostics.Any(message => message.Contains("'imp'", StringComparison.Ordinal) && message.Contains("resolves to actor 'rat'", StringComparison.Ordinal)),
            $"the mismatched mobile reference was not named: {string.Join(" | ", mismatch.Diagnostics)}");
    }

    private static byte[] Payload() => TestPayload.CombinedBytes;
}
