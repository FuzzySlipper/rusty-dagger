using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class CardSetTests
{
    [Fact]
    public void The_canon_cards_give_every_family_cards_and_every_power_a_way_onto_the_board()
    {
        CardSet cards = CardSet.Canon();
        BoardSetup canon = BoardSetup.Canon();

        Assert.Empty(cards.Problems(canon));
        Assert.All(Enum.GetValues<CardFamily>(), family => Assert.Contains(cards.Cards, card => card.Family == family && card.Seat is null));
        Assert.Contains(cards.Cards, card => card.Visibility == Visibility.Secret);
        Assert.Contains(cards.Cards, card => card.Weeks > 0);
        Assert.All(canon.Seats.Where(seat => seat.Kind == SeatKind.Power), seat => Assert.Contains(cards.Deck(seat), card =>
            card.Effects.Any(effect => effect.Kind is CardEffectKind.Influence or CardEffectKind.ChallengeOffice)));
        // Every effect kind is played by some canon card.
        Assert.All(Enum.GetValues<CardEffectKind>(), kind => Assert.Contains(cards.Cards, card => card.Effects.Any(effect => effect.Kind == kind)));
    }

    [Fact]
    public void A_card_set_names_every_problem_it_has()
    {
        CardSet cards = new(
        [
            TestBoards.Card("garrison"),
            TestBoards.Card("garrison") with { Force = 13, Weeks = -1, Copies = 0 },
            new("nothing", "Nothing", CardFamily.Court, 0, 0, 0, CardTarget.None, Visibility.Public, []),
            new("loose", "Loose", CardFamily.Trade, 0, 0, 0, CardTarget.None, Visibility.Public,
                [new(CardEffectKind.Gain, 1), new(CardEffectKind.Control, 1), new(CardEffectKind.Take, 1, Resource.Coin)]),
            new("misfit", "Misfit", CardFamily.Shadow, 0, 0, 0, CardTarget.OtherSeat, Visibility.Secret,
                [new(CardEffectKind.ChallengeOffice, 1), new(CardEffectKind.MoveInfluence, 1), new(CardEffectKind.Annex, Resource: Resource.Coin)]),
            new("haggle", "Haggle", CardFamily.Trade, 0, 0, 0, CardTarget.OtherSeat, Visibility.Public, [new(CardEffectKind.Deal, Gives: ["coin:1/2"], Asks: ["grain"])]),
            new("dole", "Dole", CardFamily.Trade, 0, 0, 0, CardTarget.Deal, Visibility.Public, [new(CardEffectKind.Gain, 1, Resource.Coin)]),
            new("promise", "Promise", CardFamily.Trade, 0, 0, 0, CardTarget.Deal, Visibility.Public, [new(CardEffectKind.Deal, Gives: ["grain:granary"], Asks: ["coin:1/2"])]),
            new("slow-pay", "Slow Pay", CardFamily.Trade, 0, 0, 0, CardTarget.Deal, Visibility.Public, [new(CardEffectKind.Deal, Gives: ["grain"], Asks: ["coin:1/9"])]),
            new("empty", "Empty", CardFamily.Trade, 0, 0, 0, CardTarget.Deal, Visibility.Public, [new(CardEffectKind.Deal, Gives: [], Asks: ["coin:1/2"])]),
            new("orphan", "Orphan", CardFamily.Force, 0, 0, 0, CardTarget.None, Visibility.Public, [new(CardEffectKind.Gain, 1, Resource.Force)], Seat: "nobody"),
        ]);

        IReadOnlyList<string> problems = cards.Problems(TestBoards.TwoCrowns().WithGuild());

        Assert.Contains("card 'garrison' is listed twice", problems);
        Assert.Contains("card 'garrison' costs 13 force, outside 0..12", problems);
        Assert.Contains("card 'garrison' takes -1 weeks, outside 0..52", problems);
        Assert.Contains("card 'garrison' has 0 copies", problems);
        Assert.Contains("card 'nothing' does nothing", problems);
        Assert.Contains("card 'loose' Gain: names no resource", problems);
        Assert.Contains("card 'loose' Control: needs a region target", problems);
        Assert.Contains("card 'loose' Take: needs a seat target", problems);
        Assert.Contains("card 'misfit' ChallengeOffice: names no office", problems);
        Assert.Contains("card 'misfit' MoveInfluence: needs a region pair", problems);
        Assert.Contains("card 'misfit' Annex: names a resource it does not use", problems);
        Assert.Contains("card 'haggle' Deal: needs a deal target", problems);
        Assert.Contains("card 'dole' Gain: cannot be played on a deal", problems);
        Assert.Contains("card 'promise' Deal: term 'grain:granary' is not grain or resource:amount/weeks", problems);
        Assert.Contains("card 'slow-pay' Deal: term 'coin:1/9' falls due outside every 1..8 weeks", problems);
        Assert.Contains("card 'empty' Deal: offers an empty menu of terms", problems);
        Assert.Contains("card 'orphan' is the signature of unknown seat 'nobody'", problems);
        // The guild draws only shadow cards and its own: here, just Misfit.
        Assert.Contains("seat 'guild' draws from 1 cards, fewer than a hand of 3", problems);
    }

    [Fact]
    public void A_card_file_with_a_missing_or_unknown_field_is_refused_rather_than_defaulted()
    {
        string directory = Directory.CreateTempSubdirectory("politics-").FullName;
        try
        {
            string missing = Path.Combine(directory, "missing.json");
            File.WriteAllText(missing, """{ "cards": [ { "id": "a", "name": "A", "family": "Trade", "coin": 0, "favour": 0, "force": 0, "target": "None", "effects": [ { "kind": "Gain", "amount": 1, "resource": "Coin" } ] } ] }""");
            string unknown = Path.Combine(directory, "unknown.json");
            File.WriteAllText(unknown, """{ "cards": [ { "id": "a", "name": "A", "family": "Trade", "coin": 0, "favour": 0, "force": 0, "target": "None", "visibility": "Public", "effects": [ { "kind": "Gain", "amount": 1, "resource": "Coin" } ], "rarity": 3 } ] }""");

            Assert.Contains("visibility", Assert.Throws<BoardSetupException>(() => CardSet.Load(missing)).Message, StringComparison.Ordinal);
            Assert.Contains("rarity", Assert.Throws<BoardSetupException>(() => CardSet.Load(unknown)).Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
