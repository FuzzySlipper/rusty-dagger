using System.Globalization;
using WorldRpg.Politics;

return PoliticsCommand.Run(args, Console.Out, Console.Error);

/// <summary>
/// <c>politics run [--years N] [--seed S] [--format text|jsonl] [--board FILE] [--cards FILE]</c> plays the board and
/// prints each week; <c>politics balance [--seeds N] [--years N] [--board FILE] [--cards FILE]</c> plays seeds 1 to N
/// and reports the table's balance (<see cref="BalanceReport"/>).
/// </summary>
internal static class PoliticsCommand
{
    private const string Usage = "usage: politics run [--years N] [--seed S] [--format text|jsonl] [--board FILE] [--cards FILE]"
        + "\n       politics balance [--seeds N] [--years N] [--board FILE] [--cards FILE]";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] is not ("run" or "balance")) return Fail(error, Usage);
        bool balance = args[0] == "balance";
        int years = balance ? 50 : 1;
        int seeds = 8;
        ulong? seed = null;
        string format = "text";
        string? boardFile = null;
        string? cardsFile = null;
        for (int index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length) return Fail(error, $"{args[index]} needs a value{Environment.NewLine}{Usage}");
            string value = args[index + 1];
            switch (args[index])
            {
                case "--years" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0: years = parsed; break;
                case "--seed" when !balance && ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed): seed = parsed; break;
                case "--seeds" when balance && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0: seeds = parsed; break;
                case "--format" when !balance && value is "text" or "jsonl": format = value; break;
                case "--board": boardFile = value; break;
                case "--cards": cardsFile = value; break;
                default: return Fail(error, $"bad option {args[index]} {value}{Environment.NewLine}{Usage}");
            }
        }

        BoardSetup setup;
        CardSet cards;
        Board board;
        try
        {
            setup = boardFile is null ? BoardSetup.Canon() : BoardSetup.Load(boardFile);
            cards = cardsFile is null ? CardSet.Canon() : CardSet.Load(cardsFile);
            board = Board.Create(setup, cards, seed);
        }
        catch (Exception exception) when (exception is BoardSetupException or IOException or UnauthorizedAccessException)
        {
            return Fail(error, exception.Message);
        }

        if (balance)
        {
            BalanceReport.Write(output, setup, BalanceReport.Run(setup, cards, [.. Enumerable.Range(1, seeds).Select(index => (ulong)index)], years));
            return 0;
        }

        for (int turn = 0; turn < TurnTrace.TurnsIn(years); turn++)
        {
            TurnTrace trace = BoardTurn.Play(board);
            if (format == "jsonl") BoardTraceWriter.WriteJsonLine(output, trace);
            else BoardTraceWriter.WriteText(output, trace);
        }
        return 0;
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine(message);
        return 2;
    }
}
