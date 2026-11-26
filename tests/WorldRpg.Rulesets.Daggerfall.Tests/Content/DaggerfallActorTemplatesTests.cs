using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Load-time inheritance resolution, capability projection, encounter spawner IDs, and concrete
/// instance identity resolution for the unified actor template and spawner spec catalog (#11093).
/// </summary>
public sealed class DaggerfallActorTemplatesTests
{
    [Fact]
    public void Resolves_inheritance_chains_capabilities_and_career_defaults_cleanly()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorCatalog catalog = definitions.ActorCatalog;

        Assert.NotEmpty(catalog.Templates);
        Assert.NotEmpty(catalog.Specs);

        DaggerfallActorTemplateDefinition orcShaman = catalog.Templates["orc-shaman"];
        Assert.Equal(["actor-base", "creature-base", "humanoid-monster", "orc", "orc-shaman"], orcShaman.InheritanceChain);
        Assert.True(orcShaman.Capabilities.CanOpenDoors);
        Assert.Equal(ActorGearPolicy.FixedVariantGear, orcShaman.Capabilities.GearPolicy);
        Assert.Equal(0, orcShaman.Capabilities.FixedEquipmentVariant);
        Assert.Equal(DaggerfallEnemyGroup.Humanoid, orcShaman.Capabilities.EnemyGroup);
        Assert.Equal(DaggerfallSkills.Orcish, orcShaman.Capabilities.LanguageSkill);

        DaggerfallActorTemplateDefinition mage = catalog.Templates["mage"];
        Assert.True(mage.Capabilities.CastsMagic);
        Assert.Equal("class00", mage.CareerRef);

        DaggerfallActorTemplateDefinition skeleton = catalog.Templates["skeletal-warrior"];
        Assert.True(skeleton.Capabilities.TakesExtraBluntDamage);
        Assert.True(skeleton.Capabilities.UsesNaturalArmor);
        Assert.Equal(DaggerfallEnemyGroup.Undead, skeleton.Capabilities.EnemyGroup);
        Assert.Equal([ActorGender.Neutral], skeleton.Capabilities.EffectiveAllowedGenders);

        DaggerfallActorTemplateDefinition cityWatch = catalog.Templates["city-watch"];
        Assert.Equal("class18", cityWatch.CareerRef);
        Assert.Equal(ActorScalingMode.CareerLeveled, cityWatch.Capabilities.Scaling);
        Assert.Equal(ActorGearPolicy.GeneratedClassGear, cityWatch.Capabilities.GearPolicy);
        Assert.True(cityWatch.Capabilities.CityWatchArrest);
        Assert.Equal(ActorBodyWeightMode.GenderedHumanoid, cityWatch.Capabilities.BodyWeightMode);

        // Enemy spawner specs preserve runtime ids while pointing to canonical class templates.
        Assert.True(definitions.Actors.ContainsKey(new("encounter-mage")));
        Assert.Equal("mage", definitions.Actors[new("encounter-mage")].TemplateId);
        Assert.Equal("encounter-mage", definitions.Actors[new("encounter-mage")].SpawnerId);
        Assert.Equal("thief", definitions.Actors[new("thief")].TemplateId);
        Assert.Equal("archer", definitions.Actors[new("archer")].TemplateId);
        Assert.Equal(62, definitions.Actors.Count); // player + 42 published creatures + 19 enemy classes
    }

    [Fact]
    public void Encounter_tables_resolve_spawner_spec_ids_into_published_mobiles()
    {
        DaggerfallEncounterSet encounters = TestPayload.Definitions.Encounters;
        Assert.Equal(45, encounters.Tables.Count);
        Assert.NotNull(encounters.SpawnerTables);
        Assert.Equal(45, encounters.SpawnerTables!.Count);
        Assert.All(encounters.SpawnerTables!, table =>
        {
            Assert.NotEmpty(table);
            Assert.All(table, spawnerId => Assert.True(TestPayload.Definitions.ActorCatalog.Specs.ContainsKey(spawnerId),
                $"Encounter table references unknown spawner '{spawnerId}'."));
        });
    }

    [Fact]
    public void Inheritance_supports_scalar_override_map_merge_list_replace_and_null_clear()
    {
        JsonObject sections = TestPayload.Sections("actorTemplateCreatures");
        JsonArray templates = sections["actorTemplateCreatures"]!["templates"]!.AsArray();
        JsonObject rat = templates.First(t => t!["id"]!.GetValue<string>() == "rat")!.AsObject();
        rat["attributes"] = new JsonObject { ["strength"] = 42, ["endurance"] = 55 };
        rat["attacks"] = new JsonArray { new JsonObject { ["minimumDamage"] = 2, ["maximumDamage"] = 5 } };
        rat["lootTableKey"] = null;

        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(TestPayload.Splice(sections));
        DaggerfallActorTemplateDefinition resolved = definitions.ActorCatalog.Templates["rat"];

        Assert.Equal(42, resolved.Stats.Values[new("strength")]);
        Assert.Equal(55, resolved.Stats.Values[new("endurance")]);
        Assert.Equal(50, resolved.Stats.Values[new("luck")]); // inherited from actor-base
        Assert.Single(resolved.Attacks);
        Assert.Equal(new DaggerfallAttackRange(2, 5), resolved.Attacks[0]);
        Assert.Null(resolved.LootTableKey); // explicitly cleared
    }

    [Fact]
    public void Validation_refuses_unknown_parent_cycle_excess_depth_forbidden_field_and_disallowed_gender()
    {
        // Unknown parent
        JsonObject badParent = TestPayload.Sections("actorTemplateCreatures");
        badParent["actorTemplateCreatures"]!["templates"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "orphan-test",
            ["extends"] = "missing-parent",
        });
        DaggerfallContentException orphanEx = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(badParent)));
        Assert.Contains(orphanEx.Diagnostics, d => d.Contains("missing-parent", StringComparison.Ordinal));

        // Cycle
        JsonObject cycle = TestPayload.Sections("actorTemplateCreatures");
        cycle["actorTemplateCreatures"]!["templates"]!.AsArray().Add(new JsonObject { ["id"] = "cycle-a", ["extends"] = "cycle-b" });
        cycle["actorTemplateCreatures"]!["templates"]!.AsArray().Add(new JsonObject { ["id"] = "cycle-b", ["extends"] = "cycle-a" });
        DaggerfallContentException cycleEx = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(cycle)));
        Assert.Contains(cycleEx.Diagnostics, d => d.Contains("cycle", StringComparison.OrdinalIgnoreCase));

        // Depth > 4 (actor-base -> creature-base -> humanoid-monster -> orc -> orc-shaman is 4; one more child is 5)
        JsonObject tooDeep = TestPayload.Sections("actorTemplateCreatures");
        tooDeep["actorTemplateCreatures"]!["templates"]!.AsArray().Add(new JsonObject { ["id"] = "orc-shaman-elder", ["extends"] = "orc-shaman" });
        DaggerfallContentException deepEx = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(tooDeep)));
        Assert.Contains(deepEx.Diagnostics, d => d.Contains("maximum depth 4", StringComparison.Ordinal));

        // Forbidden instance field on template
        JsonObject badField = TestPayload.Sections("actorTemplateCreatures");
        badField["actorTemplateCreatures"]!["templates"]!.AsArray().First(t => t!["id"]!.GetValue<string>() == "rat")!["gender"] = "male";
        DaggerfallContentException fieldEx = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(badField)));
        Assert.Contains(fieldEx.Diagnostics, d => d.Contains("instance-only field 'gender'", StringComparison.Ordinal));

        // Disallowed gender on spawner (skeletal-warrior inherits allowedGenders: ["neutral"] from undead)
        JsonObject badGender = TestPayload.Sections("actorSpecsCreatures");
        badGender["actorSpecsCreatures"]!["specs"]!.AsArray().First(s => s!["id"]!.GetValue<string>() == "skeletal-warrior")!["genders"] =
            new JsonArray { new JsonObject { ["gender"] = "female", ["weight"] = 100 } };
        DaggerfallContentException genderEx = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(badGender)));
        Assert.Contains(genderEx.Diagnostics, d => d.Contains("gender 'female'", StringComparison.Ordinal));
    }

    [Fact]
    public void Concrete_actor_instance_identity_resolves_deterministically_for_every_live_actor()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition skeleton = definitions.RequireActor(new("skeletal-warrior"));
        DaggerfallActorDefinition nymph = definitions.RequireActor(new("nymph"));
        DaggerfallActorDefinition archer = definitions.RequireActor(new("archer"));

        ActorInstanceIdentity skeletonId = DaggerfallActorInstanceResolver.Resolve(definitions, skeleton, 1001L);
        Assert.Equal("skeletal-warrior", skeletonId.SpawnerId);
        Assert.Equal("skeletal-warrior", skeletonId.TemplateId);
        Assert.Equal(ActorGender.Neutral, skeletonId.Gender);
        Assert.Equal("skeletal-warrior", skeletonId.Race);
        Assert.Equal("undead", skeletonId.FactionId);

        ActorInstanceIdentity nymphId = DaggerfallActorInstanceResolver.Resolve(definitions, nymph, 1002L);
        Assert.Equal(ActorGender.Female, nymphId.Gender);
        Assert.Equal("nymphs", nymphId.FactionId);

        ActorInstanceIdentity archerA = DaggerfallActorInstanceResolver.Resolve(definitions, archer, 2001L, regionIndex: 17);
        ActorInstanceIdentity archerB = DaggerfallActorInstanceResolver.Resolve(definitions, archer, 2001L, regionIndex: 17);
        Assert.Equal(archerA.Gender, archerB.Gender);
        Assert.Equal(archerA.Race, archerB.Race);
        Assert.True(archerA.Gender is ActorGender.Male or ActorGender.Female);
        Assert.False(string.IsNullOrWhiteSpace(archerA.Race));
        Assert.Equal("archer", archerA.TemplateId);
        Assert.Equal("archer", archerA.SpawnerId);
    }
}
