using System.Text.Json.Nodes;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11156: consistent canon-seeded named nobility — every classic province has a named ruling lord/lady
/// and a named noble house (with classic cast lords taking precedence), deterministic across seeds, costing zero
/// save bytes until overridden, materialized at their capital seat, and visible across court dialogue, %rn/%lgn
/// quest text, notices, social.region, and succession/deposition/regency save round trips.
/// </summary>
public sealed class ProvincialNobilityTests
{
    private const int BetonyRegion = 19;
    private const int GlenpointRegion = 18;

    [Fact]
    public void Every_classic_province_resolves_a_named_ruler_and_noble_house_with_classic_cast_precedence()
    {
        JsonArray questRecords = TestPayload.QuestRecords();
        questRecords.Add(JsonNode.Parse("""
            {
              "name": "glenpoint-nobility-probe",
              "displayName": "",
              "sourceFile": "glenpoint-nobility-probe.txt",
              "messages": [
                { "id": 1000, "lines": ["Ruler: %rn, sworn to %lgn."] }
              ],
              "blocks": [
                { "kind": "person", "firstLine": 1, "lines": ["Person _ruler_ office Province"] }
              ]
            }
            """)!);
        DaggerfallDefinitions definitions = TestPayload.WithQuests(questRecords);
        DaggerfallSiteContext sites = new(definitions.Locations);
        DaggerfallOfficeHolders holders = new(definitions, sites);

        int[] provinceRegions =
        [
            .. definitions.Factions.Factions.Values
                .Where(f => f.Type == 7 && f.Region >= 0)
                .Select(f => f.Region)
                .Distinct()
                .Order(),
        ];
        Assert.True(provinceRegions.Length >= 44, $"Expected at least 44 classic provinces, found {provinceRegions.Length}.");

        foreach (int region in provinceRegions)
        {
            DaggerfallOfficeHolder? ruler = holders.ResolveRegionalRuler(region);
            Assert.NotNull(ruler);
            Assert.True(ruler!.HasLivingHolder, $"Province region {region} should have a living named ruler.");
            Assert.False(string.IsNullOrWhiteSpace(ruler.DisplayName), $"Province region {region} ruler lacks DisplayName.");
            Assert.False(string.IsNullOrWhiteSpace(ruler.Title), $"Province region {region} ruler lacks Title.");
            Assert.NotNull(ruler.ActorId);
            Assert.False(string.IsNullOrWhiteSpace(ruler.CastId));
            Assert.False(string.IsNullOrWhiteSpace(ruler.FamilyId));

            Assert.True(definitions.ProvincialNobility.TryGetProvinceHouse(region, out DaggerfallProvincialNobleHouse? house));
            Assert.NotNull(house);
            Assert.NotEmpty(house!.Members);
            Assert.False(string.IsNullOrWhiteSpace(house.HouseLabel));

            // Every province with at least one town (all except Cybiades 61) places its ruler/nobles in its capital seat.
            if (region != 61 && !house.HasClassicCastRuler)
            {
                Assert.NotNull(house.SeatSite);
                Assert.NotNull(ruler.Home);
                Assert.Equal(house.SeatSite, ruler.Home!.Site.Id);
            }
        }

        // Classic cast lords ALWAYS take precedence over generated nobility (#11141 and the crowns).
        Assert.Equal("king-gothryd", holders.ResolveRegionalRuler(17)!.CastId);
        Assert.Equal("queen-akorithi", holders.ResolveRegionalRuler(20)!.CastId);
        Assert.Equal("king-eadwyre", holders.ResolveRegionalRuler(23)!.CastId);
        Assert.Equal("medora", holders.ResolveRegionalRuler(9)!.CastId);
        Assert.Equal("gortwog", holders.ResolveRegionalRuler(26)!.CastId);
        Assert.Equal("baron-shrike", holders.ResolveRegionalRuler(22)!.CastId);
        Assert.Equal("Lady Doryanna Flyte", holders.ResolveRegionalRuler(21)!.DisplayName);

        // Betony (region 19, ruler=11 Lord) now resolves a canon-seeded named Lord with a 5-member noble house.
        DaggerfallOfficeHolder betonyRuler = holders.ResolveRegionalRuler(BetonyRegion)!;
        Assert.Equal("noble:r19:ruler", betonyRuler.CastId);
        Assert.Equal("fam:dynasty:r19", betonyRuler.FamilyId);
        Assert.Equal("Lord", betonyRuler.Title);
        Assert.StartsWith("Lord ", betonyRuler.DisplayName, StringComparison.Ordinal);
        DaggerfallProvincialNobleHouse betonyHouse = definitions.ProvincialNobility.ByRegion[BetonyRegion];
        Assert.Equal(5, betonyHouse.Members.Count);
        Assert.Contains(betonyHouse.Members, m => m.Role == DaggerfallProvincialNobleRole.Heir);
        Assert.Contains(betonyHouse.Members, m => m.Role == DaggerfallProvincialNobleRole.Spouse);
        Assert.Equal(2, betonyHouse.Members.Count(m => m.Role == DaggerfallProvincialNobleRole.CourtNoble));
    }

    [Fact]
    public void Provincial_nobility_is_identical_across_playthrough_seeds_and_costs_zero_save_bytes_until_overridden()
    {
        using var fixtureA = SourceBackedGuildBankSessionFixture.Create();
        DaggerfallTuning tuningA = DaggerfallTuning.Defaults with { Social = new DaggerfallSocialTuning(111UL) };
        DaggerfallTuning tuningB = DaggerfallTuning.Defaults with { Social = new DaggerfallSocialTuning(999_888UL) };

        List<string> releasesA = [], releasesB = [];
        ContentFake contentA = new(releasesA), contentB = new(releasesB);
        PopulateContent(contentA, fixtureA.ExteriorProfile);
        PopulateContent(contentB, fixtureA.ExteriorProfile);
        SpatialFake spatialA = SpatialFake.Create(fixtureA.ExteriorProfile.Geometry.Spatial[0].Sha256, releasesA);
        SpatialFake spatialB = SpatialFake.Create(fixtureA.ExteriorProfile.Geometry.Spatial[0].Sha256, releasesB);
        EngineContextFake engineA = EngineContextFake.Create(contentA, spatialA.Service, new AppearanceFake(releasesA));
        EngineContextFake engineB = EngineContextFake.Create(contentB, spatialB.Service, new AppearanceFake(releasesB));

        using DaggerfallSession sessionA = DaggerfallSession.StartNew(engineA.Context,
            new DaggerfallSessionComposition(TestPayload.Definitions, fixtureA.ExteriorProfile, tuningA) { GameSeed = 1UL });
        using DaggerfallSession sessionB = DaggerfallSession.StartNew(engineB.Context,
            new DaggerfallSessionComposition(TestPayload.Definitions, fixtureA.ExteriorProfile, tuningB) { GameSeed = 777UL });

        // Canon seed: Betony and Glenpoint rulers and houses are identical across different GameSeed and WorldSocialSeed!
        Assert.Equal(
            sessionA.OfficeHolders.ResolveRegionalRuler(BetonyRegion)!.DisplayName,
            sessionB.OfficeHolders.ResolveRegionalRuler(BetonyRegion)!.DisplayName);
        Assert.Equal(
            sessionA.OfficeHolders.ResolveRegionalRuler(GlenpointRegion)!.DisplayName,
            sessionB.OfficeHolders.ResolveRegionalRuler(GlenpointRegion)!.DisplayName);

        // Untouched save writes zero OfficeLedger overrides and zero provincial-noble ActorLedger entries!
        DaggerfallSavePayload saveA = DaggerfallSavePayload.Read(sessionA.CaptureSave());
        Assert.True(saveA.OfficeLedger is null || saveA.OfficeLedger.Overrides.Length == 0);
        long betonyRulerId = sessionA.OfficeHolders.ResolveRegionalRuler(BetonyRegion)!.ActorId!.Value;
        long glenpointRulerId = sessionA.OfficeHolders.ResolveRegionalRuler(GlenpointRegion)!.ActorId!.Value;
        Assert.DoesNotContain(saveA.ActorLedger, e => e.DurableId == betonyRulerId || e.DurableId == glenpointRulerId);

        // Conquest governor helper draws a named noble from Daggerfall's court for #11143.
        DaggerfallProvincialNobleMember? governor = sessionA.OfficeLedger.TrySelectConquestGovernorCandidate("daggerfall", BetonyRegion);
        Assert.NotNull(governor);
        Assert.Equal(DaggerfallProvincialNobleRole.CourtNoble, governor!.Role);
    }

    [Fact]
    public void Minor_province_capital_palace_materializes_ruler_speaks_ruler_and_liege_updates_macros_and_handles_deposition_succession_across_save_load()
    {
        JsonArray questRecords = TestPayload.QuestRecords();
        questRecords.Add(JsonNode.Parse("""
            {
              "name": "glenpoint-nobility-probe",
              "displayName": "",
              "sourceFile": "glenpoint-nobility-probe.txt",
              "messages": [
                { "id": 1000, "lines": ["Ruler: %rn, sworn to %lgn."] }
              ],
              "blocks": [
                { "kind": "person", "firstLine": 1, "lines": ["Person _ruler_ office Province"] }
              ]
            }
            """)!);
        DaggerfallDefinitions definitions = TestPayload.WithQuests(questRecords);
        DaggerfallProvincialNobleHouse glenpointHouse = definitions.ProvincialNobility.ByRegion[GlenpointRegion];
        Assert.NotNull(glenpointHouse.SeatSite);
        Assert.NotNull(glenpointHouse.SeatBlock);
        Assert.NotNull(glenpointHouse.SeatBuildingIndex);

        DaggerfallSiteRecord capital = definitions.Locations.Records.Single(r => r.Id == glenpointHouse.SeatSite!.Value);
        DaggerfallSiteBlock block = Assert.Single(capital.Exterior!.Blocks, b => b.SourceName == glenpointHouse.SeatBlock);
        DaggerfallWorldProfileKey palaceKey = DaggerfallWorldProfileIds.Interior(
            capital.Id, new DaggerfallSiteBuildingId(block.X, block.Y, glenpointHouse.SeatBuildingIndex!.Value));

        DaggerfallSiteProfile palace = Assembled(palaceKey);
        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(capital.Id));
        DaggerfallSiteProfiles profiles = new([exterior, palace]);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, palace);
        SpatialFake spatial = SpatialFake.Create(palace.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, palace, DaggerfallTuning.Defaults)
        {
            Profiles = profiles,
            Blocks = DaggerfallBlocksContent.Read(TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json"))),
        };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.AdvanceElapsedTime(12 * 60 * 60);

        DaggerfallOfficeHolder initialRuler = session.OfficeHolders.ResolveRegionalRuler(GlenpointRegion)!;
        long rulerId = initialRuler.ActorId!.Value;
        DaggerfallProvincialNobleMember expectedHeir = glenpointHouse.Members.Single(m => m.Role == DaggerfallProvincialNobleRole.Heir);
        long heirId = DaggerfallPeopleSlots.CastIndividualIdentity(expectedHeir.Cast.Id);

        // 1. Ruling lord physically materializes inside Glenpoint's capital palace with their canon name & dynasty!
        Assert.Single(session.State.Actors.All, a => a.DurableId == rulerId);
        Assert.Equal(initialRuler.DisplayName, session.State.Npcs.Require(rulerId).DisplayName);
        Assert.Equal(glenpointHouse.FamilyId, session.State.Npcs.Require(rulerId).FamilyId);

        // 2. Betony starts sworn to Daggerfall; swearing Glenpoint to Daggerfall on the board makes court dialogue
        //    name BOTH Glenpoint's ruling lord AND the crown Glenpoint is sworn to!
        Assert.Equal("Daggerfall", session.State.Social.Liege.LiegeOf(BetonyRegion)?.Name);
        session.PoliticalBoard!.Board.SetAllegiance(
            session.PoliticalBoard.Board.Region("glenpoint"),
            session.PoliticalBoard.Board.Seat("daggerfall"));
        string? liegeName = session.State.Social.Liege.LiegeOf(GlenpointRegion)?.Name;
        Assert.Equal("Daggerfall", liegeName);
        DaggerfallActivationTarget rulerTarget = Assert.Single(
            session.Dialogue.NpcTargets(),
            candidate => candidate.Identity.Value == checked((ulong)rulerId));
        DaggerfallActivationOutcome opened = session.Dialogue.ActivateNpc(new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, rulerTarget));
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView view = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains(initialRuler.DisplayName, view.Greeting, StringComparison.Ordinal);
        Assert.Contains($"sworn to the Crown of {liegeName}", view.Greeting, StringComparison.Ordinal);
        session.Dialogue.Close();

        // 3. Quest text macros (%rn and %lgn), office Province binding, and social.region all report the named ruler and liege!
        DaggerfallQuestInstanceSave probeQuest = session.State.Quests.Start(new(
            "probe:glenpoint", "glenpoint-nobility-probe.txt", "glenpoint-nobility-probe",
            DaggerfallQuestLifecycle.Active, null, [], [])
        { FactionId = initialRuler.OfficeFactionId });
        Assert.Equal([rulerId], probeQuest.Resources.Single(r => r.Symbol == "ruler").Binding.ActorIds);
        string renderedMacros = session.State.Quests.RenderMessage(probeQuest, 1000, DaggerfallQuestMessageDelivery.Popup, 0).Text;
        Assert.Contains(initialRuler.DisplayName, renderedMacros, StringComparison.Ordinal);
        Assert.Contains(liegeName!, renderedMacros, StringComparison.Ordinal);

        DaggerfallSocialInspection inspection = new(
            definitions, session.Site, session.State.Actors, session.State.Npcs, session.State.ActorLedger,
            session.State.Social, session.SocialConsequences, session.Introductions, session.OfficeHolders,
            session.OfficeLedger, session.State.Crime, new DaggerfallNames(definitions, RandomMinimum.Create()),
            () => session.WorldSocialSeed, () => session.Calendar);
        string regionReport = inspection.Region().Message;
        Assert.Contains(initialRuler.DisplayName, regionReport, StringComparison.Ordinal);

        // 4. Depose Glenpoint's ruling lord and advance past vacancy -> named heir succeeds, persists across save/load!
        DaggerfallOfficeOverrideSave? deposed = session.OfficeLedger.DeposeRegionalRuler(
            GlenpointRegion,
            DaggerfallOfficeChangeCause.Quest,
            checked((int)session.Calendar.DayNumber),
            "was deposed by court intrigue",
            session.Calendar.MinuteNumber);
        Assert.NotNull(deposed);
        Assert.True(deposed!.Vacant);
        Assert.NotNull(session.OfficeLedger.TryGetExile(rulerId));

        long minutesToSuccession = deposed.VacancyUntilMinute!.Value - session.Calendar.MinuteNumber + 1;
        session.AdvanceElapsedTime(checked(minutesToSuccession * 60L));

        DaggerfallOfficeHolder successor = session.OfficeHolders.ResolveRegionalRuler(GlenpointRegion)!;
        Assert.True(successor.HasLivingHolder);
        Assert.Equal(expectedHeir.Cast.Id, successor.CastId);
        Assert.Equal(heirId, successor.ActorId);
        Assert.Equal(expectedHeir.Cast.Name, successor.DisplayName);

        // Save/load preserves both the new named heir on Glenpoint's throne and the exiled former lord!
        RulesetSavePayload saveBytes = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, saveBytes);
        DaggerfallOfficeHolder restoredRuler = restored.OfficeHolders.ResolveRegionalRuler(GlenpointRegion)!;
        Assert.Equal(expectedHeir.Cast.Id, restoredRuler.CastId);
        Assert.Equal(heirId, restoredRuler.ActorId);
        Assert.NotNull(restored.OfficeLedger.TryGetExile(rulerId));
    }
}
