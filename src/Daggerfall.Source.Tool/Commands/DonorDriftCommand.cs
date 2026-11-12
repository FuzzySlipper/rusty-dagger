using System.Diagnostics;
using System.Text.Json.Nodes;
using Daggerfall.Source.Tool.Donor;

namespace Daggerfall.Source.Tool.Commands;

/// <summary>
/// Reports where our tables taken from Daggerfall Unity differ from what a Daggerfall Unity checkout says. Each table
/// is read again from the donor's own files with the parsers that first took it, and compared value by value; the
/// commit a table names is where it was taken from, not a value, so it is not compared. Nothing else reads the donor:
/// the import reads only our tables, and this command is not one of its inputs.
/// </summary>
internal static class DonorDriftCommand
{
    private const int ShownPerTable = 20;

    public static ToolCommand Command { get; } = new("donor-drift",
        [CommandOption.Required("--donor", "DAGGERFALL_UNITY_DIR"), CommandOption.Staged("--repository", "DIR", ".")], Run, Program.Tool);

    private static int Run(CommandArguments args)
    {
        string donor = args["--donor"];
        string repository = args["--repository"];
        Console.WriteLine($"donor-drift: {donor} at {Head(donor) ?? "an unknown commit (not a Git checkout)"}");
        int differing = 0;
        foreach (DonorTake take in DonorTakes.All)
        {
            IReadOnlyList<string> differences = Differences(take.Ours(repository), take.Donor(donor));
            if (differences.Count == 0)
            {
                Console.WriteLine($"same: {take.Label}");
                continue;
            }

            differing++;
            Console.WriteLine($"differs: {take.Label} ({differences.Count} value{(differences.Count == 1 ? "" : "s")})");
            foreach (string difference in differences.Take(ShownPerTable)) Console.WriteLine($"  {difference}");
            if (differences.Count > ShownPerTable) Console.WriteLine($"  … and {differences.Count - ShownPerTable} more");
        }

        Console.WriteLine(differing == 0 ? "donor-drift: no difference" : $"donor-drift: {differing} of {DonorTakes.All.Count} tables differ");
        return differing == 0 ? 0 : 1;
    }

    /// <summary>Every value path where the two documents disagree, ours first; a table's <c>commit</c> is skipped.</summary>
    internal static IReadOnlyList<string> Differences(JsonNode? ours, JsonNode? donor)
    {
        List<string> found = [];
        Compare(ours, donor, "$", found);
        return found;
    }

    private static void Compare(JsonNode? ours, JsonNode? donor, string path, List<string> found)
    {
        if (ours is JsonObject ourObject && donor is JsonObject donorObject)
        {
            foreach (string key in ourObject.Select(pair => pair.Key).Union(donorObject.Select(pair => pair.Key), StringComparer.Ordinal))
            {
                if (key == "commit") continue;
                if (!ourObject.ContainsKey(key)) found.Add($"{path}.{key}: only the donor has it: {Shown(donorObject[key])}");
                else if (!donorObject.ContainsKey(key)) found.Add($"{path}.{key}: only ours has it: {Shown(ourObject[key])}");
                else Compare(ourObject[key], donorObject[key], $"{path}.{key}", found);
            }

            return;
        }

        if (ours is JsonArray ourArray && donor is JsonArray donorArray)
        {
            for (int index = 0; index < Math.Max(ourArray.Count, donorArray.Count); index++)
            {
                if (index >= ourArray.Count) found.Add($"{path}[{index}]: only the donor has it: {Shown(donorArray[index])}");
                else if (index >= donorArray.Count) found.Add($"{path}[{index}]: only ours has it: {Shown(ourArray[index])}");
                else Compare(ourArray[index], donorArray[index], $"{path}[{index}]", found);
            }

            return;
        }

        if (!JsonNode.DeepEquals(ours, donor)) found.Add($"{path}: ours {Shown(ours)}, the donor {Shown(donor)}");
    }

    private static string Shown(JsonNode? node)
    {
        string text = node?.ToJsonString() ?? "null";
        return text.Length <= 120 ? text : text[..117] + "...";
    }

    /// <summary>The checkout's commit, when it is a Git checkout and Git is installed.</summary>
    private static string? Head(string donor)
    {
        try
        {
            using Process? git = Process.Start(new ProcessStartInfo("git", ["-C", donor, "rev-parse", "HEAD"]) { RedirectStandardOutput = true, RedirectStandardError = true });
            if (git is null) return null;
            string head = git.StandardOutput.ReadToEnd().Trim();
            git.WaitForExit();
            return git.ExitCode == 0 && head.Length != 0 ? head : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
