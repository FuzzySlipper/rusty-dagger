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

        Assert.Equal(28, people.Individuals.Count);
        Assert.Contains("king-gothryd", people.Individuals.Keys);
        Assert.Contains("queen-aubk-i", people.Individuals.Keys);
        Assert.Contains("the-night-mother", people.Individuals.Keys);

        Assert.True(people.Spawners.Count >= 20);
        Assert.Contains("banker", people.Spawners.Keys);
        Assert.Contains("weaponsmith", people.Spawners.Keys);
        Assert.Contains("alchemist", people.Spawners.Keys);
        Assert.Contains("tavern-keeper", people.Spawners.Keys);
        Assert.Contains("tavern-patron", people.Spawners.Keys);
        Assert.Contains("mage-guild-member", people.Spawners.Keys);
        Assert.Contains("house-resident", people.Spawners.Keys);
        Assert.Contains("clutter-person", people.Spawners.Keys);

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
    public void Pack_reader_rejects_cloned_type4_individual_faction_on_non_cast_spawner_slot()
    {
        DaggerfallPeoplePackFiles tampered = new(
            "tampered.people",
            Encoding.UTF8.GetBytes("""{"castFile":"cast/individuals.json","spawnersFile":"spawners.json","blocksFolder":"blocks"}"""),
            Encoding.UTF8.GetBytes("""{"individuals":[]}"""),
            Encoding.UTF8.GetBytes("""
            {
              "spawners": [
                {
                  "id": "house-resident",
                  "role": "person",
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
            ]);

        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() =>
            DaggerfallPeopleContent.Read(tampered, TestPayload.Definitions.Factions));
        Assert.Contains("illegally carries Type-4 Individual faction 406", error.Message, StringComparison.Ordinal);
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

        DaggerfallResolvedPeopleSlot inDaggerfall = DaggerfallPeopleSlots.ResolveSlot(definitions.People, definitions.Factions, 0UL, daggerfallSite, slot);
        DaggerfallResolvedPeopleSlot inSentinel = DaggerfallPeopleSlots.ResolveSlot(definitions.People, definitions.Factions, 0UL, sentinelSite, slot);
        DaggerfallResolvedPeopleSlot inWayrest = DaggerfallPeopleSlots.ResolveSlot(definitions.People, definitions.Factions, 0UL, wayrestSite, slot);

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
    public void Regional_spawner_pools_and_world_social_seed_resolve_deterministically_per_slot()
    {
        DaggerfallPeopleSpawnerDefinition banker = new(
            "banker",
            "bank teller",
            "full-dialogue",
            "law-protected-civilian",
            DeriveRegionalFaction: true,
            VisualMode: "classic-slot",
            DefaultPool: [new(182, 20, "Male", 1)],
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

        // Region without override falls back to classic slot visual, while keeping explicit service faction + regional people faction.
        DaggerfallResolvedPeopleSlot dfResolved = DaggerfallPeopleSlots.ResolveSlot(customPeople, TestPayload.Definitions.Factions, 0UL, daggerfall, slot, "daggerfall");
        Assert.Equal(new DaggerfallPeopleVisual(182, 20, "Male"), dfResolved.Visual);
        Assert.Equal(510, dfResolved.PrimaryFactionId);
        Assert.Equal(17, TestPayload.Definitions.Factions.Factions[dfResolved.RegionalPeopleFactionId!.Value].Region);

        // Region with a regional pool selects deterministically from its pool without editing any block file.
        DaggerfallResolvedPeopleSlot s1 = DaggerfallPeopleSlots.ResolveSlot(customPeople, TestPayload.Definitions.Factions, 0UL, sentinel, slot, "sentinel");
        DaggerfallResolvedPeopleSlot s2 = DaggerfallPeopleSlots.ResolveSlot(customPeople, TestPayload.Definitions.Factions, 0UL, sentinel, slot, "sentinel");
        Assert.Equal(s1, s2);
        Assert.Equal(357, s1.Visual.Archive);

        // Changing WorldSocialSeed changes the per-slot seed while keeping the slot's durable ID stable.
        DaggerfallResolvedPeopleSlot sOtherSeed = DaggerfallPeopleSlots.ResolveSlot(customPeople, TestPayload.Definitions.Factions, 999_999UL, sentinel, slot, "sentinel");
        Assert.Equal(s1.DurableId, sOtherSeed.DurableId);
        Assert.NotEqual(s1.SlotSeed, sOtherSeed.SlotSeed);
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
}
