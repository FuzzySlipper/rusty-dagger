namespace Daggerfall.Import.Arena2;

/// <summary>One parsed classic faction: every field the file states for it.</summary>
/// <param name="Id">The faction's identity, reassigned past the resolver when the file repeats one.</param>
/// <param name="FiledId">The identity the file states, before duplicate resolution.</param>
/// <param name="Parent">The parent's identity from the tab hierarchy, or zero when top-level.</param>
/// <param name="Name">The faction's name.</param>
/// <param name="Reputation">The filed base reputation.</param>
/// <param name="Summon">The filed summon value.</param>
/// <param name="Region">The zero-based region, or -1 when the faction names none.</param>
/// <param name="Type">The filed faction type.</param>
/// <param name="Power">The filed power.</param>
/// <param name="Flags">The filed flags, or-ed across flag lines.</param>
/// <param name="Ruler">The filed ruler value.</param>
/// <param name="Allies">Up to three filed ally identities, in file order.</param>
/// <param name="Enemies">Up to three filed enemy identities, in file order.</param>
/// <param name="Flats">The filed face flats: one shared entry, or the male and female entries.</param>
/// <param name="Face">The filed face value.</param>
/// <param name="Race">The filed race value.</param>
/// <param name="SocialGroup">The filed social group.</param>
/// <param name="GuildGroup">The filed guild group.</param>
/// <param name="MinimumFame">The filed minimum fame.</param>
/// <param name="MaximumFame">The filed maximum fame.</param>
/// <param name="Vampire">The filed vampire value.</param>
/// <param name="Rank">The filed rank.</param>
/// <param name="Children">The identities that name this faction parent, in file order.</param>
public sealed record ClassicFaction(
    int Id,
    int FiledId,
    int Parent,
    string Name,
    int Reputation,
    int Summon,
    int Region,
    int Type,
    int Power,
    int Flags,
    int Ruler,
    IReadOnlyList<int> Allies,
    IReadOnlyList<int> Enemies,
    IReadOnlyList<int> Flats,
    int Face,
    int Race,
    int SocialGroup,
    int GuildGroup,
    int MinimumFame,
    int MaximumFame,
    int Vampire,
    int Rank,
    IReadOnlyList<int> Children);

/// <summary>Reader for classic FACTION.TXT faction blocks.</summary>
public static class FactionReader
{
    /// <summary>The first reassigned identity when the file repeats one.</summary>
    public const int DuplicateResolverStart = 980;

    /// <summary>
    /// Reads every faction the file states: blocks split at # lines, parents from the tab
    /// hierarchy, children relinked after the read. Comment and empty lines never reach a block.
    /// A repeated identity is reassigned past the resolver rather than dropped, and a repeated
    /// name keeps its first identity: both rules are the donor's, because a reader that disagreed
    /// would address factions the donor never produces.
    /// </summary>
    public static IReadOnlyList<ClassicFaction> Read(string text, string source)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        List<string[]> blocks = SplitBlocks(text);
        Dictionary<int, MutableFaction> factions = [];
        int resolver = DuplicateResolverStart;
        Stack<int> parents = [];
        int lastTabs = 0;
        int lastId = 0;

        foreach (string[] block in blocks)
        {
            int tabs = CountTabs(block[0]);
            if (tabs > lastTabs)
            {
                parents.Push(lastId);
            }
            else
            {
                while (parents.Count > tabs)
                {
                    parents.Pop();
                }
            }

            lastTabs = tabs;
            MutableFaction faction = ParseBlock(block, source);
            faction.Parent = parents.Count > 0 ? parents.Peek() : 0;
            faction.FiledId = faction.Id;
            if (factions.ContainsKey(faction.Id))
            {
                faction.Id = resolver++;
            }

            factions.Add(faction.Id, faction);
            lastId = faction.Id;
        }

        foreach (MutableFaction faction in factions.Values)
        {
            if (factions.ContainsKey(faction.Parent))
            {
                factions[faction.Parent].Children.Add(faction.Id);
            }
        }

        return [.. factions.Values.Select(faction => faction.Freeze())];
    }

    private static List<string[]> SplitBlocks(string text)
    {
        List<string[]> blocks = [];
        List<string> current = [];
        using StringReader reader = new(text);
        while (reader.ReadLine() is string line)
        {
            if (line.StartsWith(';') || line.Length == 0)
            {
                continue;
            }

            if (line.Contains('#'))
            {
                if (current.Count > 0)
                {
                    blocks.Add([.. current]);
                }

                current.Clear();
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            blocks.Add([.. current]);
        }

        return blocks;
    }

    private static MutableFaction ParseBlock(string[] block, string source)
    {
        MutableFaction faction = new();
        int allies = 0;
        int enemies = 0;
        int flats = 0;
        foreach (string line in block)
        {
            if (line.Contains('#'))
            {
                faction.Id = ParseInt(line.Split('#')[1].Trim(), line, faction.Id, source);
                continue;
            }

            if (line.Trim().Length == 0)
            {
                continue;
            }

            string[] parts = line.Split(':');
            if (parts.Length != 2)
            {
                // The file carries a tag line with no colon; the donor falls back to a space split
                // and refuses a line neither shape divides, so this reader does the same.
                parts = line.Split(' ');
                if (parts.Length != 2)
                {
                    throw new Arena2FormatException(source, 0, $"Faction {faction.Id} carries the malformed tag line '{line}'.");
                }
            }

            string tag = parts[0].Trim().ToLowerInvariant();
            string value = parts[1].Trim();
            switch (tag)
            {
                case "name": faction.Name = value; break;
                case "rep": faction.Reputation = ParseInt(value, line, faction.Id, source); break;
                case "summon": faction.Summon = ParseInt(value, line, faction.Id, source); break;
                case "region":
                    faction.Region = ParseInt(value, line, faction.Id, source);
                    // Filed 1-based; the donor converts to 0-based the way classic does.
                    if (faction.Region != -1) faction.Region--;
                    break;
                case "type": faction.Type = ParseInt(value, line, faction.Id, source); break;
                case "power": faction.Power = ParseInt(value, line, faction.Id, source); break;
                case "flags": faction.Flags |= ParseInt(value, line, faction.Id, source); break;
                case "ruler": faction.Ruler = ParseInt(value, line, faction.Id, source); break;
                case "face": faction.Face = ParseInt(value, line, faction.Id, source); break;
                case "race": faction.Race = ParseInt(value, line, faction.Id, source); break;
                case "sgroup": faction.SocialGroup = ParseInt(value, line, faction.Id, source); break;
                case "ggroup": faction.GuildGroup = ParseInt(value, line, faction.Id, source); break;
                case "minf": faction.MinimumFame = ParseInt(value, line, faction.Id, source); break;
                case "maxf": faction.MaximumFame = ParseInt(value, line, faction.Id, source); break;
                case "vam": faction.Vampire = ParseInt(value, line, faction.Id, source); break;
                case "rank": faction.Rank = ParseInt(value, line, faction.Id, source); break;
                case "ally":
                    if (allies >= 3) throw new Arena2FormatException(source, 0, $"Faction {faction.Id} names more than three allies.");
                    faction.Allies.Add(ParseInt(value, line, faction.Id, source));
                    allies++;
                    break;
                case "enemy":
                    if (enemies >= 3) throw new Arena2FormatException(source, 0, $"Faction {faction.Id} names more than three enemies.");
                    faction.Enemies.Add(ParseInt(value, line, faction.Id, source));
                    enemies++;
                    break;
                case "flat":
                    int flat = ParseFlat(value, line, faction.Id, source);
                    if (flat > 0)
                    {
                        // One flat serves both faces; the second flat found is the female face.
                        if (flats == 0) { faction.Flats.Add(flat); faction.Flats.Add(flat); }
                        else if (flats == 1) faction.Flats[1] = flat;
                        else throw new Arena2FormatException(source, 0, $"Faction {faction.Id} names more than two flats.");
                        flats++;
                    }

                    break;
                default:
                    throw new Arena2FormatException(source, 0, $"Faction {faction.Id} carries the unexpected tag '{tag}'.");
            }
        }

        return faction;
    }

    private static int ParseInt(string value, string line, int faction, string source)
    {
        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
        {
            throw new Arena2FormatException(source, 0, $"Faction {faction} carries the malformed value '{line}'.");
        }

        return parsed;
    }

    private static int ParseFlat(string value, string line, int faction, string source)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return 0;
        }

        if (parts.Length != 2)
        {
            throw new Arena2FormatException(source, 0, $"Faction {faction} carries the malformed flat '{line}'.");
        }

        return (ParseInt(parts[0], line, faction, source) << 7) + ParseInt(parts[1], line, faction, source);
    }

    private static int CountTabs(string line)
    {
        int count = 0;
        while (count < line.Length && line[count] == '\t')
        {
            count++;
        }

        return count;
    }

    private sealed class MutableFaction
    {
        // Unset fields read as zero: the donor parses into a struct, so a tag the file never
        // states leaves the zero the struct starts with rather than a sentinel.
        public int Id;
        public int FiledId;
        public int Parent;
        public string Name = string.Empty;
        public int Reputation;
        public int Summon;
        public int Region;
        public int Type;
        public int Power;
        public int Flags;
        public int Ruler;
        public readonly List<int> Allies = [];
        public readonly List<int> Enemies = [];
        public readonly List<int> Flats = [];
        public int Face;
        public int Race;
        public int SocialGroup;
        public int GuildGroup;
        public int MinimumFame;
        public int MaximumFame;
        public int Vampire;
        public int Rank;
        public readonly List<int> Children = [];

        public ClassicFaction Freeze() => new(
            Id, FiledId, Parent, Name, Reputation, Summon, Region, Type, Power, Flags, Ruler,
            Allies, Enemies, Flats, Face, Race, SocialGroup, GuildGroup, MinimumFame, MaximumFame,
            Vampire, Rank, Children);
    }
}
