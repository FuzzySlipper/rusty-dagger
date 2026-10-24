namespace Daggerfall.Import.Tool;

/// <summary>
/// One option a command accepts: a <c>--name VALUE</c> pair, required, optional or staged, or a bare
/// <c>--switch</c>.
/// </summary>
/// <param name="Default">
/// For a staged option, the repository-relative path the import uses for it in its staging tree or records, so
/// a command rerun by hand during importer development reads and writes what <c>pack</c> packs.
/// </param>
internal sealed record CommandOption(string Name, string? Value, bool IsRequired, string? Default = null)
{
    public static CommandOption Required(string name, string value) => new(name, value, true);

    public static CommandOption Optional(string name, string value) => new(name, value, false);

    /// <summary>An option that, when left out, names the import's own path for it in the repository root.</summary>
    public static CommandOption Staged(string name, string value, string defaultPath) => new(name, value, false, defaultPath);

    public static CommandOption Switch(string name) => new(name, null, false);

    public bool IsSwitch => Value is null;

    public string Usage => IsSwitch ? $"[{Name}]"
        : IsRequired ? $"{Name} {Value}"
        : Default is not null ? $"[{Name} {Value} (default {Default})]"
        : $"[{Name} {Value}]";
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
/// A staged option left out takes its default, which is a path in the repository root: outside one it is
/// refused with the usage, rather than read or written somewhere the import never looks.
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

    /// <summary>A required or staged option's value.</summary>
    public string this[string name] => Optional(name) ?? throw new ArgumentException($"{name} is required. {Command.Usage}");

    /// <summary>An option's value, a staged option's default when it is left out, or null.</summary>
    public string? Optional(string name)
    {
        if (values.TryGetValue(name, out string? value)) return value;
        if (Command.Options.FirstOrDefault(option => option.Name == name)?.Default is not { } staged) return null;
        return File.Exists(ImportLayout.Inventory)
            ? staged
            : throw Invalid($"{name} defaults to '{staged}' in the repository root, and '{Directory.GetCurrentDirectory()}' is not one; run from the repository root or pass {name}.");
    }

    /// <summary>Whether the option was supplied, or is staged and so always has a value.</summary>
    public bool Has(string name) => values.ContainsKey(name) || Command.Options.Any(option => option.Name == name && option.Default is not null);

    public bool Switch(string name) => switches.Contains(name);

    /// <summary>A refusal naming what is wrong, followed by the command's usage.</summary>
    public ArgumentException Invalid(string reason) => new($"{reason} {Command.Usage}");
}
