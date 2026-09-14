using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Daggerfall.Import.Arena2;

/// <summary>What happened to one mobile the donor's static table defines.</summary>
public enum MobileLedgerDisposition
{
    /// <summary>A published actor carries the mobile's identity.</summary>
    Published,

    /// <summary>The donor defines the name more than once and the product publishes the second as a variant.</summary>
    PublishedVariant,

    /// <summary>The donor's human-mobile space; the product publishes the classes as careers and the two it places as actors.</summary>
    HumanClass,

    /// <summary>The donor defines the mobile and nothing published carries it.</summary>
    Unpublished,
}

/// <summary>One donor mobile reconciled against what the product publishes.</summary>
public sealed record MobileLedgerEntry(int Id, string Name, string Identity, MobileLedgerDisposition Disposition, string Note);

/// <summary>
/// The donor's static mobile table reconciled with the pack's published identities, naming every
/// difference rather than leaving coverage implied.
/// </summary>
public sealed record MobileLedger(int DonorEntries, int PublishedActors, int CatalogEntries, IReadOnlyList<MobileLedgerEntry> Entries)
{
    /// <summary>Entries the donor defines and nothing published carries.</summary>
    public IReadOnlyList<MobileLedgerEntry> Unpublished => [.. Entries.Where(entry => entry.Disposition == MobileLedgerDisposition.Unpublished)];
}

/// <summary>
/// Reads the donor's <c>EnemyBasics.Enemies</c> table and the published pack, and reports the exact
/// reconciliation between them.
/// </summary>
/// <remarks>
/// The donor is the authority for which classic mobiles exist; the pack is the authority for which of
/// them this product publishes. Both sides are read here rather than kept as a list beside them, so a
/// donor entry that gains or loses a published actor shows up as a difference instead of staying
/// invisible. The classic mobile space is structural: ids below 128 are monsters, ids from 128 are the
/// human mobiles the donor also uses for classes, which is why the two are reported separately.
/// </remarks>
public static class MobileLedgerBuilder
{
    /// <summary>The first id the donor's human-mobile space occupies.</summary>
    public const int FirstHumanMobileId = 128;

    private static readonly Regex EntryPattern = new(
        @"//[ \t]*(?<name>[^\n]*?)[ \t]*\r?\n[ \t]*new MobileEnemy\(\)[ \t]*\r?\n[ \t]*\{[ \t]*\r?\n[ \t]*ID[ \t]*=[ \t]*(?<id>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Builds the reconciliation from the donor's table text and the published pack's JSON.</summary>
    public static MobileLedger Build(string donorEnemyBasics, string packJson)
    {
        ArgumentNullException.ThrowIfNull(donorEnemyBasics);
        ArgumentNullException.ThrowIfNull(packJson);
        List<(int Id, string Name)> donor = [];
        foreach (Match match in EntryPattern.Matches(donorEnemyBasics))
        {
            donor.Add((int.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture), match.Groups["name"].Value.Trim()));
        }

        if (donor.Count == 0)
        {
            throw new InvalidOperationException("The donor's static mobile table was not found; the ledger cannot report coverage it cannot read.");
        }

        JsonNode pack = JsonNode.Parse(packJson) ?? throw new InvalidOperationException("The published pack is not JSON.");
        List<string> actors = [.. (pack["actors"]?.AsArray() ?? throw new InvalidOperationException("The published pack carries no actors."))
            .Select(actor => actor!["id"]?.GetValue<string>() ?? throw new InvalidOperationException("A published actor carries no id."))];
        HashSet<string> actorIds = new(actors, StringComparer.Ordinal);
        int catalogEntries = pack["catalogs"]?["enemies"]?.AsArray().Count ?? 0;

        // A donor name is published under its own identity; a repeated donor name is published once
        // under the name and once as a numbered variant, which is how the pack keeps both addressable.
        Dictionary<string, int> nameCounts = [];
        foreach ((_, string name) in donor)
        {
            string identity = Identity(name);
            nameCounts[identity] = nameCounts.TryGetValue(identity, out int seen) ? seen + 1 : 1;
        }

        List<MobileLedgerEntry> entries = [];
        foreach ((int id, string name) in donor)
        {
            string identity = Identity(name);
            bool repeated = nameCounts[identity] > 1;
            // The pack keeps a repeated donor name addressable by numbering the entry after its source
            // id, so the numbered identity is checked first and the plain one is the entry it repeats.
            string candidate = $"{identity}-{id}";
            MobileLedgerDisposition disposition =
                actorIds.Contains(candidate) ? MobileLedgerDisposition.PublishedVariant
                : actorIds.Contains(identity) ? MobileLedgerDisposition.Published
                : id >= FirstHumanMobileId ? MobileLedgerDisposition.HumanClass
                : MobileLedgerDisposition.Unpublished;
            string note = disposition switch
            {
                MobileLedgerDisposition.PublishedVariant => $"the donor names '{name}' more than once; the pack publishes this entry as '{candidate}'",
                MobileLedgerDisposition.Published when repeated => $"the donor names '{name}' more than once; this entry is the one the pack publishes as '{identity}'",
                MobileLedgerDisposition.HumanClass => "human mobile; the pack publishes the humanoid classes through its careers and places the two it needs as actors",
                MobileLedgerDisposition.Unpublished => $"the donor defines '{name}' (id {id}) and no published actor carries it",
                _ => string.Empty,
            };
            entries.Add(new MobileLedgerEntry(id, name, candidate, disposition, note));
        }

        return new MobileLedger(donor.Count, actorIds.Count, catalogEntries, entries);
    }

    /// <summary>The pack's identity for a donor comment name.</summary>
    public static string Identity(string name)
    {
        string lowered = name.ToLowerInvariant();
        // The donor's comments carry qualifiers in parentheses; the identity is the name itself.
        int parenthesis = lowered.IndexOf('(', StringComparison.Ordinal);
        if (parenthesis >= 0) lowered = lowered[..parenthesis];
        return Regex.Replace(lowered, "[^a-z0-9]+", "-").Trim('-');
    }
}
