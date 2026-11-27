using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11141: verifies all 10 promoted classic cast individuals have deterministic home placements,
/// materialize cleanly (including The Great Knight beside Lady Brisienna at Blades HQ 17/1045),
/// expose faction/rivalry dialogue and court/vouching ties, and write zero save bytes when untouched.
/// </summary>
public sealed class PromotedClassicCastIntegrationTests
{
    private static readonly (string Id, int FactionId, DaggerfallSiteId Site, string Block, string SlotId)[] PromotedCast =
    [
        ("the-great-knight", 351, new DaggerfallSiteId(17, 1045), "PALAAA00.RMB", "b:0:p00"),
        ("lord-harth", 355, new DaggerfallSiteId(50, 90), "TVRNAL04.RMB", "b:17:p01"),
        ("baron-shrike", 379, new DaggerfallSiteId(22, 103), "PALAAA00.RMB", "b:0:p02"),
        ("lord-plessington", 359, new DaggerfallSiteId(39, 263), "PALAAA00.RMB", "b:0:p00"),
        ("chulmore-quill", 361, new DaggerfallSiteId(23, 601), "DARKAA02.RMB", "b:0:p02"),
        ("lord-bertram-spode", 375, new DaggerfallSiteId(17, 1231), "THIEAL00.RMB", "b:16:p02"),
        ("baltham-greyman", 376, new DaggerfallSiteId(17, 1231), "TVRNAL06.RMB", "b:13:p02"),
        ("lord-auberon-flyte", 401, new DaggerfallSiteId(21, 600), "PALAAA02.RMB", "b:0:p02"),
        ("lord-quistley", 402, new DaggerfallSiteId(21, 600), "TEMPAAC0.RMB", "b:21:p00"),
        ("baroness-dh-emka", 405, new DaggerfallSiteId(22, 103), "PALAAA00.RMB", "b:0:p00"),
    ];

    [Fact]
    public void All_10_promoted_classic_cast_individuals_resolve_homes_barks_and_zero_untouched_save_bytes()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteContext sites = new(definitions.Locations);

        foreach ((string id, int factionId, DaggerfallSiteId site, string block, string slotId) in PromotedCast)
        {
            DaggerfallCastIndividualDefinition cast = definitions.People.Individuals[id];
            Assert.Equal(factionId, cast.FactionId);
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
        foreach ((string id, _, _, _, _) in PromotedCast)
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
}
