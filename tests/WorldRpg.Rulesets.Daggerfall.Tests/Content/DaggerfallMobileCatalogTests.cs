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

        // Publishing a previously unpublished creature spec reaches the catalog with no new import.
        JsonObject payload = TestPayload.Sections("actorTemplateCreatures", "actorSpecsCreatures");
        JsonObject horseTemplate = payload["actorTemplateCreatures"]!["templates"]!.AsArray().First(template => template!["id"]!.GetValue<string>() == "horse")!.AsObject();
        horseTemplate["health"] = new JsonObject { ["minimum"] = 10, ["maximum"] = 20 };
        horseTemplate["level"] = 1;
        horseTemplate["attacks"] = new JsonArray { new JsonObject { ["minimumDamage"] = 1, ["maximumDamage"] = 4 } };
        JsonObject horse = payload["actorSpecsCreatures"]!["specs"]!.AsArray().First(spec => spec!["id"]!.GetValue<string>() == "horse")!.AsObject();
        horse["published"] = true;

        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(TestPayload.Splice(payload));

        DaggerfallMobileDefinition published = Assert.Contains(39, definitions.Mobiles.Mobiles);
        Assert.Equal("horse", published.Actor);
        Assert.Equal("published", published.Disposition);
        Assert.Empty(definitions.Mobiles.Unpublished);
        Assert.Equal(39, definitions.Mobiles.ForActor("horse")?.DonorId);
    }

    [Fact]
    public void ReadsAMobilesTacticDefaultingToLegacyAndRefusesABlankOne()
    {
        // The first opt-ins: two class enemies weigh their kit with authored tendencies, and the bear is a beast; every other
        // mobile fights with the legacy tactic and authors no tendencies.
        var read = DaggerfallBaseContent.Read(Payload()).Mobiles.Mobiles;
        Assert.Equal(("intelligent", new DaggerfallTacticTendencies(.6d, .8d)), (read[130].Tactic, read[130].Tendencies));
        Assert.Equal(("intelligent", new DaggerfallTacticTendencies(.4d, .3d)), (read[142].Tactic, read[142].Tendencies));
        Assert.Equal(("beast", (DaggerfallTacticTendencies?)null), (read[4].Tactic, read[4].Tendencies));
        Assert.All(read.Values.Where(mobile => mobile.DonorId is not (130 or 142 or 4)), mobile =>
            Assert.Equal((DaggerfallMobileDefinition.LegacyTactic, (DaggerfallTacticTendencies?)null), (mobile.Tactic, mobile.Tendencies)));

        JsonObject payload = TestPayload.Sections("actorTemplateCreatures");
        JsonObject ratTemplate = payload["actorTemplateCreatures"]!["templates"]!.AsArray().First(t => t!["id"]!.GetValue<string>() == "rat")!.AsObject();
        ratTemplate["tactic"] = "beast";
        Assert.Equal("beast", DaggerfallBaseContent.Read(TestPayload.Splice(payload)).Mobiles.Mobiles[0].Tactic);

        ratTemplate["tactic"] = " ";
        DaggerfallContentException blank = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(payload)));
        Assert.True(blank.Diagnostics.Any(message => message.Contains("rat", StringComparison.Ordinal) && message.Contains("tactic", StringComparison.Ordinal)),
            $"the blank tactic was not named: {string.Join(" | ", blank.Diagnostics)}");

        ratTemplate["tactic"] = "intelligent";
        ratTemplate["tendencies"] = new JsonObject { ["courage"] = 1.5, ["discipline"] = .2 };
        DaggerfallContentException outOfRange = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(payload)));
        Assert.Contains(outOfRange.Diagnostics, message => message.Contains("tendencies", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusesDuplicateSpecIdsAndDuplicateMobileIds()
    {
        JsonObject payload = TestPayload.Sections("actorSpecsCreatures");
        JsonObject imp = payload["actorSpecsCreatures"]!["specs"]!.AsArray().First(spec => spec!["id"]!.GetValue<string>() == "imp")!.AsObject();
        imp["id"] = "rat";

        DaggerfallContentException dupId = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(payload)));
        Assert.Contains(dupId.Diagnostics, message => message.Contains("Duplicate actor spawner spec 'rat'", StringComparison.Ordinal));

        JsonObject dupMobile = TestPayload.Sections("actorSpecsCreatures");
        JsonObject impMobile = dupMobile["actorSpecsCreatures"]!["specs"]!.AsArray().First(spec => spec!["id"]!.GetValue<string>() == "imp")!.AsObject();
        impMobile["classicBinding"]!["mobileId"] = 0;

        DaggerfallContentException dupMobileEx = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(dupMobile)));
        Assert.Contains(dupMobileEx.Diagnostics, message => message.Contains("mobileId 0", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusesAPayloadWithNoActorTemplatesOrNoSpecs()
    {
        JsonObject emptyTemplates = TestPayload.Sections("actorTemplateBases", "actorTemplateCreatures", "actorTemplateClasses", "actorTemplateRoles");
        foreach (string name in new[] { "actorTemplateBases", "actorTemplateCreatures", "actorTemplateClasses", "actorTemplateRoles" })
            emptyTemplates[name] = new JsonObject { ["templates"] = new JsonArray() };
        DaggerfallContentException missingTemplates = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(emptyTemplates)));
        Assert.Contains(missingTemplates.Diagnostics, message => message.Contains("no actor templates", StringComparison.Ordinal));

        JsonObject emptySpecs = TestPayload.Sections("actorSpecsCreatures", "actorSpecsEnemies", "actorSpecsTownsfolk", "actorSpecsCast");
        foreach (string name in new[] { "actorSpecsCreatures", "actorSpecsEnemies", "actorSpecsTownsfolk", "actorSpecsCast" })
            emptySpecs[name] = new JsonObject { ["specs"] = new JsonArray() };
        DaggerfallContentException missingSpecs = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(emptySpecs)));
        Assert.Contains(missingSpecs.Diagnostics, message => message.Contains("no actor spawner specs", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusesASpawnerWhoseTemplateDangles()
    {
        JsonObject payload = TestPayload.Sections("actorSpecsCreatures");
        JsonObject rat = payload["actorSpecsCreatures"]!["specs"]!.AsArray().First(spec => spec!["id"]!.GetValue<string>() == "rat")!.AsObject();
        rat["template"] = "no-such-template";
        DaggerfallContentException dangling = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(payload)));
        Assert.Contains(dangling.Diagnostics, message => message.Contains("'rat'", StringComparison.Ordinal) && message.Contains("no-such-template", StringComparison.Ordinal));
    }

    private static byte[] Payload() => TestPayload.CombinedBytes;
}
