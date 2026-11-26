using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace WorldRpg.Politics.Tests;

public sealed class DeterminismTests
{
    [Fact]
    public void The_same_seed_plays_the_same_game_and_another_seed_another()
    {
        Assert.Equal(Trace(seed: null, years: 5), Trace(seed: null, years: 5));
        Assert.NotEqual(Trace(seed: null, years: 5), Trace(seed: 1, years: 5));
    }

    /// <summary>
    /// The canon game's first twenty years, byte for byte. A change to the rules or the canon start changes this
    /// on purpose, and the new hash goes in with it; a change that was meant to leave the game alone, or a machine
    /// or runtime that plays it differently, fails here.
    /// </summary>
    [Fact]
    public void The_canon_game_is_pinned()
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Trace(seed: null, years: 20))));

        Assert.Equal("11f89286a079661b7adea3783dd457fde416e635bb6ae5044148f188532e461d", hash);
    }

    [Fact]
    public void A_year_is_fifty_two_weekly_turns_rounded_up_and_turns_carry_their_date()
    {
        Assert.Equal(52, TurnTrace.TurnsIn(1));
        Assert.Equal(515, TurnTrace.TurnsIn(10));
        TurnTrace turn = new(53, [], null, [], []);
        Assert.Equal(2, turn.Year);
        Assert.Equal(5, turn.Day);
    }

    private static string Trace(ulong? seed, int years)
    {
        Board board = Board.Create(BoardSetup.Canon(), CardSet.Canon(), seed);
        using StringWriter writer = new();
        for (int turn = 0; turn < TurnTrace.TurnsIn(years); turn++) BoardTraceWriter.WriteJsonLine(writer, BoardTurn.Play(board));
        return writer.ToString();
    }
}
