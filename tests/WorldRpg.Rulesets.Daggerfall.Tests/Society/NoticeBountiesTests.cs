using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A bounty taken from a notice board pays out (#11137): breaking its condition earns it, and a guard or court official
/// of its region pays the reward with standing and a journal entry; it lapses with its condition and saves.
/// </summary>
public sealed class NoticeBountiesTests
{
    private const int Glenpoint = 18, Betony = 19;
    private const string TokenId = "board:glenpoint:monster-surge:orc";

    private sealed class Stage
    {
        internal int Day = 10;
        internal int Paid;
        internal readonly DaggerfallWorldConditions World = new();
        internal readonly DaggerfallBookNotebook Notebook = new(TestPayload.Definitions, new DaggerfallTextResolver(TestPayload.Definitions.Text));
        internal readonly DaggerfallSocialState Social = new(TestPayload.Definitions.Factions);
        internal readonly DaggerfallNoticeBounties Bounties;

        internal Stage()
        {
            Bounties = new(World, Notebook, Social, TestPayload.Definitions, DaggerfallTuning.Defaults.Law, () => Day, gold => Paid += gold);
            World.Set(new DaggerfallWorldConditionToken(TokenId, DaggerfallWorldConditionKind.MonsterSurge, Region: Glenpoint, SubjectTag: "orc", Severity: 2).Validate());
        }
    }

    private static DaggerfallNoticeEntry Posting(string leadId = "lead:notice:cond:orc") => new(
        $"notice:cond:{TokenId}", DaggerfallNoticeEntryKind.Bounty, "Bounty: Orc Surge in Glenpoint", "Orc warbands roam.", Glenpoint,
        ConditionTokenId: TokenId, Lead: new DaggerfallNoticeLead(leadId, DaggerfallNoticeLeadKind.RegionalBounty, "Take bounty lead"));

    private static DaggerfallNpc Npc(long id, string role, int faction, int region = Glenpoint) => new(id, DaggerfallNpcKind.Static, $"npc-{id}",
        new DaggerfallNpcSite(region, "Somewhere", "Gate"), new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, faction), role, [], DaggerfallNpcPresence.Active, null, null, null);

    private static DaggerfallConditionCounterplayRecord Stripped(int day, int startedDay = 0) => new DaggerfallConditionCounterplayRecord("cp-1",
        DaggerfallConditionCounterplayKind.LairCleared, TokenId, DaggerfallWorldConditionKind.MonsterSurge, Glenpoint, 1, 0, true, day, day * 1440L, startedDay).Validate();

    private static DaggerfallWorldConditionToken Surge(int startedDay, int severity = 2) =>
        new DaggerfallWorldConditionToken(TokenId, DaggerfallWorldConditionKind.MonsterSurge, Region: Glenpoint, SubjectTag: "orc", Severity: severity, StartedDay: startedDay).Validate();

    [Fact]
    public void A_taken_bounty_is_earned_when_the_player_breaks_its_condition_and_a_guard_of_its_region_pays_it()
    {
        Stage stage = new();
        DaggerfallNpc guard = Npc(1, "guard", 522), commoner = Npc(2, "civilian", 522), courtier = Npc(3, "noble", 521), stranger = Npc(4, "guard", 0, Betony);
        int standing = stage.Social.RegionalReputation(Glenpoint);

        Assert.Equal(2 * DaggerfallTuning.Defaults.Law.BountyRewardPerSeverity, stage.Bounties.Take(Posting(), "the watch of Glenpoint"));
        Assert.Null(stage.Bounties.Take(Posting(), "the watch of Glenpoint"));
        Assert.Empty(stage.Bounties.Claimable(guard));
        Assert.False(stage.Bounties.Claim(guard, "lead:notice:cond:orc").Accepted);

        // Weakening it earns nothing; stripping it does.
        stage.Bounties.ObserveCounterplay(Stripped(stage.Day) with { NewSeverity = 1, Stripped = false, PreviousSeverity = 2 });
        Assert.Empty(stage.Bounties.Claimable(guard));
        stage.Bounties.ObserveCounterplay(Stripped(stage.Day));
        stage.World.Remove(TokenId);
        Assert.True(stage.Notebook.HasNote("bounty-earned:lead:notice:cond:orc"));

        // A guard or a court official of the region pays; the common people and other regions' guards don't.
        Assert.Single(stage.Bounties.Claimable(guard));
        Assert.Single(stage.Bounties.Claimable(courtier));
        Assert.Empty(stage.Bounties.Claimable(commoner));
        Assert.Empty(stage.Bounties.Claimable(stranger));
        Assert.False(stage.Bounties.Claim(commoner, "lead:notice:cond:orc").Accepted);

        DaggerfallNoticeBountyClaim paid = stage.Bounties.Claim(guard, "lead:notice:cond:orc");
        Assert.True(paid.Accepted, paid.Message);
        Assert.Equal(2 * DaggerfallTuning.Defaults.Law.BountyRewardPerSeverity, stage.Paid);
        Assert.Equal(standing + DaggerfallTuning.Defaults.Law.BountyReputation, stage.Social.RegionalReputation(Glenpoint));
        Assert.True(stage.Notebook.HasNote("bounty-paid:lead:notice:cond:orc"));
        Assert.Empty(stage.Bounties.All);
        Assert.False(stage.Bounties.Claim(guard, "lead:notice:cond:orc").Accepted);
    }

    [Fact]
    public void A_bounty_lapses_with_its_condition_or_when_left_unclaimed_and_round_trips_its_save()
    {
        Stage lapsing = new();
        lapsing.Bounties.Take(Posting(), "the watch of Glenpoint");
        lapsing.World.Remove(TokenId);
        Assert.Empty(lapsing.Bounties.Claimable(Npc(1, "guard", 522)));
        Assert.Empty(lapsing.Bounties.All);

        Stage unclaimed = new();
        unclaimed.Bounties.Take(Posting(), "the watch of Glenpoint");
        unclaimed.Bounties.ObserveCounterplay(Stripped(unclaimed.Day));
        DaggerfallNoticeBountiesSave saved = unclaimed.Bounties.Capture();
        DaggerfallNoticeBountySave earned = Assert.Single(saved.Bounties);
        Assert.Equal((DaggerfallNoticeBountyStatus.Earned, (int?)unclaimed.Day), (earned.Status, earned.EarnedDay));

        Stage resumed = new() { Day = unclaimed.Day };
        resumed.Bounties.Restore(saved);
        Assert.Equal(saved.Bounties, resumed.Bounties.Capture().Bounties);
        resumed.Day += DaggerfallTuning.Defaults.Law.BountyClaimDays + 1;
        Assert.Empty(resumed.Bounties.Claimable(Npc(1, "guard", 522)));
        Assert.Empty(resumed.Bounties.All);

        Assert.Throws<ArgumentException>(() => resumed.Bounties.Restore(new DaggerfallNoticeBountiesSave([earned with { EarnedDay = null }])));
    }

    /// <summary>
    /// A board token id recurs when the same condition comes back (R11137-1): a bounty belongs to its occurrence, so the old
    /// one lapses with the first, and the new occurrence is a new bounty to take and be paid, across a save.
    /// </summary>
    [Fact]
    public void A_bounty_belongs_to_its_conditions_occurrence_so_a_recurrence_is_a_new_bounty()
    {
        Stage stage = new();
        Assert.NotNull(stage.Bounties.Take(Posting("lead:notice:cond:" + Surge(0).OccurrenceKey), "the watch of Glenpoint"));

        // The first occurrence ends and the same condition comes back on day 20, under the same token id.
        stage.World.Remove(TokenId);
        stage.Day = 20;
        stage.World.Set(Surge(20));
        Assert.Empty(stage.Bounties.Claimable(Npc(1, "guard", 522)));
        Assert.Empty(stage.Bounties.All);
        Assert.True(stage.Notebook.Notes.Any(note => note.Id.StartsWith("bounty-lapsed:", StringComparison.Ordinal)));

        // Breaking the old occurrence would earn nothing; the new one is taken under its own lead and paid.
        string fresh = "lead:notice:cond:" + Surge(20).OccurrenceKey;
        Assert.NotNull(stage.Bounties.Take(Posting(fresh), "the watch of Glenpoint"));
        Stage resumed = new() { Day = 20 };
        resumed.World.Set(Surge(20));
        resumed.Bounties.Restore(stage.Bounties.Capture());
        resumed.Bounties.ObserveCounterplay(Stripped(20, startedDay: 0));
        Assert.Empty(resumed.Bounties.Claimable(Npc(1, "guard", 522)));
        resumed.Bounties.ObserveCounterplay(Stripped(20, startedDay: 20));
        Assert.True(resumed.Bounties.Claim(Npc(1, "guard", 522), fresh).Accepted);
    }

    [Fact]
    public void A_notice_boards_lead_names_the_conditions_occurrence()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallSiteRecord town = TestPayload.Definitions.Locations.Records.First(site => site.Id.Region == Glenpoint && site.Exterior is not null);
        string Lead() => session.NoticeBoard.BuildForSite(town, session.Calendar.MinuteNumber).Entries
            .Single(entry => entry.ConditionTokenId == TokenId).Lead!.LeadId;

        session.WorldConditions.Set(Surge(0));
        string first = Lead();
        session.WorldConditions.Remove(TokenId);
        session.AdvanceElapsedTime(6 * 86400);
        session.WorldConditions.Set(Surge(5));
        Assert.NotEqual(first, Lead());
        Assert.Equal("lead:notice:cond:" + Surge(5).OccurrenceKey, Lead());
    }

    [Fact]
    public void A_session_saves_the_bounties_it_holds()
    {
        using DaggerfallSession session = FreshSession();
        int day = checked((int)session.Calendar.DayNumber);
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(TokenId, DaggerfallWorldConditionKind.MonsterSurge, Region: Glenpoint, SubjectTag: "orc", Severity: 1).Validate());
        Assert.NotNull(session.NoticeBounties.Take(Posting(), "the watch of Glenpoint"));

        DaggerfallSavePayload decoded = DaggerfallSavePayload.Read(session.CaptureSave());
        DaggerfallNoticeBountySave held = Assert.Single(decoded.NoticeBounties.Bounties);
        Assert.Equal((TokenId, Glenpoint, day), (held.TokenId, held.Region, held.TakenDay));
    }
}
