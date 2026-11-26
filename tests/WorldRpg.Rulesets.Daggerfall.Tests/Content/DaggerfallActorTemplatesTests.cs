using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
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

    [Fact]
    public void Career_defaults_apply_at_declaring_layer_preserving_parent_overrides_and_explicit_null_clears()
    {
        JsonObject sections = TestPayload.Sections("actorTemplateClasses");
        JsonArray classes = sections["actorTemplateClasses"]!["templates"]!.AsArray();
        JsonObject mage = classes.First(t => t!["id"]!.GetValue<string>() == "mage")!.AsObject();
        mage["attributes"] = new JsonObject { ["strength"] = 99 };
        mage["hitPointsPerLevel"] = 25;
        mage["health"] = new JsonObject { ["minimum"] = 80, ["maximum"] = 120 };

        // Child extending mage with NO overrides inherits mage's overrides (depth 4: actor-base -> humanoid -> combat-class -> mage -> archmage).
        classes.Add(new JsonObject { ["id"] = "archmage", ["extends"] = "mage" });
        // Child explicitly clearing hitPointsPerLevel to null keeps null instead of reverting to career default.
        classes.Add(new JsonObject { ["id"] = "mage-fixed-hp", ["extends"] = "mage", ["hitPointsPerLevel"] = null });

        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(TestPayload.Splice(sections));
        DaggerfallActorTemplateDefinition archmage = definitions.ActorCatalog.Templates["archmage"];
        DaggerfallActorTemplateDefinition mageFixed = definitions.ActorCatalog.Templates["mage-fixed-hp"];

        Assert.Equal(99, archmage.Stats.Values[new("strength")]);
        Assert.Equal(25, archmage.HitPointsPerLevel);
        Assert.Equal(new DaggerfallVitalRange(80, 120), archmage.Health);
        Assert.Null(mageFixed.HitPointsPerLevel);
    }

    [Fact]
    public void Template_race_constraints_are_enforced_at_load_and_instance_resolution()
    {
        // Conflicting fixedRace vs spawner race is refused at load.
        JsonObject badFixed = TestPayload.Sections("actorTemplateClasses", "actorSpecsEnemies");
        badFixed["actorTemplateClasses"]!["templates"]!.AsArray().First(t => t!["id"]!.GetValue<string>() == "mage")!["fixedRace"] = "breton";
        badFixed["actorSpecsEnemies"]!["specs"]!.AsArray().First(s => s!["id"]!.GetValue<string>() == "encounter-mage")!["race"] = "redguard";
        DaggerfallContentException fixedEx = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(badFixed)));
        Assert.Contains(fixedEx.Diagnostics, d => d.Contains("encounter-mage", StringComparison.Ordinal) && d.Contains("fixedRace", StringComparison.Ordinal));

        // Conflicting allowedRaces vs spawner race is refused at load.
        JsonObject badAllowed = TestPayload.Sections("actorTemplateClasses", "actorSpecsEnemies");
        badAllowed["actorTemplateClasses"]!["templates"]!.AsArray().First(t => t!["id"]!.GetValue<string>() == "mage")!["allowedRaces"] =
            new JsonArray { "breton", "high-elf" };
        badAllowed["actorSpecsEnemies"]!["specs"]!.AsArray().First(s => s!["id"]!.GetValue<string>() == "encounter-mage")!["race"] = "nord";
        DaggerfallContentException allowedEx = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(badAllowed)));
        Assert.Contains(allowedEx.Diagnostics, d => d.Contains("encounter-mage", StringComparison.Ordinal) && d.Contains("disallowed", StringComparison.Ordinal));

        // Valid allowedRaces prevents even an explicit runtime race from violating the template.
        JsonObject validAllowed = TestPayload.Sections("actorTemplateClasses");
        validAllowed["actorTemplateClasses"]!["templates"]!.AsArray().First(t => t!["id"]!.GetValue<string>() == "mage")!["allowedRaces"] =
            new JsonArray { "breton", "high-elf" };
        DaggerfallDefinitions defs = DaggerfallBaseContent.Read(TestPayload.Splice(validAllowed));
        DaggerfallActorDefinition mageDef = defs.RequireActor(new("encounter-mage"));
        ActorInstanceIdentity identity = DaggerfallActorInstanceResolver.Resolve(defs, mageDef, 4242L, explicitRace: "redguard");
        Assert.Contains(identity.Race, new[] { "breton", "high-elf" });
    }

    [Fact]
    public void Template_owned_spells_validate_references_and_drive_caster_selection()
    {
        // Unknown spell key is refused at load.
        JsonObject badSpell = TestPayload.Sections("actorTemplateCreatures");
        badSpell["actorTemplateCreatures"]!["templates"]!.AsArray().First(t => t!["id"]!.GetValue<string>() == "rat")!["spells"] =
            new JsonArray { "spell.does-not-exist" };
        DaggerfallContentException spellEx = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(badSpell)));
        Assert.Contains(spellEx.Diagnostics, d => d.Contains("rat", StringComparison.Ordinal) && d.Contains("spell.does-not-exist", StringComparison.Ordinal));

        // Adding spells to a template makes it a live caster; clearing spells on a caster removes its spells.
        JsonObject customSpells = TestPayload.Sections("actorTemplateCreatures");
        JsonArray creatures = customSpells["actorTemplateCreatures"]!["templates"]!.AsArray();
        creatures.First(t => t!["id"]!.GetValue<string>() == "rat")!["spells"] = new JsonArray { "spell.007", "spell.010" };
        creatures.First(t => t!["id"]!.GetValue<string>() == "orc-shaman")!["spells"] = null;

        DaggerfallDefinitions defs = DaggerfallBaseContent.Read(TestPayload.Splice(customSpells));
        DaggerfallActorDefinition rat = defs.RequireActor(new("rat"));
        DaggerfallActorDefinition shaman = defs.RequireActor(new("orc-shaman"));

        Assert.True(defs.EnemySpells.IsCaster(rat));
        Assert.Equal(["spell.007", "spell.010"], defs.EnemySpells.For(rat));
        Assert.False(defs.EnemySpells.IsCaster(shaman));
        Assert.Empty(defs.EnemySpells.For(shaman));

        // Explicit spellPolicy 'none' on an existing fixed caster OR a child extending a fixed caster
        // must stay None and must NOT silently flip back to Fixed when spells are still present.
        JsonObject nonePolicy = TestPayload.Sections("actorTemplateCreatures", "actorSpecsCreatures");
        JsonArray noneCreatures = nonePolicy["actorTemplateCreatures"]!["templates"]!.AsArray();
        noneCreatures.First(t => t!["id"]!.GetValue<string>() == "imp")!["spellPolicy"] = "none";
        // Child extending fixed-caster 'daedroth' (depth 4: actor-base -> creature-base -> daedra -> daedroth -> mute-daedroth)
        // overriding only spellPolicy to 'none' while inheriting daedroth's spells list.
        noneCreatures.Add(new JsonObject
        {
            ["id"] = "mute-daedroth",
            ["extends"] = "daedroth",
            ["spellPolicy"] = "none",
        });
        nonePolicy["actorSpecsCreatures"]!["specs"]!.AsArray()
            .First(s => s!["id"]!.GetValue<string>() == "daedroth")!["template"] = "mute-daedroth";

        DaggerfallDefinitions noneDefs = DaggerfallBaseContent.Read(TestPayload.Splice(nonePolicy));
        DaggerfallActorDefinition impDef = noneDefs.RequireActor(new("imp"));
        DaggerfallActorDefinition muteDaedroth = noneDefs.RequireActor(new("daedroth"));
        Assert.Equal(ActorSpellPolicy.None, impDef.SpellPolicy);
        Assert.False(noneDefs.EnemySpells.IsCaster(impDef));
        Assert.Empty(noneDefs.EnemySpells.For(impDef));
        Assert.Equal(ActorSpellPolicy.None, muteDaedroth.SpellPolicy);
        Assert.False(noneDefs.EnemySpells.IsCaster(muteDaedroth));
        Assert.Empty(noneDefs.EnemySpells.For(muteDaedroth));
    }

    [Fact]
    public void Live_combat_spawn_honors_spawner_gender_distribution_and_matches_appearance()
    {
        // Tighten encounter-mage spawner to female-only while keeping mage template allowing male/female.
        JsonObject femaleOnly = TestPayload.Sections("actorSpecsEnemies");
        femaleOnly["actorSpecsEnemies"]!["specs"]!.AsArray().First(s => s!["id"]!.GetValue<string>() == "encounter-mage")!["genders"] =
            new JsonArray { new JsonObject { ["gender"] = "female", ["weight"] = 100 } };
        DaggerfallDefinitions defs = DaggerfallBaseContent.Read(TestPayload.Splice(femaleOnly), TestPayload.Data);

        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: defs);
        for (int i = 0; i < 8; i++)
        {
            long id = fixture.Session.SpawnActor("encounter-mage", new(new(3f + i, 0f, -4f), 0f));
            var actor = fixture.Session.State.Actors.Get(id);
            ActorInstanceIdentity identity = actor.Actor.Get<ActorInstanceIdentity>();
            DaggerfallActorGender spriteGender = actor.Actor.Get<DaggerfallActorGender>();
            Assert.Equal(ActorGender.Female, identity.Gender);
            Assert.Equal("female", spriteGender.Value);
        }
    }

    [Fact]
    public void Initial_authored_placement_and_reentry_sprites_honor_spawner_gender_distribution()
    {
        // Tighten encounter-mage to female-only while mage template allows both male and female.
        JsonObject femaleOnly = TestPayload.Sections("actorSpecsEnemies");
        femaleOnly["actorSpecsEnemies"]!["specs"]!.AsArray().First(s => s!["id"]!.GetValue<string>() == "encounter-mage")!["genders"] =
            new JsonArray { new JsonObject { ["gender"] = "female", ["weight"] = 100 } };
        DaggerfallDefinitions defs = DaggerfallBaseContent.Read(TestPayload.Splice(femaleOnly), TestPayload.Data);

        long[] entityIds = [5001L, 5002L, 5003L, 5004L, 5005L, 5006L];
        string? expectedFemalePath = null;
        string? maleBasePath = null;

        using var fixture = new SanguineRoseSessionTests.Fixture(
            definitions: defs,
            prepareInputs: inputs =>
            {
                NormalizedActorSprite mageSprite = inputs.MobileSprites[128];
                Assert.NotNull(mageSprite.Female);
                maleBasePath = mageSprite.TexturePath;
                expectedFemalePath = mageSprite.Female!.TexturePath;
                Assert.NotEqual(maleBasePath, expectedFemalePath);

                Dictionary<long, AuthoredActor> actors = new(inputs.Project.Actors);
                Dictionary<long, NormalizedActorSprite> sprites = new(inputs.ActorSprites);
                foreach (long id in entityIds)
                {
                    actors[id] = new AuthoredActor(id, new("encounter-mage"), new(id - 5000f, 0f, -4f), Gender: "unspecified");
                    sprites[id] = mageSprite;
                }
                ProjectFacts project = new(inputs.Project.PlayerPosition, actors);
                return new DaggerfallSiteProfile(
                    project, inputs.Geometry, inputs.WorldAppearance, inputs.InitialLook, inputs.Materials,
                    sprites, inputs.MobileSprites, inputs.Audio, inputs.ClassicPresentation, inputs.Site,
                    inputs.Doors, inputs.ProfileKind, inputs.ProfileKey.LogicalId, inputs.Portals,
                    inputs.Anchors.Values.ToArray(), inputs.Lights, inputs.GroundContainerSprites,
                    inputs.DungeonMap, inputs.DungeonActions, inputs.DungeonActionModels,
                    inputs.InteriorBuilding, inputs.QuestMarkers, inputs.BillboardSprites);
            });

        foreach (long entityId in entityIds)
        {
            var actor = fixture.Session.State.Actors.Get(entityId);
            Assert.Equal(ActorGender.Female, actor.Actor.Get<ActorInstanceIdentity>().Gender);
            Assert.Equal("female", actor.Actor.Get<DaggerfallActorGender>().Value);
            NormalizedActorSprite? admitted = fixture.Session.ActiveAppearance.SpriteFor(entityId);
            Assert.NotNull(admitted);
            Assert.Equal(expectedFemalePath, admitted.TexturePath);
        }

        // Restoring / re-entering the site selects the identical female atlas for every placement.
        using DaggerfallSession restored = fixture.Restore();
        foreach (long entityId in entityIds)
        {
            var actor = restored.State.Actors.Get(entityId);
            Assert.Equal(ActorGender.Female, actor.Actor.Get<ActorInstanceIdentity>().Gender);
            Assert.Equal("female", actor.Actor.Get<DaggerfallActorGender>().Value);
            NormalizedActorSprite? reentrySprite = restored.ActiveAppearance.SpriteFor(entityId);
            Assert.NotNull(reentrySprite);
            Assert.Equal(expectedFemalePath, reentrySprite.TexturePath);
        }
    }

    [Fact]
    public void Shared_class_templates_townsfolk_cast_specs_and_board_seats_validate_cleanly()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorCatalog catalog = definitions.ActorCatalog;

        // One mage/warrior/assassin template shared by hostile enemy specs and town guild specs.
        Assert.Equal("mage", catalog.Specs["encounter-mage"].TemplateId);
        Assert.Equal("mage", catalog.Specs["town-mage-guild-member"].TemplateId);
        Assert.Equal("warrior", catalog.Specs["encounter-warrior"].TemplateId);
        Assert.Equal("warrior", catalog.Specs["town-fighters-guild-member"].TemplateId);
        Assert.Equal("assassin", catalog.Specs["encounter-assassin"].TemplateId);
        Assert.Equal("assassin", catalog.Specs["town-dark-brotherhood-member"].TemplateId);

        // Cast specs never patch mechanics: adding a mechanic field is refused.
        JsonObject badCast = TestPayload.Sections("actorSpecsCast");
        badCast["actorSpecsCast"]!["specs"]!.AsArray().First(s => s!["id"]!.GetValue<string>() == "king-gothryd")!["armor"] = 10;
        DaggerfallContentException castEx = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(badCast)));
        Assert.Contains(castEx.Diagnostics, d => d.Contains("king-gothryd", StringComparison.Ordinal) && d.Contains("armor", StringComparison.Ordinal));

        // Board seat bindings load all 13 canonical seats with no holder field, and refuse holder/defaultCastId.
        Assert.Equal(13, definitions.BoardSeats.All.Count);
        JsonObject badSeat = TestPayload.Sections("boardSeats");
        badSeat["boardSeats"]!["seats"]!.AsArray().First(s => s!["id"]!.GetValue<string>() == "daggerfall")!["holder"] = "king-gothryd";
        DaggerfallContentException seatEx = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(TestPayload.Splice(badSeat)));
        Assert.Contains(seatEx.Diagnostics, d => d.Contains("must not declare a holder", StringComparison.Ordinal));
    }
}
