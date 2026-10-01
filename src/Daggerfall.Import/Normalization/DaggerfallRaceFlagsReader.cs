using System.Text.RegularExpressions;

namespace Daggerfall.Import.Normalized;

public sealed record DaggerfallRaceFlags(int ResistanceFlags, int ImmunityFlags, int LowToleranceFlags, int CriticalWeaknessFlags);

/// <summary>Reads the donor's explicit default RaceTemplate constructors offline, with no runtime source loading.</summary>
public static class DaggerfallRaceFlagsReader
{
    private static readonly Dictionary<string, int> Effects = new(StringComparer.Ordinal)
    { ["None"] = 0, ["Paralysis"] = 1, ["Magic"] = 2, ["Poison"] = 4, ["Fire"] = 8, ["Frost"] = 16, ["Shock"] = 32, ["Disease"] = 64 };

    public static IReadOnlyDictionary<string, DaggerfallRaceFlags> Read(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        string text = Regex.Replace(source, @"//[^\r\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
        string[] fields = ["ResistanceFlags", "ImmunityFlags", "LowToleranceFlags", "CriticalWeaknessFlags"];
        foreach (string field in fields)
            if (Regex.Matches(text, $@"public\s+DFCareer\.EffectFlags\s+{field}\s*;").Count != 1)
                throw new InvalidOperationException($"RaceTemplate must declare the default-zero '{field}' field once; otherwise race exposure flags would be misread.");
        if (Regex.IsMatch(text, @"public\s+RaceTemplate\s*\("))
            throw new InvalidOperationException("RaceTemplate has an explicit base constructor; refusing to assume inherited exposure flags are zero.");
        MatchCollection classes = Regex.Matches(text, @"public\s+class\s+(?<name>\w+)\s*:\s*RaceTemplate\s*\{");
        Dictionary<string, DaggerfallRaceFlags> result = new(StringComparer.Ordinal);
        for (int i = 0; i < classes.Count; i++)
        {
            Match match = classes[i]; string name = match.Groups["name"].Value;
            string body = text[match.Index..(i + 1 < classes.Count ? classes[i + 1].Index : text.Length)];
            if (!Regex.IsMatch(body, $@"\bID\s*=\s*\(int\)Races\.{name}\s*;"))
                throw new InvalidOperationException($"Race '{name}' must name its own donor identity; otherwise flags could attach to the wrong race.");
            if (Regex.IsMatch(body, @"\b(if|switch|for|foreach|while|do)\b"))
                throw new InvalidOperationException($"Race '{name}' contains conditional constructor logic; refusing to publish a guessed flag outcome.");
            int Flags(string field)
            {
                MatchCollection assignments = Regex.Matches(body, $@"\b{field}\s*=\s*(?<value>[^;]+);");
                if (Regex.Matches(body, $@"\b{field}\b").Count != assignments.Count)
                    throw new InvalidOperationException($"Race '{name}' uses '{field}' outside one explicit constant assignment; refusing to publish guessed flags.");
                if (assignments.Count == 0) return 0;
                if (assignments.Count != 1) throw new InvalidOperationException($"Race '{name}' repeats '{field}'; its effective flags are ambiguous.");
                int flags = 0;
                foreach (string part in assignments[0].Groups["value"].Value.Split('|'))
                {
                    Match value = Regex.Match(part.Trim(), @"^DFCareer\.EffectFlags\.(?<name>\w+)$");
                    if (!value.Success || !Effects.TryGetValue(value.Groups["name"].Value, out int flag))
                        throw new InvalidOperationException($"Race '{name}' uses unsupported '{field}' expression '{part.Trim()}'; refusing to publish incorrect exposure flags.");
                    flags |= flag;
                }
                return flags;
            }
            if (!result.TryAdd(name, new(Flags(fields[0]), Flags(fields[1]), Flags(fields[2]), Flags(fields[3]))))
                throw new InvalidOperationException($"RaceTemplate repeats race '{name}'.");
        }
        if (result.Count == 0) throw new InvalidOperationException("RaceTemplate publishes no readable default races.");
        return result;
    }
}
