using System.Reflection;
using System.Text;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;

namespace Daggerfall.Source.Tool.Commands;

/// <summary>
/// Prints the developer commands, read from the compiled <see cref="DebugCommandAttribute"/>s in the Kit, with their
/// parameters and descriptions, grouped by prefix, as Markdown to the terminal or to <c>--out</c> (an ignored file, such
/// as <c>local/debug-commands.md</c>). It needs no running host, and nothing it writes is committed: the attributes are
/// the reference. A <c>--live-debug</c> host serves the same commands, beside the Engine's own, in the developer
/// shell's Commands panel.
/// </summary>
internal static class DebugCommandsCommand
{
    public static ToolCommand Command { get; } = new("debug-commands", [CommandOption.Optional("--out", "FILE")], Run, Program.Tool);

    /// <summary>One command as its attribute declares it, with the parameter names the console takes in order.</summary>
    internal sealed record Entry(string Name, string Description, IReadOnlyList<string> Parameters)
    {
        public string Group => Name.Split('.')[0];
    }

    /// <summary>Every <see cref="DebugCommandAttribute"/> on a Kit module, in name order.</summary>
    internal static IReadOnlyList<Entry> Read() =>
    [
        .. typeof(IDeveloperPeopleCommands).Assembly.GetTypes()
            .Where(type => typeof(IDebugCommandModule).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(method => (method, attribute: method.GetCustomAttribute<DebugCommandAttribute>()))
            .Where(found => found.attribute is not null)
            .Select(found => new Entry(found.attribute!.Name, found.attribute.Description ?? string.Empty,
                [.. found.method.GetParameters().Select(parameter => parameter.Name!)]))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal),
    ];

    /// <summary>The listing as Markdown: a heading for each prefix, then each command's usage and description.</summary>
    internal static string Markdown(IReadOnlyList<Entry> entries)
    {
        StringBuilder text = new("# Developer commands\n\n");
        text.Append($"{entries.Count} commands from the Kit's [DebugCommand] attributes; the Engine's own are listed by a live host's `help` and the developer shell's Commands panel.\n");
        foreach (IGrouping<string, Entry> group in entries.GroupBy(entry => entry.Group))
        {
            text.Append($"\n## {group.Key}\n\n");
            foreach (Entry entry in group)
                text.Append($"- `{string.Join(' ', [entry.Name, .. entry.Parameters])}`: {entry.Description}\n");
        }
        return text.ToString();
    }

    private static int Run(CommandArguments args)
    {
        string text = Markdown(Read());
        if (args.Optional("--out") is not { } path)
        {
            Console.Write(text);
            return 0;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text);
        Console.WriteLine($"debug-commands: wrote {path}");
        return 0;
    }
}
