namespace Daggerfall.Source.Tool.ReferenceExports;

internal sealed record NpcReferenceId(string Id, string PrimarySource, string[] Aliases, string[] PortraitSources, string? Name);

/// <summary>Editorial IDs stay fixed when another source record is added or consolidated.</summary>
internal static class NpcReferenceIds
{
    public static IReadOnlyDictionary<string, NpcReferenceId> Read()
    {
        using StreamReader reader = new(typeof(NpcReferenceIds).Assembly.GetManifestResourceStream("NpcReferenceIds.txt")!);
        Dictionary<string, NpcReferenceId> bySource = new(StringComparer.Ordinal);
        HashSet<string> numbers = new(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
            string[] fields = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // A repeated number would overwrite another NPC's page. A repeated source would attach its art to the wrong page.
            if (fields.Length < 2 || fields[0].Length != 3 || !fields[0].All(char.IsAsciiDigit) || !numbers.Add(fields[0]))
                throw new InvalidDataException($"Invalid or reused NPC reference number in '{line}'.");
            string[] aliases = [.. fields.Skip(2).Where(source =>
                source.Length == 7 && source.StartsWith("npc-", StringComparison.Ordinal) && source[4..].All(char.IsAsciiDigit))];
            foreach (string alias in aliases)
                if (!numbers.Add(alias[4..]))
                    throw new InvalidDataException($"Previous NPC ID '{alias}' is assigned more than once.");
            string[] portraitSources = [.. fields.Skip(2).Where(source => source.StartsWith("npc-face-", StringComparison.Ordinal))];
            NpcReferenceId reference = new($"npc-{fields[0]}", fields[1], aliases, portraitSources, parts.Length > 1 ? parts[1] : null);
            foreach (string source in fields.Skip(1))
                if (!bySource.TryAdd(source, reference))
                    throw new InvalidDataException($"NPC reference source '{source}' is assigned more than once.");
        }
        return bySource;
    }
}
