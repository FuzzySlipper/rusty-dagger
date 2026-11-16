using System.Collections.ObjectModel;
using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>The donor world shape a transition profile admits; lifecycle policy never infers it from a site name.</summary>
internal enum DaggerfallWorldProfileKind { Exterior, Interior, Dungeon }

/// <summary>One source-normalized point-light placement in an admitted world profile.</summary>
internal sealed record DaggerfallSiteLight(string Id, WorldPoint Position, float Range, float Intensity, Vector3 Color)
{
    internal bool CityLight { get; init; }
    internal DaggerfallSiteLight Validate()
    {
        if (!DaggerfallBaseContent.ValidId(Id)) throw new ArgumentException("A site light must have a stable id.", nameof(Id));
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z))
            throw new ArgumentOutOfRangeException(nameof(Position));
        if (!float.IsFinite(Range) || Range <= 0F) throw new ArgumentOutOfRangeException(nameof(Range));
        if (!float.IsFinite(Intensity) || Intensity < 0F) throw new ArgumentOutOfRangeException(nameof(Intensity));
        if (!float.IsFinite(Color.X) || !float.IsFinite(Color.Y) || !float.IsFinite(Color.Z)
            || Color.X < 0F || Color.Y < 0F || Color.Z < 0F)
            throw new ArgumentOutOfRangeException(nameof(Color));
        return this;
    }
}

/// <summary>
/// Identity of one admitted projection. A site remains the geographic location used by map,
/// discovery, region policy, and return context; the logical profile distinguishes its exterior,
/// dungeon, and any repeated building interiors without inventing location indices.
/// </summary>
internal readonly record struct DaggerfallWorldProfileKey(DaggerfallSiteId Site, DaggerfallWorldProfileKind Kind, string LogicalId)
{
    internal DaggerfallWorldProfileKey Validate()
    {
        if (string.IsNullOrWhiteSpace(LogicalId) || !DaggerfallBaseContent.ValidId(LogicalId.Replace('/', '-')))
            throw new ArgumentException("A world profile must carry a stable logical profile id.", nameof(LogicalId));
        return this;
    }
}

/// <summary>One source-derived portal that normal interaction may select inside an admitted world profile.</summary>
/// <param name="ArrivalAnchor">
/// The destination anchor the portal lands at when the player has no entrance to return through (a door's
/// own landing); null lands at the destination's start. A remembered entrance always wins.
/// </param>
/// <param name="Normal">
/// For a portal on a source door plane, the plane's unit normal toward the side a player uses it from. Its
/// position is then the plane's centre, on the wall the door is set into, and interaction sights it from that
/// side as a Kit surface target; null is a portal standing in open space.
/// </param>
internal sealed record DaggerfallSitePortal(string Id, WorldPoint Position, float Radius, string DestinationLogicalProfile, string? ArrivalAnchor = null,
    Vector3? Normal = null)
{
    internal DaggerfallSitePortal Validate()
    {
        if (ArrivalAnchor is not null && !DaggerfallBaseContent.ValidId(ArrivalAnchor))
            throw new ArgumentException("A site portal's arrival must name a stable anchor.", nameof(ArrivalAnchor));
        if (!DaggerfallBaseContent.ValidId(Id)) throw new ArgumentException("A site portal must have a stable id.", nameof(Id));
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z))
            throw new ArgumentOutOfRangeException(nameof(Position));
        if (!float.IsFinite(Radius) || Radius <= 0f) throw new ArgumentOutOfRangeException(nameof(Radius));
        if (Normal is Vector3 normal && (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z)
            || MathF.Abs(normal.Length() - 1f) > 1e-3f))
            throw new ArgumentOutOfRangeException(nameof(Normal), "A door-plane portal's normal must be a unit vector.");
        if (string.IsNullOrWhiteSpace(DestinationLogicalProfile) || !DaggerfallBaseContent.ValidId(DestinationLogicalProfile.Replace('/', '-')))
            throw new ArgumentException("A site portal must name a stable destination profile.", nameof(DestinationLogicalProfile));
        return this;
    }
}

/// <summary>
/// A named, authored landing pose in one admitted profile. Dungeon actions, travel, recall, and
/// quest policy resolve this stable content identity before changing the live site projection.
/// </summary>
internal sealed record DaggerfallSiteAnchor(string Id, WorldPoint Position, float YawRadians, float PitchRadians)
{
    internal DaggerfallSiteAnchor Validate()
    {
        if (!DaggerfallBaseContent.ValidId(Id)) throw new ArgumentException("A site anchor must have a stable id.", nameof(Id));
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z))
            throw new ArgumentOutOfRangeException(nameof(Position));
        if (!float.IsFinite(YawRadians)) throw new ArgumentOutOfRangeException(nameof(YawRadians));
        if (!float.IsFinite(PitchRadians)) throw new ArgumentOutOfRangeException(nameof(PitchRadians));
        return this;
    }
}

/// <summary>A ruleset-selected profile, named landing anchor, and durable actor for one relocation.</summary>
internal sealed record DaggerfallRelocationDestination(
    DaggerfallWorldProfileKey Profile,
    string AnchorId,
    long ActorId = DaggerfallActorIdentity.PlayerEntityId)
{
    internal DaggerfallRelocationDestination Validate()
    {
        Profile.Validate();
        if (!DaggerfallBaseContent.ValidId(AnchorId)) throw new ArgumentException("A relocation destination must name a stable anchor id.", nameof(AnchorId));
        if (ActorId <= 0) throw new ArgumentOutOfRangeException(nameof(ActorId), "A relocation destination must name a live actor identity.");
        return this;
    }
}

/// <summary>
/// Resolves a world profile when it is first needed: the location's exterior, building interior or dungeon assembled
/// from the catalog and the per-block publication. A resolved profile is kept for the composition's lifetime; a profile
/// the catalog does not place is refused by name.
/// </summary>
internal sealed partial class DaggerfallSiteProfiles
{
    private readonly Catalog _catalog;

    /// <summary>
    /// A catalog seeded with profiles already resolved, as fixtures and focused compositions build it: each seeded
    /// profile answers for its own id, and the assembly, when one is given, for every other place.
    /// </summary>
    internal DaggerfallSiteProfiles(IEnumerable<DaggerfallSiteProfile> resolved, DaggerfallLocationAssembly? assembly = null)
        : this(new Catalog([.. resolved ?? throw new ArgumentNullException(nameof(resolved))], assembly, null))
    {
    }

    private DaggerfallSiteProfiles(Catalog catalog) => _catalog = catalog;

    /// <summary>The product catalog: every location profile assembled when first needed, each checked by <paramref name="admit"/>.</summary>
    internal static DaggerfallSiteProfiles Resolve(DaggerfallLocationAssembly assembly, Action<DaggerfallSiteProfile>? admit) =>
        new(new Catalog([], assembly ?? throw new ArgumentNullException(nameof(assembly)), admit));

    /// <summary>
    /// Whether the catalog has this profile, without resolving it: a seeded profile, or a location profile the
    /// catalog and the per-block index place.
    /// </summary>
    internal bool Contains(DaggerfallWorldProfileKey key) => _catalog.Contains(key);

    /// <summary>Whether this profile has been resolved yet; resolution is lazy.</summary>
    internal bool IsResolved(DaggerfallWorldProfileKey key) => _catalog.IsResolved(key);

    internal bool TryGet(DaggerfallWorldProfileKey key, out DaggerfallSiteProfile profile) => _catalog.TryResolve(key, out profile!);

    internal DaggerfallSiteProfile Require(DaggerfallWorldProfileKey key) => TryGet(key, out DaggerfallSiteProfile profile)
        ? profile
        : throw new InvalidOperationException($"No world profile is assembled for '{key.LogicalId}'.");

    /// <summary>The profile a logical id names: every profile id spells its location and kind.</summary>
    internal bool TryGetLogicalProfile(string logicalId, out DaggerfallSiteProfile profile)
    {
        profile = null!;
        if (DaggerfallWorldProfileIds.TryParse(logicalId, out DaggerfallWorldProfileKey key)) return TryGet(key, out profile);
        // A fixture's seeded profile may carry an id outside the location scheme; it resolves by that id alone.
        return _catalog.TrySeededLogicalId(logicalId, out key) && TryGet(key, out profile);
    }

    /// <summary>
    /// The profile key a logical id names, without resolving the profile: an entrance decides where it leads before
    /// the (slow) read of the place itself.
    /// </summary>
    internal DaggerfallWorldProfileKey RequireLogicalKey(string logicalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalId);
        if ((DaggerfallWorldProfileIds.TryParse(logicalId, out DaggerfallWorldProfileKey key) || _catalog.TrySeededLogicalId(logicalId, out key))
            && Contains(key))
            return key;
        throw new InvalidOperationException($"No world profile is assembled for logical id '{logicalId}'.");
    }

    internal DaggerfallSiteProfile RequireLogicalProfile(string logicalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalId);
        return TryGetLogicalProfile(logicalId, out DaggerfallSiteProfile profile)
            ? profile
            : throw new InvalidOperationException($"No world profile is assembled for logical id '{logicalId}'.");
    }

    private sealed class Catalog
    {
        private readonly Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteProfile> _resolved = [];
        private readonly Dictionary<string, DaggerfallWorldProfileKey> _seededByLogicalId = new(StringComparer.Ordinal);
        private readonly DaggerfallLocationAssembly? _assembly;
        private readonly Action<DaggerfallSiteProfile>? _admit;
        private readonly object _gate = new();

        internal Catalog(IReadOnlyList<DaggerfallSiteProfile> seeded, DaggerfallLocationAssembly? assembly, Action<DaggerfallSiteProfile>? admit)
        {
            _assembly = assembly;
            _admit = admit;
            foreach (DaggerfallSiteProfile profile in seeded)
            {
                ArgumentNullException.ThrowIfNull(profile);
                if (profile.Site is null) throw new ArgumentException("A seeded world profile must name its Daggerfall site.", nameof(seeded));
                DaggerfallWorldProfileKey key = profile.ProfileKey.Validate();
                if (!_resolved.TryAdd(key, Admit(profile))) throw new ArgumentException($"The seeded profiles repeat world profile '{key.LogicalId}'.");
            }

            foreach (IGrouping<string, DaggerfallWorldProfileKey> named in _resolved.Keys.GroupBy(key => key.LogicalId, StringComparer.Ordinal))
                if (named.Count() == 1) _seededByLogicalId.Add(named.Key, named.Single());
        }

        /// <summary>The one seeded profile a logical id names, for a fixture id outside the location scheme.</summary>
        internal bool TrySeededLogicalId(string logicalId, out DaggerfallWorldProfileKey key) => _seededByLogicalId.TryGetValue(logicalId, out key);

        internal bool IsResolved(DaggerfallWorldProfileKey key)
        {
            lock (_gate) return _resolved.ContainsKey(key);
        }

        internal bool Contains(DaggerfallWorldProfileKey key)
        {
            lock (_gate) return _resolved.ContainsKey(key) || _assembly?.Places(key) == true;
        }

        internal bool TryResolve(DaggerfallWorldProfileKey key, out DaggerfallSiteProfile? profile)
        {
            lock (_gate)
            {
                if (_resolved.TryGetValue(key, out profile)) return true;
                if (_assembly?.Places(key) != true) return false;
                profile = Admit(_assembly.Assemble(key));
                _resolved.Add(key, profile);
                return true;
            }
        }

        /// <summary>Runs the composition's admission checks over a profile as it is first resolved.</summary>
        private DaggerfallSiteProfile Admit(DaggerfallSiteProfile profile)
        {
            _admit?.Invoke(profile);
            return profile;
        }
    }
}
