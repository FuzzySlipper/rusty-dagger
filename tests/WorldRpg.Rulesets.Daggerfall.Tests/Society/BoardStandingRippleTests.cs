using WorldRpg.Politics;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The board's quarrels follow the player's standing (#11121): a gain with a seat's faction costs standing with the
/// seats the live board sets against it, once, and never ripples further.
/// </summary>
public sealed class BoardStandingRippleTests
{
    private const int Daggerfall = 201, Wayrest = 207, Sentinel = 204, MagesGuild = 40, ThievesGuild = 42;

    private static void SetRelation(Board board, string a, string b, int value) =>
        board.AdjustRelation(board.Seat(a), board.Seat(b), value - board.Relation(a, b));

    [Fact]
    public void Helping_a_seat_costs_standing_with_the_seats_hostile_to_it_and_with_no_one_else()
    {
        using DaggerfallSession session = FreshSession();
        Board board = session.PoliticalBoard!.Board;
        DaggerfallSocialState social = session.State.Social;
        SetRelation(board, "daggerfall", "wayrest", Supply.Hostile);
        SetRelation(board, "daggerfall", "sentinel", 0);
        int wayrest = social.FactionReputation(Wayrest), sentinel = social.FactionReputation(Sentinel);

        social.ChangeFactionReputation(Daggerfall, 10, DaggerfallFactionReputationChange.Propagate);

        int share = 10 * DaggerfallTuning.Defaults.Politics.OpposedStandingPercent / 100;
        Assert.Equal(wayrest - share, social.FactionReputation(Wayrest));
        Assert.Equal(sentinel, social.FactionReputation(Sentinel));
        Assert.Equal(["wayrest"], session.StandingRipple!.Opposed("daggerfall").Where(seat => seat is "wayrest" or "sentinel"));

        // At war with Daggerfall, Sentinel is set against it too, hostile or not.
        board.OpenWar(board.Seat("daggerfall"), board.Seat("sentinel"), WarAim.Conquest);
        SetRelation(board, "daggerfall", "sentinel", 0);
        sentinel = social.FactionReputation(Sentinel);
        social.ChangeFactionReputation(Daggerfall, 10, DaggerfallFactionReputationChange.Propagate);
        Assert.Equal(sentinel - share, social.FactionReputation(Sentinel));

        // A loss never ripples on: Wayrest's loss costs Daggerfall nothing more.
        int daggerfall = social.FactionReputation(Daggerfall);
        social.ChangeFactionReputation(Wayrest, -6, DaggerfallFactionReputationChange.Propagate);
        Assert.Equal(daggerfall, social.FactionReputation(Daggerfall));
    }

    [Fact]
    public void A_gain_with_a_faction_under_a_seat_is_the_seats_and_a_faction_under_none_ripples_nowhere()
    {
        using DaggerfallSession session = FreshSession();
        Board board = session.PoliticalBoard!.Board;
        DaggerfallSocialState social = session.State.Social;
        SetRelation(board, "mages-guild", "thieves-guild", Supply.Hostile - 1);
        int underMages = TestPayload.Definitions.Factions.Factions.Values.First(faction => faction.Parent == MagesGuild).Id;
        Assert.Equal("mages-guild", session.PoliticalBoard.SeatOfFaction(underMages));

        int thieves = social.FactionReputation(ThievesGuild);
        social.ChangeFactionReputation(underMages, 8, DaggerfallFactionReputationChange.Propagate);
        Assert.Equal(thieves - 8 * DaggerfallTuning.Defaults.Politics.OpposedStandingPercent / 100, social.FactionReputation(ThievesGuild));

        // The People of an independent region answer to no seat: nobody's standing moves for them.
        int peopleOfGlenpoint = 522;
        Assert.Null(session.PoliticalBoard.SeatOfFaction(peopleOfGlenpoint));
        thieves = social.FactionReputation(ThievesGuild);
        social.ChangeFactionReputation(peopleOfGlenpoint, 8, DaggerfallFactionReputationChange.Propagate);
        Assert.Equal(thieves, social.FactionReputation(ThievesGuild));
    }
}
