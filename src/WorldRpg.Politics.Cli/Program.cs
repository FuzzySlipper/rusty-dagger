using System.Globalization;
using WorldRpg.Politics;

return PoliticsCommand.Run(args, Console.Out, Console.Error);

/// <summary><c>politics run [--years N] [--seed S] [--format text|jsonl] [--board FILE]</c>: plays the board and prints each week.</summary>
internal static class PoliticsCommand
{
    private const string Usage = "usage: politics run [--years N] [--seed S] [--format text|jsonl] [--board FILE]";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] != "run") return Fail(error, Usage);
        int years = 1;
        ulong? seed = null;
        string format = "text";
        string? boardFile = null;
        for (int index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length) return Fail(error, $"{args[index]} needs a value{Environment.NewLine}{Usage}");
            string value = args[index + 1];
            switch (args[index])
            {
                case "--years" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0: years = parsed; break;
                case "--seed" when ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed): seed = parsed; break;
                case "--format" when value is "text" or "jsonl": format = value; break;
                case "--board": boardFile = value; break;
                default: return Fail(error, $"bad option {args[index]} {value}{Environment.NewLine}{Usage}");
            }
        }

        BoardSetup setup;
        try
        {
            setup = boardFile is null ? BoardSetup.Canon() : BoardSetup.Load(boardFile);
        }
        catch (Exception exception) when (exception is BoardSetupException or IOException or UnauthorizedAccessException)
        {
            return Fail(error, exception.Message);
        }

        Board board = Board.Create(setup, seed);
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
