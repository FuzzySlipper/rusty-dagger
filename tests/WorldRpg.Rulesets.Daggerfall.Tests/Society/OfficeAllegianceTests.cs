using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A board office challenge turns the chief town's incumbent, who answers to the seat (#11127, settled with Patch):
/// public takes are posted, secret ones only rumoured; the seat's members are received better, its enemies worse.
/// </summary>
public sealed class OfficeAllegianceTests
{
    private const int MagesGuild = 40, ThievesGuild = 42;

    private static (DaggerfallSession Session, Board Board, Region Glenpoint, DaggerfallSiteRecord Town) Start()
    {
        DaggerfallSession session = FreshSession();
        DaggerfallPoliticalBoard seam = session.PoliticalBoard!;
        return (session, seam.Board, seam.Board.Region("glenpoint"), seam.Town("glenpoint")!);
    }

    private static IReadOnlyList<DaggerfallNoticeEntry> Notices(DaggerfallSession session, DaggerfallSiteRecord town) =>
        session.NoticeBoard.BuildForSite(town, session.Calendar.MinuteNumber).Entries;

    [Fact]
    public void A_public_take_is_posted_naming_the_seat_and_a_secret_one_only_rumoured()
    {
        (DaggerfallSession session, Board board, Region glenpoint, DaggerfallSiteRecord town) = Start();
        using (session)
        {
            Assert.Empty(session.OfficeAllegiance!.All(town));
            string mages = board.Seat("mages-guild").Name, thieves = board.Seat("thieves-guild").Name;

            board.SetOffice(glenpoint, Office.Hall, board.Seat("mages-guild"));
            DaggerfallOfficeAllegianceView hall = Assert.Single(session.OfficeAllegiance.All(town));
            Assert.Equal((DaggerfallOfficeKind.GuildHallMaster, "mages-guild", false), (hall.Kind, hall.SeatId, hall.Covert));
            Assert.Contains(Notices(session, town), entry => entry.Kind == DaggerfallNoticeEntryKind.OfficeNotice && entry.Body.Contains(mages, StringComparison.Ordinal));

            board.SetOffice(glenpoint, Office.Watch, board.Seat("thieves-guild"), covert: true);
            Assert.True(session.OfficeAllegiance.Of(town, DaggerfallOfficeKind.WatchCaptain)!.Covert);
            Assert.Contains(Notices(session, town), entry => entry.Kind == DaggerfallNoticeEntryKind.SignRumor && entry.Body.Contains("looks the other way", StringComparison.Ordinal));
            Assert.DoesNotContain(Notices(session, town), entry => entry.Body.Contains(thieves, StringComparison.Ordinal));

            // The crown that rules a region holding its offices is the town's ordinary order, not a seat's hold.
            Region capital = board.Region("daggerfall");
            Assert.Equal("daggerfall", capital.Offices[Office.Watch]);
            Assert.Empty(session.OfficeAllegiance.All(session.PoliticalBoard!.Town("daggerfall")!).Where(held => held.SeatId == "daggerfall"));

            // Only the region's chief town has board offices.
            DaggerfallSiteRecord other = TestPayload.Definitions.Locations.Records.First(site => site.Id.Region == town.Id.Region && site.Id != town.Id);
            Assert.Empty(session.OfficeAllegiance.All(other));
        }
    }

    [Fact]
    public void The_watch_passes_its_seats_members_and_holds_a_hostile_seats_members_to_heightened_scrutiny()
    {
        (DaggerfallSession session, Board board, Region glenpoint, DaggerfallSiteRecord town) = Start();
        using (session)
        {
            board.SetOffice(glenpoint, Office.Watch, board.Seat("mages-guild"));
            session.WorldConditions.Set(new DaggerfallWorldConditionToken("cond:crackdown:glenpoint", DaggerfallWorldConditionKind.CrimeCrackdown,
                Region: town.Id.Region, Severity: 1).Validate());
            Assert.False(session.Scrutiny.EvaluateGateScrutiny(town.Id.Region, town).PlayerExempt);

            session.State.Social.JoinGuild(MagesGuild, 1);
            Assert.Equal(DaggerfallOfficeStanding.Member, session.OfficeAllegiance!.Watch(town));
            Assert.True(session.Scrutiny.EvaluateGateScrutiny(town.Id.Region, town).PlayerExempt);
        }

        (session, board, glenpoint, town) = Start();
        using (session)
        {
            board.SetOffice(glenpoint, Office.Watch, board.Seat("mages-guild"));
            Assert.Equal(DaggerfallScrutinyLevel.Normal, session.Scrutiny.EvaluateGateScrutiny(town.Id.Region, town).Level);
            session.State.Social.JoinGuild(ThievesGuild, 1);
            board.AdjustRelation(board.Seat("thieves-guild"), board.Seat("mages-guild"), Supply.Hostile - board.Relation("thieves-guild", "mages-guild"));
            Assert.Equal(DaggerfallOfficeStanding.Enemy, session.OfficeAllegiance!.Watch(town));
            DaggerfallGateScrutinyAssessment gate = session.Scrutiny.EvaluateGateScrutiny(town.Id.Region, town);
            Assert.Equal(DaggerfallScrutinyLevel.Heightened, gate.Level);
            Assert.Contains("answers to your enemies", gate.Reason, StringComparison.Ordinal);
        }
    }

    /// <summary>The scrutiny of <paramref name="session"/>, standing in <paramref name="town"/>, with its watch's allegiance as the session wires it.</summary>
    private static DaggerfallScrutinyPolicy ScrutinyIn(DaggerfallSession session, DaggerfallSiteRecord town) => new(
        session.State.Social, session.State.Crime, session.LegalServices, session.State.ActorLedger, session.WorldConditions, TestPayload.Definitions,
        () => checked((int)session.Calendar.DayNumber), _ => null, () => null,
        hasWatchOrRulerVouch: site => session.OfficeAllegiance!.Watch(site) == DaggerfallOfficeStanding.Member,
        activeSite: () => town, hasUsableVouchFor: _ => false,
        watchAgainstPlayer: site => session.OfficeAllegiance!.Watch(site) == DaggerfallOfficeStanding.Enemy);

    /// <summary>R11127-1: the town's watch decides service and room-rental scrutiny there, as it does its gates.</summary>
    [Fact]
    public void Service_and_room_rental_follow_the_towns_watch_its_seats_members_served_and_its_enemies_refused()
    {
        (DaggerfallSession session, Board board, Region glenpoint, DaggerfallSiteRecord town) = Start();
        using (session)
        {
            DaggerfallNpc provider = new(9002, DaggerfallNpcKind.Static, "provider", new DaggerfallNpcSite(town.Id.Region, town.Name, "Shop"),
                new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, 0), "merchant", ["merchant"], DaggerfallNpcPresence.Active, null, null, null);
            DaggerfallScrutinyPolicy scrutiny = ScrutinyIn(session, town);
            session.State.Social.ChangeRegionalReputation(town.Id.Region, -6 - session.State.Social.RegionalReputation(town.Id.Region));
            board.SetOffice(glenpoint, Office.Watch, board.Seat("mages-guild"));

            // A friendly watch under a crackdown serves and lodges the player.
            session.WorldConditions.Set(new DaggerfallWorldConditionToken("cond:crackdown:glenpoint", DaggerfallWorldConditionKind.CrimeCrackdown,
                Region: town.Id.Region, Severity: 1).Validate());
            Assert.True(scrutiny.EvaluateServiceScrutiny(provider).Denied);
            Assert.True(scrutiny.IsRoomRentalDenied(town.Id.Region, null, provider));
            session.State.Social.JoinGuild(MagesGuild, 1);
            Assert.False(scrutiny.EvaluateServiceScrutiny(provider).Denied);
            Assert.False(scrutiny.IsRoomRentalDenied(town.Id.Region, null, provider));

            // Wanted is wanted, whoever keeps the watch.
            session.State.Social.ChangeRegionalReputation(town.Id.Region, -5);
            Assert.Equal(DaggerfallServiceDenial.WantedOrBanished, scrutiny.EvaluateServiceScrutiny(provider).Denial);
        }

        (session, board, glenpoint, town) = Start();
        using (session)
        {
            DaggerfallNpc provider = new(9002, DaggerfallNpcKind.Static, "provider", new DaggerfallNpcSite(town.Id.Region, town.Name, "Shop"),
                new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, 0), "merchant", ["merchant"], DaggerfallNpcPresence.Active, null, null, null);
            DaggerfallScrutinyPolicy scrutiny = ScrutinyIn(session, town);
            session.State.Social.ChangeRegionalReputation(town.Id.Region, -6 - session.State.Social.RegionalReputation(town.Id.Region));
            board.SetOffice(glenpoint, Office.Watch, board.Seat("mages-guild"));
            Assert.False(scrutiny.EvaluateServiceScrutiny(provider).Denied);
            Assert.False(scrutiny.IsRoomRentalDenied(town.Id.Region, null, provider));

            // A hostile watch, with no condition in force, still refuses them.
            session.State.Social.JoinGuild(ThievesGuild, 1);
            board.AdjustRelation(board.Seat("thieves-guild"), board.Seat("mages-guild"), Supply.Hostile - board.Relation("thieves-guild", "mages-guild"));
            DaggerfallServiceScrutinyAssessment refused = scrutiny.EvaluateServiceScrutiny(provider);
            Assert.Equal(DaggerfallServiceDenial.UnderScrutiny, refused.Denial);
            Assert.Contains("answers to your enemies", refused.Reason, StringComparison.Ordinal);
            Assert.True(scrutiny.IsRoomRentalDenied(town.Id.Region, null, provider));
        }
    }

    [Fact]
    public void A_factor_answering_to_the_players_seat_warms_the_towns_shopkeepers_and_the_holder_says_whom_they_answer_to()
    {
        (DaggerfallSession session, Board board, Region glenpoint, DaggerfallSiteRecord town) = Start();
        using (session)
        {
            DaggerfallNpc shopkeeper = new(9001, DaggerfallNpcKind.Static, "shop", new DaggerfallNpcSite(town.Id.Region, town.Name, "Shop"),
                new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, 0), "merchant", ["merchant"], DaggerfallNpcPresence.Active, null, null, null);
            int before = session.State.Social.TradeReactionForNpc(shopkeeper);
            board.SetOffice(glenpoint, Office.Factor, board.Seat("mages-guild"));
            Assert.Equal(before, session.State.Social.TradeReactionForNpc(shopkeeper));

            session.State.Social.JoinGuild(MagesGuild, 1);
            Assert.Equal(before + DaggerfallTuning.Defaults.Politics.OfficeMemberReaction, session.State.Social.TradeReactionForNpc(shopkeeper));

            // The watch captain, turned, says whom they answer to: openly for a public take, to the seat's own only for a secret one.
            DaggerfallLocalOffice watch = session.OfficeLedger.ResolveLocalOffices(town).First(office => office.Kind == DaggerfallOfficeKind.WatchCaptain && office.HolderActorId is not null);
            DaggerfallNpc captain = shopkeeper with { DurableId = watch.HolderActorId!.Value };
            board.SetOffice(glenpoint, Office.Watch, board.Seat("thieves-guild"));
            Assert.Contains($"I answer to {board.Seat("thieves-guild").Name} now", session.Dialogue.ResolveOfficeAllegianceLine(captain, town), StringComparison.Ordinal);
            board.SetOffice(glenpoint, Office.Watch, null);
            board.SetOffice(glenpoint, Office.Watch, board.Seat("thieves-guild"), covert: true);
            Assert.Null(session.Dialogue.ResolveOfficeAllegianceLine(captain, town));
            session.State.Social.JoinGuild(ThievesGuild, 1);
            Assert.Contains("Between us", session.Dialogue.ResolveOfficeAllegianceLine(captain, town), StringComparison.Ordinal);
        }
    }
}
