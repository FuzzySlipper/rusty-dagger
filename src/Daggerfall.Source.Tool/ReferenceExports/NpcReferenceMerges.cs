using System.Text.Json;

namespace Daggerfall.Source.Tool.ReferenceExports;

/// <summary>Applies confirmed catalog merges after classic art and placements have been resolved.</summary>
internal static class NpcReferenceMerges
{
    public static IReadOnlyList<NpcEntry> Apply(ReferenceExport export, IReadOnlyList<NpcEntry> entries, string path)
    {
        var filed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(path), ReferenceExport.Json) ?? [];
        if (filed.Count == 0) return entries;

        HashSet<string> ids = entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string> redirects = new(StringComparer.Ordinal);
        foreach ((string source, string keeper) in filed)
        {
            if (filed.ContainsKey(keeper))
            {
                // A keeper that is also retired would have no page for the merged references.
                throw new InvalidDataException($"NPC merge {source} → {keeper} points to a retired keeper.");
            }
            if (!ids.Contains(source) || !ids.Contains(keeper))
            {
                export.Warn($"Skipping NPC merge {source} → {keeper}: one of the references is absent in this export.");
                continue;
            }
            redirects.Add(source, keeper);
        }

        var members = entries.Where(entry => redirects.ContainsKey(entry.Id)).ToLookup(entry => redirects[entry.Id]);
        List<NpcEntry> result = [];
        foreach (NpcEntry entry in entries.Where(entry => !redirects.ContainsKey(entry.Id)))
        {
            NpcEntry[] merged = [.. members[entry.Id].OrderBy(member => member.Id, StringComparer.Ordinal)];
            NpcEntry[] sources = [entry, .. merged];
            NpcLink[] links = [.. sources.SelectMany(source => source.Links)
                .Select(link => RedirectLink(redirects, link))
                .Where(link => link.Href != $"../{entry.Id}/index.html").DistinctBy(link => link.Href)];
            result.Add(merged.Length == 0 ? entry with { Links = links } : entry with
            {
                Art = MergeArt(export.Root, entry, merged),
                Placements = MergePlacements(sources),
                Links = links,
                Aliases = [.. entry.Aliases.Concat(merged.SelectMany(member => member.Aliases.Prepend(member.Id))).Distinct()],
                Merged = merged,
            });
        }
        return result;
    }

    private static NpcLink RedirectLink(IReadOnlyDictionary<string, string> redirects, NpcLink link) =>
        link.Href.Split('/') is ["..", var id, "index.html"] && redirects.TryGetValue(id, out string? keeper)
            ? new($"Related NPC {keeper}", $"../{keeper}/index.html")
            : link;

    private static IReadOnlyList<NpcArt> MergeArt(string root, NpcEntry keeper, IReadOnlyList<NpcEntry> members)
    {
        List<NpcArt> art = [.. keeper.Art];
        foreach (NpcEntry member in members)
            foreach (NpcArt variant in member.Art.Where(variant => variant.Frames.Count > 0))
                art.Add(variant with
                {
                    Label = $"{variant.Label} · {member.Id}",
                    Identical = art.Any(existing => !existing.Identical && existing.Frames.Count > 0 &&
                        existing.Frames[0].Role == variant.Frames[0].Role && NpcReferenceExport.SameFrames(root, existing, variant)),
                });
        return art;
    }

    private static IReadOnlyList<NpcPlacement> MergePlacements(IEnumerable<NpcEntry> sources)
    {
        // A named entry and a billboard entry can both report the same block placement.
        // Keep the highest count from any single entry so genuine repeats inside a block survive.
        var counts = sources.SelectMany(entry => entry.Placements
            .GroupBy(use => use with { Pose = null }).Select(group => new
            {
                group.Key,
                Count = group.Count(),
                Pose = group.FirstOrDefault(use => use.Pose is not null)?.Pose,
            }))
            .GroupBy(item => item.Key);
        return [.. counts.SelectMany(group => Enumerable.Repeat(
            group.Key with { Pose = group.Select(item => item.Pose).FirstOrDefault(pose => pose is not null) }, group.Max(item => item.Count)))];
    }
}
