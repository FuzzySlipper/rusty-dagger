using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Where the session is and what it has changed about the sites it has seen.
/// </summary>
/// <remarks>
/// This owns the location-and-region context the rest of the session reads: which region the player is
/// standing in, which site they are at, the site they entered from, and which sites play has revealed.
/// It resolves all of that against the location records the bundle already carries, so looking a site up
/// needs no rendering, no directory of its own and no second read of the payload.
/// <para>
/// A site's <em>identity</em> is its region and index, never its name. The corpus publishes 15,251
/// locations under 12,672 distinct names, and within a single region 129 (region, name) pairs across 44
/// distinct names share their name with another location.
/// </para>
/// <para>
/// What is durable here is the delta rather than the record: a save carries which sites play revealed,
/// not the authored <see cref="DaggerfallSiteRecord.Discovered"/> flag, which the bundle still owns and
/// which a reload therefore cannot lose.
/// </para>
/// </remarks>
internal sealed class DaggerfallSiteContext
{
    private readonly IReadOnlyDictionary<DaggerfallSiteId, DaggerfallSiteRecord> _records;
    private readonly IReadOnlyList<DaggerfallSiteRecord> _ordered;
    private readonly HashSet<DaggerfallSiteId> _discovered = [];

    internal DaggerfallSiteContext(DaggerfallLocationSet locations)
        : this(locations, null, null, [])
    {
    }

    internal DaggerfallSiteContext(
        DaggerfallLocationSet locations,
        DaggerfallSiteId? active,
        DaggerfallSiteId? returnAnchor,
        IEnumerable<DaggerfallSiteId> discovered)
    {
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(discovered);
        _records = locations.Records.ToDictionary(record => record.Id);
        // A stable order independent of how the dictionary happens to enumerate, so a consumer that
        // walks every site - a map, a directory, a corpus check - walks them the same way twice.
        _ordered = [.. locations.Records.OrderBy(record => record.Id.Region).ThenBy(record => record.Id.Index)];
        if (active is { } start)
        {
            // A start the bundle does not carry is refused rather than held as a site that resolves to
            // nothing: every read below would then answer from a location nobody published.
            Active = Require(start).Id;
        }

        if (returnAnchor is { } anchor)
        {
            // The save section refuses this pair for the same reason, and holding one invariant in two
            // places is what keeps a Leave from moving a player who is nowhere onto a site they were
            // never shown to be at.
            if (Active is null)
            {
                throw new ArgumentException("A site context cannot hold a return anchor for a player it places at no site.", nameof(returnAnchor));
            }

            ReturnAnchor = Require(anchor).Id;
        }

        foreach (DaggerfallSiteId id in discovered)
        {
            // Routed through the same rule as play, so a save that carries a site the bundle already
            // marks discovered cannot make this context re-persist an authored fact. Such a save is
            // redundant rather than wrong - IsDiscovered still answers true from the record - but the
            // delta this context captures stays what play added and nothing else.
            Reveal(Require(id));
        }
    }

    /// <summary>The site the player is at, or null before any site owns them.</summary>
    internal DaggerfallSiteId? Active { get; private set; }

    /// <summary>The site a <see cref="Leave"/> returns to, or null when the player is not inside a site.</summary>
    internal DaggerfallSiteId? ReturnAnchor { get; private set; }

    /// <summary>
    /// The region the session is in, which is what every donor formula that takes a region index reads.
    /// </summary>
    /// <remarks>
    /// It is the active site's region and nothing else: a return anchor only exists while the player is
    /// somewhere, so it can never be the only region this could answer with.
    /// </remarks>
    internal int? Region => Active?.Region;

    /// <summary>How many site records the bundle publishes.</summary>
    internal int RecordCount => _records.Count;

    /// <summary>Every site record the bundle publishes, ordered by region and then index.</summary>
    internal IReadOnlyList<DaggerfallSiteRecord> Records => _ordered;

    /// <summary>The active site's record, or null when no site owns the player.</summary>
    internal DaggerfallSiteRecord? ActiveSite => Active is { } id ? _records[id] : null;

    /// <summary>Whether the bundle carries a record for this identity.</summary>
    internal bool Contains(DaggerfallSiteId id) => _records.ContainsKey(id);

    /// <summary>Resolves a site's record, or reports that the bundle does not carry it.</summary>
    internal bool TryFind(DaggerfallSiteId id, out DaggerfallSiteRecord record) =>
        _records.TryGetValue(id, out record!);

    /// <summary>Resolves a site's record, naming the identity the bundle does not carry.</summary>
    internal DaggerfallSiteRecord Require(DaggerfallSiteId id) =>
        _records.TryGetValue(id, out DaggerfallSiteRecord? record)
            ? record
            : throw new InvalidOperationException($"The selected content carries no location {id}; a site is identified by its region and index, not by its name.");

    /// <summary>
    /// Every site the bundle carries under one display name, in identity order.
    /// </summary>
    /// <remarks>
    /// This returns a list rather than a record because the name is not a key: 1,467 names are shared by
    /// more than one location, so a caller that wants one site has to say which identity it means. An
    /// empty result is a name nothing publishes; a single result is unambiguous; more than one is the
    /// caller's to disambiguate, and this says so rather than picking.
    /// </remarks>
    internal IReadOnlyList<DaggerfallSiteRecord> FindByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        // Walked in the ordered list rather than the dictionary so the answer does not depend on how a
        // dictionary happens to enumerate, which is not a property this type promised.
        return [.. _ordered.Where(record => string.Equals(record.Name, name, StringComparison.Ordinal))];
    }

    /// <summary>
    /// Whether the player has been shown this site, which the bundle's authored flag or play can
    /// each establish.
    /// </summary>
    internal bool IsDiscovered(DaggerfallSiteId id) => Require(id).Discovered || _discovered.Contains(id);

    /// <summary>
    /// Moves the player to a site, remembering where they came from so <see cref="Leave"/> can return
    /// them.
    /// </summary>
    /// <remarks>
    /// The site the player was already at becomes the anchor, so entering an interior from an exterior
    /// and from a different interior both leave a truthful place to return to. Entering a site reveals
    /// it, which is the one per-site change this context owns.
    /// </remarks>
    internal void Enter(DaggerfallSiteId id)
    {
        DaggerfallSiteRecord record = Require(id);
        if (Active is { } previous && previous != record.Id)
        {
            ReturnAnchor = previous;
        }

        Active = record.Id;
        Reveal(record);
    }

    /// <summary>Returns the player to the site they entered from, clearing the anchor.</summary>
    internal void Leave()
    {
        Active = ReturnAnchor;
        ReturnAnchor = null;
    }

    /// <summary>Reveals a site without moving the player there.</summary>
    internal void Discover(DaggerfallSiteId id) => Reveal(Require(id));

    /// <summary>
    /// Records that play revealed a site, which is only a change when the bundle had not already said so.
    /// </summary>
    /// <remarks>
    /// A site the corpus publishes as discovered is not a delta, so putting it in the save would make
    /// every reload carry a fact it can re-derive from the bundle - and would keep carrying it after the
    /// bundle changed its mind.
    /// </remarks>
    private void Reveal(DaggerfallSiteRecord record)
    {
        if (!record.Discovered)
        {
            _discovered.Add(record.Id);
        }
    }

    /// <summary>The delta a save carries: where the player is and which sites play revealed.</summary>
    internal DaggerfallSiteSave Capture() => new(
        Active is { } active ? new DaggerfallSiteIdSave(active.Region, active.Index) : null,
        ReturnAnchor is { } anchor ? new DaggerfallSiteIdSave(anchor.Region, anchor.Index) : null,
        [.. _discovered.OrderBy(id => id.Region).ThenBy(id => id.Index).Select(id => new DaggerfallSiteIdSave(id.Region, id.Index))]);
}
