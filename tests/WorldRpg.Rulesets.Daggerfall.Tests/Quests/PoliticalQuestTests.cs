using WorldRpg.Politics;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Political quests (#11121): a seat's own questgivers offer them from the live board's opportunities, the quest text
/// names whose ground the player gains and who will resent it, and success moves the board by the play and costs the
/// opposed seats' standing in full.
/// </summary>
public sealed class PoliticalQuestTests
{
    private const int MagesGuild = 40, Necromancers = 306;

    private static readonly DaggerfallTuning AlwaysPolitical = DaggerfallTuning.Defaults with
    {
        Politics = DaggerfallTuning.Defaults.Politics with { PoliticalOfferPercent = 100 },
    };

    [Fact]
    public void The_board_offers_a_seat_plays_against_the_rival_of_its_war_front_or_of_a_region_it_contests()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallPoliticalBoard political = session.PoliticalBoard!;
        Board board = political.Board;

        // At war with Sentinel, Daggerfall's plays on a front are set against Sentinel.
        board.OpenWar(board.Seat("daggerfall"), board.Seat("sentinel"), WarAim.Conquest);
        Region front = board.Regions.First(region => Wars.Against(board, board.Seat("daggerfall"), region) is not null);
        IReadOnlyList<DaggerfallPoliticalPlay> war = political.PoliticalPlays("daggerfall", front.CatalogRegion);
        Assert.Contains(war, play => play.Kind == DaggerfallPoliticalPlayKind.CarryWord);
        Assert.All(war, play => Assert.Equal(("sentinel", front.Id), (play.Rival, play.Region)));

        // The Mages Guild contests a region where the Thieves Guild leads it in influence: carrying its word, undermining
        // the Thieves Guild there, and turning each office the board lets it take.
        Region contested = board.Regions.First(region => !region.Capital && region.Influence.Count == 0
            && Wars.Against(board, board.Seat("mages-guild"), region) is null);
        board.AdjustInfluence(contested, board.Seat("mages-guild"), 1);
        board.AdjustInfluence(contested, board.Seat("thieves-guild"), Board.InfluenceLimit);
        board.SetOffice(contested, Office.Hall, board.Seat("mages-guild"));
        IReadOnlyList<DaggerfallPoliticalPlay> plays = political.PoliticalPlays("mages-guild", contested.CatalogRegion);
        Assert.All(plays, play => Assert.Equal(("thieves-guild", contested.Id), (play.Rival, play.Region)));
        Assert.Contains(plays, play => play.Kind == DaggerfallPoliticalPlayKind.UndermineRival);
        Assert.Equal([.. Enum.GetValues<Office>().Where(office => Card.MayTakeOffice(board, board.Seat("mages-guild"), contested, office))],
            plays.Where(play => play.Kind == DaggerfallPoliticalPlayKind.TurnOfficial).Select(play => play.Office!.Value));
        Assert.DoesNotContain(plays, play => play.Office == Office.Hall);

        // Leading it, the Mages Guild has no rival there and nothing to play.
        board.AdjustInfluence(contested, board.Seat("thieves-guild"), -Board.InfluenceLimit);
        Assert.Empty(political.PoliticalPlays("mages-guild", contested.CatalogRegion));
    }

    [Fact]
    public void Each_play_moves_the_board_by_its_kind_at_the_next_sunday()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallPoliticalBoard political = session.PoliticalBoard!;
        Board board = political.Board;
        DaggerfallPoliticsTuning tuning = DaggerfallTuning.Defaults.Politics;
        Region region = board.Regions.First(held => !held.Capital && held.InfluenceOf("mages-guild") == 0);
        board.AdjustInfluence(region, board.Seat("thieves-guild"), Board.InfluenceLimit);
        int thieves = region.InfluenceOf("thieves-guild");

        political.ObservePoliticalQuest(new(DaggerfallPoliticalPlayKind.CarryWord, "mages-guild", region.Id, "thieves-guild", null), tuning);
        political.ObservePoliticalQuest(new(DaggerfallPoliticalPlayKind.UndermineRival, "mages-guild", region.Id, "thieves-guild", null), tuning);
        political.ObservePoliticalQuest(new(DaggerfallPoliticalPlayKind.TurnOfficial, "mages-guild", region.Id, "thieves-guild", Office.Factor), tuning);
        Assert.Equal(3, board.Queued.Count);
        foreach (WorldNudge nudge in board.TakeQueued()) Assert.Equal(NudgeOutcome.Applied, Nudges.Apply(board, nudge));

        Assert.Equal(tuning.CarryWordInfluence + tuning.UndermineInfluence, region.InfluenceOf("mages-guild"));
        Assert.Equal(thieves - tuning.UndermineInfluence, region.InfluenceOf("thieves-guild"));
        Assert.Equal("mages-guild", region.Offices[Office.Factor]);
        Assert.Contains(Office.Factor, region.Covert);
    }

    [Fact]
    public void A_seat_questgiver_offers_a_political_quest_that_names_the_stakes_survives_a_save_and_lands_on_success()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create(AlwaysPolitical);
        using DaggerfallSession session = fixture.Start(fixture.MagesProfile);
        DaggerfallNpc giver = Assert.Single(session.State.Npcs.All, npc => npc.Kind == DaggerfallNpcKind.Static
            && npc.Presence == DaggerfallNpcPresence.Active && npc.Services.Contains("quests"));
        Board board = session.PoliticalBoard!.Board;
        Region region = Assert.Single(board.Regions, held => held.CatalogRegion == giver.Site.Region);
        board.AdjustInfluence(region, board.Seat("mages-guild"), 1);
        board.AdjustInfluence(region, board.Seat("thieves-guild"), Board.InfluenceLimit);
        board.AdjustRelation(board.Seat("mages-guild"), board.Seat("necromancers"), Supply.Hostile - board.Relation("mages-guild", "necromancers"));
        Assert.NotEmpty(session.PoliticalBoard.PoliticalPlays("mages-guild", giver.Site.Region));

        Assert.True(session.QuestWork.HasWork(giver.DurableId));
        string text = session.QuestWork.Offer(giver.DurableId);
        DaggerfallQuestOfferSave offer = Assert.IsType<DaggerfallQuestOfferSave>(session.State.Quests.PendingOffer);
        DaggerfallPoliticalPlay play = Assert.IsType<DaggerfallPoliticalPlay>(offer.Quest.Political);
        Assert.Equal(DaggerfallPoliticalQuests.SourceFile(play.Kind), offer.Quest.SourceFile);
        Assert.Equal(("mages-guild", region.Id), (play.Seat, play.Region));
        Assert.Contains(board.Seat("mages-guild").Name, text);
        Assert.Contains(region.Name, text);
        Assert.Contains(board.Seat(play.Rival).Name, text);
        Assert.Contains(board.Seat("necromancers").Name, text);

        session.AnswerQuestOffer(offer.Quest.InstanceId, true);
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        DaggerfallQuestInstanceSave accepted = Assert.Single(restored.State.Quests.All);
        Assert.Equal(play, accepted.Political);

        // Success moves the board by the play, not by an ordinary quest's little ground, and the seats set against the
        // Mages Guild take the whole gain from the player's standing.
        DaggerfallSocialState expected = new(TestPayload.Definitions.Factions);
        expected.Restore(restored.State.Social.Capture());
        expected.StandingGained += new DaggerfallBoardStandingRipple(restored.PoliticalBoard!, expected, TestPayload.Definitions.BoardSeats,
            DaggerfallTuning.Defaults.Politics).ObserveGain;
        expected.ChangeFactionReputation(MagesGuild, 5, DaggerfallFactionReputationChange.Propagate, DaggerfallStandingGain.Political);
        DaggerfallSocialState ordinary = new(TestPayload.Definitions.Factions);
        ordinary.Restore(restored.State.Social.Capture());
        ordinary.StandingGained += new DaggerfallBoardStandingRipple(restored.PoliticalBoard!, ordinary, TestPayload.Definitions.BoardSeats,
            DaggerfallTuning.Defaults.Politics).ObserveGain;
        ordinary.ChangeFactionReputation(MagesGuild, 5, DaggerfallFactionReputationChange.Propagate);

        restored.State.Quests.Complete(accepted.InstanceId, "done");

        Assert.Equal(expected.FactionReputation(Necromancers), restored.State.Social.FactionReputation(Necromancers));
        Assert.True(restored.State.Social.FactionReputation(Necromancers) < ordinary.FactionReputation(Necromancers));
        WorldNudge queued = Assert.Single(restored.PoliticalBoard!.Board.Queued);
        Assert.Equal(play.Kind == DaggerfallPoliticalPlayKind.TurnOfficial ? typeof(OfficeTurned) : typeof(QuestOutcome), queued.GetType());
    }

    [Fact]
    public void Winning_over_an_official_binds_the_chief_towns_office_holder_and_each_kind_prepares_from_its_giver()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create(AlwaysPolitical);
        using DaggerfallSession session = fixture.Start(fixture.MagesProfile);
        DaggerfallNpc giver = Assert.Single(session.State.Npcs.All, npc => npc.Kind == DaggerfallNpcKind.Static
            && npc.Presence == DaggerfallNpcPresence.Active && npc.Services.Contains("quests"));
        Board board = session.PoliticalBoard!.Board;
        Region region = Assert.Single(board.Regions, held => held.CatalogRegion == giver.Site.Region);
        DaggerfallPoliticalQuests political = session.PoliticalQuests!;

        foreach (DaggerfallPoliticalPlayKind kind in Enum.GetValues<DaggerfallPoliticalPlayKind>())
        {
            DaggerfallPoliticalPlay play = new(kind, "mages-guild", region.Id, "thieves-guild",
                kind == DaggerfallPoliticalPlayKind.TurnOfficial ? Office.Watch : null);
            DaggerfallQuestOfferSave offer = session.State.Quests.PreparePoliticalOffer(giver.DurableId, giver.Site, MagesGuild, play);
            Assert.Equal(play, offer.Quest.Political);
            Assert.DoesNotContain("_seat_", session.State.Quests.ReadOffer()!.Text);
            if (kind == DaggerfallPoliticalPlayKind.TurnOfficial)
            {
                DaggerfallOfficeHolder official = Assert.IsType<DaggerfallOfficeHolder>(political.Official(play));
                DaggerfallQuestResourceState bound = Assert.Single(offer.Quest.Resources, resource => resource.Symbol == "official");
                Assert.Equal([official.ActorId!.Value], bound.Binding.ActorIds);
                Assert.Contains(official.DisplayName, session.State.Quests.ReadOffer()!.Text);
            }
            session.State.Quests.AnswerOffer(offer.Quest.InstanceId, false, true);
        }
    }
}
