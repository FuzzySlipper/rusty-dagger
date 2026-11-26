using System.Text;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallPeopleContentTests
{
    private static readonly int[] DemotedClonedIndividualFactions = [302, 360, 373, 388, 404, 406, 407];

    [Fact]
    public void Committed_daggerfall_people_pack_validates_with_zero_cloned_type4_or_molag_bal_leaks()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallPeopleSet people = definitions.People;

        Assert.Equal(33, people.Individuals.Count);
        Assert.Contains("king-gothryd", people.Individuals.Keys);
        // The political board's seat heads that no classic block places live at homes pinned to one site each: the
        // site places the home block, and a slot home is one of that block's people slots.
        foreach ((string id, DaggerfallSiteId site) in new[]
        {
            ("lady-brisienna", new DaggerfallSiteId(17, 1045)), ("the-squid", new DaggerfallSiteId(23, 601)),
            ("aldous-verreine", new DaggerfallSiteId(23, 601)), ("oswin-harrowgate", new DaggerfallSiteId(52, 152)),
            ("the-underking", new DaggerfallSiteId(16, 559)),
        })
        {
            DaggerfallCastIndividualDefinition cast = people.Individuals[id];
            Assert.Equal(site, cast.HomeSite);
            DaggerfallSiteRecord record = Assert.Single(definitions.Locations.Records, value => value.Id == site);
            string block = cast.HomeBlock!;
            if (block.EndsWith(".RDB", StringComparison.Ordinal))
            {
                Assert.Single(record.DungeonBlocks, value => value.SourceKey == block);
                Assert.NotNull(cast.HomePosition);
            }
            else
            {
                Assert.Single(record.Exterior!.Blocks, value => value.SourceName == block);
                int building = int.Parse(cast.HomeSlotId!.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture);
                Assert.True(people.TryGetRmbInteriorSlots(block, building, out DaggerfallPeopleBuildingSlots? slots));
                Assert.Contains(slots!.Slots, slot => slot.SlotId == cast.HomeSlotId);
            }
        }
        Assert.Contains("queen-aubk-i", people.Individuals.Keys);
        Assert.Contains("the-night-mother", people.Individuals.Keys);

        Assert.True(people.Archetypes.Count >= DaggerfallArchetypeIds.Required.Count);
        foreach (string requiredId in DaggerfallArchetypeIds.Required)
            Assert.Contains(requiredId, people.Archetypes.Keys);
        Assert.All(people.Archetypes.Values, archetype =>
        {
            Assert.True(archetype.Health.Minimum > 1, $"Archetype '{archetype.Id}' should have real HP (> 1), not 1-HP stub.");
            Assert.True(archetype.Health.Maximum >= archetype.Health.Minimum);
        });

        Assert.True(people.Spawners.Count >= 20);
        Assert.All(people.Spawners.Values, spawner =>
        {
            Assert.Equal("pool", spawner.VisualMode);
            Assert.NotEmpty(spawner.DefaultPool);
            Assert.False(string.IsNullOrWhiteSpace(spawner.ArchetypeId));
            Assert.Contains(spawner.ArchetypeId!, people.Archetypes.Keys);
        });
        Assert.Contains("banker", people.Spawners.Keys);
        Assert.Contains("weaponsmith", people.Spawners.Keys);
        Assert.Contains("alchemist", people.Spawners.Keys);
        Assert.Contains("tavern-keeper", people.Spawners.Keys);
        Assert.Contains("tavern-patron", people.Spawners.Keys);
        Assert.Contains("mage-guild-member", people.Spawners.Keys);
        Assert.Contains("house-resident", people.Spawners.Keys);
        Assert.Contains("clutter-person", people.Spawners.Keys);

        // Authored regional & climate spawner pools are populated with zero cast-individual visual clashes.
        HashSet<(int Archive, int Record)> castVisuals =
            [.. people.Individuals.Values.Select(c => (c.Archive, c.Record))];
        Assert.True(people.Spawners.Values.Count(s => s.RegionalPools.Count >= 4) >= 20);
        Assert.True(people.Spawners.Values.Count(s => s.ClimatePools.Count >= 4) >= 20);
        foreach (DaggerfallPeopleSpawnerDefinition spawner in people.Spawners.Values)
        {
            foreach (DaggerfallPeoplePoolEntry poolEntry in spawner.RegionalPools.Values.SelectMany(p => p)
                .Concat(spawner.ClimatePools.Values.SelectMany(p => p)))
            {
                Assert.DoesNotContain((poolEntry.Archive, poolEntry.Record), castVisuals);
            }
        }

        // Authored ambient barks and referral templates are populated.
        Assert.NotEmpty(people.Barks.DefaultBarks);
        Assert.NotEmpty(people.Barks.ByRole);
        Assert.NotEmpty(people.Barks.ByArchetype);
        Assert.NotEmpty(people.Barks.ByRegion);
        Assert.NotEmpty(people.Barks.ByClimate);
        Assert.NotEmpty(people.Barks.Referrals.DefaultIssueTemplates);
        Assert.NotEmpty(people.Barks.Referrals.DefaultResponseTemplates);
        Assert.NotEmpty(people.Barks.Referrals.ContactNames);

        Assert.NotEmpty(people.RmbInteriors);
        Assert.NotEmpty(people.RmbExteriors);
        Assert.NotEmpty(people.RdbDungeons);

        IEnumerable<DaggerfallPeopleSlotDefinition> allSlots = people.RmbInteriors.Values
            .SelectMany(buildings => buildings.Values)
            .SelectMany(building => building.Slots)
            .Concat(people.RmbExteriors.Values.SelectMany(slots => slots))
            .Concat(people.RdbDungeons.Values.SelectMany(slots => slots));

        int totalSlots = 0;
        foreach (DaggerfallPeopleSlotDefinition slot in allSlots)
        {
            totalSlots++;
            if (slot.CastId is null && slot.FactionId is int fid)
            {
                Assert.DoesNotContain(fid, DemotedClonedIndividualFactions);
                Assert.True(definitions.Factions.Factions.TryGetValue(fid, out DaggerfallFactionDefinition? faction));
                Assert.NotEqual(4, faction.Type);
                if (slot.ClassicVisual.Archive != 175)
                    Assert.NotEqual(3, fid);
            }
        }
        Assert.True(totalSlots > 14_000);
    }

    [Fact]
    public void Pack_reader_admits_unplaced_individuals_and_homes_pinned_to_one_site_by_slot_or_position()
    {
        DaggerfallPeopleSet people = DaggerfallPeopleContent.Read(CastOnly("homes.people", """
            {"individuals":[
              {"id":"unplaced","factionId":353,"name":"Unplaced","race":null,"gender":"Male","visual":{"archive":182,"record":54},"interaction":"full-dialogue","harm":"protected"},
              {"id":"lady-brisienna","factionId":352,"name":"Lady Brisienna","race":"breton","gender":"Female","visual":{"archive":180,"record":1},"homeSite":{"region":17,"index":1045},"homeBlock":"PALAAA00.RMB","homeSlotId":"b:0:p02","interaction":"full-dialogue","harm":"protected"},
              {"id":"the-squid","factionId":399,"name":"The Squid","race":"breton","gender":"Male","visual":{"archive":183,"record":16},"homeSite":{"region":16,"index":559},"homeBlock":"N0000048.RDB","homePosition":[40.4,0.6,-46.8],"interaction":"full-dialogue","harm":"protected"}
            ]}
            """), TestPayload.Definitions.Factions, TestPayload.Definitions.Vocabulary, TestPayload.Definitions.Items);

        DaggerfallCastIndividualDefinition unplaced = people.Individuals["unplaced"];
        Assert.Null(unplaced.HomeBlock);
        Assert.Null(unplaced.HomeSite);
        DaggerfallCastIndividualDefinition brisienna = people.Individuals["lady-brisienna"];
        Assert.Equal(new DaggerfallSiteId(17, 1045), brisienna.HomeSite);
        Assert.Equal("b:0:p02", brisienna.HomeSlotId);
        Assert.Null(brisienna.HomePosition);
        Assert.Equal([brisienna], people.HomedAt(new(17, 1045), "PALAAA00.RMB"));
        Assert.Empty(people.HomedAt(new(17, 1046), "PALAAA00.RMB"));
        DaggerfallCastIndividualDefinition squid = people.Individuals["the-squid"];
        Assert.Equal(new WorldPoint(40.4f, 0.6f, -46.8f), squid.HomePosition);
        Assert.Null(squid.HomeSlotId);

        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallPeopleContent.Read(CastOnly("bad-homes.people", """
            {"individuals":[
              {"id":"half-placed","factionId":352,"name":"Half Placed","race":"breton","gender":"Female","visual":{"archive":180,"record":1},"homeBlock":"S0000160.RDB","interaction":"full-dialogue","harm":"protected"},
              {"id":"both-ways","factionId":353,"name":"Both Ways","race":"breton","gender":"Male","visual":{"archive":182,"record":54},"homeSite":{"region":16,"index":559},"homeBlock":"N0000048.RDB","homeSlotId":"d:p00","homePosition":[0,0,0],"interaction":"full-dialogue","harm":"protected"},
              {"id":"nowhere","factionId":399,"name":"Nowhere","race":"breton","gender":"Male","visual":{"archive":183,"record":16},"homeBlock":"N0000048.RDB","homePosition":[0,0,0],"interaction":"full-dialogue","harm":"protected"},
              {"id":"factionless","name":"Factionless","race":"breton","gender":"Male","visual":{"archive":334,"record":15},"interaction":"full-dialogue","harm":"protected"}
            ]}
            """), TestPayload.Definitions.Factions, TestPayload.Definitions.Vocabulary, TestPayload.Definitions.Items));
        Assert.Contains("Cast individual 'half-placed' must name a homeBlock with exactly one of homeSlotId and homePosition", error.Message, StringComparison.Ordinal);
        Assert.Contains("Cast individual 'both-ways' must name a homeBlock with exactly one of homeSlotId and homePosition", error.Message, StringComparison.Ordinal);
        Assert.Contains("Cast individual 'nowhere' has a homePosition but no homeSite to set it in.", error.Message, StringComparison.Ordinal);
        Assert.Contains("factionId", error.Message, StringComparison.Ordinal);
    }

    private static DaggerfallPeoplePackFiles CastOnly(string id, string cast) => new(
        id,
        Encoding.UTF8.GetBytes("""{"castFile":"cast/individuals.json","spawnersFile":"spawners.json","archetypesFile":"archetypes.json","barksFile":"barks.json","blocksFolder":"blocks"}"""),
        Encoding.UTF8.GetBytes(cast),
        Encoding.UTF8.GetBytes("""{"spawners":[]}"""),
        [],
        TestPayload.PeoplePack().ArchetypesBytes,
        TestPayload.PeoplePack().BarksBytes);

    [Fact]
    public void Pack_reader_rejects_cloned_type4_individual_faction_on_non_cast_spawner_slot()
    {
        DaggerfallPeoplePackFiles tampered = new(
            "tampered.people",
            Encoding.UTF8.GetBytes("""{"castFile":"cast/individuals.json","spawnersFile":"spawners.json","archetypesFile":"archetypes.json","barksFile":"barks.json","blocksFolder":"blocks"}"""),
            Encoding.UTF8.GetBytes("""{"individuals":[]}"""),
            Encoding.UTF8.GetBytes("""
            {
              "spawners": [
                {
                  "id": "house-resident",
                  "role": "person",
                  "archetype": "civilian",
                  "interaction": "full-dialogue",
                  "harm": "law-protected-civilian",
                  "deriveRegionalFaction": true,
                  "visualMode": "classic-slot",
                  "defaultPool": [{"archive":184,"record":20,"gender":"Male","weight":1}],
                  "regionalPools": {}
                }
              ]
            }
            """),
            [
                new DaggerfallPeopleBlockFile("blocks/bad.json", Encoding.UTF8.GetBytes("""
                {
                  "kind": "rmb-interior",
                  "blocks": {
                    "BAD.RMB": {
                      "0": {
                        "buildingType": 19,
                        "buildingFactionId": 0,
                        "slots": [
                          {"slotId":"b:0:p00","spawner":"house-resident","position":[0,0,0],"interaction":"full-dialogue","factionId":406,"deriveRegionalFaction":true,"visual":{"archive":184,"record":20,"gender":"Male"}}
                        ]
                      }
                    }
                  }
                }
                """)),
            ],
            TestPayload.PeoplePack().ArchetypesBytes,
            TestPayload.PeoplePack().BarksBytes);

        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() =>
            DaggerfallPeopleContent.Read(tampered, TestPayload.Definitions.Factions, TestPayload.Definitions.Vocabulary, TestPayload.Definitions.Items));
        Assert.Contains("illegally carries Type-4 Individual faction 406", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pack_reader_rejects_invalid_archetypes_missing_required_unknown_skill_bad_item_and_unknown_spawner_ref()
    {
        DaggerfallPeoplePackFiles tampered = new(
            "bad-archetypes.people",
            Encoding.UTF8.GetBytes("""{"castFile":"cast/individuals.json","spawnersFile":"spawners.json","archetypesFile":"archetypes.json","barksFile":"barks.json","blocksFolder":"blocks"}"""),
            Encoding.UTF8.GetBytes("""{"individuals":[]}"""),
            Encoding.UTF8.GetBytes("""
            {
              "spawners": [
                {
                  "id": "banker",
                  "role": "bank teller",
                  "archetype": "nonexistent-archetype",
                  "interaction": "full-dialogue",
                  "harm": "law-protected-civilian",
                  "deriveRegionalFaction": true,
                  "visualMode": "pool",
                  "defaultPool": [{"archive":183,"record":5,"gender":"Male","weight":1}],
                  "regionalPools": {}
                }
              ]
            }
            """),
            [],
            Encoding.UTF8.GetBytes("""
            {
              "archetypes": [
                {
                  "id": "civilian",
                  "health": { "min": 20, "max": 10 },
                  "fatigue": 50,
                  "magicka": 0,
                  "baseAttribute": 45,
                  "attributes": { "not-an-attr": 50 },
                  "skills": { "not-a-skill": 25 },
                  "defaultHarm": "law-protected-civilian",
                  "defaultInteraction": "full-dialogue",
                  "walks": true,
                  "loadout": [
                    { "itemId": "missing-item", "quantity": 5 },
                    { "itemId": "iron-dagger", "quantity": 1 }
                  ]
                }
              ]
            }
            """),
            TestPayload.PeoplePack().BarksBytes);

        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() =>
            DaggerfallPeopleContent.Read(tampered, TestPayload.Definitions.Factions, TestPayload.Definitions.Vocabulary, TestPayload.Definitions.Items));
        Assert.Contains("invalid health range", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown attribute 'not-an-attr'", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown skill 'not-a-skill'", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown item 'missing-item'", error.Message, StringComparison.Ordinal);
        Assert.Contains("must be fungible", error.Message, StringComparison.Ordinal);
        Assert.Contains("required archetype 'guard'", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown archetype 'nonexistent-archetype'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Interior_spawner_slots_derive_regional_people_faction_from_site_region()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        Assert.True(definitions.People.TryGetRmbInteriorSlots("AAANEW.RMB", 0, out DaggerfallPeopleBuildingSlots? building));
        DaggerfallPeopleSlotDefinition slot = Assert.Single(building!.Slots);

        DaggerfallSiteId daggerfallSite = new(17, 0);
        DaggerfallSiteId sentinelSite = new(20, 0);
        DaggerfallSiteId wayrestSite = new(16, 0);
        string placementKey = DaggerfallPeopleSlots.InteriorBuildingSlotKey(0, 0, slot.SlotId);

        DaggerfallResolvedPeopleSlot inDaggerfall = DaggerfallPeopleSlots.ResolveSlot(definitions.People, definitions.Factions, 0UL, daggerfallSite, placementKey, slot);
        DaggerfallResolvedPeopleSlot inSentinel = DaggerfallPeopleSlots.ResolveSlot(definitions.People, definitions.Factions, 0UL, sentinelSite, placementKey, slot);
        DaggerfallResolvedPeopleSlot inWayrest = DaggerfallPeopleSlots.ResolveSlot(definitions.People, definitions.Factions, 0UL, wayrestSite, placementKey, slot);

        Assert.NotNull(inDaggerfall.PrimaryFactionId);
        Assert.NotNull(inSentinel.PrimaryFactionId);
        Assert.NotNull(inWayrest.PrimaryFactionId);

        Assert.Equal(inDaggerfall.RegionalPeopleFactionId, inDaggerfall.PrimaryFactionId);
        Assert.Equal(17, definitions.Factions.Factions[inDaggerfall.PrimaryFactionId!.Value].Region);
        Assert.Equal(20, definitions.Factions.Factions[inSentinel.PrimaryFactionId!.Value].Region);
        Assert.Equal(16, definitions.Factions.Factions[inWayrest.PrimaryFactionId!.Value].Region);
        Assert.NotEqual(inDaggerfall.PrimaryFactionId, inSentinel.PrimaryFactionId);
    }

    [Fact]
    public void Central_default_pool_and_regional_pools_affect_slots_without_editing_blocks()
    {
        DaggerfallPeopleSpawnerDefinition banker = new(
            "banker",
            "bank teller",
            "full-dialogue",
            "law-protected-civilian",
            DeriveRegionalFaction: true,
            VisualMode: "pool",
            DefaultPool: [new(182, 42, "Female", 1)],
            RegionalPools: new Dictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>(StringComparer.OrdinalIgnoreCase)
            {
                ["sentinel"] =
                [
                    new(357, 1, "Male", 1),
                    new(357, 2, "Female", 1),
                    new(357, 3, "Male", 1),
                    new(357, 4, "Female", 1),
                ],
            });
        DaggerfallPeopleSet customPeople = DaggerfallPeopleSet.Empty with
        {
            Spawners = new Dictionary<string, DaggerfallPeopleSpawnerDefinition>(StringComparer.Ordinal) { ["banker"] = banker },
        };
        DaggerfallPeopleSlotDefinition slot = new(
            "b:0:p00", CastId: null, SpawnerId: "banker", new WorldPoint(1, 0, 2),
            "full-dialogue", FactionId: 510, DeriveRegionalFaction: true,
            new DaggerfallPeopleVisual(182, 20, "Male"), BuildingType: 3, BuildingFactionId: 510);

        DaggerfallSiteId daggerfall = new(17, 5);
        DaggerfallSiteId sentinel = new(20, 5);
        string placementKey = DaggerfallPeopleSlots.InteriorBuildingSlotKey(1, 2, slot.SlotId);

        // Region without override resolves through central defaultPool (not per-slot classic visual),
        // while keeping explicit service faction + regional people faction.
        DaggerfallResolvedPeopleSlot dfResolved = DaggerfallPeopleSlots.ResolveSlot(customPeople, TestPayload.Definitions.Factions, 0UL, daggerfall, placementKey, slot, "daggerfall");
        Assert.Equal(new DaggerfallPeopleVisual(182, 42, "Female"), dfResolved.Visual);
        Assert.Equal(510, dfResolved.PrimaryFactionId);
        Assert.Equal(17, TestPayload.Definitions.Factions.Factions[dfResolved.RegionalPeopleFactionId!.Value].Region);

        // Editing central defaultPool to a multi-entry pool affects slots deterministically without touching the block slot.
        DaggerfallPeopleSet multiDefaultPeople = DaggerfallPeopleSet.Empty with
        {
            Spawners = new Dictionary<string, DaggerfallPeopleSpawnerDefinition>(StringComparer.Ordinal)
            {
                ["banker"] = banker with
                {
                    DefaultPool =
                    [
                        new(182, 10, "Male", 1),
                        new(182, 11, "Female", 1),
                        new(182, 12, "Male", 1),
                    ],
                },
            },
        };
        DaggerfallResolvedPeopleSlot m1 = DaggerfallPeopleSlots.ResolveSlot(multiDefaultPeople, TestPayload.Definitions.Factions, 0UL, daggerfall, placementKey, slot, "daggerfall");
        DaggerfallResolvedPeopleSlot m2 = DaggerfallPeopleSlots.ResolveSlot(multiDefaultPeople, TestPayload.Definitions.Factions, 0UL, daggerfall, placementKey, slot, "daggerfall");
        Assert.Equal(m1, m2);
        Assert.Contains(m1.Visual.Record, new[] { 10, 11, 12 });

        // Region with a regional pool selects deterministically from its regional pool.
        DaggerfallResolvedPeopleSlot s1 = DaggerfallPeopleSlots.ResolveSlot(customPeople, TestPayload.Definitions.Factions, 0UL, sentinel, placementKey, slot, "sentinel");
        DaggerfallResolvedPeopleSlot s2 = DaggerfallPeopleSlots.ResolveSlot(customPeople, TestPayload.Definitions.Factions, 0UL, sentinel, placementKey, slot, "sentinel");
        Assert.Equal(s1, s2);
        Assert.Equal(357, s1.Visual.Archive);
        Assert.Equal(placementKey, s1.SlotKey);

        // Changing WorldSocialSeed changes the per-slot seed while keeping the slot's durable ID stable.
        DaggerfallResolvedPeopleSlot sOtherSeed = DaggerfallPeopleSlots.ResolveSlot(customPeople, TestPayload.Definitions.Factions, 999_999UL, sentinel, placementKey, slot, "sentinel");
        Assert.Equal(s1.DurableId, sOtherSeed.DurableId);
        Assert.NotEqual(s1.SlotSeed, sOtherSeed.SlotSeed);
    }

    [Fact]
    public void Site_qualified_placement_keys_prevent_collisions_across_distinct_and_repeated_blocks_in_one_site()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        Assert.True(definitions.People.TryGetRmbInteriorSlots("AAANEW.RMB", 0, out DaggerfallPeopleBuildingSlots? blockA));
        Assert.True(definitions.People.TryGetRmbInteriorSlots("ALCHAL00.RMB", 0, out DaggerfallPeopleBuildingSlots? blockB));
        DaggerfallPeopleSlotDefinition slotA = blockA!.Slots[0];
        DaggerfallPeopleSlotDefinition slotB = blockB!.Slots[0];
        Assert.Equal("b:0:p00", slotA.SlotId);
        Assert.Equal("b:0:p00", slotB.SlotId);

        DaggerfallSiteId town = new(17, 42);
        string keyA0 = DaggerfallPeopleSlots.InteriorBuildingSlotKey(0, 0, slotA.SlotId);
        string keyB1 = DaggerfallPeopleSlots.InteriorBuildingSlotKey(1, 0, slotB.SlotId);
        string keyARepeat = DaggerfallPeopleSlots.InteriorBuildingSlotKey(2, 1, slotA.SlotId);

        DaggerfallResolvedPeopleSlot resolvedA0 = DaggerfallPeopleSlots.ResolveSlot(definitions.People, definitions.Factions, 0UL, town, keyA0, slotA);
        DaggerfallResolvedPeopleSlot resolvedB1 = DaggerfallPeopleSlots.ResolveSlot(definitions.People, definitions.Factions, 0UL, town, keyB1, slotB);
        DaggerfallResolvedPeopleSlot resolvedARepeat = DaggerfallPeopleSlots.ResolveSlot(definitions.People, definitions.Factions, 0UL, town, keyARepeat, slotA);

        Assert.Equal(keyA0, resolvedA0.SlotKey);
        Assert.Equal(keyB1, resolvedB1.SlotKey);
        Assert.Equal(keyARepeat, resolvedARepeat.SlotKey);

        // Distinct blocks and repeated instances of the same template inside one site never collide.
        Assert.Equal(3, new HashSet<long> { resolvedA0.DurableId, resolvedB1.DurableId, resolvedARepeat.DurableId }.Count);
        Assert.Equal(3, new HashSet<ulong> { resolvedA0.SlotSeed, resolvedB1.SlotSeed, resolvedARepeat.SlotSeed }.Count);

        // Re-resolving across reload yields identical durable IDs, seeds, and visuals.
        DaggerfallResolvedPeopleSlot reloadedA0 = DaggerfallPeopleSlots.ResolveSlot(definitions.People, definitions.Factions, 0UL, town, keyA0, slotA);
        DaggerfallResolvedPeopleSlot reloadedARepeat = DaggerfallPeopleSlots.ResolveSlot(definitions.People, definitions.Factions, 0UL, town, keyARepeat, slotA);
        Assert.Equal(resolvedA0, reloadedA0);
        Assert.Equal(resolvedARepeat, reloadedARepeat);
    }

    [Fact]
    public void Deterministic_slot_and_cast_durable_ids_sit_in_high_band_disjoint_from_monotonic_allocator()
    {
        DaggerfallSiteId siteA = new(17, 10);
        DaggerfallSiteId siteB = new(17, 11);

        long slotIdA = DaggerfallPeopleSlots.SeededSlotIdentity(siteA, "b:0:p00");
        long slotIdB = DaggerfallPeopleSlots.SeededSlotIdentity(siteB, "b:0:p00");
        long walkerId = DaggerfallPeopleSlots.SeededSlotIdentity(siteA, DaggerfallPeopleSlots.WalkerSlotKey(0));
        long castId = DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd");

        foreach (long id in new[] { slotIdA, slotIdB, walkerId, castId })
        {
            Assert.True((id & 0x4000_0000_0000L) != 0);
            Assert.True(id > (long)DaggerfallActorRoster.DynamicFirstIdentity);
        }

        Assert.Equal(slotIdA, DaggerfallPeopleSlots.SeededSlotIdentity(siteA, "b:0:p00"));
        Assert.NotEqual(slotIdA, slotIdB);
        Assert.NotEqual(slotIdA, walkerId);
        Assert.NotEqual(slotIdA, castId);
        Assert.Equal("w:007", DaggerfallPeopleSlots.WalkerSlotKey(7));
    }

    [Fact]
    public void WorldSocialSeed_snapshots_in_save_payload_independently_of_GameSeed()
    {
        DaggerfallTuning customTuning = DaggerfallTuning.Defaults with { Social = new DaggerfallSocialTuning(777_123UL) };
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord castle = definitions.Locations.Records.Single(record =>
            record.DungeonBlocks.Any(block => string.Equals(block.SourceKey, "S0000160.RDB", StringComparison.OrdinalIgnoreCase)));
        DaggerfallSiteProfile inputs = Assembled(DaggerfallWorldProfileIds.Dungeon(castle.Id));
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, inputs, customTuning) { GameSeed = 41UL };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        Assert.Equal(777_123UL, session.WorldSocialSeed);
        RulesetSavePayload save = session.CaptureSave();
        DaggerfallSavePayload decoded = DaggerfallSavePayload.Read(save);
        Assert.Equal(41UL, decoded.GameSeed);
        Assert.Equal(777_123UL, decoded.WorldSocialSeed);

        // Restoring with different tuning and GameSeed keeps both snapshotted seeds intact.
        DaggerfallTuning otherTuning = DaggerfallTuning.Defaults with { Social = new DaggerfallSocialTuning(999_999UL) };
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context,
            new DaggerfallSessionComposition(definitions, inputs, otherTuning) { GameSeed = 42UL }, save);
        DaggerfallSavePayload restoredDecoded = DaggerfallSavePayload.Read(restored.CaptureSave());
        Assert.Equal(41UL, restoredDecoded.GameSeed);
        Assert.Equal(777_123UL, restoredDecoded.WorldSocialSeed);
    }

    [Fact]
    public void Pack_reader_rejects_invalid_barks_and_spawner_climate_pools()
    {
        DaggerfallPeoplePackFiles tampered = new(
            "bad-barks.people",
            Encoding.UTF8.GetBytes("""{"castFile":"cast/individuals.json","spawnersFile":"spawners.json","archetypesFile":"archetypes.json","barksFile":"barks.json","blocksFolder":"blocks"}"""),
            Encoding.UTF8.GetBytes("""{"individuals":[]}"""),
            Encoding.UTF8.GetBytes("""
            {
              "spawners": [
                {
                  "id": "house-resident",
                  "role": "person",
                  "archetype": "civilian",
                  "interaction": "full-dialogue",
                  "harm": "law-protected-civilian",
                  "deriveRegionalFaction": true,
                  "visualMode": "pool",
                  "defaultPool": [{"archive":182,"record":0,"gender":"Male","weight":1}],
                  "regionalPools": {},
                  "climatePools": {
                    "Volcanic": [{"archive":182,"record":0,"gender":"Male","weight":1}]
                  }
                }
              ]
            }
            """),
            [],
            TestPayload.PeoplePack().ArchetypesBytes,
            Encoding.UTF8.GetBytes("""
            {
              "defaultBarks": [],
              "byArchetype": { "missing-archetype": ["Hello"] },
              "byClimate": { "Tundra": ["Cold"] },
              "referrals": {
                "defaultIssueTemplates": ["Go to {target} and talk to someone."],
                "defaultResponseTemplates": ["I heard something."],
                "contactNames": []
              }
            }
            """));

        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() =>
            DaggerfallPeopleContent.Read(tampered, TestPayload.Definitions.Factions, TestPayload.Definitions.Vocabulary, TestPayload.Definitions.Items));
        Assert.Contains("unknown climate 'Volcanic'", error.Message, StringComparison.Ordinal);
        Assert.Contains("barks.defaultBarks", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown archetype 'missing-archetype'", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown climate 'Tundra'", error.Message, StringComparison.Ordinal);
        Assert.Contains("placeholder '{contact}'", error.Message, StringComparison.Ordinal);
        Assert.Contains("placeholder '{tag}'", error.Message, StringComparison.Ordinal);
        Assert.Contains("referrals.contactNames", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_driven_barks_and_referrals_resolve_by_role_archetype_region_climate_and_fallback()
    {
        DaggerfallPeopleBarks barks = TestPayload.Definitions.People.Barks;

        // Specificity cascade: role -> archetype -> region -> climate -> defaultBarks.
        string merchantLine = barks.ResolveBarkLine(42UL, role: "merchant", archetypeId: "noble", regionIndex: 17, climateName: "Desert");
        Assert.Contains(merchantLine, barks.ByRole["merchant"]);

        string nobleLine = barks.ResolveBarkLine(42UL, role: "person", archetypeId: "noble", regionIndex: 17, climateName: "Desert");
        Assert.Contains(nobleLine, barks.ByArchetype["noble"]);

        string daggerfallLine = barks.ResolveBarkLine(42UL, role: "bystander", archetypeId: "civilian", regionIndex: 17, climateName: "Desert");
        Assert.Contains(daggerfallLine, barks.ByRegion["17"]);

        string desertLine = barks.ResolveBarkLine(42UL, role: "bystander", archetypeId: "civilian", regionIndex: 43, climateName: "Desert");
        Assert.Contains(desertLine, barks.ByClimate["Desert"]);

        string fallbackLine = barks.ResolveBarkLine(42UL, role: "bystander", archetypeId: "civilian", regionIndex: 43, climateName: null);
        Assert.Contains(fallbackLine, barks.DefaultBarks);

        // Referral contact names and templates resolve regionally and fall back deterministically.
        string dfContact = barks.Referrals.ResolveContactName(7UL, regionIndex: 17, climateName: "Woodlands");
        Assert.Contains(dfContact, barks.Referrals.ContactNamesByRegion["17"]);
        string sentinelContact = barks.Referrals.ResolveContactName(7UL, regionIndex: 20, climateName: "Subtropical");
        Assert.Contains(sentinelContact, barks.Referrals.ContactNamesByRegion["20"]);
        string desertContact = barks.Referrals.ResolveContactName(7UL, regionIndex: 43, climateName: "Desert");
        Assert.Contains(desertContact, barks.Referrals.ContactNamesByClimate["Desert"]);
        string defaultContact = barks.Referrals.ResolveContactName(7UL, regionIndex: 43, climateName: null);
        Assert.Contains(defaultContact, barks.Referrals.ContactNames);

        string issue = barks.Referrals.FormatIssueLine(3UL, "Gothway Garden", dfContact, "Alena", regionIndex: 17, climateName: "Woodlands");
        Assert.Contains("Gothway Garden", issue, StringComparison.Ordinal);
        Assert.Contains(dfContact, issue, StringComparison.Ordinal);
        Assert.Contains("Alena", issue, StringComparison.Ordinal);

        string response = barks.Referrals.FormatResponseLine(3UL, dfContact, "referred-from:Charing", role: "bystander");
        Assert.Contains(dfContact, response, StringComparison.Ordinal);
        Assert.Contains("referred-from:Charing", response, StringComparison.Ordinal);
    }

    [Fact]
    public void Regional_and_climate_spawner_pools_resolve_distinct_visuals_across_regions_and_climates()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallPeopleSpawnerDefinition resident = definitions.People.Spawners["house-resident"];
        DaggerfallPeopleSlotDefinition slot = new(
            "b:0:p00", CastId: null, SpawnerId: "house-resident", new WorldPoint(1, 0, 2),
            "full-dialogue", FactionId: null, DeriveRegionalFaction: true,
            new DaggerfallPeopleVisual(182, 0, "Male"), BuildingType: 17, BuildingFactionId: 0);

        HashSet<(int Archive, int Record)> expectedHammerfell = [.. resident.RegionalPools["20"].Select(e => (e.Archive, e.Record))];
        HashSet<(int Archive, int Record)> expectedDaggerfall = [.. resident.RegionalPools["17"].Select(e => (e.Archive, e.Record))];
        HashSet<(int Archive, int Record)> expectedWayrest = [.. resident.RegionalPools["23"].Select(e => (e.Archive, e.Record))];
        HashSet<(int Archive, int Record)> expectedMountain = [.. resident.ClimatePools["Mountain"].Select(e => (e.Archive, e.Record))];

        DaggerfallResolvedPeopleSlot inSentinel = DaggerfallPeopleSlots.ResolveSlot(
            definitions.People, definitions.Factions, 0UL, new DaggerfallSiteId(20, 1), "int:0,0:b:0:p00", slot,
            regionKey: "Sentinel", climateKey: "Subtropical");
        DaggerfallResolvedPeopleSlot inDaggerfall = DaggerfallPeopleSlots.ResolveSlot(
            definitions.People, definitions.Factions, 0UL, new DaggerfallSiteId(17, 1), "int:0,0:b:0:p00", slot,
            regionKey: "Daggerfall", climateKey: "Woodlands");
        DaggerfallResolvedPeopleSlot inWayrest = DaggerfallPeopleSlots.ResolveSlot(
            definitions.People, definitions.Factions, 0UL, new DaggerfallSiteId(23, 1), "int:0,0:b:0:p00", slot,
            regionKey: "Wayrest", climateKey: "Swamp");
        // Region 38 (Phrygias) has no explicit regionalPool override -> resolves cleanly via Mountain climatePool!
        DaggerfallResolvedPeopleSlot inMountainClimate = DaggerfallPeopleSlots.ResolveSlot(
            definitions.People, definitions.Factions, 0UL, new DaggerfallSiteId(38, 1), "int:0,0:b:0:p00", slot,
            regionKey: "Phrygias", climateKey: "Mountain");

        Assert.Contains((inSentinel.Visual.Archive, inSentinel.Visual.Record), expectedHammerfell);
        Assert.Contains((inDaggerfall.Visual.Archive, inDaggerfall.Visual.Record), expectedDaggerfall);
        Assert.Contains((inWayrest.Visual.Archive, inWayrest.Visual.Record), expectedWayrest);
        Assert.Contains((inMountainClimate.Visual.Archive, inMountainClimate.Visual.Record), expectedMountain);
    }

    [Fact]
    public void Assembled_interior_full_dialogue_npcs_apply_regional_and_climate_spawner_pools_while_preserving_ids_and_services()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        // Find a real RMB block + building index placed in both Daggerfall (region 17) and Sentinel (region 20)
        // whose authored full-dialogue spawner carries distinct regionalPools for regions 17 and 20.
        (DaggerfallSiteRecord Site, DaggerfallSiteBlock Block, int BuildingIndex, string SpawnerId)? FindPlacedInterior(
            int region, string? requiredBlock = null, int? requiredBuildingIndex = null)
        {
            foreach (DaggerfallSiteRecord site in definitions.Locations.Records)
            {
                if (site.Id.Region != region || site.Exterior is not { Blocks.Count: > 0 } ext) continue;
                foreach (DaggerfallSiteBlock block in ext.Blocks)
                {
                    if (requiredBlock is not null && !StringComparer.OrdinalIgnoreCase.Equals(block.SourceName, requiredBlock))
                        continue;
                    if (!definitions.People.RmbInteriors.TryGetValue(block.SourceName, out var buildings))
                        continue;
                    foreach ((int buildingIndex, DaggerfallPeopleBuildingSlots building) in buildings)
                    {
                        if (requiredBuildingIndex is int reqIdx && buildingIndex != reqIdx) continue;
                        DaggerfallPeopleSlotDefinition? slot = building.Slots.FirstOrDefault(s =>
                            s.CastId is null
                            && s.SpawnerId is { } sid
                            && string.Equals(s.Interaction, "full-dialogue", StringComparison.Ordinal)
                            && definitions.People.Spawners.TryGetValue(sid, out var sp)
                            && sp.RegionalPools.ContainsKey("17")
                            && sp.RegionalPools.ContainsKey("20"));
                        if (slot is not null)
                            return (site, block, buildingIndex, slot.SpawnerId!);
                    }
                }
            }
            return null;
        }

        // Pair one real building placed in region 17 (Daggerfall) and another placed in region 20 (Sentinel) using the same spawner.
        var dfMatch = FindPlacedInterior(17);
        Assert.NotNull(dfMatch);
        var snMatch = FindPlacedInterior(20, dfMatch!.Value.Block.SourceName, dfMatch.Value.BuildingIndex)
            ?? FindPlacedInterior(20);
        Assert.NotNull(snMatch);

        DaggerfallPeopleSpawnerDefinition dfSpawner = definitions.People.Spawners[dfMatch.Value.SpawnerId];
        DaggerfallPeopleSpawnerDefinition snSpawner = definitions.People.Spawners[snMatch!.Value.SpawnerId];
        HashSet<(int Archive, int Record)> daggerfallPool =
            [.. dfSpawner.RegionalPools["17"].Select(e => (e.Archive, e.Record))];
        HashSet<(int Archive, int Record)> sentinelPool =
            [.. snSpawner.RegionalPools["20"].Select(e => (e.Archive, e.Record))];

        DaggerfallSiteProfile dfInterior = Assembled(DaggerfallWorldProfileIds.Interior(
            dfMatch.Value.Site.Id, new DaggerfallSiteBuildingId(dfMatch.Value.Block.X, dfMatch.Value.Block.Y, dfMatch.Value.BuildingIndex)));
        DaggerfallSiteProfile snInterior = Assembled(DaggerfallWorldProfileIds.Interior(
            snMatch.Value.Site.Id, new DaggerfallSiteBuildingId(snMatch.Value.Block.X, snMatch.Value.Block.Y, snMatch.Value.BuildingIndex)));

        DaggerfallStaticNpcPlacement dfNpc = dfInterior.StaticNpcs.First(n => n.SpawnerId == dfMatch.Value.SpawnerId);
        DaggerfallStaticNpcPlacement snNpc = snInterior.StaticNpcs.First(n => n.SpawnerId == snMatch.Value.SpawnerId);

        // Canonical placement IDs and full-dialogue services remain intact while visuals draw from regional pools.
        Assert.StartsWith("person/", dfNpc.Id, StringComparison.Ordinal);
        Assert.StartsWith("person/", snNpc.Id, StringComparison.Ordinal);
        Assert.Equal(ActorInteractionPolicyKind.FullDialogue, dfNpc.Interaction);
        Assert.Equal(ActorInteractionPolicyKind.FullDialogue, snNpc.Interaction);
        Assert.Contains("talk", dfNpc.Services);
        Assert.Contains("talk", snNpc.Services);

        Assert.Contains((dfNpc.Appearance.BillboardArchive, dfNpc.Appearance.BillboardRecord), daggerfallPool);
        Assert.Contains((snNpc.Appearance.BillboardArchive, snNpc.Appearance.BillboardRecord), sentinelPool);

        // Also verify a region with no regionalPools override (e.g. region 18 Glenpoint) resolves via climatePools.
        var climateMatch = FindPlacedInterior(18);
        Assert.NotNull(climateMatch);
        DaggerfallSiteRecord climateSite = climateMatch!.Value.Site;
        string climateName = definitions.Grids.Climate.GetCell(climateSite.MapPixelX + 1, climateSite.MapPixelY).Name;
        DaggerfallPeopleSpawnerDefinition climateSpawner = definitions.People.Spawners[climateMatch.Value.SpawnerId];
        Assert.False(climateSpawner.RegionalPools.ContainsKey("18"));
        HashSet<(int Archive, int Record)> expectedClimatePool =
            [.. climateSpawner.ClimatePools[climateName].Select(e => (e.Archive, e.Record))];

        DaggerfallSiteProfile climateInterior = Assembled(DaggerfallWorldProfileIds.Interior(
            climateSite.Id, new DaggerfallSiteBuildingId(climateMatch.Value.Block.X, climateMatch.Value.Block.Y, climateMatch.Value.BuildingIndex)));
        DaggerfallStaticNpcPlacement climateNpc = climateInterior.StaticNpcs.First(n => n.SpawnerId == climateMatch.Value.SpawnerId);
        Assert.Contains((climateNpc.Appearance.BillboardArchive, climateNpc.Appearance.BillboardRecord), expectedClimatePool);
        Assert.Contains("talk", climateNpc.Services);
    }
}
