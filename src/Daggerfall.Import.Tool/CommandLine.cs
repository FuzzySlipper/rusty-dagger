namespace Daggerfall.Import.Tool;

/// <summary>One option a command accepts: a <c>--name VALUE</c> pair, required or optional, or a bare <c>--switch</c>.</summary>
internal sealed record CommandOption(string Name, string? Value, bool IsRequired)
{
    public static CommandOption Required(string name, string value) => new(name, value, true);

    public static CommandOption Optional(string name, string value) => new(name, value, false);

    public static CommandOption Switch(string name) => new(name, null, false);

    public bool IsSwitch => Value is null;

    public string Usage => IsSwitch ? $"[{Name}]" : IsRequired ? $"{Name} {Value}" : $"[{Name} {Value}]";
}

/// <summary>A tool command: its verb, the options it accepts and what it runs.</summary>
internal sealed record ToolCommand(string Name, IReadOnlyList<CommandOption> Options, Func<CommandArguments, int> Run)
{
    public string Usage => $"usage: daggerfall-import-tool {Name} {string.Join(' ', Options.Select(option => option.Usage))}";

    public int Invoke(IReadOnlyList<string> args) => Run(CommandArguments.Parse(args, this));
}

/// <summary>
/// The one option grammar every command shares: <c>--name value</c> pairs and bare switches, in any order.
/// An unknown, repeated or valueless option, or a missing required one, is refused with the command's usage.
/// </summary>
internal sealed class CommandArguments
{
    private readonly Dictionary<string, string> values;
    private readonly HashSet<string> switches;

    private CommandArguments(ToolCommand command, IReadOnlyList<string> raw, Dictionary<string, string> values, HashSet<string> switches)
    {
        Command = command;
        Raw = raw;
        this.values = values;
        this.switches = switches;
    }

    public ToolCommand Command { get; }

    /// <summary>The arguments as the caller spelled them, verb first.</summary>
    public IReadOnlyList<string> Raw { get; }

    public static CommandArguments Parse(IReadOnlyList<string> args, ToolCommand command)
    {
        Dictionary<string, CommandOption> options = command.Options.ToDictionary(option => option.Name, StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        HashSet<string> switches = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            if (!options.TryGetValue(args[index], out CommandOption? option)) throw new ArgumentException(command.Usage);
            if (option.IsSwitch)
            {
                if (!switches.Add(option.Name)) throw new ArgumentException(command.Usage);
                continue;
            }

            if (index + 1 >= args.Count || string.IsNullOrEmpty(args[index + 1])
                || args[index + 1].StartsWith("--", StringComparison.Ordinal)
                || !values.TryAdd(option.Name, args[++index])) throw new ArgumentException(command.Usage);
        }

        if (command.Options.Any(option => option.IsRequired && !values.ContainsKey(option.Name))) throw new ArgumentException(command.Usage);
        return new(command, args, values, switches);
    }

    /// <summary>A required option's value.</summary>
    public string this[string name] => values.TryGetValue(name, out string? value)
        ? value
        : throw new ArgumentException($"{name} is required. {Command.Usage}");

    public string? Optional(string name) => values.GetValueOrDefault(name);

    public bool Has(string name) => values.ContainsKey(name);

    public bool Switch(string name) => switches.Contains(name);

    /// <summary>A refusal naming what is wrong, followed by the command's usage.</summary>
    public ArgumentException Invalid(string reason) => new($"{reason} {Command.Usage}");
}
