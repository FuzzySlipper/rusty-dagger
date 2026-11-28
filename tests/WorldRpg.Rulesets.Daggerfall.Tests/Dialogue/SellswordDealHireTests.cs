using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11223: a seat's deal that gives force sends its sellswords out. Orsinium's stand in the taverns of the regions
/// either side of the deal holds, the Fighters Guild's in its halls. They come cheaper by the deal's terms, through the
/// hazard-pay path, and the hire dialogue says why. They are gone when the deal ends, while a hire already made keeps its
/// contract through a save.
/// </summary>
public sealed class SellswordDealHireTests
{
    private const string OrcSpec = "hire-orc-sellsword";

    [Fact]
    public void Orsiniums_deal_sends_orc_sellswords_to_its_partners_taverns_cheaper_until_it_ends()
    {
        using SourceBackedGuildBankSessionFixture fixture = SourceBackedGuildBankSessionFixture.Create();
        DaggerfallSiteProfile tavern = fixture.AdmitProfile(CharingBuilding(15, faction: null));
        using DaggerfallSession town = fixture.Start(tavern);
        Assert.DoesNotContain(town.State.Npcs.All, npc => npc.Role == "orc-sellsword");
        Assert.Contains(town.State.Npcs.All, npc => npc.Role == "road-guard");

        // Daggerfall, which holds Charing's region, pays Orsinium for force: Orsinium's sellswords come to its taverns.
        Agreement deal = Offer(town, "orsinium", "daggerfall", "force:1/2", "coin:1/1");
        using DaggerfallSession dealt = fixture.Restore(town.CaptureSave());
        DaggerfallNpc orc = Assert.Single(dealt.State.Npcs.All, npc => npc.Role == "orc-sellsword" && npc.Presence == DaggerfallNpcPresence.Active);
        Assert.Contains(dealt.State.Npcs.All, npc => npc.Role == "road-guard");
        Assert.True(dealt.PartyHire.IsHireable(orc));

        int region = orc.Site.Region;
        int hazard = dealt.PartyHire.RegionalHazardPercent(region);
        DaggerfallSellswordDeal sent = Assert.IsType<DaggerfallSellswordDeal>(dealt.PartyHire.SellswordDealAt(region, tavern.InteriorBuilding, OrcSpec));
        Assert.Equal(20, dealt.PartyHire.DealPercent(sent));
        DaggerfallPartyHireQuote quote = dealt.PartyHire.QuoteHire(orc, ActorPartyRole.Companion);
        Assert.True(quote.Eligible, quote.RefusalReason);
        Assert.Equal(OrcSpec, quote.SpawnerId);
        Assert.Equal(20 * (hazard - 20) / 100, quote.DailyWage);
        Assert.Equal((ulong)(40 * (hazard - 20) / 100), quote.Deposit);
        Assert.Equal("Orsinium has swords to spare while its deal with Kingdom of Daggerfall stands", quote.Deal);
        Assert.Contains(dealt.PartyHire.TopicsFor(orc), topic => topic.Id == "hire-companion"
            && topic.Label.Contains("Orcish Sellsword", StringComparison.Ordinal) && topic.Label.EndsWith(quote.Deal, StringComparison.Ordinal));

        // While the deal stands the sellsword would drive the player's wagon too.
        GrantCart(dealt);
        dealt.State.Currency.ReceiveGold(500UL);
        Assert.True(dealt.PartyHire.QuoteHire(orc, ActorPartyRole.Driver).Eligible);

        // The deal ends: the sellsword still standing here will sign on for nothing, companion or driver, and the next
        // visit finds none.
        Board board = dealt.PoliticalBoard!.Board;
        board.EndAgreement(board.Agreements.Single(open => open.Id == deal.Id), "test");
        foreach (ActorPartyRole role in new[] { ActorPartyRole.Companion, ActorPartyRole.Driver })
        {
            DaggerfallPartyHireQuote ended = dealt.PartyHire.QuoteHire(orc, role);
            Assert.False(ended.Eligible);
            Assert.Equal(DaggerfallPartyHireService.DealEndedReason, ended.RefusalReason);
        }
        IReadOnlyList<DaggerfallDialogueTopicOption> endedTopics = dealt.PartyHire.TopicsFor(orc);
        Assert.Contains(endedTopics, topic => topic.Id == "hire-companion" && topic.Label.Contains(DaggerfallPartyHireService.DealEndedReason, StringComparison.Ordinal));
        Assert.Contains(endedTopics, topic => topic.Id == "hire-driver" && topic.Label.Contains(DaggerfallPartyHireService.DealEndedReason, StringComparison.Ordinal));
        ulong gold = dealt.State.Currency.Read().Gold;
        Assert.False(dealt.PartyHire.Resolve(orc, DaggerfallDialogueTopic.HireCompanion).Accepted);
        Assert.False(dealt.PartyHire.Resolve(orc, DaggerfallDialogueTopic.HireDriver).Accepted);
        Assert.Equal(gold, dealt.State.Currency.Read().Gold);
        Assert.False(dealt.PartyHire.TryGetStagedHiredRole(orc.DurableId, out _));
        using DaggerfallSession after = fixture.Restore(dealt.CaptureSave());
        Assert.DoesNotContain(after.State.Npcs.All, npc => npc.Role == "orc-sellsword");
        Assert.Contains(after.State.Npcs.All, npc => npc.Role == "road-guard");
    }

    [Fact]
    public void The_fighters_guilds_deal_sends_mercenaries_to_its_halls_and_a_hire_keeps_its_terms_after_the_deal_through_a_save()
    {
        using SourceBackedGuildBankSessionFixture fixture = SourceBackedGuildBankSessionFixture.Create();
        DaggerfallSiteProfile hall = fixture.AdmitProfile(CharingBuilding(11, faction: 41));
        using DaggerfallSession start = fixture.Start(hall);
        Assert.DoesNotContain(start.State.Npcs.All, npc => npc.Role == "mercenary");

        // The guild hires out its swords to Sentinel: they stand in every hall of its own, even in a region neither holds.
        Agreement deal = Offer(start, "fighters-guild", "sentinel", "force:1/4", "coin:1/2");
        using DaggerfallSession dealt = fixture.Restore(start.CaptureSave());
        dealt.State.Currency.ReceiveGold(500UL);
        DaggerfallNpc mercenary = Assert.Single(dealt.State.Npcs.All, npc => npc.Role == "mercenary" && npc.Presence == DaggerfallNpcPresence.Active);
        DaggerfallPartyHireQuote quote = dealt.PartyHire.QuoteHire(mercenary, ActorPartyRole.Companion);
        int hazard = dealt.PartyHire.RegionalHazardPercent(mercenary.Site.Region);
        Assert.True(quote.Eligible, quote.RefusalReason);
        Assert.Equal(DaggerfallPartyHireService.MercenarySpecId, quote.SpawnerId);
        Assert.Equal(18 * (hazard - 10) / 100, quote.DailyWage);
        Assert.Equal("The Fighters Guild has swords to spare while its deal with Kingdom of Sentinel stands", quote.Deal);

        DaggerfallPartyHireResult hired = dealt.PartyHire.Resolve(mercenary, DaggerfallDialogueTopic.HireCompanion);
        Assert.True(hired.Accepted, hired.Message);
        Assert.Contains(quote.Deal, hired.Message, StringComparison.Ordinal);
        Assert.True(dealt.PartyHire.TryGetContract(mercenary.DurableId, out ActorPartyContractDiff contract));
        Assert.Equal(quote.DailyWage, contract.DailyWage);

        // The deal ends; the hire made under it keeps its wage, across a save.
        Board board = dealt.PoliticalBoard!.Board;
        board.EndAgreement(board.Agreements.Single(open => open.Id == deal.Id), "test");
        using DaggerfallSession restored = fixture.Restore(dealt.CaptureSave());
        Assert.Empty(restored.PoliticalBoard!.SellswordDeals());
        Assert.True(restored.PartyHire.TryGetContract(mercenary.DurableId, out ActorPartyContractDiff kept));
        Assert.Equal(quote.DailyWage, kept.DailyWage);
        Assert.Equal(DaggerfallPartyHireService.MercenarySpecId, kept.SpawnerId);
        Assert.True(restored.PartyHire.TryGetStagedHiredRole(mercenary.DurableId, out ActorPartyRole role));
        Assert.Equal(ActorPartyRole.Companion, role);
    }

    [Fact]
    public void Only_a_seat_that_keeps_sellswords_sends_them_and_only_while_it_gives_force()
    {
        using SourceBackedGuildBankSessionFixture fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallPoliticalBoard political = session.PoliticalBoard!;
        Assert.Empty(political.SellswordDeals());

        Offer(session, "wayrest", "daggerfall", "force:1/4", "coin:1/2");
        Offer(session, "orsinium", "wayrest", "coin:1/2", "force:1/4");
        Assert.Empty(political.SellswordDeals());

        Offer(session, "daggerfall", "orsinium", "coin:1/1", "force:1/2");
        DaggerfallSellswordDeal sent = Assert.Single(political.SellswordDeals());
        Assert.Equal(("orsinium", "Kingdom of Daggerfall", OrcSpec, 1, 2), (sent.Seat, sent.PartnerName, sent.Spec, sent.Amount, sent.EveryWeeks));
        Assert.Equal(358, sent.OfficeFaction);
        Assert.Contains(17, sent.Regions);
        Assert.Contains(26, sent.Regions);
    }

    [Fact]
    public void A_deals_sellswords_stand_where_their_specs_venue_says()
    {
        using SourceBackedGuildBankSessionFixture fixture = SourceBackedGuildBankSessionFixture.Create();
        DaggerfallInteriorBuilding tavern = fixture.AdmitProfile(CharingBuilding(15, faction: null)).InteriorBuilding!;
        DaggerfallInteriorBuilding hall = fixture.AdmitProfile(CharingBuilding(11, faction: 41)).InteriorBuilding!;
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        Offer(session, "fighters-guild", "daggerfall", "force:1/2", "coin:1/1");
        Offer(session, "orsinium", "daggerfall", "force:1/2", "coin:1/1");

        // The guild's Mercenary Scouts are a tavern-or-guild hire: in its halls and in its partner's taverns.
        // Orsinium's sellswords are a tavern hire: in the taverns only, not in a hall of another seat's.
        Assert.Equal(["hire-mercenary", OrcSpec], session.PartyHire.SellswordSpecsAt(Charing.Region, tavern).Order(StringComparer.Ordinal));
        Assert.Equal(["hire-mercenary"], session.PartyHire.SellswordSpecsAt(Charing.Region, hall));
        Assert.Empty(session.PartyHire.SellswordSpecsAt(20, tavern)); // Sentinel, which neither side holds
    }

    private static void GrantCart(DaggerfallSession session)
    {
        DurableIdentityReference cart = new(DurableIdentityKind.Item, 900_011);
        DaggerfallItemDefinition definition = TestPayload.Definitions.RequireItem(new DaggerfallItemId(DaggerfallTransportPolicy.CartItemId));
        session.State.Inventory.GrantAtomic([new InventoryAtomicGrant(new InventoryItemId(DaggerfallTransportPolicy.CartItemId), UniqueItem: cart)]);
        session.State.ItemInstances.RegisterUnique(cart.Value, DaggerfallItemInstanceMetadata.Default(definition, DaggerfallItemOwner.Player));
    }

    /// <summary>Puts a deal in force on the board at once: <paramref name="proposer"/> gives <paramref name="gives"/> for <paramref name="asks"/>.</summary>
    private static Agreement Offer(DaggerfallSession session, string proposer, string partner, string gives, string asks)
    {
        Board board = session.PoliticalBoard!.Board;
        board.AddAgreement(Trade.Offer(board, board.Seat(proposer), board.Seat(partner), TradeTerm.Parse(gives)!, TradeTerm.Parse(asks)!));
        return board.Agreements[^1];
    }

    /// <summary>The interior of Charing's first building of <paramref name="type"/> (and office faction <paramref name="faction"/>).</summary>
    private static DaggerfallWorldProfileKey CharingBuilding(int type, int? faction)
    {
        DaggerfallSiteRecord charing = TestPayload.Definitions.Locations.Records.Single(record => record.Id == Charing);
        DaggerfallSiteBuildingSource building = charing.Exterior!.Buildings.Values
            .Where(value => value.Source.BuildingType == type && (faction is null || value.Source.FactionId == faction))
            .OrderBy(value => value.Id.BlockY).ThenBy(value => value.Id.BlockX).ThenBy(value => value.Id.Index)
            .First();
        return DaggerfallWorldProfileIds.Interior(Charing, building.Id);
    }
}
