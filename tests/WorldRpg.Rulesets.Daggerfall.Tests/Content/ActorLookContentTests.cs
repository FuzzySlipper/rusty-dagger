using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11224: an actor's look is chosen in content, on its template or spec. A spec inherits its template's look unless
/// it states its own, and the look is validated as it loads.
/// </summary>
public sealed class ActorLookContentTests
{
    [Fact]
    public void The_orc_sellsword_draws_as_a_barbarian_splat_by_gender_and_specs_without_a_look_have_none()
    {
        DaggerfallActorCatalog catalog = TestPayload.Definitions.ActorCatalog;
        DaggerfallActorLook look = Assert.IsType<DaggerfallActorLook>(catalog.Specs["hire-orc-sellsword"].Look);
        Assert.Equal((DaggerfallActorLookKind.Mobile, (int?)143), (look.Kind, look.MobileId));
        Assert.Equal(("npc-470", "npc-469"), (look.SplatFor("male"), look.SplatFor("female")));
        Assert.Null(look.SplatFor("unspecified"));
        Assert.Null(catalog.Specs["hire-road-guard"].Look);
        DaggerfallActorDefinition published = TestPayload.Definitions.RequireActor(new DaggerfallActorId("hire-orc-sellsword"));
        Assert.Same(look, published.Look);
        Assert.Equal(143, published.DrawnMobileId);
    }

    [Fact]
    public void A_templates_look_reaches_its_specs_unless_they_state_their_own()
    {
        JsonObject sections = TestPayload.Sections("actorTemplateClasses", "actorSpecsPartyHire");
        JsonObject warrior = Template(sections, "actorTemplateClasses", "warrior");
        warrior["look"] = new JsonObject { ["kind"] = "mobile", ["mobileId"] = 144 };
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(TestPayload.Splice(sections));

        DaggerfallActorLook inherited = Assert.IsType<DaggerfallActorLook>(definitions.ActorCatalog.Specs["hire-road-guard"].Look);
        Assert.Equal((DaggerfallActorLookKind.Mobile, (int?)144), (inherited.Kind, inherited.MobileId));
        Assert.Same(definitions.ActorCatalog.Templates["warrior"].Look, inherited);
        Assert.Equal(143, definitions.ActorCatalog.Specs["hire-orc-sellsword"].Look!.MobileId);
    }

    [Theory]
    [InlineData("{\"kind\":\"portrait\"}", "unknown kind 'portrait'")]
    [InlineData("{\"kind\":\"mobile\"}", "mobileId exactly when its kind is mobile")]
    [InlineData("{\"kind\":\"townsperson\",\"mobileId\":143}", "mobileId exactly when its kind is mobile")]
    [InlineData("{\"kind\":\"billboard\",\"splat\":{\"male\":\"381\"}}", "names no splat character")]
    [InlineData("{\"kind\":\"townsperson\",\"splat\":{\"elder\":\"381\"}}", "unknown gender 'elder'")]
    [InlineData("{\"kind\":\"townsperson\",\"splat\":{\"male\":\"\"}}", "must name a splat character")]
    [InlineData("{\"kind\":\"mobile\",\"mobileId\":9999}", "names mobile 9999, which no spec binds")]
    [InlineData("{\"kind\":\"townsperson\",\"rig\":\"woman\"}", "unknown field 'rig'")]
    public void A_look_content_cannot_draw_is_refused_as_it_loads(string look, string refusal)
    {
        JsonObject sections = TestPayload.Sections("actorSpecsPartyHire");
        Spec(sections, "actorSpecsPartyHire", "hire-road-guard")["look"] = JsonNode.Parse(look);
        DaggerfallContentException refused = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(sections)));
        Assert.Contains(refused.Diagnostics, diagnostic => diagnostic.Contains("hire-road-guard", StringComparison.Ordinal)
            && diagnostic.Contains(refusal, StringComparison.Ordinal));
    }

    [Fact]
    public void A_spec_that_binds_a_classic_mobile_draws_only_in_a_mobile_look()
    {
        JsonObject sections = TestPayload.Sections("actorSpecsEnemies");
        JsonObject bound = sections["actorSpecsEnemies"]!["specs"]!.AsArray()
            .First(spec => spec!["classicBinding"] is JsonObject)!.AsObject();
        bound["look"] = new JsonObject { ["kind"] = "townsperson" };
        DaggerfallContentException refused = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(sections)));
        Assert.Contains(refused.Diagnostics, diagnostic => diagnostic.Contains(bound["id"]!.GetValue<string>(), StringComparison.Ordinal)
            && diagnostic.Contains("must be a mobile look", StringComparison.Ordinal));
    }

    private static JsonObject Template(JsonObject sections, string section, string id) =>
        sections[section]!["templates"]!.AsArray().First(entry => entry!["id"]!.GetValue<string>() == id)!.AsObject();

    private static JsonObject Spec(JsonObject sections, string section, string id) =>
        sections[section]!["specs"]!.AsArray().First(entry => entry!["id"]!.GetValue<string>() == id)!.AsObject();
}
