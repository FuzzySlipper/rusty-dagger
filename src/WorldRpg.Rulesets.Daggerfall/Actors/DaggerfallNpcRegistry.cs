using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What kind of person an NPC is: identity stability follows the kind.</summary>
public enum DaggerfallNpcKind
{
    /// <summary>A fixed person of a site: one stable identity across sessions.</summary>
    Static,
    /// <summary>A quest person: stable while its quest names it.</summary>
    Questor,
    /// <summary>An incidental civilian, either transient or a source placement with a stable key.</summary>
    Civilian,
}

/// <summary>Whether an NPC currently answers the world.</summary>
public enum DaggerfallNpcPresence
{
    Active,
    Hidden,
    Removed,
}

/// <summary>One NPC's site binding: where it belongs, not where it stands.</summary>
/// <param name="Region">The classic region.</param>
/// <param name="Location">The location name.</param>
/// <param name="Building">The building key, empty when the NPC belongs to no building.</param>
public readonly record struct DaggerfallNpcSite(int Region, string Location, string Building, string? ProfileId = null);

/// <summary>One NPC: its durable identity, kind, site, appearance, role and presence.</summary>
/// <param name="DurableId">The durable actor identity.</param>
/// <param name="Kind">What kind of person it is.</param>
/// <param name="StableKey">The site-stable key for statics and questors; empty for civilians.</param>
/// <param name="Site">The site it belongs to.</param>
/// <param name="Appearance">What the world sees.</param>
/// <param name="Role">The role: questor, civilian, guard, shopkeeper.</param>
/// <param name="Services">The services it offers: talk, shop, quest.</param>
/// <param name="Presence">Whether it currently answers the world.</param>
/// <param name="X">The detached profile-frame X position, held only while no live actor owns the pose.</param>
/// <param name="Y">The detached profile-frame Y position, held only while no live actor owns the pose.</param>
/// <param name="Z">The detached profile-frame Z position, held only while no live actor owns the pose.</param>
public sealed record DaggerfallNpc(
    long DurableId,
    DaggerfallNpcKind Kind,
    string StableKey,
    DaggerfallNpcSite Site,
    DaggerfallNpcAppearance Appearance,
    string Role,
    IReadOnlyList<string> Services,
    DaggerfallNpcPresence Presence,
    float? X,
    float? Y,
    float? Z)
{
    public string? DisplayName { get; init; }
    /// <summary>Current physical profile; Site retains the stable social origin.</summary>
    internal DaggerfallWorldProfileKey? Profile { get; init; }
}

/// <summary>
/// Live person metadata attached directly to materialized person entities in <c>EntityStore</c>.
/// Eliminates any separate runtime NPC dictionary beside <c>EntityStore</c> and <see cref="DaggerfallActorLedger"/>.
/// </summary>
internal sealed class ActorPersonComponent(DaggerfallNpc snapshot)
{
    internal DaggerfallNpc Snapshot { get; set; } = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
}

/// <summary>
/// Thin stateless view and person-registration facade over <c>EntityStore</c> (<see cref="ActorPersonComponent"/>)
/// and <see cref="DaggerfallActorLedger"/>. Holds zero duplicated mutable NPC dictionaries: live active-scene
/// people live on their Engine entities in <c>EntityStore</c>, while off-scene, unmaterialized, detached,
/// travelling, and removed people live in <see cref="DaggerfallActorLedger"/>.
/// </summary>
public sealed class DaggerfallNpcRegistry
{
    private readonly ActorsState? _actors;
    private readonly DaggerfallActorLedger _ledger;
    private Func<DaggerfallNpc, string>? _name;
    private Func<long, bool> _questUnavailable = _ => false;

    /// <summary>Standalone constructor for unit tests without a session <c>EntityStore</c>; backed by a local ledger.</summary>
    public DaggerfallNpcRegistry() : this(null, new DaggerfallActorLedger()) { }

    /// <summary>Session constructor projecting directly over the canonical <c>EntityStore</c> and <see cref="DaggerfallActorLedger"/>.</summary>
    internal DaggerfallNpcRegistry(ActorsState? actors, DaggerfallActorLedger ledger)
    {
        _actors = actors;
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    internal void BindNames(Func<DaggerfallNpc, string> name)
    {
        _name = name;
        if (_actors is not null)
        {
            foreach (ActorState actor in _actors.All)
            {
                if (actor.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? person)
                    && person.Snapshot.DisplayName is null)
                {
                    person.Snapshot = person.Snapshot with { DisplayName = name(person.Snapshot) };
                }
            }
        }
        foreach (ActorLedgerEntry entry in _ledger.All.ToArray())
        {
            if (entry.Social is { DisplayNameOverride: null } social
                && FromLedgerEntry(entry) is { } npc)
            {
                string resolved = name(npc);
                _ledger.Upsert(entry with { Social = social with { DisplayNameOverride = resolved } });
            }
        }
    }

    internal void BindQuestPresence(Func<long, bool> unavailable) => _questUnavailable = unavailable;

    /// <summary>The identity allocator the session wires once; civilians cannot mint without it.</summary>
    public DurableIdentityAllocator? Identities { get; set; }

    /// <summary>
    /// Registers a static or questor: the same site and key always answer the same identity, so a
    /// quest reference survives unload and restore.
    /// </summary>
    public long RegisterStable(DaggerfallNpcKind kind, string stableKey, DaggerfallNpcSite site, DaggerfallNpcAppearance appearance, string role, IReadOnlyList<string> services)
    {
        if (kind == DaggerfallNpcKind.Civilian)
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Civilians regenerate; they never hold a stable key.");

        ArgumentException.ThrowIfNullOrWhiteSpace(stableKey);
        ValidateSite(site);
        ValidateAppearance(appearance);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(services);
        if (FindByStableKey(kind, site, stableKey) is long existing)
            return existing;

        DurableIdentityAllocator allocator = Identities ?? throw new InvalidOperationException("The NPC registry mints no identity before the session wires its allocator.");
        long durableId = checked((long)allocator.Allocate(DurableIdentityKind.Actor).Value);
        DaggerfallNpc created = ApplyDefaultName(new DaggerfallNpc(
            durableId, kind, stableKey, site, appearance, role, [.. services], DaggerfallNpcPresence.Active, null, null, null));
        ActorOrigin origin = ActorOrigin.MintedActor(DaggerfallActorKinds.StaticNpc, gender: appearance.Gender);
        _ledger.Upsert(new ActorLedgerEntry(durableId, origin, Social: ActorSocialDiff.FromNpc(created)));
        return durableId;
    }

    /// <summary>
    /// Idempotently registers a deterministic <c>SeededSlot</c> or <c>CastIndividual</c> NPC under its deterministic
    /// high-band <paramref name="durableId"/> without allocating from the monotonic identity ledger.
    /// </summary>
    internal long RegisterDeterministic(long durableId, DaggerfallNpcKind kind, string stableKey, DaggerfallNpcSite site,
        DaggerfallNpcAppearance appearance, string role, IReadOnlyList<string> services, string? displayName = null,
        DaggerfallWorldProfileKey? homeProfile = null)
    {
        if (durableId <= 0) throw new ArgumentOutOfRangeException(nameof(durableId));
        ArgumentException.ThrowIfNullOrWhiteSpace(stableKey);
        ValidateSite(site);
        ValidateAppearance(appearance);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(services);

        if (TryGetLivePerson(durableId, out ActorPersonComponent? livePerson))
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                SetDisplayName(durableId, displayName);
            return durableId;
        }

        if (_ledger.TryGet(durableId, out ActorLedgerEntry? existing) && existing is not null)
        {
            if (existing.Social is not null)
            {
                if (!string.IsNullOrWhiteSpace(displayName))
                    SetDisplayName(durableId, displayName);
                return durableId;
            }
            // Existing entry without Social (e.g., pre-visit ActorReferralDiff): hydrate baseline Social while preserving Referral/Location/etc.
            string? resolvedName = !string.IsNullOrWhiteSpace(displayName)
                ? displayName
                : existing.Referral?.DisplayNameOverride;
            DaggerfallNpc hydrated = ApplyDefaultName(new DaggerfallNpc(
                durableId, kind, stableKey, site, appearance, role, [.. services], DaggerfallNpcPresence.Active, null, null, null)
            {
                DisplayName = resolvedName,
            });
            _ledger.Upsert(existing with { Social = ActorSocialDiff.FromNpc(hydrated) });
            return durableId;
        }

        DaggerfallNpc staged = ApplyDefaultName(new DaggerfallNpc(
            durableId, kind, stableKey, site, appearance, role, [.. services], DaggerfallNpcPresence.Active, null, null, null)
        {
            DisplayName = displayName,
        });
        ActorOrigin origin = ResolveDeterministicOrigin(kind, stableKey, homeProfile, appearance.Gender);
        _ledger.Upsert(new ActorLedgerEntry(durableId, origin, Social: ActorSocialDiff.FromNpc(staged)));
        return durableId;
    }

    /// <summary>Registers a civilian: always a fresh identity, even for identical visuals.</summary>
    public long RegisterCivilian(DaggerfallNpcSite site, DaggerfallNpcAppearance appearance, string role, IReadOnlyList<string> services)
    {
        ValidateSite(site);
        ValidateAppearance(appearance);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(services);
        DurableIdentityAllocator allocator = Identities ?? throw new InvalidOperationException("The NPC registry mints no identity before the session wires its allocator.");
        long durableId = checked((long)allocator.Allocate(DurableIdentityKind.Actor).Value);
        DaggerfallNpc created = ApplyDefaultName(new DaggerfallNpc(
            durableId, DaggerfallNpcKind.Civilian, string.Empty, site, appearance, role, [.. services], DaggerfallNpcPresence.Active, null, null, null));
        ActorOrigin origin = ActorOrigin.MintedActor(DaggerfallActorKinds.Civilian, gender: appearance.Gender);
        _ledger.Upsert(new ActorLedgerEntry(durableId, origin, Social: ActorSocialDiff.FromNpc(created)));
        return durableId;
    }

    /// <summary>
    /// Registers one source-backed population placement idempotently. The key is the importer placement
    /// identity, not a generated demo name, so cell re-entry returns the existing civilian and keeps its
    /// durable actor/corpse/quest relationships intact.
    /// </summary>
    public long RegisterPopulationCivilian(DaggerfallNpcSite site, string sourceKey, DaggerfallNpcAppearance appearance, string role, IReadOnlyList<string> services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ValidateSite(site);
        ValidateAppearance(appearance);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(services);
        if (FindByStableKey(DaggerfallNpcKind.Civilian, site, sourceKey) is long existing)
            return existing;

        DurableIdentityAllocator allocator = Identities ?? throw new InvalidOperationException("The NPC registry mints no identity before the session wires its allocator.");
        long durableId = checked((long)allocator.Allocate(DurableIdentityKind.Actor).Value);
        DaggerfallNpc created = ApplyDefaultName(new DaggerfallNpc(
            durableId, DaggerfallNpcKind.Civilian, sourceKey, site, appearance, role, [.. services], DaggerfallNpcPresence.Active, null, null, null));
        ActorOrigin origin = ActorOrigin.MintedActor(DaggerfallActorKinds.Civilian, gender: appearance.Gender);
        _ledger.Upsert(new ActorLedgerEntry(durableId, origin, Social: ActorSocialDiff.FromNpc(created)));
        return durableId;
    }

    /// <summary>
    /// Refreshes source-authored appearance and service facts while retaining the one durable
    /// identity, current pose, presence and any actor/corpse relationship.
    /// </summary>
    public void RefreshPopulationFacts(long durableId, DaggerfallNpcAppearance appearance, string role, IReadOnlyList<string> services)
    {
        DaggerfallNpc npc = Require(durableId);
        if (npc.Kind != DaggerfallNpcKind.Civilian || string.IsNullOrWhiteSpace(npc.StableKey)
            || !npc.StableKey.StartsWith("population/", StringComparison.Ordinal))
            throw new ArgumentException($"NPC {durableId} is not a source-backed population civilian.", nameof(durableId));
        ValidateAppearance(appearance);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(services);
        Mutate(durableId, current => current with { Appearance = appearance, Role = role, Services = [.. services] }, updateLocation: null);
    }

    /// <summary>Reads one NPC by its durable identity.</summary>
    public DaggerfallNpc Require(long durableId) =>
        TryGet(durableId, out DaggerfallNpc? npc)
            ? npc
            : throw new InvalidOperationException($"No NPC answers durable identity {durableId}.");

    /// <summary>Reads one NPC by its durable identity, preferring live <c>EntityStore</c> metadata and falling back to <see cref="DaggerfallActorLedger"/>.</summary>
    internal bool TryGet(long durableId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DaggerfallNpc? npc)
    {
        if (TryGetLivePerson(durableId, out ActorPersonComponent? person))
        {
            npc = person.Snapshot;
            return true;
        }
        if (_ledger.TryGet(durableId, out ActorLedgerEntry? entry) && entry is not null
            && FromLedgerEntry(entry) is { } ledgerNpc)
        {
            npc = ledgerNpc;
            return true;
        }
        npc = null;
        return false;
    }

    internal bool IsStatic(long durableId) => TryGet(durableId, out DaggerfallNpc? npc) && npc.Kind == DaggerfallNpcKind.Static;

    /// <summary>
    /// Reads presence for a live or off-scene actor. Actors with no person record are ordinary
    /// authored or encounter actors and remain eligible; an admitted civilian becomes ineligible
    /// as soon as the population owner hides it for night or unload.
    /// </summary>
    internal bool IsGameplayActive(long durableId) =>
        (!TryGet(durableId, out DaggerfallNpc? npc) || npc.Presence == DaggerfallNpcPresence.Active)
        && !_questUnavailable(durableId);

    /// <summary>Every known NPC projected from live <c>EntityStore</c> people and <see cref="DaggerfallActorLedger"/> social records.</summary>
    public IReadOnlyList<DaggerfallNpc> All
    {
        get
        {
            Dictionary<long, DaggerfallNpc> merged = [];
            if (_actors is not null)
            {
                foreach (ActorState actor in _actors.All)
                {
                    if (actor.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? person))
                        merged[actor.DurableId] = person.Snapshot;
                }
            }
            foreach (ActorLedgerEntry entry in _ledger.All)
            {
                if (merged.ContainsKey(entry.DurableId)) continue;
                if (FromLedgerEntry(entry) is { } ledgerNpc)
                    merged[entry.DurableId] = ledgerNpc;
            }
            return [.. merged.Values.OrderBy(npc => npc.DurableId)];
        }
    }

    /// <summary>Retains the selected display name on the canonical NPC identity.</summary>
    public void SetDisplayName(long durableId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Mutate(durableId, current => current with { DisplayName = name }, updateLocation: null);
    }

    /// <summary>Hides or reveals one NPC without duplicating it.</summary>
    public void SetPresence(long durableId, DaggerfallNpcPresence presence)
    {
        if (!Enum.IsDefined(presence))
            throw new ArgumentOutOfRangeException(nameof(presence), presence, "An NPC presence names a state the contract does not declare.");

        Mutate(durableId,
            current => current with { Presence = presence },
            updateLocation: existing => presence == DaggerfallNpcPresence.Removed
                ? ActorLocationOverride.Removed
                : existing.Location?.Kind == ActorLocationKind.Removed ? null : existing.Location);
    }

    /// <summary>
    /// Places one NPC in a physical profile at a detached profile-frame pose. The pose is the
    /// placement for a person with no live actor (a projected questor, or a civilian about to be
    /// materialized); once an actor exists its Engine Transform owns the pose.
    /// </summary>
    internal void Place(long durableId, DaggerfallWorldProfileKey profile, WorldPoint position)
    {
        profile.Validate();
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new ArgumentException("NPC profile position must be finite.");

        DaggerfallNpc updated = Require(durableId) with
        {
            Profile = profile,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Presence = DaggerfallNpcPresence.Active,
        };
        if (TryGetLivePerson(durableId, out ActorPersonComponent? person))
            person.Snapshot = updated;

        ActorPoseOverride pose = new(position.X, position.Y, position.Z, 0f);
        if (_ledger.TryGet(durableId, out ActorLedgerEntry? existing) && existing is not null)
        {
            bool detached = existing.Location?.Detached == true;
            bool trafficVisitor = existing.Location?.TrafficVisitor == true;
            ActorLocationOverride nextLocation = existing.Location?.Kind == ActorLocationKind.Dead
                ? existing.Location
                : ActorLocationOverride.AtSite(profile, pose, detached, trafficVisitor);
            _ledger.Upsert(existing with
            {
                Social = ActorSocialDiff.FromNpc(updated, existing.Social),
                Location = nextLocation,
            });
        }
        else
        {
            ActorOrigin origin = ResolveOriginForActor(durableId, updated, profile);
            _ledger.Upsert(new ActorLedgerEntry(
                durableId,
                origin,
                Social: ActorSocialDiff.FromNpc(updated),
                Location: ActorLocationOverride.AtSite(profile, pose, detached: false)));
        }
    }

    /// <summary>
    /// Binds one NPC to the physical profile where its live actor stands. The live actor's Transform
    /// is the only pose owner, so any detached registry pose is dropped. Untouched deterministic staging
    /// entries prune from <see cref="DaggerfallActorLedger"/> once live in <c>EntityStore</c>.
    /// </summary>
    internal void Bind(long durableId, DaggerfallWorldProfileKey profile)
    {
        profile.Validate();
        DaggerfallNpc updated = Require(durableId) with
        {
            Profile = profile,
            X = null,
            Y = null,
            Z = null,
            Presence = DaggerfallNpcPresence.Active,
        };
        bool live = TryGetLivePerson(durableId, out ActorPersonComponent? person);
        if (live)
            person!.Snapshot = updated;

        if (_ledger.TryGet(durableId, out ActorLedgerEntry? existing) && existing is not null)
        {
            if (live && IsUntouchedDeterministicStagingEntry(existing, durableId, profile))
            {
                _ledger.Remove(durableId);
                return;
            }
            ActorLocationOverride? nextLocation = existing.Location switch
            {
                { Kind: ActorLocationKind.WithParty or ActorLocationKind.OnOverlandBand or ActorLocationKind.Dead or ActorLocationKind.Removed } keep => keep,
                { Kind: ActorLocationKind.AtSite } atSite => atSite with { Profile = DaggerfallWorldProfileKeySave.Capture(profile), Pose = null },
                null when !live || existing.Origin.Kind == ActorOriginKind.Minted => ActorLocationOverride.AtSite(profile, pose: null, detached: false),
                _ => existing.Location,
            };
            _ledger.Upsert(existing with
            {
                Social = ActorSocialDiff.FromNpc(updated, existing.Social),
                Location = nextLocation,
            });
        }
        else if (!live)
        {
            ActorOrigin origin = ResolveOriginForActor(durableId, updated, profile);
            _ledger.Upsert(new ActorLedgerEntry(
                durableId,
                origin,
                Social: ActorSocialDiff.FromNpc(updated),
                Location: ActorLocationOverride.AtSite(profile, pose: null, detached: false)));
        }
    }

    /// <summary>Drops the detached pose once a live actor owns this NPC's pose.</summary>
    internal void ReleasePose(long durableId)
    {
        if (TryGetLivePerson(durableId, out ActorPersonComponent? person)
            && (person.Snapshot.X is not null || person.Snapshot.Y is not null || person.Snapshot.Z is not null))
        {
            person.Snapshot = person.Snapshot with { X = null, Y = null, Z = null };
        }
        if (_ledger.TryGet(durableId, out ActorLedgerEntry? existing) && existing is not null)
        {
            if (_actors is not null && _actors.TryGet(durableId, out ActorState? liveActor)
                && liveActor.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? livePerson)
                && livePerson.Snapshot.Profile is { } liveProfile
                && IsUntouchedDeterministicStagingEntry(existing, durableId, liveProfile))
            {
                _ledger.Remove(durableId);
                return;
            }
            if (existing.Location is { Kind: ActorLocationKind.AtSite, Pose: not null } loc)
                _ledger.Upsert(existing with { Location = loc with { Pose = null } });
        }
    }

    /// <summary>Removes a failed admission's current placement while retaining its stable identity in the ledger.</summary>
    internal void Unplace(long durableId)
    {
        DaggerfallNpc updated = Require(durableId) with { Profile = null, X = null, Y = null, Z = null };
        if (TryGetLivePerson(durableId, out ActorPersonComponent? person))
            person.Snapshot = updated;

        if (_ledger.TryGet(durableId, out ActorLedgerEntry? existing) && existing is not null)
        {
            _ledger.Upsert(existing with
            {
                Social = ActorSocialDiff.FromNpc(updated, existing.Social),
                Location = null,
            });
        }
        else
        {
            ActorOrigin origin = ResolveOriginForActor(durableId, updated, fallbackProfile: null);
            _ledger.Upsert(new ActorLedgerEntry(durableId, origin, Social: ActorSocialDiff.FromNpc(updated), Location: null));
        }
    }

    internal IReadOnlyList<DaggerfallNpc> Capture() => All;

    /// <summary>Restores NPCs into the backing ledger; refuses a record the contract would not accept live.</summary>
    internal void Restore(IEnumerable<DaggerfallNpc> npcs)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        _ledger.Clear();
        HashSet<string> seenStable = new(StringComparer.Ordinal);
        foreach (DaggerfallNpc npc in npcs)
        {
            ArgumentNullException.ThrowIfNull(npc);
            ValidateSite(npc.Site);
            ValidateAppearance(npc.Appearance);
            npc.Profile?.Validate();
            if ((npc.X is null) != (npc.Y is null) || (npc.X is null) != (npc.Z is null)
                || npc.X is float x && !float.IsFinite(x) || npc.Y is float y && !float.IsFinite(y) || npc.Z is float z && !float.IsFinite(z))
                throw new ArgumentException($"NPC {npc.DurableId} has invalid current profile coordinates.");
            if (npc.DisplayName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(npc.DisplayName);
            if (!Enum.IsDefined(npc.Kind) || !Enum.IsDefined(npc.Presence))
                throw new ArgumentOutOfRangeException(nameof(npcs), npc.Kind, "A saved NPC names a kind or presence the contract does not declare.");

            if (npc.Kind != DaggerfallNpcKind.Civilian || !string.IsNullOrWhiteSpace(npc.StableKey))
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(npc.StableKey);
                if (!seenStable.Add(StableIdentityKey(npc.Kind, npc.Site, npc.StableKey)))
                    throw new ArgumentException($"Duplicate restored stable NPC key '{npc.StableKey}'.");
            }

            ActorOrigin origin = ResolveOriginForActor(npc.DurableId, npc, npc.Profile);
            ActorPoseOverride? detachedPose = npc.X is float px && npc.Y is float py && npc.Z is float pz
                ? new ActorPoseOverride(px, py, pz, 0f)
                : null;
            ActorLocationOverride? location = npc.Presence == DaggerfallNpcPresence.Removed
                ? ActorLocationOverride.Removed
                : npc.Profile is { } p
                    ? ActorLocationOverride.AtSite(p, detachedPose, detached: false)
                    : null;
            _ledger.Upsert(new ActorLedgerEntry(npc.DurableId, origin, Social: ActorSocialDiff.FromNpc(npc), Location: location));
        }
    }

    internal static DaggerfallNpc? FromLedgerEntry(ActorLedgerEntry entry)
    {
        if (entry.Social is not { } s) return null;
        DaggerfallWorldProfileKey? profile = entry.Location is { Kind: ActorLocationKind.AtSite or ActorLocationKind.Dead, Profile: { } p }
            ? p.Require()
            : null;
        ActorPoseOverride? pose = entry.Location is { Kind: ActorLocationKind.AtSite or ActorLocationKind.Dead, Pose: { } lp } ? lp : null;
        DaggerfallNpcPresence presence = entry.Location?.Kind == ActorLocationKind.Removed
            ? DaggerfallNpcPresence.Removed
            : (DaggerfallNpcPresence)s.Presence;
        return new DaggerfallNpc(
            entry.DurableId,
            (DaggerfallNpcKind)s.NpcKind,
            s.StableKey,
            new DaggerfallNpcSite(s.Region, s.Location, s.Building, s.ProfileId),
            new DaggerfallNpcAppearance(s.Race, s.Gender, s.BillboardArchive, s.BillboardRecord, s.NameSeed, s.FactionId),
            s.Role,
            s.Services,
            presence,
            pose?.X,
            pose?.Y,
            pose?.Z)
        {
            DisplayName = s.DisplayNameOverride,
            Profile = profile,
        };
    }

    private void Mutate(long durableId, Func<DaggerfallNpc, DaggerfallNpc> update,
        Func<ActorLedgerEntry, ActorLocationOverride?>? updateLocation)
    {
        DaggerfallNpc updated = update(Require(durableId));
        bool live = TryGetLivePerson(durableId, out ActorPersonComponent? person);
        if (live)
            person!.Snapshot = updated;

        if (_ledger.TryGet(durableId, out ActorLedgerEntry? existing) && existing is not null)
        {
            ActorLocationOverride? nextLocation = updateLocation is not null ? updateLocation(existing) : existing.Location;
            _ledger.Upsert(existing with
            {
                Social = ActorSocialDiff.FromNpc(updated, existing.Social),
                Location = nextLocation,
            });
        }
        else if (!live || updated.Presence == DaggerfallNpcPresence.Removed)
        {
            ActorOrigin origin = ResolveOriginForActor(durableId, updated, updated.Profile);
            ActorLocationOverride? location = updated.Presence == DaggerfallNpcPresence.Removed
                ? ActorLocationOverride.Removed
                : updated.Profile is { } p
                    ? ActorLocationOverride.AtSite(p,
                        updated.X is float px && updated.Y is float py && updated.Z is float pz ? new ActorPoseOverride(px, py, pz, 0f) : null,
                        detached: false)
                    : null;
            _ledger.Upsert(new ActorLedgerEntry(durableId, origin, Social: ActorSocialDiff.FromNpc(updated), Location: location));
        }
    }

    private bool TryGetLivePerson(long durableId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ActorPersonComponent? person)
    {
        if (_actors is not null
            && _actors.TryGet(durableId, out ActorState? actor)
            && actor.Actor.TryGet<ActorPersonComponent>(out person))
        {
            return true;
        }
        person = null;
        return false;
    }

    private bool IsUntouchedDeterministicStagingEntry(ActorLedgerEntry entry, long durableId, DaggerfallWorldProfileKey activeProfile)
    {
        if (!DaggerfallPeopleSlots.IsDeterministicId(durableId)) return false;
        if (entry.Vitals is not null || entry.Pose is not null || entry.Combat is not null || entry.Referral is not null)
            return false;
        if (entry.Location is { Kind: not ActorLocationKind.AtSite })
            return false;
        if (entry.Location is { Kind: ActorLocationKind.AtSite, Profile: { } lp } && lp.Require() != activeProfile)
            return false;
        if (entry.Social is { } s
            && (s.Met || s.Muted || s.DispositionDelta != 0 || s.WitnessedCrimes is { Length: > 0 }
                || s.ReferralTags is { Length: > 0 } || s.EscortPortrait is not null || s.StagedPartyRole is not null
                || s.Presence != (int)DaggerfallNpcPresence.Active))
            return false;
        if (_actors is not null && _actors.TryGet(durableId, out ActorState? actor)
            && actor.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? originComp))
        {
            if (originComp.IsRelocated) return false;
            if (originComp.Origin.SiteProfile is { } homeProfile && homeProfile.Require() != activeProfile) return false;
            if (originComp.BaselineNpc is { DisplayName: not null } baseNpc
                && entry.Social?.DisplayNameOverride is { } savedName
                && !StringComparer.Ordinal.Equals(savedName, baseNpc.DisplayName))
                return false;
        }
        return true;
    }

    private ActorOrigin ResolveOriginForActor(long durableId, DaggerfallNpc npc, DaggerfallWorldProfileKey? fallbackProfile)
    {
        if (_actors is not null && _actors.TryGet(durableId, out ActorState? live)
            && live.Actor.TryGet<ActorOriginComponent>(out ActorOriginComponent? originComp))
        {
            return originComp.Origin;
        }
        if (DaggerfallPeopleSlots.IsDeterministicId(durableId))
            return ResolveDeterministicOrigin(npc.Kind, npc.StableKey, fallbackProfile, npc.Appearance.Gender);
        return ActorOrigin.MintedActor(
            npc.Kind == DaggerfallNpcKind.Civilian ? DaggerfallActorKinds.Civilian : DaggerfallActorKinds.StaticNpc,
            gender: npc.Appearance.Gender);
    }

    private static ActorOrigin ResolveDeterministicOrigin(DaggerfallNpcKind kind, string stableKey,
        DaggerfallWorldProfileKey? homeProfile, string gender)
    {
        if (stableKey.StartsWith("cast:", StringComparison.Ordinal))
            return ActorOrigin.Cast(stableKey["cast:".Length..], homeProfile);
        if (homeProfile is { } hp)
            return ActorOrigin.Seeded(hp, stableKey);
        return ActorOrigin.MintedActor(
            kind == DaggerfallNpcKind.Civilian ? DaggerfallActorKinds.Civilian : DaggerfallActorKinds.StaticNpc,
            gender: gender);
    }

    private long? FindByStableKey(DaggerfallNpcKind kind, DaggerfallNpcSite site, string stableKey)
    {
        string target = StableIdentityKey(kind, site, stableKey);
        foreach (DaggerfallNpc npc in All)
        {
            if (StringComparer.Ordinal.Equals(StableIdentityKey(npc.Kind, npc.Site, npc.StableKey), target))
                return npc.DurableId;
        }
        return null;
    }

    private DaggerfallNpc ApplyDefaultName(DaggerfallNpc npc) =>
        npc.DisplayName is null && _name is not null
            ? npc with { DisplayName = _name(npc) }
            : npc;

    private static string StableIdentityKey(DaggerfallNpcKind kind, DaggerfallNpcSite site, string stableKey) =>
        $"{kind}\0{site.Region}\0{site.Location}\0{site.Building}\0{site.ProfileId}\0{stableKey}";

    private static void ValidateSite(DaggerfallNpcSite site)
    {
        if (site.Region is < 0 or > 61 || string.IsNullOrWhiteSpace(site.Location))
            throw new ArgumentOutOfRangeException(nameof(site), site.Region, "An NPC site names no classic region or location.");
    }

    private static void ValidateAppearance(DaggerfallNpcAppearance appearance)
    {
        if (string.IsNullOrWhiteSpace(appearance.Race) || (appearance.Gender != "Male" && appearance.Gender != "Female"))
            throw new ArgumentOutOfRangeException(nameof(appearance), appearance.Race, "An NPC appearance states no race or gender.");

        if (appearance.BillboardArchive < 0 || appearance.BillboardRecord < 0 || appearance.FactionId < 0)
            throw new ArgumentOutOfRangeException(nameof(appearance), appearance.BillboardArchive, "An NPC appearance states no billboard or faction.");
    }
}
