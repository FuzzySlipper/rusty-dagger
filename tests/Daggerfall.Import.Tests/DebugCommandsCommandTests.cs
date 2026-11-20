using System.Text.RegularExpressions;
using Daggerfall.Source.Tool.Commands;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed partial class DebugCommandsCommandTests
{
    [Fact]
    public void The_listing_holds_every_debug_command_the_kit_declares_once_with_its_description()
    {
        // The attributes as written in the Kit's source, against what the tool reads from the compiled assembly.
        string[] declared =
        [
            .. Directory.EnumerateFiles(Path.Combine(TestData.RepositoryRoot, "src/WorldRpg.Kit"), "*.cs", SearchOption.AllDirectories)
                .SelectMany(file => Declaration().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
                .Order(StringComparer.Ordinal),
        ];
        IReadOnlyList<DebugCommandsCommand.Entry> listed = DebugCommandsCommand.Read();

        Assert.NotEmpty(declared);
        Assert.Equal(declared, listed.Select(entry => entry.Name));
        Assert.All(listed, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Description), entry.Name));
        string markdown = DebugCommandsCommand.Markdown(listed);
        Assert.All(listed, entry => Assert.Contains($"`{entry.Name}", markdown));
        Assert.Contains("\n## people\n", markdown);
        Assert.Contains("- `people.stage count metres spread`: ", markdown);
    }

    [GeneratedRegex(@"\[DebugCommand\(""([^""]+)""")]
    private static partial Regex Declaration();
}
