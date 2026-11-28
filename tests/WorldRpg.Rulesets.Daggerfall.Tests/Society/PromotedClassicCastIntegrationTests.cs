using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11141: verifies all 10 promoted classic cast individuals have deterministic home placements,
/// unique cast-specific visual/splat asset bindings, materialize cleanly (including The Great Knight beside
/// Lady Brisienna at Blades HQ 17/1045), expose faction/rivalry dialogue and court/vouching ties,
/// select distinct published visuals without colliding on shared classic billboards, and write zero save bytes when untouched.
/// </summary>
public sealed class PromotedClassicCastIntegrationTests
{
    private static readonly (string Id, int FactionId, string AssetId, DaggerfallSiteId Site, string Block, string SlotId)[] PromotedCast =
    [
        ("the-great-knight", 351, "npc-022", new DaggerfallSiteId(17, 1045), "PALAAA00.RMB", "b:0:p00"),
        ("lord-harth", 355, "npc-025", new DaggerfallSiteId(50, 90), "TVRNAL04.RMB", "b:17:p01"),
        ("baron-shrike", 379, "npc-045", new DaggerfallSiteId(22, 103), "PALAAA00.RMB", "b:0:p02"),
        ("lord-plessington", 359, "npc-028", new DaggerfallSiteId(39, 263), "PALAAA00.RMB", "b:0:p00"),
        ("chulmore-quill", 361, "npc-030", new DaggerfallSiteId(23, 601), "DARKAA02.RMB", "b:0:p02"),
        ("lord-bertram-spode", 375, "npc-041", new DaggerfallSiteId(17, 1231), "THIEAL00.RMB", "b:16:p02"),
        ("baltham-greyman", 376, "npc-042", new DaggerfallSiteId(17, 1231), "TVRNAL06.RMB", "b:13:p02"),
        ("lord-auberon-flyte", 401, "npc-066", new DaggerfallSiteId(21, 600), "PALAAA02.RMB", "b:0:p02"),
        ("lord-quistley", 402, "npc-067", new DaggerfallSiteId(21, 600), "TEMPAAC0.RMB", "b:21:p00"),
        ("baroness-dh-emka", 405, "npc-070", new DaggerfallSiteId(22, 103), "PALAAA00.RMB", "b:0:p00"),
    ];

    [Fact]
    public void All_10_promoted_classic_cast_individuals_resolve_homes_barks_and_zero_untouched_save_bytes()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteContext sites = new(definitions.Locations);
        HashSet<string> seenAssetIds = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string id, int factionId, string assetId, DaggerfallSiteId site, string block, string slotId) in PromotedCast)
        {
            DaggerfallCastIndividualDefinition cast = definitions.People.Individuals[id];
            Assert.Equal(factionId, cast.FactionId);
            Assert.Equal(assetId, cast.VisualAssetId);
            Assert.True(seenAssetIds.Add(assetId), $"Duplicate cast visualAssetId '{assetId}'.");
            Assert.Equal(site, cast.HomeSite);
            Assert.Equal(block, cast.HomeBlock);
            Assert.Equal(slotId, cast.HomeSlotId);

            Assert.True(DaggerfallCastHomes.TryResolve(sites, cast, site.Region, out DaggerfallCastHomeLocation? home));
            Assert.NotNull(home);
            Assert.Equal(site, home!.Site.Id);
            Assert.Equal(DaggerfallWorldProfileKind.Interior, home.ProfileKind);
            Assert.NotNull(home.Building);

            Assert.True(definitions.People.Barks.TryResolveFactionBarkLine(0UL, [factionId.ToString()], out string? bark));
            Assert.False(string.IsNullOrWhiteSpace(bark));
        }

        // Lainlyn's throne resolves directly to Baron Shrike with his palace home and House of Lainlyn dynasty.
        DaggerfallOfficeHolders holders = new(definitions, sites);
        DaggerfallOfficeHolder? lainlynRuler = holders.ResolveRegionalRuler(22);
        Assert.NotNull(lainlynRuler);
        Assert.Equal("baron-shrike", lainlynRuler!.CastId);
        Assert.Equal("fam:dynasty:lainlyn", lainlynRuler.FamilyId);
        Assert.Equal(new DaggerfallSiteId(22, 103), lainlynRuler.Home?.Site.Id);
        Assert.Equal("House of Lainlyn", DaggerfallHouseholds.FormatLabel("fam:dynasty:lainlyn", definitions, null, null, 0UL));
        Assert.Equal("House Flyte", DaggerfallHouseholds.FormatLabel("fam:dynasty:anticlere", definitions, null, null, 0UL));

        // Starting a fresh session away from all 10 homes writes zero CastIndividual ActorLedger entries.
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallSavePayload save = DaggerfallSavePayload.Read(session.CaptureSave());
        foreach ((string id, _, _, _, _, _) in PromotedCast)
        {
            long durableId = DaggerfallPeopleSlots.CastIndividualIdentity(id);
            Assert.DoesNotContain(save.ActorLedger, e => e.DurableId == durableId);
        }
    }

    [Fact]
    public void The_Great_Knight_materializes_beside_Lady_Brisienna_at_Blades_HQ_with_dialogue_vouching_and_save_load()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteId hearthwing = new(17, 1045);
        DaggerfallSiteRecord manor = definitions.Locations.Records.Single(r => r.Id == hearthwing);
        DaggerfallSiteBlock palaceBlock = Assert.Single(manor.Exterior!.Blocks, b => b.SourceName == "PALAAA00.RMB");
        DaggerfallWorldProfileKey bladesHallKey = DaggerfallWorldProfileIds.Interior(hearthwing, new(palaceBlock.X, palaceBlock.Y, 0));

        DaggerfallSiteProfile bladesHall = Assembled(bladesHallKey);
        DaggerfallSiteProfile awayTown = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfiles profiles = new([awayTown, bladesHall]);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, awayTown);
        PopulateContent(content, bladesHall);
        SpatialFake spatial = SpatialFake.Create(awayTown.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, awayTown, DaggerfallTuning.Defaults) { Profiles = profiles };

        long greatKnightId = DaggerfallPeopleSlots.CastIndividualIdentity("the-great-knight");
        long brisiennaId = DaggerfallPeopleSlots.CastIndividualIdentity("lady-brisienna");

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        Assert.True(session.TryTransitionTo(bladesHallKey));

        // Both The Great Knight and Lady Brisienna are materialized together in Hearthwing Manor on distinct slots.
        ActorState liveKnight = Assert.Single(session.State.Actors.All, a => a.DurableId == greatKnightId);
        ActorState liveBrisienna = Assert.Single(session.State.Actors.All, a => a.DurableId == brisiennaId);
        Assert.NotEqual(liveKnight.Position, liveBrisienna.Position);
        Assert.Equal("The Great Knight", session.State.Npcs.Require(greatKnightId).DisplayName);
        Assert.Equal("Lady Brisienna", session.State.Npcs.Require(brisiennaId).DisplayName);

        // Opening dialogue with The Great Knight shows Blades HQ / Lady Brisienna identity line.
        DaggerfallActivationTarget target = Assert.Single(
            session.Dialogue.NpcTargets(),
            candidate => candidate.Identity.Value == checked((ulong)greatKnightId));
        DaggerfallActivationOutcome opened = session.Dialogue.ActivateNpc(new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, target));
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView view = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains("Blades", view.Greeting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Brisienna", view.Greeting, StringComparison.OrdinalIgnoreCase);
        session.Dialogue.Close();

        // Blades HQ vouching: once the player earns high standing with The Great Knight (75), he shares the Septim
        // Empire / Blades hierarchy with Lady Brisienna and can introduce the player to her!
        session.State.Social.ChangeFactionReputation(351, 75, DaggerfallFactionReputationChange.Direct);
        DaggerfallNpc knightNpc = session.State.Npcs.Require(greatKnightId);
        Assert.Contains(session.Introductions.ListIntroductionTargets(knightNpc, manor), t => t.TargetDurableId == brisiennaId);
        DaggerfallIntroductionResult vouch = session.Introductions.TryIssueIntroduction(knightNpc, brisiennaId, manor);
        Assert.True(vouch.Accepted, vouch.Message);
        Assert.NotNull(vouch.Vouch);

        // Save and reload preserves both Blades officers and the Blades vouch cleanly without duplicate actors.
        RulesetSavePayload saveBytes = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, saveBytes);
        Assert.Single(restored.State.Actors.All, a => a.DurableId == greatKnightId);
        Assert.Single(restored.State.Actors.All, a => a.DurableId == brisiennaId);
        Assert.NotEmpty(restored.Introductions.ReadVouches(brisiennaId));
    }

    [Fact]
    public void Lainlyn_palace_materializes_Baron_Shrike_and_Baroness_Dhemka_and_reflects_Lord_Harth_rivalry()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteId lainlyn = new(22, 103);
        DaggerfallSiteRecord site = definitions.Locations.Records.Single(r => r.Id == lainlyn);
        DaggerfallSiteBlock palaceBlock = Assert.Single(site.Exterior!.Blocks, b => b.SourceName == "PALAAA00.RMB");
        DaggerfallWorldProfileKey palaceKey = DaggerfallWorldProfileIds.Interior(lainlyn, new(palaceBlock.X, palaceBlock.Y, 0));

        DaggerfallSiteProfile palace = Assembled(palaceKey);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, palace);
        SpatialFake spatial = SpatialFake.Create(palace.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, palace, DaggerfallTuning.Defaults);

        long shrikeId = DaggerfallPeopleSlots.CastIndividualIdentity("baron-shrike");
        long dhemkaId = DaggerfallPeopleSlots.CastIndividualIdentity("baroness-dh-emka");

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        // Set daytime palace hours so court-noble Baroness Dh'emka is at court beside always-stationed Baron Shrike.
        session.AdvanceElapsedTime(12 * 60 * 60);

        Assert.Single(session.State.Actors.All, a => a.DurableId == shrikeId);
        Assert.Single(session.State.Actors.All, a => a.DurableId == dhemkaId);
        Assert.Equal("fam:dynasty:lainlyn", session.State.Npcs.Require(shrikeId).FamilyId);
        Assert.Equal("fam:dynasty:lainlyn", session.State.Npcs.Require(dhemkaId).FamilyId);

        // At neutral standing, Baron Shrike introduces himself as Baron Shrike of Lainlyn / Host of the Horn.
        DaggerfallActivationTarget shrikeTarget = Assert.Single(
            session.Dialogue.NpcTargets(),
            candidate => candidate.Identity.Value == checked((ulong)shrikeId));
        DaggerfallActivationOutcome neutralTalk = session.Dialogue.ActivateNpc(new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, shrikeTarget));
        Assert.True(neutralTalk.Applied, neutralTalk.Message);
        DaggerfallDialogueView neutralView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains("Baron Shrike", neutralView.Greeting, StringComparison.OrdinalIgnoreCase);
        session.Dialogue.Close();

        // Gaining standing with his sworn enemy Lord Harth (#355) triggers Baron Shrike's rivalry warning!
        session.State.Social.ChangeFactionReputation(355, 10, DaggerfallFactionReputationChange.Direct);
        DaggerfallActivationOutcome rivalTalk = session.Dialogue.ActivateNpc(new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, shrikeTarget));
        Assert.True(rivalTalk.Applied, rivalTalk.Message);
        DaggerfallDialogueView rivalView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains("Lord Harth", rivalView.Greeting, StringComparison.OrdinalIgnoreCase);
        session.Dialogue.Close();
    }

    [Fact]
    public void Shared_fallback_cast_individuals_and_generic_occupant_select_distinct_refreshed_and_splat_visuals()
    {
        const string HashHex = "0000000000000001000000000000000200000000000000030000000000000004";
        ContentSha256 hash = DaggerfallContentHash.Parse(HashHex, "test");
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        // Both The Great Knight (npc-022) and Baltham Greyman (npc-042) share classic fallback 182/23,
        // which is also in town-alchemist's generic pool.
        DaggerfallCastIndividualDefinition greatKnight = definitions.People.Individuals["the-great-knight"];
        DaggerfallCastIndividualDefinition baltham = definitions.People.Individuals["baltham-greyman"];
        Assert.Equal((182, 23, "npc-022"), (greatKnight.Archive, greatKnight.Record, greatKnight.VisualAssetId));
        Assert.Equal((182, 23, "npc-042"), (baltham.Archive, baltham.Record, baltham.VisualAssetId));

        DaggerfallSiteProfile baseProfile = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        NormalizedBillboardSprite classicBillboard = new("classic/182-23.png", hash, 16, 32,
            [new NormalizedAtlasFrame(0, 0, 0, 16, 32)], 0, new Vector2(0.5F, 0F), new Vector2(1.0F, 1.8F));
        NormalizedActorSprite standing182 = classicBillboard.Standing((182, 23));

        DaggerfallStaticNpcPlacement knightSlot = new(
            "test/cast-knight",
            new WorldPoint(10F, 0F, 10F),
            new DaggerfallNpcAppearance("breton", "Male", 182, 23, 0, greatKnight.FactionId),
            "person",
            ["talk"],
            standing182,
            ActorInteractionPolicyKind.FullDialogue,
            ActorHarmPolicyKind.Protected,
            CastId: greatKnight.Id);
        DaggerfallStaticNpcPlacement balthamSlot = new(
            "test/cast-baltham",
            new WorldPoint(14F, 0F, 10F),
            new DaggerfallNpcAppearance("breton", "Male", 182, 23, 0, baltham.FactionId),
            "person",
            ["talk"],
            standing182,
            ActorInteractionPolicyKind.FullDialogue,
            ActorHarmPolicyKind.Protected,
            CastId: baltham.Id);
        DaggerfallStaticNpcPlacement genericAlchemistSlot = new(
            "test/generic-alchemist",
            new WorldPoint(18F, 0F, 10F),
            new DaggerfallNpcAppearance("breton", "Male", 182, 23, 77, 0),
            "alchemist",
            ["talk"],
            standing182,
            ActorInteractionPolicyKind.FullDialogue,
            ActorHarmPolicyKind.LawProtectedCivilian,
            SpawnerId: "town-alchemist",
            DeriveRegionalFaction: true);

        Dictionary<(int Archive, int Record), NormalizedBillboardSprite> billboards = new(baseProfile.BillboardSprites)
        {
            [(182, 23)] = classicBillboard,
        };
        DaggerfallSiteProfile testProfile = new(
            baseProfile.Project,
            baseProfile.Geometry,
            baseProfile.WorldAppearance,
            baseProfile.InitialLook,
            baseProfile.Materials,
            baseProfile.ActorSprites,
            baseProfile.MobileSprites,
            baseProfile.Audio,
            baseProfile.ClassicPresentation,
            baseProfile.Site,
            baseProfile.Doors,
            baseProfile.ProfileKey.Kind,
            baseProfile.ProfileKey.LogicalId,
            baseProfile.Portals,
            [.. baseProfile.Anchors.Values],
            baseProfile.Lights,
            baseProfile.GroundContainerSprites,
            baseProfile.DungeonMap,
            baseProfile.DungeonActions,
            baseProfile.DungeonActionModels,
            baseProfile.InteriorBuilding,
            baseProfile.QuestMarkers,
            billboards,
            [knightSlot, balthamSlot, genericAlchemistSlot],
            baseProfile.WaterVolumes,
            baseProfile.TerrainTextures,
            baseProfile.Population);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, testProfile);
        content.Add("classic/182-23.png", hash);
        content.Add("refreshed/cast/npc-022.png", hash);
        content.Add("refreshed/cast/npc-042.png", hash);
        content.Add("refreshed/people/texture-182-23.png", hash);
        content.Add("splats/npc-022.glb", hash);
        content.Add("splats/npc-042.glb", hash);
        AppearanceFake graphics = new(releases);
        SpatialFake spatial = SpatialFake.Create(testProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, graphics);

        // Publish refreshed sprites for npc-022, npc-042, AND generic 182/23, plus cast-specific splat characters
        // for npc-022 and npc-042 (without claiming any generic townsperson archive).
        DaggerfallArtReplacements baseSplats = DaggerfallPeopleSwitchTests.Art();
        DaggerfallArtReplacements art = BuildArtPack(HashHex, includeGeneric182: true, includeCastSplats: true);
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = DaggerfallPeopleSwitchTests.Clips(art.SplatPeople!);

        DaggerfallSessionComposition composition = new(definitions, testProfile, DaggerfallTuning.Defaults)
        {
            ArtReplacements = art,
        };
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);

        long knightId = DaggerfallPeopleSlots.CastIndividualIdentity(greatKnight.Id);
        long balthamId = DaggerfallPeopleSlots.CastIndividualIdentity(baltham.Id);
        long genericId = DaggerfallPeopleSlots.SeededSlotIdentity(
            testProfile.Site!.Value, $"{testProfile.ProfileKey.LogicalId}/{genericAlchemistSlot.Id}");

        ActorState knightActor = session.State.Actors.Get(knightId);
        ActorState balthamActor = session.State.Actors.Get(balthamId);
        ActorState genericActor = session.State.Actors.Get(genericId);

        // 1. Live ActorRenderComponent carries distinct cast asset bindings while the generic occupant carries none.
        Assert.Equal(("cast/npc-022", "npc-022"), (knightActor.Actor.Get<ActorRenderComponent>().Spec.AssetKey, knightActor.Actor.Get<ActorRenderComponent>().Spec.CrowdCharacterHint));
        Assert.Equal(("cast/npc-042", "npc-042"), (balthamActor.Actor.Get<ActorRenderComponent>().Spec.AssetKey, balthamActor.Actor.Get<ActorRenderComponent>().Spec.CrowdCharacterHint));
        Assert.Equal(("sprite/texture-182-23", (string?)null), (genericActor.Actor.Get<ActorRenderComponent>().Spec.AssetKey, genericActor.Actor.Get<ActorRenderComponent>().Spec.CrowdCharacterHint));

        // 2. Refreshed standing person selection: StartNew already wired composition.ArtReplacements into
        //    ActiveAppearance; all three actors open and draw distinct refreshed textures/appearances.
        bool refreshed = true;
        session.ActiveAppearance.Publish(session.State.Actors);
        IReadOnlyList<AppearanceFact> spriteSnapshot = graphics.Snapshots[^1];
        Appearance knightSprite = Assert.Single(spriteSnapshot, f => f.ObjectId == DaggerfallPresentationObjectIds.Gameplay(knightId, "Actor")).Appearance;
        Appearance balthamSprite = Assert.Single(spriteSnapshot, f => f.ObjectId == DaggerfallPresentationObjectIds.Gameplay(balthamId, "Actor")).Appearance;
        Appearance genericSprite = Assert.Single(spriteSnapshot, f => f.ObjectId == DaggerfallPresentationObjectIds.Gameplay(genericId, "Actor")).Appearance;

        Assert.NotSame(knightSprite, balthamSprite);
        Assert.NotSame(knightSprite, genericSprite);
        Assert.NotSame(balthamSprite, genericSprite);
        Assert.Contains("refreshed/cast/npc-022.png", graphics.OpenedResourcePaths);
        Assert.Contains("refreshed/cast/npc-042.png", graphics.OpenedResourcePaths);
        Assert.Contains("refreshed/people/texture-182-23.png", graphics.OpenedResourcePaths);

        // 3. Baked gsplat selection: cast Billboard actors resolve their distinct baked cast splats via CrowdCharacterHint,
        //    while the generic 182/23 occupant stays untouched on its generic billboard visual.
        List<string> splatWarnings = [];
        using DaggerfallPeopleSwitch peopleSwitch = new(
            baseSplats.PeopleRig,
            art.SplatPeople,
            DaggerfallPeopleSwitchTests.Shipped(),
            () => PartsRigsRecorder.Create().Service,
            () => animation.Service,
            () => content,
            walkerSpeed: 1.3F,
            DaggerfallSplatPeopleTuning.Default,
            session.State.Actors,
            new DaggerfallPeopleStage(session.State.Actors, _ => false, session.State.PlayerControl, (at, _) => WorldPoint.From(at)),
            [],
            townsperson: _ => null,
            seed: _ => 7,
            refreshed: () => refreshed,
            warn: splatWarnings.Add);
        session.ActiveAppearance.UsePeopleRigs(() => peopleSwitch);
        session.ActiveAppearance.Publish(session.State.Actors);
        Assert.DoesNotContain(splatWarnings, w => w.Contains("npc-022", StringComparison.Ordinal) || w.Contains("npc-042", StringComparison.Ordinal));

        IReadOnlyList<AppearanceFact> splatSnapshot = graphics.Snapshots[^1];
        Appearance knightSplat = Assert.Single(splatSnapshot, f => f.ObjectId == DaggerfallPresentationObjectIds.Gameplay(knightId, "Actor")).Appearance;
        Appearance balthamSplat = Assert.Single(splatSnapshot, f => f.ObjectId == DaggerfallPresentationObjectIds.Gameplay(balthamId, "Actor")).Appearance;
        Appearance genericAfterSplat = Assert.Single(splatSnapshot, f => f.ObjectId == DaggerfallPresentationObjectIds.Gameplay(genericId, "Actor")).Appearance;

        Assert.NotSame(knightSprite, knightSplat);
        Assert.NotSame(balthamSprite, balthamSplat);
        Assert.NotSame(knightSplat, balthamSplat);
        Assert.Same(genericSprite, genericAfterSplat);
        Assert.Contains("splats/npc-022.glb", animation.OpenedPaths);
        Assert.Contains("splats/npc-042.glb", animation.OpenedPaths);

        // Turning splats off falls back cleanly to the three distinct refreshed sprites.
        Assert.True(peopleSwitch.Splat("off").Succeeded);
        session.ActiveAppearance.Publish(session.State.Actors);
        IReadOnlyList<AppearanceFact> offSnapshot = graphics.Snapshots[^1];
        Assert.Same(knightSprite, Assert.Single(offSnapshot, f => f.ObjectId == DaggerfallPresentationObjectIds.Gameplay(knightId, "Actor")).Appearance);
        Assert.Same(balthamSprite, Assert.Single(offSnapshot, f => f.ObjectId == DaggerfallPresentationObjectIds.Gameplay(balthamId, "Actor")).Appearance);
        Assert.Same(genericSprite, Assert.Single(offSnapshot, f => f.ObjectId == DaggerfallPresentationObjectIds.Gameplay(genericId, "Actor")).Appearance);

        // 4. Publishing only cast-specific refreshed art (without generic 182/23) leaves the generic occupant on classic art.
        DaggerfallArtReplacements castOnlyArt = BuildArtPack(HashHex, includeGeneric182: false, includeCastSplats: false);
        Assert.Equal("refreshed/cast/npc-022.png", castOnlyArt.Person("npc-022", (182, 23))?.File.Path);
        Assert.Equal("refreshed/cast/npc-042.png", castOnlyArt.Person("npc-042", (182, 23))?.File.Path);
        Assert.Null(castOnlyArt.Person(null, (182, 23)));
    }

    private static DaggerfallArtReplacements BuildArtPack(string hashHex, bool includeGeneric182, bool includeCastSplats)
    {
        string shippedPath = Path.Combine(TestData.RepositoryRoot, "content", DaggerfallArtReplacements.Folder, "payload.json");
        JsonObject root = JsonNode.Parse(TestContentFiles.Read(shippedPath))!.AsObject();
        JsonObject section = root["artReplacements"]!.AsObject();

        JsonArray people = new()
        {
            MakePersonSpriteJson("assetId", "npc-022", "refreshed/cast/npc-022.png", hashHex),
            MakePersonSpriteJson("assetId", "npc-042", "refreshed/cast/npc-042.png", hashHex),
        };
        if (includeGeneric182)
            people.Add(MakeGenericPersonSpriteJson(182, 23, "refreshed/people/texture-182-23.png", hashHex));
        section["people"] = people;

        if (includeCastSplats)
        {
            JsonObject characters = section["splatPeople"]!["characters"]!.AsObject();
            JsonObject template = characters["385"]!.AsObject();
            JsonObject knightSplat = template.DeepClone().AsObject();
            knightSplat["name"] = "The Great Knight";
            knightSplat.Remove("archive");
            knightSplat.Remove("hybrid");
            knightSplat["body"] = new JsonObject { ["path"] = "splats/npc-022.glb", ["sha256"] = hashHex };
            characters["npc-022"] = knightSplat;

            JsonObject balthamSplat = template.DeepClone().AsObject();
            balthamSplat["name"] = "Baltham Greyman";
            balthamSplat.Remove("archive");
            balthamSplat.Remove("hybrid");
            balthamSplat["body"] = new JsonObject { ["path"] = "splats/npc-042.glb", ["sha256"] = hashHex };
            characters["npc-042"] = balthamSplat;
        }

        return DaggerfallArtReplacements.Read(Encoding.UTF8.GetBytes(root.ToJsonString()), "test.cast-art");
    }

    private static JsonObject MakePersonSpriteJson(string keyName, string keyValue, string path, string hashHex) => new()
    {
        [keyName] = keyValue,
        ["path"] = path,
        ["byteLength"] = 1,
        ["sha256"] = hashHex,
        ["width"] = 32,
        ["height"] = 64,
        ["frames"] = new JsonArray { new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = 32, ["height"] = 64 } },
        ["placement"] = new JsonObject
        {
            ["size"] = new JsonObject { ["width"] = 1.0, ["height"] = 1.8 },
            ["pivot"] = new JsonObject { ["x"] = 0.5, ["y"] = 0.5 },
            ["foot"] = new JsonObject { ["x"] = 0.5, ["y"] = 0.0 },
        },
    };

    private static JsonObject MakeGenericPersonSpriteJson(int archive, int record, string path, string hashHex) => new()
    {
        ["replaces"] = new JsonObject { ["archive"] = archive, ["record"] = record },
        ["path"] = path,
        ["byteLength"] = 1,
        ["sha256"] = hashHex,
        ["width"] = 32,
        ["height"] = 64,
        ["frames"] = new JsonArray { new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = 32, ["height"] = 64 } },
        ["placement"] = new JsonObject
        {
            ["size"] = new JsonObject { ["width"] = 1.0, ["height"] = 1.8 },
            ["pivot"] = new JsonObject { ["x"] = 0.5, ["y"] = 0.5 },
            ["foot"] = new JsonObject { ["x"] = 0.5, ["y"] = 0.0 },
        },
    };
}
