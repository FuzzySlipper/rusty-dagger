using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class WorldDeckTests
{
    private static Board WithWorld(params WorldCard[] world) =>
        Board.Create(TestBoards.TwoCrowns(), TestBoards.Cards with { Cards = [.. TestBoards.Cards.Cards.Where(card => card.Seat is null)], World = world });

    [Fact]
    public void Each_sunday_the_world_turns_a_card_and_lays_its_condition_with_no_seat_behind_it()
    {
        Board board = WithWorld(new WorldCard("famine", "Famine", ConditionKind.Famine, 2, 8, WorldTarget.Poorest, Actor: "a-minor-court"));

        TurnTrace turn = BoardTurn.Play(board);

        // The march, at prosperity 2, is the poorest region.
        Assert.Equal(("famine", "march", "a-minor-court"), (turn.World!.Card, turn.World.Region, turn.World.Actor));
        Assert.Equal([new BoardChange("march", "condition:famine", "none", "2 by world until 9")], turn.World.Changes);
        Assert.Equal(new RegionCondition(ConditionKind.Famine, null, 2, null, 1, 9), Assert.Single(board.Region("march").Conditions));
    }

    [Fact]
    public void A_spent_world_deck_reshuffles_and_a_table_without_one_turns_nothing()
    {
        Board board = WithWorld(
            new WorldCard("festival", "Festival", ConditionKind.Festival, 1, 1, WorldTarget.Richest),
            new WorldCard("unrest", "Unrest", ConditionKind.Unrest, 1, 1, WorldTarget.Unruliest));

        string[] turned = [.. Enumerable.Range(0, 6).Select(_ => BoardTurn.Play(board).World!.Card)];

        Assert.All(turned.Chunk(2), pair => Assert.Equal(["festival", "unrest"], pair.Order(StringComparer.Ordinal)));
        Assert.Null(BoardTurn.Play(TestBoards.Create(TestBoards.TwoCrowns())).World);
    }

    [Fact]
    public void The_same_seed_turns_the_same_world_and_another_seed_another()
    {
        static string[] World(ulong? seed)
        {
            Board board = Board.Create(BoardSetup.Canon(), CardSet.Canon(), seed);
            return [.. Enumerable.Range(0, 30).Select(_ => BoardTurn.Play(board).World is { } world ? $"{world.Card}@{world.Region}" : "-")];
        }

        Assert.Equal(World(null), World(null));
        Assert.NotEqual(World(null), World(5));
    }

    [Fact]
    public void A_world_card_set_names_every_problem_it_has()
    {
        CardSet cards = TestBoards.Cards with
        {
            World =
            [
                new WorldCard("storm", "Storm", ConditionKind.PriceSurge, 4, 0, WorldTarget.AnyRegion, Copies: 0),
                new WorldCard("storm", "Storm", ConditionKind.Festival, 1, 2, WorldTarget.AnyRegion, Detail: "grain"),
                new WorldCard("purge", "Purge", ConditionKind.Persecution, 1, 2, WorldTarget.AnyRegion, Detail: "nobody"),
            ],
        };

        IReadOnlyList<string> problems = cards.Problems(TestBoards.TwoCrowns().WithGuild());

        Assert.Contains("world card 'storm' is listed twice", problems);
        Assert.Contains("world card 'storm' magnitude 4 is outside 1..3", problems);
        Assert.Contains("world card 'storm' lasts no weeks", problems);
        Assert.Contains("world card 'storm' has 0 copies", problems);
        Assert.Contains("world card 'storm': a PriceSurge names no detail", problems);
        Assert.Contains("world card 'storm': a Festival names a detail it does not use", problems);
        Assert.Contains("world card 'purge' persecutes unknown seat 'nobody'", problems);
    }
}
