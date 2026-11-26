using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallHouseholdsTests
{
    [Fact]
    public void Base_pack_authors_canonical_cast_dynasties_and_town_social_groupings()
    {
        DaggerfallDefinitions defs = TestPayload.Definitions;

        // Canonical cast dynasties
        Assert.Equal("fam:dynasty:daggerfall", defs.People.Individuals["king-gothryd"].FamilyId);
        Assert.Equal("fam:dynasty:daggerfall", defs.People.Individuals["queen-aubk-i"].FamilyId);
        Assert.Equal("fam:dynasty:daggerfall", defs.People.Individuals["mynisera"].FamilyId);

        Assert.Equal("fam:dynasty:wayrest", defs.People.Individuals["king-eadwyre"].FamilyId);
        Assert.Equal("fam:dynasty:wayrest", defs.People.Individuals["queen-barenziah"].FamilyId);
        Assert.Equal("fam:dynasty:wayrest", defs.People.Individuals["princess-elysana"].FamilyId);
        Assert.Equal("fam:dynasty:wayrest", defs.People.Individuals["prince-helseth"].FamilyId);
        Assert.Equal("fam:dynasty:wayrest", defs.People.Individuals["princess-morgiah"].FamilyId);

        Assert.Equal("fam:dynasty:sentinel", defs.People.Individuals["queen-akorithi"].FamilyId);
        Assert.Equal("fam:dynasty:sentinel", defs.People.Individuals["prince-greklith"].FamilyId);
        Assert.Equal("fam:dynasty:sentinel", defs.People.Individuals["prince-lhotun"].FamilyId);

        Assert.Equal("fam:dynasty:direnni", defs.People.Individuals["medora"].FamilyId);
        Assert.Equal("fam:house:bridwell", defs.People.Individuals["lord-bridwell"].FamilyId);
        Assert.Equal("fam:house:bridwell", defs.People.Individuals["lady-bridwell"].FamilyId);

        // Unaffiliated cast individuals keep null familyId
        Assert.Null(defs.People.Individuals["lady-brisienna"].FamilyId);
        Assert.Null(defs.People.Individuals["the-underking"].FamilyId);

        // Town spawner socialGrouping policies
        Assert.Equal(ActorSocialGrouping.BuildingHousehold, defs.People.Spawners["town-house-resident"].SocialGrouping);
        Assert.Equal(ActorSocialGrouping.BuildingHousehold, defs.People.Spawners["town-alchemist"].SocialGrouping);
        Assert.Equal(ActorSocialGrouping.TavernStaff, defs.People.Spawners["town-tavern-keeper"].SocialGrouping);
        Assert.Equal(ActorSocialGrouping.TavernPatron, defs.People.Spawners["town-tavern-patron"].SocialGrouping);
        Assert.Equal(ActorSocialGrouping.TavernPatron, defs.People.Spawners["town-shop-patron"].SocialGrouping);
        Assert.Equal(ActorSocialGrouping.NobleHouse, defs.People.Spawners["town-courtier"].SocialGrouping);
        Assert.Equal(ActorSocialGrouping.NobleHouse, defs.People.Spawners["town-noble"].SocialGrouping);
        Assert.Equal(ActorSocialGrouping.NobleHouse, defs.People.Spawners["town-banker"].SocialGrouping);
        Assert.Equal(ActorSocialGrouping.FactionChapter, defs.People.Spawners["town-fighters-guild-member"].SocialGrouping);
        Assert.Equal(ActorSocialGrouping.FactionChapter, defs.People.Spawners["town-temple-priest"].SocialGrouping);
        Assert.Equal(ActorSocialGrouping.None, defs.People.Spawners["town-blades-guard"].SocialGrouping);
        Assert.Equal(ActorSocialGrouping.None, defs.People.Spawners["town-clutter-person"].SocialGrouping);
    }

    [Fact]
    public void Building_type_guards_separate_residences_taverns_palaces_and_institutions()
    {
        DaggerfallSiteId daggerfallCity = new(17, 100);
        const ulong seed = 42UL;

        // 1. Residence (type 17) & family shop (type 9): occupants share building household
        string? res1 = DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 17, 1, 2, 3, "slot/a", ActorSocialGrouping.BuildingHousehold);
        string? res2 = DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 17, 1, 2, 3, "slot/b", ActorSocialGrouping.BuildingHousehold);
        string? otherRes = DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 17, 1, 2, 4, "slot/a", ActorSocialGrouping.BuildingHousehold);
        Assert.Equal("fam:local:r17:s100:x1y2b3", res1);
        Assert.Equal(res1, res2);
        Assert.NotEqual(res1, otherRes);

        string? shopKeeper = DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 9, 0, 1, 2, "slot/k", ActorSocialGrouping.BuildingHousehold);
        string? shopPatron = DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 9, 0, 1, 2, "slot/p", ActorSocialGrouping.TavernPatron);
        Assert.Equal("fam:local:r17:s100:x0y1b2", shopKeeper);
        Assert.NotNull(shopPatron);
        Assert.NotEqual(shopKeeper, shopPatron);

        // 2. Tavern (type 15): staff share tavern household; patrons draw from town pool (never tavern staff)
        string? staff1 = DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 15, 2, 2, 1, "slot/k1", ActorSocialGrouping.TavernStaff);
        string? staff2 = DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 15, 2, 2, 1, "slot/k2", ActorSocialGrouping.TavernStaff);
        string? patron = DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 15, 2, 2, 1, "slot/pat", ActorSocialGrouping.TavernPatron);
        Assert.Equal("fam:local:r17:s100:x2y2b1", staff1);
        Assert.Equal(staff1, staff2);
        Assert.NotNull(patron);
        Assert.StartsWith("fam:local:r17:s100:h", patron, StringComparison.Ordinal);
        Assert.NotEqual(staff1, patron);

        // 3. Institutional buildings (Library 10, Guild Hall 11, Temple 14): never generate building-wide FamilyId
        Assert.Null(DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 10, 0, 0, 1, "slot/lib", ActorSocialGrouping.BuildingHousehold));
        Assert.Null(DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 11, 0, 0, 1, "slot/fg", ActorSocialGrouping.BuildingHousehold));
        Assert.Null(DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 14, 0, 0, 1, "slot/tmp", ActorSocialGrouping.NobleHouse));
        Assert.Null(DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 17, 0, 0, 1, "slot/fc", ActorSocialGrouping.FactionChapter));
        Assert.Null(DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 17, 0, 0, 1, "slot/none", ActorSocialGrouping.None));

        // 4. Palace (type 16): cast dynasty preserved; noble courtiers split across 2..4 provincial Great Houses
        string? ruler = DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 16, 0, 0, 0, "slot/king", ActorSocialGrouping.None, authoredFamilyId: "fam:dynasty:daggerfall");
        Assert.Equal("fam:dynasty:daggerfall", ruler);

        int houseCount = DaggerfallHouseholds.GreatHouseCountForRegion(seed, 17);
        Assert.InRange(houseCount, 2, 4);
        HashSet<string> palaceHouses = [];
        for (int i = 0; i < 24; i++)
        {
            string? courtierHouse = DaggerfallHouseholds.ResolveForPlacedSlot(seed, daggerfallCity, 16, 0, 0, 0, $"slot/courtier-{i}", ActorSocialGrouping.NobleHouse);
            Assert.NotNull(courtierHouse);
            Assert.StartsWith("fam:house:r17:", courtierHouse, StringComparison.Ordinal);
            Assert.True(DaggerfallHouseholds.IsGreatHouse(courtierHouse));
            Assert.NotEqual(ruler, courtierHouse);
            palaceHouses.Add(courtierHouse!);
        }
        Assert.True(palaceHouses.Count >= 2, "Palace courtiers must span multiple provincial Great Houses.");

        // 5. Exterior walkers: townsfolk get deterministic local household buckets; guards get null
        string? walkerA = DaggerfallHouseholds.ResolveForExteriorWalker(seed, daggerfallCity, "walker:001", isGuard: false, townBucketCount: 8);
        string? walkerARepeat = DaggerfallHouseholds.ResolveForExteriorWalker(seed, daggerfallCity, "walker:001", isGuard: false, townBucketCount: 8);
        string? guard = DaggerfallHouseholds.ResolveForExteriorWalker(seed, daggerfallCity, "walker:000", isGuard: true, townBucketCount: 8);
        Assert.Equal(walkerA, walkerARepeat);
        Assert.StartsWith("fam:local:r17:s100:h", walkerA, StringComparison.Ordinal);
        Assert.Null(guard);
    }

    [Fact]
    public void Shared_household_surname_applies_only_when_race_culture_bank_matches()
    {
        DaggerfallNames names = new(
            TestPayload.Definitions,
            null!,
            (key, count) => (int)(WorldRpg.Kit.World.StableHash.NonZeroFnv1a64(key) % (ulong)count));
        const string household = "fam:local:r17:s100:x1y1b2";
        const ulong seed = 99UL;

        DaggerfallNpcAppearance bretonMale = new("breton", "Male", 182, 0, 101, 0);
        DaggerfallNpcAppearance bretonFemale = new("breton", "Female", 182, 1, 202, 0);
        DaggerfallNpcAppearance argonianLodger = new("argonian", "Male", 182, 2, 303, 0);
        DaggerfallNpcAppearance kingGothryd = new("breton", "Male", 334, 19, 1, 364);

        string name1 = names.NpcName(bretonMale, region: 17, familyId: household, worldSocialSeed: seed);
        string name2 = names.NpcName(bretonFemale, region: 17, familyId: household, worldSocialSeed: seed);
        string[] parts1 = name1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] parts2 = name2.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, parts1.Length);
        Assert.Equal(2, parts2.Length);
        Assert.NotEqual(parts1[0], parts2[0]); // distinct given names
        Assert.Equal(parts1[1], parts2[1]);    // coherent shared household surname

        // Race/culture guard: Argonian in Breton region keeps their own name instead of household surname
        Assert.False(DaggerfallHouseholds.ShouldShareSurname("argonian", householdBank: 0, household));
        string lodgerWithFamily = names.NpcName(argonianLodger, region: 17, familyId: household, worldSocialSeed: seed);
        string lodgerWithoutFamily = names.NpcName(argonianLodger, region: 17, familyId: null, worldSocialSeed: seed);
        Assert.Equal(lodgerWithoutFamily, lodgerWithFamily);

        // Cast individual keeps designated faction name
        Assert.Equal("King Gothryd", names.NpcName(kingGothryd, region: 17, familyId: "fam:dynasty:daggerfall", worldSocialSeed: seed));

        // Redguard region (20, bank 1) preserves classic mononyms cleanly
        DaggerfallNpcAppearance redguard = new("redguard", "Male", 182, 0, 404, 0);
        string sentinelName = names.NpcName(redguard, region: 20, familyId: "fam:local:r20:s50:x0y0b1", worldSocialSeed: seed);
        Assert.DoesNotContain(' ', sentinelName);
    }

    [Fact]
    public void Deterministic_households_surface_in_actor_inspect_and_write_zero_ledger_bytes_when_untouched()
    {
        DaggerfallSiteProfile profile = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, profile);
        SpatialFake spatial = SpatialFake.Create(profile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, profile, DaggerfallTuning.Defaults));
        DaggerfallSiteId siteId = profile.Site!.Value;
        DaggerfallNpcSite siteBinding = new(siteId.Region, "Daggerfall", "0/0/1", profile.ProfileKey.LogicalId);

        const string sharedHousehold = "fam:local:r17:s100:x0y0b1";
        const string otherHousehold = "fam:local:r17:s100:x0y0b2";

        long kinA = RegisterAndMaterializeDeterministicStatic(session, siteId, siteBinding, profile, "person/house-a", 101, sharedHousehold, new WorldPoint(1, 0, -2));
        long kinB = RegisterAndMaterializeDeterministicStatic(session, siteId, siteBinding, profile, "person/house-b", 202, sharedHousehold, new WorldPoint(2, 0, -2));
        long outsider = RegisterAndMaterializeDeterministicStatic(session, siteId, siteBinding, profile, "person/house-c", 303, otherHousehold, new WorldPoint(3, 0, -2));

        // Coherent shared surname for household kin
        string kinAName = session.State.Npcs.Require(kinA).DisplayName!;
        string kinBName = session.State.Npcs.Require(kinB).DisplayName!;
        Assert.Equal(kinAName.Split(' ')[^1], kinBName.Split(' ')[^1]);

        // actor.inspect reports household id, label, scope, and live localKin (sibling only, not outsider)
        IDeveloperActorCommands commands = session.Commands<IDeveloperActorCommands>();
        DebugCommandResult inspectResult = commands.Inspect(kinA.ToString());
        Assert.True(inspectResult.Succeeded, inspectResult.Message);
        using JsonDocument doc = JsonDocument.Parse(inspectResult.Message);
        JsonElement social = doc.RootElement.GetProperty("social");
        Assert.Equal(sharedHousehold, social.GetProperty("familyId").GetString());
        Assert.Equal("local", social.GetProperty("scope").GetString());
        Assert.False(string.IsNullOrWhiteSpace(social.GetProperty("householdLabel").GetString()));
        long[] localKinIds = [.. social.GetProperty("localKin").EnumerateArray().Select(k => k.GetProperty("durableId").GetInt64())];
        Assert.Equal([kinB], localKinIds);

        // Zero-byte sparse persistence invariant: untouched deterministic actors with baseline FamilyId write 0 ActorLedger entries!
        DaggerfallSavePayload decodedUntouched = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.DoesNotContain(decodedUntouched.ActorLedger, e => e.DurableId == kinA);
        Assert.DoesNotContain(decodedUntouched.ActorLedger, e => e.DurableId == kinB);
        Assert.DoesNotContain(decodedUntouched.ActorLedger, e => e.DurableId == outsider);

        // Touching kinA's Met flag (without changing FamilyId) persists Met without persisting redundant FamilyId
        session.State.Actors.Get(kinA).Actor.Get<ActorOverlayComponent>().Met = true;
        // Explicitly mutating outsider's FamilyId persists ONLY outsider's new FamilyId
        session.State.Actors.Get(outsider).Actor.Get<ActorOverlayComponent>().FamilyId = "fam:house:r17:0";

        DaggerfallSavePayload decodedMutated = DaggerfallSavePayload.Read(session.CaptureSave());
        ActorLedgerEntry kinAEntry = Assert.Single(decodedMutated.ActorLedger, e => e.DurableId == kinA);
        Assert.True(kinAEntry.Social?.Met);
        Assert.Null(kinAEntry.Social?.FamilyId); // unchanged from deterministic baseline
        Assert.False(kinAEntry.Social?.FamilyIdCleared);
        Assert.DoesNotContain(decodedMutated.ActorLedger, e => e.DurableId == kinB); // still 0 bytes!
        ActorLedgerEntry outsiderEntry = Assert.Single(decodedMutated.ActorLedger, e => e.DurableId == outsider);
        Assert.Equal("fam:house:r17:0", outsiderEntry.Social?.FamilyId);
        Assert.False(outsiderEntry.Social?.FamilyIdCleared);
    }

    [Fact]
    public void FamilyId_override_clear_reset_and_met_only_deltas_round_trip_cleanly_through_save_load()
    {
        DaggerfallSiteProfile profile = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, profile);
        SpatialFake spatial = SpatialFake.Create(profile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);

        DaggerfallSessionComposition composition = new(TestPayload.Definitions, profile, DaggerfallTuning.Defaults);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        DaggerfallSiteId siteId = profile.Site!.Value;
        DaggerfallNpcSite siteBinding = new(siteId.Region, "Daggerfall", "0/0/1", profile.ProfileKey.LogicalId);

        const string baselineFamily = "fam:local:r17:s100:x0y0b1";
        const string overrideFamily = "fam:house:r17:0";
        WorldPoint poseA = new(1, 0, -2);
        WorldPoint poseB = new(2, 0, -2);
        WorldPoint poseC = new(3, 0, -2);

        long overrideAndResetActor = RegisterAndMaterializeDeterministicStatic(session, siteId, siteBinding, profile, "person/rt-override", 111, baselineFamily, poseA);
        long clearedActor = RegisterAndMaterializeDeterministicStatic(session, siteId, siteBinding, profile, "person/rt-clear", 222, baselineFamily, poseB);
        long metOnlyActor = RegisterAndMaterializeDeterministicStatic(session, siteId, siteBinding, profile, "person/rt-met", 333, baselineFamily, poseC);

        // 1. Override A -> B, clear A -> null, mark Met-only on C -> save & reload!
        session.State.Actors.Get(overrideAndResetActor).Actor.Get<ActorOverlayComponent>().FamilyId = overrideFamily;
        session.State.Actors.Get(clearedActor).Actor.Get<ActorOverlayComponent>().FamilyId = null;
        session.State.Actors.Get(metOnlyActor).Actor.Get<ActorOverlayComponent>().Met = true;

        RulesetSavePayload save1 = session.CaptureSave();
        DaggerfallSavePayload decoded1 = DaggerfallSavePayload.Read(save1);
        Assert.Equal(overrideFamily, Assert.Single(decoded1.ActorLedger, e => e.DurableId == overrideAndResetActor).Social?.FamilyId);
        ActorLedgerEntry clearedSaved = Assert.Single(decoded1.ActorLedger, e => e.DurableId == clearedActor);
        Assert.Null(clearedSaved.Social?.FamilyId);
        Assert.True(clearedSaved.Social?.FamilyIdCleared);
        ActorLedgerEntry metSaved = Assert.Single(decoded1.ActorLedger, e => e.DurableId == metOnlyActor);
        Assert.True(metSaved.Social?.Met);
        Assert.Null(metSaved.Social?.FamilyId);
        Assert.False(metSaved.Social?.FamilyIdCleared);

        // Reload into fresh session and re-materialize deterministic slots against restored ledger.
        EngineContextFake engine2 = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        using DaggerfallSession restored1 = DaggerfallSession.Restore(engine2.Context, composition, save1);
        RegisterAndMaterializeDeterministicStatic(restored1, siteId, siteBinding, profile, "person/rt-override", 111, baselineFamily, poseA);
        RegisterAndMaterializeDeterministicStatic(restored1, siteId, siteBinding, profile, "person/rt-clear", 222, baselineFamily, poseB);
        RegisterAndMaterializeDeterministicStatic(restored1, siteId, siteBinding, profile, "person/rt-met", 333, baselineFamily, poseC);

        Assert.Equal(overrideFamily, restored1.State.Actors.Get(overrideAndResetActor).Actor.Get<ActorOverlayComponent>().FamilyId);
        Assert.Null(restored1.State.Actors.Get(clearedActor).Actor.Get<ActorOverlayComponent>().FamilyId);
        Assert.Equal(baselineFamily, restored1.State.Actors.Get(metOnlyActor).Actor.Get<ActorOverlayComponent>().FamilyId);
        Assert.True(restored1.State.Actors.Get(metOnlyActor).Actor.Get<ActorOverlayComponent>().Met);

        // 2. Reset overrideAndResetActor back to baseline A -> MUST return to 0 ledger bytes (not keep stale B)!
        restored1.State.Actors.Get(overrideAndResetActor).Actor.Get<ActorOverlayComponent>().FamilyId = baselineFamily;
        RulesetSavePayload save2 = restored1.CaptureSave();
        DaggerfallSavePayload decoded2 = DaggerfallSavePayload.Read(save2);
        Assert.DoesNotContain(decoded2.ActorLedger, e => e.DurableId == overrideAndResetActor);
        // Cleared actor remains explicitly cleared!
        Assert.True(Assert.Single(decoded2.ActorLedger, e => e.DurableId == clearedActor).Social?.FamilyIdCleared);

        // Reload save2 -> reset actor comes back cleanly at baseline A, cleared actor stays null!
        EngineContextFake engine3 = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        using DaggerfallSession restored2 = DaggerfallSession.Restore(engine3.Context, composition, save2);
        RegisterAndMaterializeDeterministicStatic(restored2, siteId, siteBinding, profile, "person/rt-override", 111, baselineFamily, poseA);
        RegisterAndMaterializeDeterministicStatic(restored2, siteId, siteBinding, profile, "person/rt-clear", 222, baselineFamily, poseB);

        Assert.Equal(baselineFamily, restored2.State.Actors.Get(overrideAndResetActor).Actor.Get<ActorOverlayComponent>().FamilyId);
        Assert.Null(restored2.State.Actors.Get(clearedActor).Actor.Get<ActorOverlayComponent>().FamilyId);
    }

    [Fact]
    public void Seeded_provincial_great_houses_tie_to_tier2_regional_offices_while_keeping_board_seats_separate()
    {
        DaggerfallDefinitions defs = TestPayload.Definitions;
        DaggerfallSiteContext siteContext = new(defs.Locations);
        DaggerfallActorLedger ledger = new();
        DaggerfallNpcRegistry npcs = new(null, ledger);
        DaggerfallOfficeHolders holders = new(defs, siteContext, ledger, npcs);
        DaggerfallOfficeLedger offices = new(defs, holders, ledger, npcs, () => 42UL);

        // Tier-1 ruler resolves with canonical cast dynasty FamilyId
        DaggerfallOfficeHolder? daggerfallRuler = holders.ResolveRegionalRuler(17);
        Assert.NotNull(daggerfallRuler);
        Assert.Equal("fam:dynasty:daggerfall", daggerfallRuler!.FamilyId);

        // Tier-2 regional Great Houses in Daggerfall (region 17): 2..4 houses, tied to palace CourtFactor offices, zero save bytes
        IReadOnlyList<DaggerfallRegionalGreatHouse> houses = offices.ResolveRegionalGreatHouses(17);
        Assert.InRange(houses.Count, 2, 4);
        Assert.All(houses, house =>
        {
            Assert.StartsWith("fam:house:r17:", house.FamilyId, StringComparison.Ordinal);
            Assert.All(house.HeldOffices, office =>
            {
                Assert.Equal(DaggerfallOfficeKind.CourtFactor, office.Kind);
                Assert.Equal(house.FamilyId, office.FamilyId);
            });
        });
        Assert.True(houses.Sum(h => h.HeldOffices.Count) > 0, "Expected at least one regional Tier-2 CourtFactor office tied to a Great House.");
        Assert.Empty(offices.Capture().Overrides); // zero save bytes for untouched offices!
    }

    private static long RegisterAndMaterializeDeterministicStatic(
        DaggerfallSession session,
        DaggerfallSiteId siteId,
        DaggerfallNpcSite binding,
        DaggerfallSiteProfile profile,
        string slotId,
        int nameSeed,
        string familyId,
        WorldPoint pose)
    {
        string slotKey = $"{profile.ProfileKey.LogicalId}/{slotId}";
        long id = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, slotKey);
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, nameSeed, 0);
        session.State.Npcs.RegisterDeterministic(id, DaggerfallNpcKind.Static, slotId, binding, look, "person", ["talk"],
            homeProfile: profile.ProfileKey, familyId: familyId);
        DaggerfallNpc npc = session.State.Npcs.Require(id);
        session.Roster.MaterializeDeterministicCivilian(
            npc,
            ActorOrigin.Seeded(profile.ProfileKey, slotId),
            pose,
            new ActorPose(pose, 0f),
            Vector3.Zero,
            familyId);
        session.State.Npcs.Bind(id, profile.ProfileKey);
        return id;
    }
}
