using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestIndividualAndOfficeBindingTests
{
    [Fact]
    public void All_25_named_atHome_quests_bind_cast_individuals_to_canonical_identities_and_real_palace_homes_without_cloning()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        fixture.Random = SummonRandom.Create();
        using var game = fixture.Start(fixture.KnightlyProfile);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        long questor = game.State.Npcs.All.First(value => value.Appearance.FactionId == 368).DurableId;
        game.State.RacialOverrides.Select(DaggerfallRacialKind.Vampire, "cast-home-clan", 0, vampireClan: 153);

        string[] atHomeFiles = definitions.QuestSources.Resources
            .Where(r => r.Kind == "person" && r.Person is { Named: not null, AtHome: true }
                && definitions.QuestSources.Quests.TryGetValue(r.SourceFile, out var src)
                && src.Disposition == DaggerfallQuestDisposition.Compiled)
            .Select(r => r.SourceFile)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();

        Assert.True(atHomeFiles.Length >= 25, $"Expected at least 25 named+atHome quest files, found {atHomeFiles.Length}.");

        var emptyQuests = game.State.Quests.Capture();
        int checkedBindings = 0;

        foreach (string file in atHomeFiles)
        {
            game.State.Quests.Restore(emptyQuests);
            DaggerfallQuestSourceDefinition source = definitions.QuestSources.Resolve(file);
            DaggerfallQuestInstanceSave started = game.State.Quests.Start(new(
                "cast:" + source.Name, file, source.Name, DaggerfallQuestLifecycle.Active, null, [], [])
            { QuestorId = questor, FactionId = 368 });

            foreach (DaggerfallQuestResourceDefinition decl in definitions.QuestSources.Resources
                .Where(r => r.SourceFile == file && r.Kind == "person" && r.Person is { Named: not null, AtHome: true }))
            {
                int factionId = definitions.QuestSources.Tables.ActorItemTables.Factions.Resolve(decl.Person!.Named!).P3;
                Assert.True(definitions.People.TryGetIndividualByFaction(factionId, out DaggerfallCastIndividualDefinition? cast) && cast is not null,
                    $"{file}: named atHome person '{decl.Person.Named}' (faction {factionId}) missing from cast/individuals.json.");

                DaggerfallQuestResourceState resource = started.Resources.Single(r => r.Symbol == decl.CanonicalId);
                long expectedId = DaggerfallPeopleSlots.CastIndividualIdentity(cast.Id);
                Assert.Equal(DaggerfallQuestResourceBindingKind.Actor, resource.Binding.Kind);
                Assert.Equal([expectedId], resource.Binding.ActorIds);
                Assert.Equal(cast.Id, resource.SelectedPerson!.CastId);
                Assert.Equal(cast.Name, resource.SelectedPerson.DisplayName);

                // Home must resolve to the cast individual's real HomeBlock location, never the player's current site.
                Assert.True(DaggerfallCastHomes.TryResolve(game.Site, cast, game.Site.Region, out DaggerfallCastHomeLocation? expectedHome));
                Assert.NotNull(resource.SelectedPerson.Home);
                Assert.Equal(expectedHome!.Site.Id, resource.SelectedPerson.Home!.Binding.Places.Single().Require());
                Assert.Equal(expectedHome.ProfileKind, resource.SelectedPerson.Home.Binding.PlaceSelection!.Kind);
                Assert.Equal(expectedHome.Site.Name, resource.Text!.NameTwo);

                // Must never queue a duplicate automaticHome questor spawn for a canonical cast individual.
                Assert.DoesNotContain(started.Placements, p => p.ResourceSymbol == decl.CanonicalId && p.AutomaticHome);
                checkedBindings++;
            }
        }

        Assert.True(checkedBindings >= 50, $"Expected at least 50 named+atHome bindings verified, got {checkedBindings}.");
        // Untouched palaces must write zero ActorLedger entries.
        Assert.DoesNotContain(game.State.ActorLedger.All, e => e.Origin.Kind == ActorOriginKind.CastIndividual);
    }

    [Fact]
    public void Visiting_palace_materializes_canonical_cast_individual_and_quest_click_matches_without_duplicate_actor()
    {
        JsonArray root = TestPayload.QuestRecords();
        root.Add(JsonNode.Parse("""
            {
              "name": "akorithi-visit",
              "displayName": "",
              "sourceFile": "akorithi-visit.txt",
              "messages": [],
              "blocks": [
                { "kind": "person", "firstLine": 1, "lines": ["Person _akorithi_ named Queen_Akorithi atHome female"] },
                { "kind": "task", "firstLine": 10, "lines": ["_met_ task:", "clicked npc _akorithi_"] }
              ]
            }
            """)!);
        DaggerfallDefinitions definitions = TestPayload.WithQuests(root);
        DaggerfallCastIndividualDefinition queen = definitions.People.Individuals["queen-akorithi"];
        long queenId = DaggerfallPeopleSlots.CastIndividualIdentity(queen.Id);

        DaggerfallSiteProfile awayTown = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfile palaceSite = StaticNpcAdmissionTests.StaticProviderSite(
            Assembled(DaggerfallWorldProfileIds.Interior(Charing, new(0, 0, 0))), factionId: queen.FactionId);
        DaggerfallSiteProfiles profiles = new([awayTown, palaceSite]);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, awayTown);
        PopulateContent(content, palaceSite);
        SpatialFake spatial = SpatialFake.Create(awayTown.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, awayTown, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        DaggerfallQuestInstanceSave started = session.State.Quests.Start(new(
            "visit:1", "akorithi-visit.txt", "akorithi-visit", DaggerfallQuestLifecycle.Active, null, [], []));
        DaggerfallQuestResourceState resource = started.Resources.Single();
        Assert.Equal(DaggerfallQuestResourceBindingKind.Actor, resource.Binding.Kind);
        Assert.Equal([queenId], resource.Binding.ActorIds);
        Assert.Equal("queen-akorithi", resource.SelectedPerson!.CastId);
        Assert.Empty(started.Placements);

        // Save and reload while away: unvisited CastIndividual keeps zero ActorLedger bytes and validates cleanly!
        RulesetSavePayload awaySaveBytes = session.CaptureSave();
        DaggerfallSavePayload awaySave = DaggerfallSavePayload.Read(awaySaveBytes);
        Assert.DoesNotContain(awaySave.ActorLedger, e => e.DurableId == queenId);

        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, awaySaveBytes);
        Assert.True(restored.TryTransitionTo(palaceSite.ProfileKey));

        // Entering her palace materializes the one canonical CastIndividual (never a Questor clone).
        ActorState liveQueen = Assert.Single(restored.State.Actors.All, actor => actor.DurableId == queenId);
        Assert.DoesNotContain(restored.State.Npcs.All, npc => npc.Kind == DaggerfallNpcKind.Questor);
        Assert.Equal(queen.Name, restored.State.Npcs.Require(queenId).DisplayName);

        // Clicking her fires the quest's clicked npc task directly on her canonical durable ID.
        Assert.True(restored.State.Quests.ActorClicked(queenId));
        restored.Update(new ProductUpdate(OuterUpdate(5), []));
        Assert.True(restored.State.Quests.Capture().Instances.Single().Tasks.Single(t => t.Symbol == "met").IsSet);
    }

    [Fact]
    public void Lady_Brisienna_lives_in_the_Hearthwing_Blades_hall_where_an_atHome_quest_finds_her_across_save_reload()
    {
        JsonArray root = TestPayload.QuestRecords();
        root.Add(JsonNode.Parse("""
            {
              "name": "brisienna-visit",
              "displayName": "",
              "sourceFile": "brisienna-visit.txt",
              "messages": [],
              "blocks": [
                { "kind": "person", "firstLine": 1, "lines": ["Person _brisienna_ named Lady_Brisienna atHome female"] },
                { "kind": "task", "firstLine": 10, "lines": ["_met_ task:", "clicked npc _brisienna_"] }
              ]
            }
            """)!);
        DaggerfallDefinitions definitions = TestPayload.WithQuests(root);
        DaggerfallCastIndividualDefinition brisienna = definitions.People.Individuals["lady-brisienna"];
        long brisiennaId = DaggerfallPeopleSlots.CastIndividualIdentity(brisienna.Id);
        DaggerfallSiteId hearthwing = new(17, 1045);

        // The Blades hall, assembled from the base pack's override as the product assembles it, reached from away.
        DaggerfallSiteProfile awayTown = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfile hall = Assembled(DaggerfallWorldProfileIds.Interior(hearthwing, new(1, 0, 0)));
        DaggerfallSiteProfiles profiles = new([awayTown, hall]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, awayTown);
        PopulateContent(content, hall);
        SpatialFake spatial = SpatialFake.Create(awayTown.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, awayTown, DaggerfallTuning.Defaults)
        {
            Profiles = profiles,
            Blocks = DaggerfallBlocksContent.Read(TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json"))),
        };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        DaggerfallQuestInstanceSave started = session.State.Quests.Start(new(
            "visit:1", "brisienna-visit.txt", "brisienna-visit", DaggerfallQuestLifecycle.Active, null, [], []));
        Assert.Equal([brisiennaId], started.Resources.Single().Binding.ActorIds);
        Assert.True(session.TryTransitionTo(hall.ProfileKey));
        ActorState live = Assert.Single(session.State.Actors.All, actor => actor.DurableId == brisiennaId);
        Assert.Equal(brisienna.Name, session.State.Npcs.Require(brisiennaId).DisplayName);

        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, saved);
        Assert.Single(restored.State.Actors.All, actor => actor.DurableId == brisiennaId);
        Assert.True(restored.State.Quests.ActorClicked(brisiennaId));
        restored.Update(new ProductUpdate(OuterUpdate(5), []));
        Assert.True(restored.State.Quests.Capture().Instances.Single().Tasks.Single(t => t.Symbol == "met").IsSet);
    }

    [Fact]
    public void Relocating_cast_individual_via_place_npc_moves_canonical_actor_suppresses_home_slot_and_survives_save_reload()
    {
        JsonArray root = TestPayload.QuestRecords();
        root.Add(JsonNode.Parse("""
            {
              "name": "relocate-kavar",
              "displayName": "",
              "sourceFile": "relocate-kavar.txt",
              "messages": [],
              "blocks": [
                { "kind": "person", "firstLine": 1, "lines": ["Person _traitor_ named Lord_K'avar male"] },
                { "kind": "place", "firstLine": 2, "lines": ["Place _dest_ remote dungeon8"] },
                { "kind": "headless", "firstLine": 10, "lines": ["place npc _traitor_ at _dest_"] }
              ]
            }
            """)!);
        DaggerfallDefinitions definitions = TestPayload.WithQuests(root);
        DaggerfallCastIndividualDefinition kavar = definitions.People.Individuals["lord-k-avar"];
        long kavarId = DaggerfallPeopleSlots.CastIndividualIdentity(kavar.Id);

        DaggerfallSiteProfile homeSite = StaticNpcAdmissionTests.StaticProviderSite(
            Assembled(DaggerfallWorldProfileIds.Exterior(Charing)), factionId: kavar.FactionId);
        DaggerfallSiteProfile destSource = Assembled(DaggerfallWorldProfileIds.Interior(Charing, new(0, 0, 0)));
        Dictionary<(int Archive, int Record), NormalizedBillboardSprite> destBillboards = destSource.BillboardSprites.ToDictionary();
        NormalizedBillboardSprite sampleBillboard = homeSite.BillboardSprites.Values.First();
        destBillboards[(kavar.Archive, kavar.Record)] = sampleBillboard;
        IReadOnlyList<DaggerfallSiteMarker> destMarkers = destSource.QuestMarkers.Count > 0
            ? destSource.QuestMarkers
            : [new DaggerfallSiteMarker("dest-marker", DaggerfallSiteMarkerKind.QuestSpawn, new WorldPoint(2f, 0f, 2f))];
        DaggerfallSiteProfile destSite = new(
            destSource.Project, destSource.Geometry, destSource.WorldAppearance, destSource.InitialLook,
            destSource.Materials, destSource.ActorSprites, destSource.MobileSprites, destSource.Audio,
            destSource.ClassicPresentation, destSource.Site, destSource.Doors, destSource.ProfileKind,
            destSource.ProfileKey.LogicalId, destSource.Portals, destSource.Anchors.Values.ToArray(),
            destSource.Lights, destSource.GroundContainerSprites, destSource.DungeonMap,
            destSource.DungeonActions, destSource.DungeonActionModels, destSource.InteriorBuilding,
            destMarkers, destBillboards, destSource.StaticNpcs, population: destSource.Population);
        DaggerfallSiteProfiles profiles = new([homeSite, destSite]);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, homeSite);
        PopulateContent(content, destSite);
        SpatialFake spatial = SpatialFake.Create(homeSite.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, homeSite, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        // Initially Lord K'avar stands at homeSite.
        Assert.True(session.State.Actors.TryGet(kavarId, out _));

        DaggerfallSiteRecord destRecord = definitions.Locations.Records.Single(r => r.Id == destSite.Site);
        DaggerfallQuestResourceBinding destBinding = DaggerfallQuestResourceBinding.PlaceBuilding(
            session.Site, destRecord.MapId,
            DaggerfallQuestPlaceAllocator.BuildingKey(new(destSite.InteriorBuilding!.BlockX, destSite.InteriorBuilding.BlockY, destSite.InteriorBuilding.Building.Index)))
            with
            {
                PlaceSelection = new(DaggerfallWorldProfileKind.Interior, destRecord.MapId,
                    DaggerfallQuestPlaceAllocator.BuildingKey(new(destSite.InteriorBuilding.BlockX, destSite.InteriorBuilding.BlockY, destSite.InteriorBuilding.Building.Index)), 0),
            };
        session.State.Quests.Start(new("kavar:1", "relocate-kavar.txt", "relocate-kavar", DaggerfallQuestLifecycle.Active, null,
            [new("dest", destBinding)], []));
        session.Update(new ProductUpdate(OuterUpdate(1), []));

        // Travel to destSite: place npc relocates the canonical CastIndividual (zero Questor clone).
        Assert.True(session.TryTransitionTo(destSite.ProfileKey));
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Single(session.State.Actors.All, actor => actor.DurableId == kavarId);
        Assert.DoesNotContain(session.State.Npcs.All, npc => npc.Kind == DaggerfallNpcKind.Questor);

        // Save and reload at destSite: Lord K'avar remains materialized there with AtSite(destSite).
        RulesetSavePayload saveBytes = session.CaptureSave();
        ActorLedgerEntry savedEntry = Assert.Single(DaggerfallSavePayload.Read(saveBytes).ActorLedger, e => e.DurableId == kavarId);
        Assert.Equal(ActorOriginKind.CastIndividual, savedEntry.Origin.Kind);
        Assert.Equal(destSite.ProfileKey, savedEntry.Location?.Profile?.Require());

        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, saveBytes);
        Assert.Single(restored.State.Actors.All, actor => actor.DurableId == kavarId);

        // Returning to homeSite suppresses his home slot so no duplicate body appears!
        Assert.True(restored.TryTransitionTo(homeSite.ProfileKey));
        Assert.False(restored.State.Actors.TryGet(kavarId, out _));
    }

    [Fact]
    public void Office_addressed_quest_binds_through_office_holder_tracks_death_succession_and_respects_political_override_in_binding_and_macros()
    {
        JsonArray root = TestPayload.QuestRecords();
        root.Add(JsonNode.Parse("""
            {
              "name": "office-petition",
              "displayName": "",
              "sourceFile": "office-petition.txt",
              "messages": [
                { "id": 1000, "lines": ["Regent: %rt %rn (leader: %nrn / %fl1)."] },
                { "id": 1001, "lines": ["New %lt1 %fl1 succeeds %ol1, who %olf."] }
              ],
              "blocks": [
                { "kind": "person", "firstLine": 1, "lines": ["Person _ruler_ office Province atHome"] }
              ]
            }
            """)!);
        DaggerfallDefinitions definitions = TestPayload.WithQuests(root);
        DaggerfallSiteProfile town = Assembled(DaggerfallWorldProfileIds.Exterior(Charing)); // Region 17 (Daggerfall)
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, town);
        SpatialFake spatial = SpatialFake.Create(town.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, town, DaggerfallTuning.Defaults)
        {
            Profiles = new DaggerfallSiteProfiles([town]),
        };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        long gothrydId = DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd");
        long mobarId = DaggerfallPeopleSlots.CastIndividualIdentity("mobar");
        long akorithiId = DaggerfallPeopleSlots.CastIndividualIdentity("queen-akorithi");

        // 1. Default living ruler: Daggerfall province office resolves to King Gothryd at Castle Daggerfall.
        DaggerfallQuestInstanceSave q1 = session.State.Quests.Start(new(
            "office:1", "office-petition.txt", "office-petition", DaggerfallQuestLifecycle.Active, null, [], [])
        { FactionId = 201 });
        DaggerfallQuestResourceState r1 = q1.Resources.Single();
        Assert.Equal(DaggerfallQuestResourceBindingKind.Actor, r1.Binding.Kind);
        Assert.Equal([gothrydId], r1.Binding.ActorIds);
        Assert.Equal("king-gothryd", r1.SelectedPerson!.CastId);
        Assert.Equal(201, r1.SelectedPerson.OfficeFactionId);
        Assert.Equal("King Gothryd", r1.SelectedPerson.DisplayName);
        Assert.Contains("King King Gothryd (leader: King Gothryd / King Gothryd).",
            session.State.Quests.RenderMessage(q1, 1000, DaggerfallQuestMessageDelivery.Popup, 0).Text);

        // 2. Death-aware succession: mark King Gothryd Dead in ActorLedger -> office succeeds to next published child (Mobar).
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            gothrydId,
            ActorOrigin.Cast("king-gothryd"),
            Location: ActorLocationOverride.Dead(town.ProfileKey)));
        DaggerfallQuestInstanceSave q2 = session.State.Quests.Start(new(
            "office:2", "office-petition.txt", "office-petition", DaggerfallQuestLifecycle.Active, null, [], [])
        { FactionId = 201 });
        DaggerfallQuestResourceState r2 = q2.Resources.Single();
        Assert.Equal([mobarId], r2.Binding.ActorIds);
        Assert.Equal("mobar", r2.SelectedPerson!.CastId);
        Assert.Equal("Mobar", r2.SelectedPerson.DisplayName);
        Assert.Contains("Mobar succeeds King Gothryd, who was slain.",
            session.State.Quests.RenderMessage(q2, 1001, DaggerfallQuestMessageDelivery.Popup, 0).Text);

        // 3. A5 political office-ledger override seam: custom holder + title + predecessor fate bind cleanly and survive save/reload!
        session.OfficeHolders.BindOverrideLookup(officeId => officeId == 201
            ? new DaggerfallOfficeOverride(
                HolderActorId: akorithiId,
                HolderCastId: "queen-akorithi",
                HolderFactionId: 380,
                DisplayName: "Regent Akorithi",
                Title: "Lord Regent",
                Gender: "Female",
                PreviousHolderName: "Mobar",
                PreviousHolderFate: "was deposed")
            : null);

        DaggerfallQuestInstanceSave q3 = session.State.Quests.Start(new(
            "office:3", "office-petition.txt", "office-petition", DaggerfallQuestLifecycle.Active, null, [], [])
        { FactionId = 201 });
        DaggerfallQuestResourceState r3 = q3.Resources.Single();
        Assert.Equal([akorithiId], r3.Binding.ActorIds);
        Assert.Equal("queen-akorithi", r3.SelectedPerson!.CastId);
        Assert.Equal("Regent Akorithi", r3.SelectedPerson.DisplayName);
        Assert.Contains("Lord Regent Regent Akorithi (leader: Regent Akorithi / Regent Akorithi).",
            session.State.Quests.RenderMessage(q3, 1000, DaggerfallQuestMessageDelivery.Popup, 0).Text);
        Assert.Contains("New Lord Regent Regent Akorithi succeeds Mobar, who was deposed.",
            session.State.Quests.RenderMessage(q3, 1001, DaggerfallQuestMessageDelivery.Popup, 0).Text);

        // Save and reload preserves all bound office-holder quest resources cleanly.
        RulesetSavePayload saveBytes = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, saveBytes);
        Assert.True(restored.State.Quests.TryGet("office:3", out DaggerfallQuestInstanceSave? restoredQ3));
        Assert.Equal([akorithiId], restoredQ3!.Resources.Single().Binding.ActorIds);
        Assert.Equal(201, restoredQ3.Resources.Single().SelectedPerson!.OfficeFactionId);

        // 4. R11047-1 dead-override regression: if the overridden holder (Akorithi) dies while living successors remain,
        //    office 201 must succeed to the living candidate (Mobar) rather than binding or naming the dead override!
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            akorithiId,
            ActorOrigin.Cast("queen-akorithi"),
            Location: ActorLocationOverride.Dead(town.ProfileKey)));
        DaggerfallQuestInstanceSave q4 = session.State.Quests.Start(new(
            "office:4", "office-petition.txt", "office-petition", DaggerfallQuestLifecycle.Active, null, [], [])
        { FactionId = 201 });
        DaggerfallQuestResourceState r4 = q4.Resources.Single();
        Assert.Equal([mobarId], r4.Binding.ActorIds);
        Assert.Equal("mobar", r4.SelectedPerson!.CastId);
        Assert.Equal("Mobar", r4.SelectedPerson.DisplayName);
        Assert.Contains("King Mobar (leader: Mobar / Mobar).",
            session.State.Quests.RenderMessage(q4, 1000, DaggerfallQuestMessageDelivery.Popup, 0).Text);
        Assert.Contains("New King Mobar succeeds Regent Akorithi, who was slain.",
            session.State.Quests.RenderMessage(q4, 1001, DaggerfallQuestMessageDelivery.Popup, 0).Text);

        // 5. R11047-1 no-living-candidate regression: once EVERY candidate in office 201 is dead, the office is explicitly
        //    vacant: new office quests refuse to bind a dead or fabricated person, and macros do not announce a dead ruler.
        foreach (string remainingCastId in new[] { "mobar", "queen-aubk-i", "mynisera", "lord-bridwell", "lady-northbridge" })
        {
            long remainingId = DaggerfallPeopleSlots.CastIndividualIdentity(remainingCastId);
            session.State.ActorLedger.Upsert(new ActorLedgerEntry(
                remainingId,
                ActorOrigin.Cast(remainingCastId),
                Location: ActorLocationOverride.Dead(town.ProfileKey)));
        }
        DaggerfallOfficeHolder? vacant = session.OfficeHolders.ResolveRegionalRuler(17);
        Assert.NotNull(vacant);
        Assert.False(vacant!.HasLivingHolder);
        Assert.False(vacant.IsAlive);
        Assert.Null(vacant.ActorId);
        Assert.Null(vacant.CastId);
        Assert.Equal("Regent Akorithi", vacant.PreviousHolderName);
        Assert.Equal("was slain", vacant.PreviousHolderFate);

        Assert.Contains("no living office holder", Assert.Throws<NotSupportedException>(() =>
            session.State.Quests.Start(new(
                "office:5", "office-petition.txt", "office-petition", DaggerfallQuestLifecycle.Active, null, [], [])
            { FactionId = 201 })).Message);

        DaggerfallQuestTextContext textContext = new(definitions, session.Site, session.State.Npcs, session.State.ItemInstances,
            _ => string.Empty, () => DaggerfallTextContext.Empty, () => null, RandomMinimum.Create(), _ => null, (_, _) => null, session.OfficeHolders);
        DaggerfallQuestSourceDefinition petitionSource = definitions.QuestSources.Resolve("office-petition.txt");
        DaggerfallQuestRuntimeInstance vacantProbe = new(q4, DaggerfallQuestTaskCompiler.Compile(petitionSource));
        var vacantFactionText = textContext.For(vacantProbe).Text.Faction;
        Assert.Null(vacantFactionText.RegentName);
        Assert.Null(vacantFactionText.RegentTitle);
        Assert.Null(vacantFactionText.CurrentRegionLeader);
        Assert.Null(vacantFactionText.FactionLeaderOne);
        Assert.Null(vacantFactionText.FactionLeaderTitle);
        Assert.Equal("Regent Akorithi", vacantFactionText.OldFactionLeader);
        Assert.Equal("was slain", vacantFactionText.OldLeaderFate);
    }

    [Fact]
    public void Dead_cast_individual_is_not_resurrected_by_place_npc()
    {
        JsonArray root = TestPayload.QuestRecords();
        root.Add(JsonNode.Parse("""
            {
              "name": "dead-kavar",
              "displayName": "",
              "sourceFile": "dead-kavar.txt",
              "messages": [],
              "blocks": [
                { "kind": "person", "firstLine": 1, "lines": ["Person _traitor_ named Lord_K'avar male"] },
                { "kind": "place", "firstLine": 2, "lines": ["Place _dest_ remote dungeon8"] },
                { "kind": "headless", "firstLine": 10, "lines": ["place npc _traitor_ at _dest_"] }
              ]
            }
            """)!);
        DaggerfallDefinitions definitions = TestPayload.WithQuests(root);
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        DaggerfallSession session = fixture.Session;
        long kavarId = DaggerfallPeopleSlots.CastIndividualIdentity("lord-k-avar");
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            kavarId,
            ActorOrigin.Cast("lord-k-avar"),
            Location: ActorLocationOverride.Dead(fixture.Inputs.ProfileKey)));

        DaggerfallSiteRecord site = definitions.Locations.Records.Single(r => r.Id == fixture.Inputs.Site);
        session.State.Quests.Start(new("dead:1", "dead-kavar.txt", "dead-kavar", DaggerfallQuestLifecycle.Active, null,
            [new("dest", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with
                { PlaceSelection = new(fixture.Inputs.ProfileKind, site.MapId, null, 0) })], []));
        fixture.Update();

        Assert.False(session.State.Actors.TryGet(kavarId, out _));
        Assert.Empty(session.State.Quests.Capture().Instances.Single().Placements);
    }
}
