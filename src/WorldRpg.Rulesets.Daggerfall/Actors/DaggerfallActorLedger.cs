using System.Text.Json.Serialization;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Attached to a materialized encounter band member so remaining pending band members place beside it.</summary>
internal sealed record DaggerfallEncounterBandMember(int BandId, ulong Generation);

/// <summary>
/// Attached to every live non-player actor in <c>EntityStore</c> to record its canonical <see cref="ActorOrigin"/>
/// and initial baseline so <see cref="DaggerfallActorLedger"/> can compute true sparse diffs and self-prune
/// untouched <see cref="ActorOriginKind.SeededSlot"/> and <see cref="ActorOriginKind.CastIndividual"/> actors to zero save bytes.
/// </summary>
internal sealed class ActorOriginComponent(
    ActorOrigin origin,
    WorldPoint baselinePose,
    float baselineHeading,
    DaggerfallStatsSave? baselineStats = null,
    DaggerfallNpc? baselineNpc = null,
    bool isRelocated = false)
{
    private WorldPoint? _lastWanderedPose;
    private float _lastWanderedHeading;

    internal ActorOrigin Origin { get; } = origin ?? throw new ArgumentNullException(nameof(origin));
    internal WorldPoint BaselinePose { get; private set; } = baselinePose;
    internal float BaselineHeading { get; } = baselineHeading;
    internal DaggerfallStatsSave? BaselineStats { get; } = baselineStats;
    internal DaggerfallNpc? BaselineNpc { get; } = baselineNpc;
    internal bool IsRelocated { get; } = isRelocated;

    internal void SetBaselinePose(WorldPoint pose) => BaselinePose = pose;

    /// <summary>Records an idle street-wander pose without overwriting the authored <see cref="BaselinePose"/>.</summary>
    internal void RecordIdleWanderPose(WorldPoint profilePose, float heading)
    {
        _lastWanderedPose = profilePose;
        _lastWanderedHeading = heading;
    }

    internal bool MatchesIdleWanderPose(WorldPoint profilePose, float heading) =>
        _lastWanderedPose is { } wandered
        && MathF.Abs(profilePose.X - wandered.X) < 1e-4f
        && MathF.Abs(profilePose.Y - wandered.Y) < 1e-4f
        && MathF.Abs(profilePose.Z - wandered.Z) < 1e-4f
        && MathF.Abs(heading - _lastWanderedHeading) < 1e-4f;
}

/// <summary>How an actor in <see cref="DaggerfallActorLedger"/> came to exist.</summary>
internal enum ActorOriginKind
{
    /// <summary>Deterministic slot (block spawner slot, exterior walker slot, or fixed authored placement).</summary>
    SeededSlot,

    /// <summary>Genuine unique named individual authored in <c>daggerfall.people/cast/individuals.json</c>.</summary>
    CastIndividual,

    /// <summary>Dynamically minted actor (encounter enemy, quest foe, guard reinforcement, runtime NPC).</summary>
    Minted,
}

/// <summary>Origin metadata for an <see cref="ActorLedgerEntry"/>.</summary>
internal sealed record ActorOrigin(
    ActorOriginKind Kind,
    DaggerfallWorldProfileKeySave? SiteProfile = null,
    string? SlotKey = null,
    string? CastId = null,
    string? DefinitionId = null,
    int? Level = null,
    ulong? CreationSeed = null,
    string? Gender = null,
    string? SpawnerId = null,
    ActorHarmPolicyKind? HarmPolicy = null,
    ActorInteractionPolicyKind? InteractionPolicy = null)
{
    internal static ActorOrigin Seeded(
        DaggerfallWorldProfileKey profile,
        string slotKey,
        string? spawnerId = null,
        ActorHarmPolicyKind? harmPolicy = null,
        ActorInteractionPolicyKind? interactionPolicy = null) =>
        new(ActorOriginKind.SeededSlot, DaggerfallWorldProfileKeySave.Capture(profile), slotKey,
            SpawnerId: spawnerId, HarmPolicy: harmPolicy, InteractionPolicy: interactionPolicy);

    internal static ActorOrigin FixedPlacement(DaggerfallWorldProfileKey profile, long entityId) =>
        new(ActorOriginKind.SeededSlot, DaggerfallWorldProfileKeySave.Capture(profile), $"fixed:{entityId}");

    internal bool IsFixedPlacement =>
        Kind == ActorOriginKind.SeededSlot && SlotKey is { } slot && slot.StartsWith("fixed:", StringComparison.Ordinal);

    internal static ActorOrigin Cast(
        string castId,
        DaggerfallWorldProfileKey? homeProfile = null,
        ActorHarmPolicyKind? harmPolicy = null,
        ActorInteractionPolicyKind? interactionPolicy = null) =>
        new(ActorOriginKind.CastIndividual,
            SiteProfile: homeProfile is { } hp ? DaggerfallWorldProfileKeySave.Capture(hp) : null,
            CastId: castId,
            HarmPolicy: harmPolicy,
            InteractionPolicy: interactionPolicy);

    internal static ActorOrigin MintedActor(string definitionId, int level = 1, ulong? creationSeed = null, string? gender = null) =>
        new(ActorOriginKind.Minted, DefinitionId: definitionId, Level: level, CreationSeed: creationSeed, Gender: gender);

    internal void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind), "Saved actor origin names an unknown kind.");
        if (HarmPolicy is { } harm && !Enum.IsDefined(harm))
            throw new ArgumentOutOfRangeException(nameof(HarmPolicy), "Saved actor origin names an unknown harm policy.");
        if (InteractionPolicy is { } interaction && !Enum.IsDefined(interaction))
            throw new ArgumentOutOfRangeException(nameof(InteractionPolicy), "Saved actor origin names an unknown interaction policy.");
        switch (Kind)
        {
            case ActorOriginKind.SeededSlot:
                ArgumentNullException.ThrowIfNull(SiteProfile);
                SiteProfile.Validate();
                ArgumentException.ThrowIfNullOrWhiteSpace(SlotKey);
                break;
            case ActorOriginKind.CastIndividual:
                ArgumentException.ThrowIfNullOrWhiteSpace(CastId);
                SiteProfile?.Validate();
                break;
            case ActorOriginKind.Minted:
                ArgumentException.ThrowIfNullOrWhiteSpace(DefinitionId);
                if (Level is int lvl && lvl < 1)
                    throw new ArgumentOutOfRangeException(nameof(Level), "Saved minted actor level must be positive.");
                break;
        }
    }
}

/// <summary>Sparse pose override when an actor has moved from its baseline slot or is minted.</summary>
internal sealed record ActorPoseOverride(float X, float Y, float Z, float HeadingRadians)
{
    internal void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z) || !float.IsFinite(HeadingRadians))
            throw new ArgumentOutOfRangeException(nameof(X), "Saved actor pose must be finite.");
    }
}

/// <summary>Sparse vitals/stats diff applied after rebuilding baseline stats from origin.</summary>
internal sealed record ActorVitalsDiff(DaggerfallStatsSave Stats)
{
    internal void Validate() => ArgumentNullException.ThrowIfNull(Stats);
}

/// <summary>Sparse combat, AI, and detached-site persistence state for an actor.</summary>
internal sealed record ActorCombatStateDiff(
    int SpawnDistance = 0,
    bool Passive = false,
    string? WabbajackDefinition = null,
    bool WabbajackActive = false,
    bool ForcedHostile = false,
    bool LanguagePacified = false,
    bool MagicallyPacified = false,
    bool PlayerAllied = false,
    DaggerfallCorruptionOrigin? CorruptionOrigin = null,
    string? TeamOverride = null,
    bool? InfightingOverride = null,
    string? PromotedArchetype = null,
    int? EncounterBandId = null,
    ulong? EncounterGeneration = null,
    DaggerfallActorInventorySave? DetachedInventory = null,
    DaggerfallCorpseSave? DetachedCorpse = null,
    DaggerfallActiveEffectSave[]? DetachedEffects = null)
{
    internal void Validate()
    {
        if (SpawnDistance is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(SpawnDistance));
        if (ForcedHostile && (LanguagePacified || MagicallyPacified))
            throw new ArgumentException("An actor cannot be forced hostile and pacified together.");
        CorruptionOrigin?.Validate();
        DetachedInventory?.Validate();
        DetachedCorpse?.Validate();
        if (DetachedEffects is not null)
            foreach (DaggerfallActiveEffectSave effect in DetachedEffects)
            {
                ArgumentNullException.ThrowIfNull(effect);
                effect.Validate();
            }
    }
}

/// <summary>Sparse social, identity, and overlay state for a person in <see cref="DaggerfallActorLedger"/>.</summary>
internal sealed record ActorSocialDiff(
    int NpcKind,
    string StableKey,
    int Region,
    string Location,
    string Building,
    string? ProfileId,
    string Race,
    string Gender,
    int BillboardArchive,
    int BillboardRecord,
    int NameSeed,
    int FactionId,
    string Role,
    string[] Services,
    string? DisplayNameOverride = null,
    bool Met = false,
    int DispositionDelta = 0,
    string[]? WitnessedCrimes = null,
    string[]? ReferralTags = null,
    bool Muted = false,
    string? EscortPortrait = null,
    int EscortOrder = 0,
    int Presence = 0,
    ActorPartyRole? StagedPartyRole = null)
{
    internal void Validate()
    {
        if (!Enum.IsDefined((DaggerfallNpcKind)NpcKind) || !Enum.IsDefined((DaggerfallNpcPresence)Presence))
            throw new ArgumentOutOfRangeException(nameof(NpcKind), "Saved social diff names an invalid NPC kind or presence.");
        if (StagedPartyRole is { } stagedRole && !Enum.IsDefined(stagedRole))
            throw new ArgumentOutOfRangeException(nameof(StagedPartyRole), "Saved social diff names an invalid staged party role.");
        ArgumentNullException.ThrowIfNull(Services);
        if (DisplayNameOverride is not null) ArgumentException.ThrowIfNullOrWhiteSpace(DisplayNameOverride);
    }

    /// <summary>Builds a canonical <see cref="ActorSocialDiff"/> from an NPC snapshot, preserving prior social deltas.</summary>
    internal static ActorSocialDiff FromNpc(DaggerfallNpc npc, ActorSocialDiff? prior = null) =>
        new(
            (int)npc.Kind,
            npc.StableKey,
            npc.Site.Region,
            npc.Site.Location,
            npc.Site.Building,
            npc.Site.ProfileId,
            npc.Appearance.Race,
            npc.Appearance.Gender,
            npc.Appearance.BillboardArchive,
            npc.Appearance.BillboardRecord,
            npc.Appearance.NameSeed,
            npc.Appearance.FactionId,
            npc.Role,
            [.. npc.Services],
            DisplayNameOverride: npc.DisplayName ?? prior?.DisplayNameOverride,
            Met: prior?.Met ?? false,
            DispositionDelta: prior?.DispositionDelta ?? 0,
            WitnessedCrimes: prior?.WitnessedCrimes,
            ReferralTags: prior?.ReferralTags,
            Muted: prior?.Muted ?? false,
            EscortPortrait: prior?.EscortPortrait,
            EscortOrder: prior?.EscortOrder ?? 0,
            Presence: (int)npc.Presence,
            StagedPartyRole: prior?.StagedPartyRole);
}

/// <summary>Where an actor currently resides in the world.</summary>
internal enum ActorLocationKind
{
    AtSite,
    WithParty,
    OnOverlandBand,
    OffStage,
    Dead,
    Removed,
}

/// <summary>How a <see cref="ActorLocationKind.WithParty"/> actor travels with the player.</summary>
internal enum ActorPartyRole
{
    Escort,
    Driver,
    Companion,
}

/// <summary>Non-home presence override for an actor in <see cref="DaggerfallActorLedger"/>.</summary>
internal sealed record ActorLocationOverride(
    ActorLocationKind Kind,
    DaggerfallWorldProfileKeySave? Profile = null,
    ActorPartyRole? PartyRole = null,
    string? BandId = null,
    ActorPoseOverride? Pose = null,
    bool Detached = false,
    bool TrafficVisitor = false)
{
    internal static ActorLocationOverride AtSite(DaggerfallWorldProfileKey profile, ActorPoseOverride? pose = null, bool detached = false, bool trafficVisitor = false) =>
        new(ActorLocationKind.AtSite, DaggerfallWorldProfileKeySave.Capture(profile), Pose: pose, Detached: detached, TrafficVisitor: trafficVisitor);

    internal static ActorLocationOverride WithParty(ActorPartyRole role = ActorPartyRole.Escort) =>
        new(ActorLocationKind.WithParty, PartyRole: role);

    internal static ActorLocationOverride OnOverlandBand(string bandId) =>
        new(ActorLocationKind.OnOverlandBand, BandId: bandId);

    internal static ActorLocationOverride OffStage { get; } = new(ActorLocationKind.OffStage);
    internal static ActorLocationOverride Removed { get; } = new(ActorLocationKind.Removed);

    internal static ActorLocationOverride Dead(DaggerfallWorldProfileKey profile, ActorPoseOverride? corpsePose = null) =>
        new(ActorLocationKind.Dead, DaggerfallWorldProfileKeySave.Capture(profile), Pose: corpsePose);

    internal void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind), "Saved actor location names an unknown kind.");
        if (Profile is not null) Profile.Validate();
        if (Pose is not null) Pose.Validate();
        if (Kind == ActorLocationKind.AtSite && Profile is null)
            throw new ArgumentException("An AtSite actor location requires a profile.");
        if (Kind == ActorLocationKind.WithParty && (PartyRole is null || !Enum.IsDefined(PartyRole.Value)))
            throw new ArgumentException("A WithParty actor location requires a valid party role.");
        if (Kind == ActorLocationKind.OnOverlandBand && string.IsNullOrWhiteSpace(BandId))
            throw new ArgumentException("An OnOverlandBand actor location requires a band id.");
    }
}

/// <summary>
/// Sparse pre-visit or persistent slot referral metadata on an <see cref="ActorLedgerEntry"/>.
/// Allows callers to address a deterministic <see cref="ActorOriginKind.SeededSlot"/> ("Ask Mara at the west gate")
/// before the player ever visits the target site without synthesizing a full social/NPC record or touching neighbors.
/// </summary>
internal sealed record ActorReferralDiff(
    string[] Tags,
    string? DisplayNameOverride = null)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Tags);
        if (Tags.Length == 0 || Tags.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Saved actor referral requires at least one non-empty tag.", nameof(Tags));
        if (DisplayNameOverride is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(DisplayNameOverride);
    }
}

/// <summary>
/// One sparse world-scoped actor record in <see cref="DaggerfallActorLedger"/>. Covers all actors
/// (people and creatures alike) with no split. <see cref="ActorOriginKind.SeededSlot"/> and
/// <see cref="ActorOriginKind.CastIndividual"/> entries self-prune when all deltas are null.
/// </summary>
internal sealed record ActorLedgerEntry(
    long DurableId,
    ActorOrigin Origin,
    ActorVitalsDiff? Vitals = null,
    ActorPoseOverride? Pose = null,
    ActorCombatStateDiff? Combat = null,
    ActorSocialDiff? Social = null,
    ActorLocationOverride? Location = null,
    ActorReferralDiff? Referral = null)
{
    /// <summary>True when a deterministic slot or cast entry has no non-default delta left to persist.</summary>
    [JsonIgnore]
    internal bool IsEmptyBaseline =>
        Origin.Kind is ActorOriginKind.SeededSlot or ActorOriginKind.CastIndividual
        && Vitals is null
        && Pose is null
        && Combat is null
        && Social is null
        && Location is null
        && Referral is null;

    internal void Validate()
    {
        if (DurableId <= 0 || DurableId == DaggerfallActorIdentity.PlayerEntityId)
            throw new ArgumentOutOfRangeException(nameof(DurableId), "Saved ledger actor identity must be a positive non-player id.");
        ArgumentNullException.ThrowIfNull(Origin);
        Origin.Validate();
        Vitals?.Validate();
        Pose?.Validate();
        Combat?.Validate();
        Social?.Validate();
        Location?.Validate();
        Referral?.Validate();
    }
}

/// <summary>
/// Single world-scoped sparse actor ledger covering all actors (people and creatures alike).
/// <c>EntityStore</c> owns live active-scene entities; <c>DaggerfallActorLedger</c> owns cross-site,
/// detached, social, party-escort, banished, and save-boundary state. Untouched seeded slots carry
/// zero entries and zero save bytes; entries self-prune when their deltas return to baseline.
/// </summary>
internal sealed class DaggerfallActorLedger
{
    private readonly Dictionary<long, ActorLedgerEntry> _entries = [];

    internal int Count => _entries.Count;
    internal IEnumerable<ActorLedgerEntry> All => _entries.Values;

    internal bool TryGet(long durableId, out ActorLedgerEntry? entry) => _entries.TryGetValue(durableId, out entry);

    internal ActorLedgerEntry Require(long durableId) =>
        _entries.TryGetValue(durableId, out ActorLedgerEntry? entry)
            ? entry
            : throw new KeyNotFoundException($"Actor ledger has no entry for durable actor {durableId}.");

    /// <summary>Inserts or updates an entry, self-pruning deterministic baseline entries with no remaining delta.</summary>
    internal bool Upsert(ActorLedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsEmptyBaseline)
        {
            _entries.Remove(entry.DurableId);
            return false;
        }
        _entries[entry.DurableId] = entry;
        return true;
    }

    internal bool Remove(long durableId) => _entries.Remove(durableId);

    internal void Clear() => _entries.Clear();

    /// <summary>Removes every deterministic slot or cast entry whose sparse deltas have returned to empty.</summary>
    internal int Prune()
    {
        long[] empty = [.. _entries.Values.Where(entry => entry.IsEmptyBaseline).Select(entry => entry.DurableId)];
        foreach (long id in empty) _entries.Remove(id);
        return empty.Length;
    }

    internal void Restore(IEnumerable<ActorLedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries.Clear();
        foreach (ActorLedgerEntry entry in entries)
            Upsert(entry);
    }

    internal ActorLedgerEntry[] Capture()
    {
        Prune();
        return [.. _entries.Values.OrderBy(entry => entry.DurableId)];
    }

    /// <summary>Whether an actor is permanently removed/banished from its home slot.</summary>
    internal bool IsRemoved(long durableId) =>
        _entries.TryGetValue(durableId, out ActorLedgerEntry? entry)
        && entry.Location?.Kind == ActorLocationKind.Removed;

    /// <summary>
    /// Step 1 of 3-step deterministic scene admission: suppresses a home <c>SeededSlot</c> or
    /// <c>CastIndividual</c> whenever the ledger has relocated, party-attached, off-staged, or removed it,
    /// guaranteeing no actor is ever duplicated across scenes or party escorts.
    /// </summary>
    internal bool IsHomeSlotSuppressed(long durableId, DaggerfallWorldProfileKey homeProfile)
    {
        if (!_entries.TryGetValue(durableId, out ActorLedgerEntry? entry) || entry.Location is not { } location)
            return false;
        return location.Kind switch
        {
            ActorLocationKind.AtSite => location.Profile?.Require() != homeProfile,
            ActorLocationKind.WithParty => true,
            ActorLocationKind.OnOverlandBand => true,
            ActorLocationKind.OffStage => true,
            ActorLocationKind.Removed => true,
            ActorLocationKind.Dead => location.Profile is { } deadProfile && deadProfile.Require() != homeProfile,
            _ => false,
        };
    }

    /// <summary>All ledger entries explicitly residing at <paramref name="profile"/> (living or dead).</summary>
    internal IReadOnlyList<ActorLedgerEntry> EntriesAtSite(DaggerfallWorldProfileKey profile) =>
        [.. _entries.Values
            .Where(entry => entry.Location is { Kind: ActorLocationKind.AtSite or ActorLocationKind.Dead, Profile: { } p }
                && p.Require() == profile)
            .OrderBy(entry => entry.DurableId)];

    /// <summary>All ledger entries currently travelling with the player party (HUD escorts, drivers, companions).</summary>
    internal IReadOnlyList<ActorLedgerEntry> EntriesWithParty() =>
        [.. _entries.Values
            .Where(entry => entry.Location?.Kind == ActorLocationKind.WithParty)
            .OrderBy(entry => entry.Social?.EscortOrder ?? 0)
            .ThenBy(entry => entry.DurableId)];

    /// <summary>All ledger entries currently travelling on persistent overland band <paramref name="bandId"/>.</summary>
    internal IReadOnlyList<ActorLedgerEntry> EntriesOnOverlandBand(string bandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bandId);
        return
        [
            .. _entries.Values
                .Where(entry => entry.Location?.Kind == ActorLocationKind.OnOverlandBand
                    && StringComparer.Ordinal.Equals(entry.Location.BandId, bandId))
                .OrderBy(entry => entry.DurableId),
        ];
    }

    /// <summary>Moves an actor onto a persistent overland travelling band, suppressing its home slot while in transit.</summary>
    internal void SetOnOverlandBand(long durableId, string bandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bandId);
        if (_entries.TryGetValue(durableId, out ActorLedgerEntry? existing))
        {
            _entries[durableId] = existing with
            {
                Location = ActorLocationOverride.OnOverlandBand(bandId),
                Pose = null,
            };
        }
    }

    /// <summary>
    /// Clears an actor's <see cref="ActorLocationKind.OnOverlandBand"/> transit override on intercept or destination arrival,
    /// placing it at <paramref name="arrivalProfile"/> (or restoring home slot null location when arriving live at home).
    /// When <paramref name="detached"/> is true (arriving at a site the player is not currently visiting), retains an
    /// explicit detached <see cref="ActorLocationKind.AtSite"/> location (including at home) so saved detached state
    /// stays in the inactive combatant set until the player next visits <paramref name="arrivalProfile"/>.
    /// </summary>
    internal void ClearBandLocation(long durableId, DaggerfallWorldProfileKey arrivalProfile, ActorPoseOverride? arrivalPose = null, bool detached = false, bool trafficVisitor = false)
    {
        if (!_entries.TryGetValue(durableId, out ActorLedgerEntry? existing)) return;
        bool isHome = existing.Origin.Kind is ActorOriginKind.SeededSlot or ActorOriginKind.CastIndividual
            && existing.Origin.SiteProfile?.Require() == arrivalProfile;
        ActorLocationOverride? location = detached
            ? ActorLocationOverride.AtSite(arrivalProfile, arrivalPose, detached: true, trafficVisitor: trafficVisitor && !isHome)
            : isHome
                ? null
                : ActorLocationOverride.AtSite(arrivalProfile, arrivalPose, detached: false, trafficVisitor: trafficVisitor);
        Upsert(existing with
        {
            Location = location,
            Pose = arrivalPose ?? existing.Pose,
        });
    }

    /// <summary>Finds one detached actor residing at an inactive site (not <paramref name="activeProfile"/>).</summary>
    internal (DaggerfallWorldProfileKey Profile, ActorLedgerEntry Entry)? FindDetachedAtInactiveSite(long durableId, DaggerfallWorldProfileKey activeProfile)
    {
        if (!_entries.TryGetValue(durableId, out ActorLedgerEntry? entry)) return null;
        if (entry.Location?.Kind is not (ActorLocationKind.AtSite or ActorLocationKind.Dead) || entry.Location.Profile is null) return null;
        DaggerfallWorldProfileKey profile = entry.Location.Profile.Require();
        return profile == activeProfile ? null : (profile, entry);
    }

    /// <summary>
    /// Records a referral targeting a deterministic <see cref="ActorOriginKind.SeededSlot"/> by its canonical
    /// <paramref name="targetProfile"/> and <paramref name="slotId"/>, even before the player has ever visited
    /// <paramref name="targetProfile"/>. Only the referred slot enters the ledger; untouched neighbors stay absent.
    /// Returns the deterministic durable actor identity that resolves on first visit.
    /// </summary>
    internal long RegisterSlotReferral(
        DaggerfallWorldProfileKey targetProfile,
        string slotId,
        string referralTag,
        string? displayNameOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(referralTag);
        if (displayNameOverride is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(displayNameOverride);

        string slotKey = $"{targetProfile.LogicalId}/{slotId}";
        long durableId = DaggerfallPeopleSlots.SeededSlotIdentity(targetProfile.Site, slotKey);
        if (_entries.TryGetValue(durableId, out ActorLedgerEntry? existing))
        {
            List<string> mergedTags = existing.Referral is { Tags: { Length: > 0 } prior }
                ? [.. prior]
                : [];
            if (!mergedTags.Contains(referralTag, StringComparer.Ordinal))
                mergedTags.Add(referralTag);
            string? resolvedName = displayNameOverride ?? existing.Referral?.DisplayNameOverride;
            ActorSocialDiff? updatedSocial = existing.Social is { } s
                ? s with
                {
                    DisplayNameOverride = resolvedName ?? s.DisplayNameOverride,
                    ReferralTags = [.. (s.ReferralTags ?? []).Concat(mergedTags).Distinct(StringComparer.Ordinal)],
                }
                : null;
            _entries[durableId] = existing with
            {
                Referral = new ActorReferralDiff([.. mergedTags], resolvedName),
                Social = updatedSocial,
            };
        }
        else
        {
            _entries[durableId] = new ActorLedgerEntry(
                durableId,
                ActorOrigin.Seeded(targetProfile, slotId),
                Referral: new ActorReferralDiff([referralTag], displayNameOverride));
        }
        return durableId;
    }

    /// <summary>Marks a deterministic slot or authored placement permanently removed (banished), retaining any social tombstone.</summary>
    internal void MarkRemoved(long durableId, ActorOrigin origin, ActorSocialDiff? social = null)
    {
        if (_entries.TryGetValue(durableId, out ActorLedgerEntry? existing))
        {
            ActorSocialDiff? removedSocial = (social ?? existing.Social) is { } s
                ? s with { Presence = (int)DaggerfallNpcPresence.Removed }
                : null;
            _entries[durableId] = existing with
            {
                Location = ActorLocationOverride.Removed,
                Vitals = null,
                Pose = null,
                Combat = null,
                Social = removedSocial,
            };
        }
        else
        {
            ActorSocialDiff? removedSocial = social is { } s
                ? s with { Presence = (int)DaggerfallNpcPresence.Removed }
                : null;
            _entries[durableId] = new(durableId, origin, Social: removedSocial, Location: ActorLocationOverride.Removed);
        }
    }

    /// <summary>Moves an actor onto <see cref="ActorLocationKind.WithParty"/> (HUD escort/companion mode).</summary>
    internal void SetWithParty(long durableId, ActorPartyRole role, string? escortPortrait = null, int escortOrder = 0)
    {
        if (_entries.TryGetValue(durableId, out ActorLedgerEntry? existing))
        {
            ActorSocialDiff? social = existing.Social is { } s
                ? s with { EscortPortrait = escortPortrait ?? s.EscortPortrait, EscortOrder = escortOrder > 0 ? escortOrder : s.EscortOrder }
                : null;
            _entries[durableId] = existing with
            {
                Location = ActorLocationOverride.WithParty(role),
                Pose = null,
                Social = social,
            };
        }
    }

    /// <summary>Clears any detached effects cast by or targeting <paramref name="actorId"/> across all inactive entries.</summary>
    internal void StripDetachedEffectsOf(long actorId)
    {
        foreach (long id in _entries.Keys.ToArray())
        {
            ActorLedgerEntry entry = _entries[id];
            if (entry.Combat?.DetachedEffects is not { Length: > 0 } effects) continue;
            HashSet<string> ended = effects
                .Where(effect => effect.TargetId == actorId || effect.CasterId == actorId)
                .Select(effect => effect.Instance)
                .ToHashSet(StringComparer.Ordinal);
            if (ended.Count == 0) continue;
            DaggerfallActiveEffectSave[] kept = effects
                .Where(effect => !ended.Contains(effect.Instance))
                .ToArray();
            ActorVitalsDiff? vitals = entry.Vitals is { } v
                ? new(DaggerfallStatsSaveBoundary.WithoutEffects(v.Stats, ended))
                : null;
            _entries[id] = entry with
            {
                Vitals = vitals,
                Combat = entry.Combat with { DetachedEffects = kept.Length > 0 ? kept : null },
            };
        }
    }
}
