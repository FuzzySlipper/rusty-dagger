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
internal sealed record DaggerfallSitePortal(string Id, WorldPoint Position, float Radius, string DestinationLogicalProfile)
{
    internal DaggerfallSitePortal Validate()
    {
        if (!DaggerfallBaseContent.ValidId(Id)) throw new ArgumentException("A site portal must have a stable id.", nameof(Id));
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z))
            throw new ArgumentOutOfRangeException(nameof(Position));
        if (!float.IsFinite(Radius) || Radius <= 0f) throw new ArgumentOutOfRangeException(nameof(Radius));
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

/// <summary>One authored site pack: the identity its payload publishes, and its closure read when first needed.</summary>
internal sealed class DaggerfallAuthoredSite(DaggerfallSitePayloadHeader header, Func<DaggerfallSiteProfile> read)
{
    internal DaggerfallSitePayloadHeader Header { get; } = header ?? throw new ArgumentNullException(nameof(header));
    internal Func<DaggerfallSiteProfile> Read { get; } = read ?? throw new ArgumentNullException(nameof(read));

    /// <summary>An already read authored profile, as a fixture or a composition that admits its closures eagerly holds it.</summary>
    internal static DaggerfallAuthoredSite Of(DaggerfallSiteProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Site is null) throw new ArgumentException("A transition profile must name its selected Daggerfall site.", nameof(profile));
        return new(new DaggerfallSitePayloadHeader(profile.ProfileKey.Validate(), profile.VariantName, profile.ProfileKey.LogicalId), () => profile);
    }
}

/// <summary>
/// Resolves a world profile when it is first needed: an authored site pack's closure for the profile id it
/// explicitly overrides, otherwise the location's exterior, building interior or dungeon assembled from the
/// catalog and the per-block publication. Nothing is read at composition beyond each authored payload's
/// identity; a resolved profile is kept for the composition's lifetime. A profile neither source provides
/// is refused by name.
/// </summary>
internal sealed partial class DaggerfallSiteProfiles
{
    private readonly Catalog _catalog;

    /// <summary>
    /// A catalog of already read authored profiles and no assembly, as fixtures and focused compositions build
    /// it. Its variants are already read too, so each is checked against its base now.
    /// </summary>
    internal DaggerfallSiteProfiles(IEnumerable<DaggerfallSiteProfile> profiles)
        : this(new Catalog([.. (profiles ?? throw new ArgumentNullException(nameof(profiles))).Select(DaggerfallAuthoredSite.Of)], null, null))
    {
        _catalog.ResolveVariants();
    }

    private DaggerfallSiteProfiles(Catalog catalog) => _catalog = catalog;

    /// <summary>The product catalog: the authored site packs by the ids they override, and the location assembly for every other profile.</summary>
    internal static DaggerfallSiteProfiles Resolve(IEnumerable<DaggerfallAuthoredSite> authored, DaggerfallLocationAssembly? assembly,
        Action<DaggerfallSiteProfile>? admit) => new(new Catalog([.. authored], assembly, admit));

    /// <summary>
    /// The profiles authored site packs publish. Consumers that today reach only published closures (travel
    /// arrival, building entry, relocation and quest destinations) enumerate these; any other profile id
    /// resolves through <see cref="Require"/>.
    /// </summary>
    internal IReadOnlyCollection<DaggerfallWorldProfileKey> AuthoredKeys => _catalog.AuthoredKeys;

    /// <summary>Whether this profile has been resolved yet; resolution is lazy.</summary>
    internal bool IsResolved(DaggerfallWorldProfileKey key) => _catalog.IsResolved(key);

    internal bool TryGet(DaggerfallWorldProfileKey key, out DaggerfallSiteProfile profile)
    {
        if (_selectedVariants.TryGetValue(key.Site, out string? variant) && _catalog.TryVariant(key, variant, out profile!)) return true;
        return _catalog.TryResolve(key, out profile!);
    }

    internal DaggerfallSiteProfile Require(DaggerfallWorldProfileKey key) => TryGet(key, out DaggerfallSiteProfile profile)
        ? profile
        : throw new InvalidOperationException($"No world profile is published or assembled for '{key.LogicalId}'.");

    /// <summary>The profile a logical id names: every profile id spells its location and kind.</summary>
    internal bool TryGetLogicalProfile(string logicalId, out DaggerfallSiteProfile profile)
    {
        profile = null!;
        if (DaggerfallWorldProfileIds.TryParse(logicalId, out DaggerfallWorldProfileKey key)) return TryGet(key, out profile);
        // A fixture's authored profile may carry an id outside the location scheme; it resolves by that id alone.
        DaggerfallWorldProfileKey[] authored = [.. AuthoredKeys.Where(candidate => StringComparer.Ordinal.Equals(candidate.LogicalId, logicalId))];
        return authored.Length == 1 && TryGet(authored[0], out profile);
    }

    internal DaggerfallSiteProfile RequireLogicalProfile(string logicalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalId);
        return TryGetLogicalProfile(logicalId, out DaggerfallSiteProfile profile)
            ? profile
            : throw new InvalidOperationException($"No world profile is published or assembled for logical id '{logicalId}'.");
    }

    /// <summary>The one authored profile at a site, for a save that names its site without a profile.</summary>
    internal DaggerfallSiteProfile RequireUniqueSite(DaggerfallSiteId site)
    {
        DaggerfallWorldProfileKey[] matches = [.. AuthoredKeys.Where(key => key.Site == site)];
        return matches.Length == 1 ? Require(matches[0])
            : throw new InvalidOperationException($"Saved site '{site}' does not identify one world profile.");
    }

    private sealed class Catalog
    {
        private readonly Dictionary<DaggerfallWorldProfileKey, DaggerfallAuthoredSite> _authored = [];
        private readonly Dictionary<(DaggerfallWorldProfileKey Profile, string Variant), DaggerfallAuthoredSite> _variants = [];
        private readonly Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteProfile> _resolved = [];
        private readonly Dictionary<(DaggerfallWorldProfileKey Profile, string Variant), DaggerfallSiteProfile> _resolvedVariants = [];
        private readonly DaggerfallLocationAssembly? _assembly;
        private readonly Action<DaggerfallSiteProfile>? _admit;
        private readonly object _gate = new();

        internal Catalog(IReadOnlyList<DaggerfallAuthoredSite> authored, DaggerfallLocationAssembly? assembly, Action<DaggerfallSiteProfile>? admit)
        {
            _assembly = assembly;
            _admit = admit;
            foreach (DaggerfallAuthoredSite site in authored)
            {
                ArgumentNullException.ThrowIfNull(site);
                DaggerfallWorldProfileKey key = site.Header.Key.Validate();
                if (site.Header.VariantName is { } variant)
                {
                    if (!DaggerfallBaseContent.ValidId(variant) || variant == "-")
                        throw new ArgumentException($"Site pack '{site.Header.PackId}' names an invalid world variant '{variant}'.");
                    if (!_variants.TryAdd((key, variant), site)) throw new ArgumentException($"Duplicate world variant '{variant}' for '{key.LogicalId}'.");
                }
                else if (!_authored.TryAdd(key, site))
                    throw new ArgumentException($"The selected content repeats world profile '{key.LogicalId}'.");
            }

            foreach (var group in _variants.Keys.GroupBy(value => (value.Profile.Site, value.Variant)))
            {
                if (!group.All(value => _authored.ContainsKey(value.Profile) || assembly?.Places(value.Profile) == true))
                    throw new ArgumentException($"World variant '{group.Key.Variant}' has no base profile at '{group.Key.Site}'.");
                if (!_authored.Keys.Where(key => key.Site == group.Key.Site).ToHashSet().IsSubsetOf(group.Select(value => value.Profile)))
                    throw new ArgumentException($"Location variant '{group.Key.Variant}' must publish every admitted profile of '{group.Key.Site}'.");
            }

            AuthoredKeys = [.. _authored.Keys];
        }

        internal IReadOnlyCollection<DaggerfallWorldProfileKey> AuthoredKeys { get; }

        internal bool IsResolved(DaggerfallWorldProfileKey key)
        {
            lock (_gate) return _resolved.ContainsKey(key);
        }

        /// <summary>Reads and checks every variant against its base.</summary>
        internal void ResolveVariants()
        {
            foreach ((DaggerfallWorldProfileKey profile, string variant) in _variants.Keys.ToArray())
                _ = TryVariant(profile, variant, out _);
        }

        internal bool HasVariant(DaggerfallSiteId site, string variant) => _variants.Keys.Any(key => key.Profile.Site == site && key.Variant == variant);

        internal bool HasProfilesAt(DaggerfallSiteId site) => _authored.Keys.Any(key => key.Site == site) || _variants.Keys.Any(key => key.Profile.Site == site);

        internal bool TryResolve(DaggerfallWorldProfileKey key, out DaggerfallSiteProfile? profile)
        {
            lock (_gate)
            {
                if (_resolved.TryGetValue(key, out profile)) return true;
                if (_authored.TryGetValue(key, out DaggerfallAuthoredSite? authored))
                    profile = Admit(key, authored.Read(), authored.Header.PackId);
                else if (_assembly?.Places(key) == true)
                    profile = Admit(key, _assembly.Assemble(key), null);
                else return false;
                _resolved.Add(key, profile);
                return true;
            }
        }

        internal bool TryVariant(DaggerfallWorldProfileKey key, string variant, out DaggerfallSiteProfile? profile)
        {
            lock (_gate)
            {
                if (_resolvedVariants.TryGetValue((key, variant), out profile)) return true;
                if (!_variants.TryGetValue((key, variant), out DaggerfallAuthoredSite? site)) return false;
                if (!TryResolve(key, out DaggerfallSiteProfile? original))
                    throw new InvalidOperationException($"World variant '{variant}' has no base profile '{key.LogicalId}'.");
                profile = Admit(key, site.Read(), site.Header.PackId);
                RequireSameTopology(original!, profile, variant);
                _resolvedVariants.Add((key, variant), profile);
                return true;
            }
        }

        /// <summary>Checks a resolved profile is the one its id names, then runs the composition's admission checks.</summary>
        private DaggerfallSiteProfile Admit(DaggerfallWorldProfileKey key, DaggerfallSiteProfile profile, string? packId)
        {
            string source = packId is null ? "The assembled location" : $"Site pack '{packId}'";
            if (profile.Site is not DaggerfallSiteId || profile.ProfileKey != key)
                throw new InvalidOperationException($"{source} resolves '{profile.ProfileKey.LogicalId}' where '{key.LogicalId}' was asked for.");
            if (profile.InteriorBuilding is not null && key.Kind != DaggerfallWorldProfileKind.Interior)
                throw new InvalidOperationException($"{source}: only an interior profile can name a placed building.");
            if (key.Kind == DaggerfallWorldProfileKind.Interior
                && DaggerfallWorldProfileIds.TryParse(key.LogicalId, out _, out DaggerfallSiteBuildingId? building)
                && (profile.InteriorBuilding is not { } interior
                    || building != new DaggerfallSiteBuildingId(interior.BlockX, interior.BlockY, interior.Building.Index)))
                throw new InvalidOperationException($"{source} publishes interior '{key.LogicalId}', which is not the building its closure places.");
            _admit?.Invoke(profile);
            return profile;
        }
    }
}
