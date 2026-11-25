using System.Collections.ObjectModel;
using System.Numerics;
using Rusty.Engine;
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
/// An authored anchor names a floor, and the arriving body keeps its own stance there, as the donor's arrivals leave
/// crouch alone. A recalled pose is a body's centre instead, which holds only in the <paramref name="Stance"/> it was
/// taken in, so the body arrives in that stance, as the donor's recall restores the anchor's crouch.
/// </summary>
internal sealed record DaggerfallSiteAnchor(string Id, WorldPoint Position, float YawRadians, float PitchRadians,
    CharacterStance? Stance = null)
{
    internal DaggerfallSiteAnchor Validate()
    {
        if (!DaggerfallBaseContent.ValidId(Id)) throw new ArgumentException("A site anchor must have a stable id.", nameof(Id));
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z))
            throw new ArgumentOutOfRangeException(nameof(Position));
        if (!float.IsFinite(YawRadians)) throw new ArgumentOutOfRangeException(nameof(YawRadians));
        if (!float.IsFinite(PitchRadians)) throw new ArgumentOutOfRangeException(nameof(PitchRadians));
        if (Stance is { } stance && !Enum.IsDefined(stance)) throw new ArgumentOutOfRangeException(nameof(Stance));
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
/// from the catalog and the per-block publication, with the location variant the session's quests selected for it. A
/// resolved profile is kept for the composition's lifetime, each variant apart from the base; a profile the catalog does
/// not place is refused by name.
/// </summary>
internal sealed partial class DaggerfallSiteProfiles
{
    private readonly Catalog _catalog;
    private readonly ulong? _worldSocialSeed;

    /// <summary>
    /// A catalog seeded with profiles already resolved, as fixtures and focused compositions build it: each seeded
    /// profile answers for its own id, and the assembly, when one is given, for every other place.
    /// </summary>
    internal DaggerfallSiteProfiles(IEnumerable<DaggerfallSiteProfile> resolved, DaggerfallLocationAssembly? assembly = null)
        : this(new Catalog([.. resolved ?? throw new ArgumentNullException(nameof(resolved))], assembly, null), worldSocialSeed: null)
    {
    }

    private DaggerfallSiteProfiles(Catalog catalog, ulong? worldSocialSeed = null)
    {
        _catalog = catalog;
        _worldSocialSeed = worldSocialSeed;
    }

    /// <summary>Whether this session view overrides the catalog's default <c>WorldSocialSeed</c>.</summary>
    internal bool HasCustomWorldSocialSeed => _worldSocialSeed is ulong seed && _catalog.HasCustomWorldSocialSeed(seed);

    /// <summary>The product catalog: every location profile assembled when first needed, each checked by <paramref name="admit"/>.</summary>
    internal static DaggerfallSiteProfiles Resolve(DaggerfallLocationAssembly assembly, Action<DaggerfallSiteProfile>? admit) =>
        new(new Catalog([], assembly ?? throw new ArgumentNullException(nameof(assembly)), admit));

    /// <summary>
    /// Whether the catalog has this profile, without resolving it: a seeded profile, or a location profile the
    /// catalog and the per-block index place.
    /// </summary>
    internal bool Contains(DaggerfallWorldProfileKey key) => _catalog.Contains(key);

    /// <summary>Whether a selected pack carries this variant of the location, so selecting it redraws the location's exterior.</summary>
    internal bool Carries(DaggerfallSiteId site, string variant) => _catalog.Carries(site, variant);

    /// <summary>Whether this profile has been resolved yet; resolution is lazy.</summary>
    internal bool IsResolved(DaggerfallWorldProfileKey key) => _catalog.IsResolved(key);

    /// <summary>The profile as this session sees it: assembled with the variant a quest selected for its location, if any.</summary>
    internal bool TryGet(DaggerfallWorldProfileKey key, out DaggerfallSiteProfile profile) =>
        _catalog.TryResolve(key, _selectedVariants.GetValueOrDefault(key.Site), out profile!, _worldSocialSeed);

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
        private readonly Dictionary<(DaggerfallWorldProfileKey Profile, string Variant), DaggerfallSiteProfile> _variants = [];
        private readonly Dictionary<(DaggerfallWorldProfileKey Profile, string Variant, ulong WorldSocialSeed), DaggerfallSiteProfile> _seededResolved = [];
        private readonly HashSet<(DaggerfallSiteId Site, string Variant)> _missingVariants = [];
        private readonly HashSet<DaggerfallWorldProfileKey> _seeded = [];
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
                _seeded.Add(key);
            }

            foreach (IGrouping<string, DaggerfallWorldProfileKey> named in _resolved.Keys.GroupBy(key => key.LogicalId, StringComparer.Ordinal))
                if (named.Count() == 1) _seededByLogicalId.Add(named.Key, named.Single());
        }

        /// <summary>The one seeded profile a logical id names, for a fixture id outside the location scheme.</summary>
        internal bool TrySeededLogicalId(string logicalId, out DaggerfallWorldProfileKey key) => _seededByLogicalId.TryGetValue(logicalId, out key);

        internal bool Carries(DaggerfallSiteId site, string variant) => _assembly?.Varies(DaggerfallWorldProfileIds.Exterior(site), variant) == true;

        internal bool IsResolved(DaggerfallWorldProfileKey key)
        {
            lock (_gate) return _resolved.ContainsKey(key);
        }

        internal bool Contains(DaggerfallWorldProfileKey key)
        {
            lock (_gate) return _resolved.ContainsKey(key) || _assembly?.Places(key) == true;
        }

        internal bool HasCustomWorldSocialSeed(ulong seed) => _assembly is not null && seed != _assembly.DefaultWorldSocialSeed;

        /// <summary>
        /// Resolves a profile, with a location variant when one is selected and a selected pack carries it for this
        /// profile. A variant no pack carries leaves the base showing, as in the donor, with one warning naming it.
        /// </summary>
        internal bool TryResolve(DaggerfallWorldProfileKey key, string? variant, out DaggerfallSiteProfile? profile, ulong? worldSocialSeed = null)
        {
            lock (_gate)
            {
                if (worldSocialSeed is ulong customSeed && _assembly is not null && customSeed != _assembly.DefaultWorldSocialSeed && !_seeded.Contains(key))
                {
                    if (!_assembly.Places(key)) { profile = null; return false; }
                    string? effectiveVariant = null;
                    if (variant is not null)
                    {
                        if (_assembly.Varies(key, variant))
                            effectiveVariant = variant;
                        else if (key.Kind == DaggerfallWorldProfileKind.Exterior && _missingVariants.Add((key.Site, variant)))
                            System.Diagnostics.Trace.TraceWarning($"World location '{key.Site}' has no variant '{variant}' in the selected packs; its base content shows.");
                    }
                    var cacheKey = (key, effectiveVariant ?? string.Empty, customSeed);
                    if (_seededResolved.TryGetValue(cacheKey, out profile)) return true;
                    profile = Admit(_assembly.Assemble(key, effectiveVariant, customSeed));
                    _seededResolved.Add(cacheKey, profile);
                    return true;
                }

                if (variant is not null && !_seeded.Contains(key) && _assembly?.Places(key) == true)
                {
                    if (_variants.TryGetValue((key, variant), out profile)) return true;
                    if (_assembly.Varies(key, variant))
                    {
                        profile = Admit(_assembly.Assemble(key, variant));
                        _variants.Add((key, variant), profile);
                        return true;
                    }

                    if (key.Kind == DaggerfallWorldProfileKind.Exterior && _missingVariants.Add((key.Site, variant)))
                        System.Diagnostics.Trace.TraceWarning($"World location '{key.Site}' has no variant '{variant}' in the selected packs; its base content shows.");
                }

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
