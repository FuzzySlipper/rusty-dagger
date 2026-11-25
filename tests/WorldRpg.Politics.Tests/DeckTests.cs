using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class DeckTests
{
    [Fact]
    public void A_seat_draws_from_its_families_and_its_own_signature_cards()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns().WithGuild());

        IEnumerable<string> Cards(string seat)
        {
            SeatDeck deck = board.Deck(board.Seat(seat));
            return deck.Hand.Concat(deck.DrawPile).Concat(deck.Discard).Select(card => card.Id).Order(StringComparer.Ordinal);
        }

        Assert.Equal(["extort", "guild-secret", "suborn"], Cards("guild"));
        Assert.DoesNotContain("guild-secret", Cards("west"));
        Assert.DoesNotContain("suborn", Cards("west"));
        Assert.Contains("garrison", Cards("west"));
        Assert.All(board.Seats, seat => Assert.Equal(Board.HandSize, board.Deck(seat).Hand.Count));
    }

    [Fact]
    public void A_played_card_is_discarded_and_an_empty_draw_pile_takes_the_discards_back()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns().WithGuild());
        Seat guild = board.Seat("guild");
        SeatDeck deck = board.Deck(guild);
        // The guild's three cards are all in its hand: it draws nothing more.
        Assert.Empty(deck.DrawPile);
        board.DrawHand(guild);
        Assert.Equal(Board.HandSize, deck.Hand.Count);

        Card played = deck.Hand[0];
        board.Discard(guild, played);
        Assert.Equal([played], deck.Discard);

        board.DrawHand(guild);
        Assert.Contains(played, deck.Hand);
        Assert.Empty(deck.Discard);
        Assert.Empty(deck.DrawPile);
    }

    [Fact]
    public void Gathering_keeps_the_cards_a_seat_is_saving_for_and_gains_what_they_lack()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns().WithGuild());
        Seat guild = board.Seat("guild");
        board.Hold("guild", "guild-secret", "extort", "suborn");

        // Guild Secret costs 8 force the guild lacks; the cards it can pay for and passed over go to the discard.
        Gathering.Gather(board, guild);

        Assert.Equal(["guild-secret"], board.Deck(guild).Hand.Select(card => card.Id));
        Assert.Equal(["extort", "suborn"], board.Deck(guild).Discard.Select(card => card.Id).Order(StringComparer.Ordinal));
        Assert.Equal(5, guild.Force);
    }

    [Fact]
    public void Gathering_with_nothing_to_save_for_adds_to_the_scarcest_resource_and_sets_aside_dead_cards()
    {
        Board board = TestBoards.Create(TestBoards.TwoCrowns().WithGuild());
        Seat west = board.Seat("west");
        board.AdjustResource(west, Resource.Favour, -3);
        // No region is open to annexing, so Annex has nothing West could play it on.
        board.Hold("west", "annex");

        Gathering.Gather(board, west);

        Assert.Equal(2, west.Favour);
        Assert.Empty(board.Deck(west).Hand);
    }

    [Fact]
    public void Every_seat_is_dealt_the_same_hand_from_the_same_seed()
    {
        static string[] Hands(ulong? seed)
        {
            Board board = Board.Create(BoardSetup.Canon(), CardSet.Canon(), seed);
            return [.. board.Seats.SelectMany(seat => board.Deck(seat).Hand.Select(card => $"{seat.Id}:{card.Id}"))];
        }

        Assert.Equal(Hands(null), Hands(null));
        Assert.NotEqual(Hands(null), Hands(3));
    }
}
