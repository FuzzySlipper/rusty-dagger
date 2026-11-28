using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Travel;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// #11181: the player takes a side with a ration convoy they come upon. Each side stages its fighters with or against the
/// player; who the player hurts overrides the side; a raid is a crime and costs standing with the convoy's seats; a
/// defence that hurt the raiders earns standing once the convoy comes through, and watching earns nothing.
/// </summary>
public sealed class ConvoyFightTests
{
    private const string Band = "convoy:deal-1:east:3";
    private const long Drover = 101, GuardA = 102, GuardB = 103, Raider = 201, Raider2 = 202;

    [Theory]
    [InlineData("Defend", "arm:102 arm:103 ally:102 ally:103", true)]
    [InlineData("JoinRaiders", "arm:102 arm:103 ally:201 ally:202 foe:102 foe:103", false)]
    [InlineData("Raid", "arm:102 arm:103 foe:102 foe:103", false)]
    [InlineData("Approach", "arm:102 arm:103", true)]
    public void Each_side_stages_the_convoys_guards_and_raiders_with_or_against_the_player(string sideName, string staged, bool lawful)
    {
        DaggerfallConvoySide side = Enum.Parse<DaggerfallConvoySide>(sideName);
        (DaggerfallRationConvoys convoys, Fight fight) = Convoys();
        Assert.True(convoys.Choose(Band, side));

        convoys.Staged(Band, [Drover, GuardA, GuardB], side == DaggerfallConvoySide.Raid || side == DaggerfallConvoySide.Approach ? [] : [Raider, Raider2]);

        Assert.Equal(staged, string.Join(' ', fight.Sides));
        // Joining the raiders or raiding is a raid at once; defending or approaching is not.
        Assert.Equal(lawful ? 0 : 1, fight.Raids);
        Assert.Equal(lawful, convoys.All.Single().Raided == false);
    }

    [Fact]
    public void The_sides_a_convoy_come_upon_offers_are_defend_or_join_under_attack_and_raid_when_quiet()
    {
        (DaggerfallRationConvoys convoys, _) = Convoys();
        Assert.Equal([DaggerfallConvoySide.Defend, DaggerfallConvoySide.JoinRaiders], convoys.Sides(Band, underAttack: true));
        Assert.Equal([DaggerfallConvoySide.Raid], convoys.Sides(Band, underAttack: false));
        Assert.Empty(convoys.Sides("traffic:3:caravan", underAttack: true));
    }

    [Fact]
    public void Striking_the_convoy_raids_it_whatever_the_side_and_turns_its_guards_on_a_defender()
    {
        (DaggerfallRationConvoys convoys, Fight fight) = Convoys();
        convoys.Choose(Band, DaggerfallConvoySide.Defend);
        convoys.Staged(Band, [Drover, GuardA, GuardB], [Raider]);
        fight.Sides.Clear();

        convoys.Struck(Drover);
        convoys.Struck(GuardA);

        Assert.Equal(1, fight.Raids);
        Assert.Equal(["foe:102", "foe:103", "foe:102", "foe:103"], fight.Sides);
        DaggerfallRationConvoySave convoy = convoys.All.Single();
        Assert.True(convoy.Raided && convoy.StruckConvoy);
        // A raider of the convoy earns no defence, though it came through.
        convoys.Struck(Raider);
        Assert.True(convoys.Arrived(Band));
        Assert.Equal(0, fight.Defences);
    }

    [Fact]
    public void Striking_the_raiders_you_joined_turns_them_on_you()
    {
        (DaggerfallRationConvoys convoys, Fight fight) = Convoys();
        convoys.Choose(Band, DaggerfallConvoySide.JoinRaiders);
        convoys.Staged(Band, [Drover, GuardA, GuardB], [Raider, Raider2]);
        fight.Sides.Clear();

        convoys.Struck(Raider2);
        convoys.Struck(Raider2);

        Assert.Equal(["foe:201", "foe:202"], fight.Sides);
        Assert.True(convoys.All.Single().StruckRaiders);
    }

    [Fact]
    public void A_defence_earns_standing_only_for_a_hand_in_the_fight_and_only_if_the_convoy_comes_through()
    {
        // Watching: a defender who struck no raider earns nothing.
        (DaggerfallRationConvoys watched, Fight watching) = Convoys();
        watched.Choose(Band, DaggerfallConvoySide.Defend);
        watched.Staged(Band, [Drover, GuardA, GuardB], [Raider]);
        Assert.True(watched.Arrived(Band));
        Assert.Equal(0, watching.Defences);

        // Fighting: one who struck a raider earns the defence when the convoy comes through.
        (DaggerfallRationConvoys fought, Fight fighting) = Convoys();
        fought.Choose(Band, DaggerfallConvoySide.Defend);
        fought.Staged(Band, [Drover, GuardA, GuardB], [Raider]);
        fought.Struck(Raider);
        Assert.True(fought.Arrived(Band));
        Assert.Equal(1, fighting.Defences);

        // Lost: a convoy that fell earns its defender nothing.
        (DaggerfallRationConvoys lost, Fight losing) = Convoys();
        lost.Choose(Band, DaggerfallConvoySide.Defend);
        lost.Staged(Band, [Drover, GuardA, GuardB], [Raider]);
        lost.Struck(Raider);
        Assert.True(lost.OnBandLost(Band, raidedUnder: null, second: 0));
        Assert.Equal(0, losing.Defences);
    }

    [Fact]
    public void A_raid_is_a_crime_and_costs_standing_with_both_seats_that_their_enemies_welcome_kept_through_a_save()
    {
        using DaggerfallSession session = FreshSession();
        (string bandId, DaggerfallRationConvoySave convoy) = SendConvoy(session);
        DaggerfallBoardSeatBinding giver = Seat(convoy.Giver), receiver = Seat(convoy.Receiver);
        int giverBefore = session.State.Social.FactionReputation(giver.OfficeFactionId);
        int receiverBefore = session.State.Social.FactionReputation(receiver.OfficeFactionId);

        Assert.True(session.RationConvoys.Choose(bandId, DaggerfallConvoySide.Raid));

        DaggerfallCrimeIncidentSave raid = Assert.Single(session.State.Crime.Incidents, incident => incident.OperationId == $"convoy-raid:{bandId}");
        Assert.Equal(DaggerfallCrimeKind.Theft, raid.Crime);
        Assert.Equal(convoy.Riders.Order(), raid.Witnesses.WitnessActorIds.Order());
        int raidCost = DaggerfallTuning.Defaults.Politics.ConvoyRaidStanding;
        Assert.True(session.State.Social.FactionReputation(giver.OfficeFactionId) <= giverBefore - raidCost + 1);
        Assert.True(session.State.Social.FactionReputation(receiver.OfficeFactionId) <= receiverBefore - raidCost + 1);

        // A second strike on its people raids it no further.
        session.RationConvoys.Struck(convoy.Riders[1]);
        Assert.Single(session.State.Crime.Incidents, incident => incident.OperationId == $"convoy-raid:{bandId}");

        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        DaggerfallRationConvoySave kept = Assert.Single(saved.RationConvoys, kept => kept.BandId == bandId);
        Assert.Equal((DaggerfallConvoySide.Raid, true), (kept.Side, kept.Raided));
        Assert.Contains(saved.Crime.Incidents, incident => incident.OperationId == $"convoy-raid:{bandId}");
    }

    [Fact]
    public void A_convoy_defended_and_seen_through_earns_standing_with_both_seats_at_its_sunday()
    {
        using DaggerfallSession session = FreshSession();
        (string bandId, DaggerfallRationConvoySave convoy) = SendConvoy(session);
        DaggerfallBoardSeatBinding giver = Seat(convoy.Giver), receiver = Seat(convoy.Receiver);

        session.RationConvoys.Choose(bandId, DaggerfallConvoySide.Defend);
        session.RationConvoys.Staged(bandId, convoy.Riders, [Raider]);
        session.RationConvoys.Struck(Raider);
        // Met on the map, its band left it; no one aboard died.
        Assert.True(session.Expedition.RemovePersistentBand(bandId));
        int giverBefore = session.State.Social.FactionReputation(giver.OfficeFactionId);
        int receiverBefore = session.State.Social.FactionReputation(receiver.OfficeFactionId);

        session.AdvanceElapsedTime((session.PoliticalBoard!.NextTurnDay * 86400) + 3600 - session.Calendar.ToAbsoluteSeconds());

        int defence = DaggerfallTuning.Defaults.Politics.ConvoyDefenceStanding;
        Assert.True(session.State.Social.FactionReputation(giver.OfficeFactionId) >= giverBefore + defence - 1);
        Assert.True(session.State.Social.FactionReputation(receiver.OfficeFactionId) >= receiverBefore + defence - 1);
        Assert.Empty(session.State.Crime.Incidents.Where(incident => incident.OperationId == $"convoy-raid:{bandId}"));
    }

    [Theory]
    [InlineData("Defend")]
    [InlineData("Raid")]
    public void A_convoy_met_on_the_map_stands_its_guards_beside_a_defender_and_against_a_raider_in_the_scene(string sideName)
    {
        DaggerfallConvoySide side = Enum.Parse<DaggerfallConvoySide>(sideName);
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        (string bandId, DaggerfallRationConvoySave convoy) = SendConvoy(session);
        session.OpenExpedition();
        (double partyX, double partyY) = session.Expedition.Position;

        // The convoy camped beside the party, which comes upon it and is offered a side.
        DaggerfallPersistentOverlandBand band = Assert.Single(session.Expedition.PersistentBands, band => band.BandId == bandId);
        (band.X, band.Y, band.Camp, band.OnRoad) = (partyX + 0.1, partyY, true, false);
        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        session.Expedition.SetSpeed(1);
        _ = session.Expedition.Advance(0.25);
        Assert.Equal(bandId, session.Expedition.BandAhead!.PersistentBandId);
        Assert.Equal([DaggerfallConvoySide.Raid], session.RationConvoys.Sides(bandId, session.Expedition.BandAhead.IsClash));

        Assert.True(session.RationConvoys.Choose(bandId, side));
        session.EnterExpedition();

        foreach (long guard in convoy.Guards)
        {
            Assert.True(session.State.Actors.TryGet(guard, out ActorState? live));
            Assert.Equal(ActorHarmPolicyKind.Combatant, live!.Actor.Get<ActorHarmPolicy>().Policy);
            // A guard fights with a hired road guard's craft, not as the townsperson it was staged as.
            Assert.Equal("warrior", live.Actor.Get<ActorOverlayComponent>().ArchetypeId);
            bool allied = live.Actor.Get<FactionComponent>().TeamOverride == "player-ally";
            bool hostile = live.Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile;
            Assert.Equal(side == DaggerfallConvoySide.Defend ? (true, false) : (false, true), (allied, hostile));
        }
    }

    private static DaggerfallBoardSeatBinding Seat(string seat) =>
        TestPayload.Definitions.BoardSeats.TryGetBySeatId(seat, out DaggerfallBoardSeatBinding? binding) ? binding! : throw new InvalidOperationException(seat);

    /// <summary>Strikes a grain deal and sends its convoy now, as the developer's board.send-convoy does.</summary>
    private static (string BandId, DaggerfallRationConvoySave Convoy) SendConvoy(DaggerfallSession session)
    {
        Board board = session.PoliticalBoard!.Board;
        foreach (Seat giver in board.Seats)
            foreach (Seat receiver in board.Seats.Where(seat => seat != giver))
                if (Trade.Resolve(board, TradeTerm.Parse("grain")!, giver, receiver) is { } grain
                    && Trade.Fits(board, receiver, giver, TradeTerm.Parse("coin:1/4")!, grain))
                {
                    board.AddAgreement(Trade.Offer(board, receiver, giver, TradeTerm.Parse("coin:1/4")!, grain));
                    DaggerfallRationConvoySave convoy = session.PoliticalBoard.SendConvoys().First();
                    return (convoy.BandId, convoy);
                }
        throw new InvalidOperationException("No two seats on the canon board can trade grain.");
    }

    private static (DaggerfallRationConvoys, Fight) Convoys()
    {
        DaggerfallRationConvoys convoys = new(TestPayload.Definitions, new DaggerfallActorLedger(), () => 1UL, null, () => null, _ => null);
        Fight fight = new();
        convoys.BindFight(fight);
        convoys.Restore([new DaggerfallRationConvoySave(Band, "deal-1", "east", "west", new(17, 1), [new(17, 2)], 0, 3, [Drover, GuardA, GuardB])]);
        return (convoys, fight);
    }

    private sealed class Fight : IDaggerfallConvoyFight
    {
        public List<string> Sides { get; } = [];
        public int Raids { get; private set; }
        public int Defences { get; private set; }
        public void Arm(long guardId) => Sides.Add($"arm:{guardId}");
        public void Ally(long actorId) => Sides.Add($"ally:{actorId}");
        public void Foe(long actorId) => Sides.Add($"foe:{actorId}");
        public void Raided(DaggerfallRationConvoySave convoy) => Raids++;
        public void Defended(DaggerfallRationConvoySave convoy) => Defences++;
    }
}
