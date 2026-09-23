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

/// <summary>Admitted, normalized world closures selectable by their real Daggerfall site identity.</summary>
internal sealed class DaggerfallSiteProfiles
{
    private readonly IReadOnlyDictionary<DaggerfallWorldProfileKey, PrivateersHoldInputs> _profiles;

    internal DaggerfallSiteProfiles(IEnumerable<PrivateersHoldInputs> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        Dictionary<DaggerfallWorldProfileKey, PrivateersHoldInputs> admitted = [];
        foreach (PrivateersHoldInputs profile in profiles)
        {
            ArgumentNullException.ThrowIfNull(profile);
            if (profile.Site is not DaggerfallSiteId)
                throw new ArgumentException("A transition profile must name its selected Daggerfall site.", nameof(profiles));
            DaggerfallWorldProfileKey key = profile.ProfileKey.Validate();
            if (!admitted.TryAdd(key, profile))
                throw new ArgumentException($"The selected content repeats world profile '{key.LogicalId}'.", nameof(profiles));
        }
        _profiles = new ReadOnlyDictionary<DaggerfallWorldProfileKey, PrivateersHoldInputs>(admitted);
    }

    internal IReadOnlyCollection<DaggerfallWorldProfileKey> Keys => _profiles.Keys.ToArray();
    internal bool TryGet(DaggerfallWorldProfileKey key, out PrivateersHoldInputs profile) => _profiles.TryGetValue(key, out profile!);
    internal PrivateersHoldInputs Require(DaggerfallWorldProfileKey key) => TryGet(key, out PrivateersHoldInputs profile)
        ? profile
        : throw new InvalidOperationException($"No normalized world profile is admitted for '{key.LogicalId}'.");

    internal PrivateersHoldInputs RequireLogicalProfile(string logicalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalId);
        PrivateersHoldInputs[] matches = _profiles.Where(entry => StringComparer.Ordinal.Equals(entry.Key.LogicalId, logicalId))
            .Select(entry => entry.Value).ToArray();
        return matches.Length == 1 ? matches[0]
            : throw new InvalidOperationException($"No unique admitted world profile has logical id '{logicalId}'.");
    }

    internal PrivateersHoldInputs RequireUniqueSite(DaggerfallSiteId site)
    {
        PrivateersHoldInputs[] matches = _profiles.Where(entry => entry.Key.Site == site).Select(entry => entry.Value).ToArray();
        return matches.Length == 1 ? matches[0]
            : throw new InvalidOperationException($"Saved site '{site}' does not identify one world profile.");
    }
}
