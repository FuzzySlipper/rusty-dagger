using System.Globalization;
using System.Numerics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Why a hostile vendetta NPC or hired killer is attacking the player (hookable for Den #11164 non-blocking signaling).</summary>
internal enum DaggerfallHostileSignalKind
{
    KinVendettaConfrontation = 0,
    HiredTough = 1,
    BountyHunter = 2,
    DarkBrotherhoodAssassin = 3,
}

/// <summary>
/// Non-blocking hostility reason signal emitted whenever a feuding household's kin confronts the player
/// or a hired tough/bounty hunter/assassin engages (consumed by HUD outcome line today and TTS/bubble signaling in #11164).
/// </summary>
internal sealed record DaggerfallHostileReasonSignal(
    long ActorId,
    string SpeakerName,
    string Gender,
    string FamilyId,
    string HouseholdLabel,
    DaggerfallHostileSignalKind Kind,
    string ReasonLine,
    WorldPoint? Position);

/// <summary>Sparse persisted vendetta retaliation record for one feuding household.</summary>
internal sealed record DaggerfallFeudVendettaEntrySave(
    string FamilyId,
    int LastDispatchDay,
    int DispatchedWaveCount,
    long[]? ActiveHunterActorIds = null,
    long[]? ActiveKinAggressorIds = null)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FamilyId);
        if (!DaggerfallHouseholds.IsValidFamilyId(FamilyId))
            throw new ArgumentException($"Saved vendetta entry family id '{FamilyId}' is invalid.", nameof(FamilyId));
        if (LastDispatchDay < 0)
            throw new ArgumentOutOfRangeException(nameof(LastDispatchDay), "Saved vendetta last-dispatch day must be non-negative.");
        if (DispatchedWaveCount < 0)
            throw new ArgumentOutOfRangeException(nameof(DispatchedWaveCount), "Saved vendetta wave count must be non-negative.");
        if (ActiveHunterActorIds is not null)
        {
            foreach (long id in ActiveHunterActorIds)
                if (id <= 0 || id == DaggerfallActorIdentity.PlayerEntityId)
                    throw new ArgumentOutOfRangeException(nameof(ActiveHunterActorIds), "Saved vendetta hunter actor id must be positive non-player.");
        }
        if (ActiveKinAggressorIds is not null)
        {
            foreach (long id in ActiveKinAggressorIds)
                if (id <= 0 || id == DaggerfallActorIdentity.PlayerEntityId)
                    throw new ArgumentOutOfRangeException(nameof(ActiveKinAggressorIds), "Saved vendetta kin aggressor id must be positive non-player.");
        }
    }
}

/// <summary>Sparse persisted vendetta retaliation state across all feuding households; empty when untouched or settled.</summary>
internal sealed record DaggerfallFeudVendettaSave(DaggerfallFeudVendettaEntrySave[] Entries)
{
    internal static DaggerfallFeudVendettaSave Empty { get; } = new([]);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Entries);
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (DaggerfallFeudVendettaEntrySave entry in Entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            entry.Validate();
            if (!keys.Add(entry.FamilyId))
                throw new ArgumentException($"Saved vendetta state repeats family '{entry.FamilyId}'.");
        }
    }
}

/// <summary>
/// Active blood-feud vendetta retaliation owner (Den #11162, settled design <c>wiki/rusty-dagger/households-and-feuds.md</c>):
/// - Armed/bold adult kin of an open, unmediated blood feud confront and attack the player on sight in town.
/// - Wealthier households, Great Houses, and royal dynasties hire toughs, bounty hunters, or Dark Brotherhood assassins
///   (scaled by household tier/wealth) to pursue the player on the overland map and ambush them in settlements until
///   the feud is settled or a truce is mediated.
/// - Fighting back against vendetta kin aggressors or hired hunters is lawful self-defense via <see cref="IsVendettaAggressor"/>.
/// - Emits typed non-blocking <see cref="DaggerfallHostileReasonSignal"/> events so Den #11164 can attach TTS audio barks
///   or near-actor comic speech bubbles without interrupting combat.
/// Untouched and settled households write zero save bytes.
/// </summary>
internal sealed class DaggerfallFeudVendetta(
    DaggerfallDefinitions definitions,
    DaggerfallSocialConsequences socialConsequences,
    ActorsState actors,
    DaggerfallNpcRegistry npcs,
    DaggerfallNames names,
    Func<ulong> worldSocialSeed,
    Func<int> playerLevel,
    Func<string, WorldPoint, float, float, ActorPose?> trySpawnPose,
    Func<string, ActorPose, int?, long> spawnCombatant,
    Action<long> armVendettaKin,
    Action<long> disarmVendettaKin,
    Action<long> retireVendettaHunter,
    Action<long> makeHostile,
    Action<string> retireOverlandBand,
    Func<IReadOnlyList<DaggerfallPersistentOverlandBand>> activeOverlandBands,
    Action<string> setOutcome,
    DaggerfallSocialTuning? socialTuning = null)
{
    internal const float KinConfrontationSightMeters = 14f;
    internal const float MinHunterAmbushDistanceMeters = 4f;
    internal const float HunterAmbushDistanceMeters = 14f;
    internal const int WealthyLocalBlockThreshold = 4;

    private const int HiredToughMobileId = 138;             // Rogue / Thief tough
    private const int DarkBrotherhoodAssassinMobileId = 139; // Assassin / Nightblade
    private const int BountyHunterMobileId = 144;            // Warrior / Mercenary bounty hunter

    private sealed class VendettaState(string familyId)
    {
        internal string FamilyId { get; } = familyId;
        internal int LastDispatchDay { get; set; }
        internal int DispatchedWaveCount { get; set; }
        internal HashSet<long> ActiveHunterIds { get; } = [];
        internal HashSet<long> ActiveKinAggressorIds { get; } = [];

        internal bool IsEmpty =>
            DispatchedWaveCount == 0
            && ActiveHunterIds.Count == 0
            && ActiveKinAggressorIds.Count == 0;
    }

    private readonly DaggerfallSocialTuning _tuning = (socialTuning ?? DaggerfallSocialTuning.Default).Validate();
    private readonly Dictionary<string, VendettaState> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<long> _signaledThisScene = [];

    /// <summary>Raised whenever a vendetta kin or hired hunter announces their reason for attacking (hook for Den #11164).</summary>
    internal event Action<DaggerfallHostileReasonSignal>? HostilitySignaled;

    internal DaggerfallFeudVendettaSave Capture()
    {
        if (_entries.Count == 0)
            return DaggerfallFeudVendettaSave.Empty;

        List<DaggerfallFeudVendettaEntrySave> saved = [];
        foreach (VendettaState state in _entries.Values.OrderBy(e => e.FamilyId, StringComparer.Ordinal))
        {
            if (state.IsEmpty)
                continue;
            saved.Add(new DaggerfallFeudVendettaEntrySave(
                state.FamilyId,
                state.LastDispatchDay,
                state.DispatchedWaveCount,
                state.ActiveHunterIds.Count == 0 ? null : [.. state.ActiveHunterIds.OrderBy(id => id)],
                state.ActiveKinAggressorIds.Count == 0 ? null : [.. state.ActiveKinAggressorIds.OrderBy(id => id)]));
        }
        return saved.Count == 0 ? DaggerfallFeudVendettaSave.Empty : new([.. saved]);
    }

    internal void Restore(DaggerfallFeudVendettaSave? saved)
    {
        _entries.Clear();
        _signaledThisScene.Clear();
        if (saved is null || saved.Entries.Length == 0)
            return;
        saved.Validate();
        foreach (DaggerfallFeudVendettaEntrySave entry in saved.Entries)
        {
            VendettaState state = new(entry.FamilyId)
            {
                LastDispatchDay = entry.LastDispatchDay,
                DispatchedWaveCount = entry.DispatchedWaveCount,
            };
            if (entry.ActiveHunterActorIds is { Length: > 0 })
                state.ActiveHunterIds.UnionWith(entry.ActiveHunterActorIds);
            if (entry.ActiveKinAggressorIds is { Length: > 0 })
                state.ActiveKinAggressorIds.UnionWith(entry.ActiveKinAggressorIds);
            _entries[entry.FamilyId] = state;
        }
    }

    /// <summary>Clears per-scene announcement de-duplication when moving to a new profile.</summary>
    internal void ResetSceneSignals() => _signaledThisScene.Clear();

    /// <summary>Returns true when <paramref name="actorId"/> is an active vendetta kin aggressor or hired hunter (lawful self-defense target).</summary>
    internal bool IsVendettaAggressor(long actorId)
    {
        if (actorId <= 0 || actorId == DaggerfallActorIdentity.PlayerEntityId)
            return false;
        foreach (VendettaState state in _entries.Values)
        {
            if (state.ActiveHunterIds.Contains(actorId) || state.ActiveKinAggressorIds.Contains(actorId))
                return true;
        }
        return false;
    }

    /// <summary>Returns a short player-facing vendetta pursuit description for <paramref name="familyId"/> (for the Relations journal), or null.</summary>
    internal string? DescribeActiveVendetta(string? familyId)
    {
        if (string.IsNullOrWhiteSpace(familyId))
            return null;
        if (!socialConsequences.TryGetHouseholdSnapshot(familyId, out DaggerfallHouseholdSnapshot snap)
            || !snap.FeudActive
            || snap.TruceMediated)
        {
            return null;
        }
        if (!_entries.TryGetValue(familyId, out VendettaState? state))
            return null;

        int liveHunters = state.ActiveHunterIds.Count(id => actors.TryGet(id, out ActorState? a) && !a.IsDefeated);
        if (liveHunters > 0 || state.DispatchedWaveCount > 0)
        {
            DaggerfallHostileSignalKind tier = ResolveHunterTier(snap.Scope, snap.MurderCount, settlementBlocks: WealthyLocalBlockThreshold).Kind;
            return tier switch
            {
                DaggerfallHostileSignalKind.DarkBrotherhoodAssassin => "Dark Brotherhood assassins hunting you",
                DaggerfallHostileSignalKind.BountyHunter => "Hired bounty hunters pursuing you",
                _ => "Hired toughs pursuing you",
            };
        }
        if (state.ActiveKinAggressorIds.Count > 0)
            return "Armed kin hunting you on sight";
        return null;
    }

    /// <summary>Records when a vendetta aggressor or hired hunter dies, pruning dead IDs from active tracking.</summary>
    internal void ObserveActorDied(long actorId)
    {
        foreach (VendettaState state in _entries.Values)
        {
            state.ActiveHunterIds.Remove(actorId);
            state.ActiveKinAggressorIds.Remove(actorId);
        }
    }

    /// <summary>
    /// Registers overland vendetta hunter actor IDs minted by <see cref="DaggerfallHostileOverlandBands"/> so their
    /// household association persists across encounter materialization, scene unload, and save/load, and truce or
    /// settlement retires every surviving hunter cleanly.
    /// </summary>
    internal void RegisterDispatchedHunters(string familyId, IReadOnlyList<long> hunterActorIds)
    {
        if (string.IsNullOrWhiteSpace(familyId) || hunterActorIds is null || hunterActorIds.Count == 0)
            return;
        VendettaState state = GetOrCreate(familyId);
        foreach (long id in hunterActorIds)
        {
            if (id > 0 && id != DaggerfallActorIdentity.PlayerEntityId)
                state.ActiveHunterIds.Add(id);
        }
    }

    /// <summary>
    /// Emits one household-specific non-blocking hostility signal per feuding household when registered overland
    /// vendetta hunters materialize into live 3D combat, deduplicated once per encounter/scene (Den #11164).
    /// </summary>
    internal int OnHuntersMaterialized(IReadOnlyList<long> materializedActorIds)
    {
        if (materializedActorIds is null || materializedActorIds.Count == 0)
            return 0;

        HashSet<long> materialized = [.. materializedActorIds];
        int signaled = 0;

        foreach (VendettaState state in _entries.Values.OrderBy(e => e.FamilyId, StringComparer.Ordinal))
        {
            if (state.ActiveHunterIds.Count == 0)
                continue;

            List<ActorState> matched = [];
            foreach (long hunterId in state.ActiveHunterIds.Order())
            {
                if (materialized.Contains(hunterId)
                    && actors.TryGet(hunterId, out ActorState? hunterActor)
                    && !hunterActor.IsDefeated)
                {
                    matched.Add(hunterActor);
                }
            }
            if (matched.Count == 0)
                continue;

            if (!socialConsequences.TryGetHouseholdSnapshot(state.FamilyId, out DaggerfallHouseholdSnapshot snap)
                || !snap.FeudActive
                || snap.TruceMediated)
            {
                continue;
            }

            if (matched.Any(a => _signaledThisScene.Contains(a.DurableId)))
                continue;
            foreach (ActorState hunter in matched)
                _signaledThisScene.Add(hunter.DurableId);

            ActorState speaker = matched[0];
            DaggerfallHouseholdScope scope = DaggerfallHouseholds.ScopeOf(state.FamilyId);
            (DaggerfallHostileSignalKind kind, _, _, string squadTitle) =
                ResolveHunterTier(scope, Math.Max(1, snap.MurderCount), WealthyLocalBlockThreshold);
            string label = FormatHouseholdLabel(state.FamilyId);
            string gender = speaker.Actor.TryGet<ActorInstanceIdentity>(out ActorInstanceIdentity? identity)
                && identity.Gender is ActorGender.Male or ActorGender.Female
                ? (identity.Gender == ActorGender.Female ? "Female" : "Male")
                : (((StableHash.NonZeroFnv1a64($"daggerfall.vendetta-overland:{worldSocialSeed()}:{speaker.DurableId}") >> 3) & 1UL) == 0UL ? "Male" : "Female");
            string reason = BuildHunterReasonLine(kind, squadTitle, label);

            EmitSignal(new DaggerfallHostileReasonSignal(
                ActorId: speaker.DurableId,
                SpeakerName: squadTitle,
                Gender: gender,
                FamilyId: state.FamilyId,
                HouseholdLabel: label,
                Kind: kind,
                ReasonLine: reason,
                Position: speaker.Position));
            signaled++;
        }

        return signaled;
    }

    /// <summary>
    /// Cancels active vendetta confrontations, materialized or detached hired hunters, and overland hunter bands
    /// when a household feud is mediated or settled.
    /// When <paramref name="settled"/> is true, prunes the sparse vendetta record completely (zero save bytes).
    /// </summary>
    internal void OnFeudPausedOrSettled(string familyId, bool settled)
    {
        if (string.IsNullOrWhiteSpace(familyId))
            return;

        // First retire any active overland pursuit bands still on the map for this household's vendetta.
        string vendettaSlug = $"hostile:vendetta-{StableHash.NonZeroFnv1a64(familyId):x16}:";
        foreach (DaggerfallPersistentOverlandBand band in activeOverlandBands().ToArray())
        {
            if (band.BandId.StartsWith(vendettaSlug, StringComparison.Ordinal))
                retireOverlandBand(band.BandId);
        }

        if (_entries.TryGetValue(familyId, out VendettaState? state))
        {
            foreach (long kinId in state.ActiveKinAggressorIds.ToArray())
                disarmVendettaKin(kinId);
            state.ActiveKinAggressorIds.Clear();

            foreach (long hunterId in state.ActiveHunterIds.ToArray())
                retireVendettaHunter(hunterId);
            state.ActiveHunterIds.Clear();

            if (settled)
                _entries.Remove(familyId);
        }
    }

    /// <summary>
    /// Evaluates living adult kin in the current scene: armed/bold members of any open, unmediated blood feud known
    /// at <paramref name="activeSite"/> confront and attack the player on sight, emitting a non-blocking hostility signal.
    /// Returns the number of newly triggered kin confrontations.
    /// </summary>
    internal int EvaluateSceneKinConfrontations(DaggerfallSiteRecord? activeSite, WorldPoint? playerPosition, int currentDay)
    {
        if (activeSite is null || playerPosition is not { } pPos)
            return 0;

        // Reassert hostility on any still-living vendetta hunters/kin restored from save in the active scene.
        foreach (VendettaState existing in _entries.Values)
        {
            if (!IsOpenUnmediatedFeudKnownAt(existing.FamilyId, activeSite))
                continue;
            foreach (long hunterId in existing.ActiveHunterIds)
            {
                if (actors.TryGet(hunterId, out ActorState? hunterActor) && !hunterActor.IsDefeated)
                    makeHostile(hunterId);
            }
            foreach (long kinId in existing.ActiveKinAggressorIds)
            {
                if (actors.TryGet(kinId, out ActorState? kinActor) && !kinActor.IsDefeated)
                {
                    armVendettaKin(kinId);
                    makeHostile(kinId);
                }
            }
        }

        // First group live eligible adult kin by feuding family so each present feuding household guarantees
        // at least one bold/armed relative confronts the player when in sight.
        Dictionary<string, List<(ActorState Actor, DaggerfallNpc Npc, float Distance)>> feudingKinByFamily = new(StringComparer.Ordinal);
        foreach (ActorState actor in actors.All.OrderBy(a => a.DurableId))
        {
            if (actor.DurableId == DaggerfallActorIdentity.PlayerEntityId || actor.IsDefeated)
                continue;
            if (!npcs.TryGet(actor.DurableId, out DaggerfallNpc? npc) || npc is null)
                continue;
            if (npc.Presence != DaggerfallNpcPresence.Active)
                continue;
            if (string.Equals(npc.Role, "guard", StringComparison.OrdinalIgnoreCase)
                || DaggerfallNpcServiceFacts.IsChild(npc.Appearance))
                continue;

            string? familyId = ResolveActorFamilyId(actor, npc);
            if (string.IsNullOrWhiteSpace(familyId))
                continue;
            if (!IsOpenUnmediatedFeudKnownAt(familyId, activeSite))
                continue;

            float dist = Vector3.Distance(actor.Position.ToVector(), pPos.ToVector());
            if (dist > KinConfrontationSightMeters)
                continue;

            if (!feudingKinByFamily.TryGetValue(familyId, out List<(ActorState, DaggerfallNpc, float)>? bucket))
            {
                bucket = [];
                feudingKinByFamily[familyId] = bucket;
            }
            bucket.Add((actor, npc, dist));
        }

        if (feudingKinByFamily.Count == 0)
            return 0;

        int triggered = 0;
        ulong seed = worldSocialSeed();
        foreach ((string familyId, List<(ActorState Actor, DaggerfallNpc Npc, float Distance)> kinList) in feudingKinByFamily)
        {
            VendettaState state = GetOrCreate(familyId);
            List<(ActorState Actor, DaggerfallNpc Npc, float Distance)> bold = kinList
                .Where(k => IsBoldVendettaKin(k.Actor.DurableId, k.Npc, seed))
                .ToList();
            if (bold.Count == 0)
                bold.Add(kinList.OrderBy(k => k.Distance).ThenBy(k => k.Actor.DurableId).First());

            string label = FormatHouseholdLabel(familyId);
            foreach ((ActorState kinActor, DaggerfallNpc kinNpc, _) in bold)
            {
                bool alreadyActive = state.ActiveKinAggressorIds.Contains(kinActor.DurableId);
                armVendettaKin(kinActor.DurableId);
                makeHostile(kinActor.DurableId);
                state.ActiveKinAggressorIds.Add(kinActor.DurableId);

                if (_signaledThisScene.Add(kinActor.DurableId))
                {
                    string speaker = !string.IsNullOrWhiteSpace(kinNpc.DisplayName) ? kinNpc.DisplayName! : kinNpc.Role;
                    string reason = $"{speaker}: \"You murdered our kin — {label} will have your blood!\"";
                    EmitSignal(new DaggerfallHostileReasonSignal(
                        ActorId: kinActor.DurableId,
                        SpeakerName: speaker,
                        Gender: kinNpc.Appearance.Gender,
                        FamilyId: familyId,
                        HouseholdLabel: label,
                        Kind: DaggerfallHostileSignalKind.KinVendettaConfrontation,
                        ReasonLine: reason,
                        Position: kinActor.Position));
                }

                if (!alreadyActive)
                    triggered++;
            }
        }

        return triggered;
    }

    /// <summary>
    /// Stages hired toughs, bounty hunters, or Dark Brotherhood assassins in the active exterior settlement
    /// for open, unmediated blood feuds whose household tier/wealth qualifies and whose dispatch cooldown has elapsed.
    /// Returns the durable IDs of newly spawned vendetta hunters.
    /// </summary>
    internal IReadOnlyList<long> EvaluateSiteHunterDispatch(
        DaggerfallSiteRecord? activeSite,
        DaggerfallWorldProfileKind profileKind,
        WorldPoint? playerPosition,
        int currentDay,
        bool allowInteriorAmbush = false)
    {
        if (activeSite is null || playerPosition is not { } pPos)
            return [];
        if (profileKind != DaggerfallWorldProfileKind.Exterior && !allowInteriorAmbush)
            return [];

        PruneDeadHunters();
        int liveActiveHunters = CountLiveActiveHunters();
        if (liveActiveHunters >= _tuning.MaxActiveVendettaHunters)
            return [];

        DaggerfallLocalHouseholdsSave captured = socialConsequences.Capture();
        if (captured.Standings.Length == 0)
            return [];

        int settlementBlocks = activeSite.Exterior?.Blocks.Count ?? 0;
        List<long> spawnedIds = [];

        foreach (DaggerfallLocalHouseholdStandingSave standing in captured.Standings
            .OrderByDescending(s => (int)DaggerfallHouseholds.ScopeOf(s.FamilyId))
            .ThenByDescending(s => s.MurderCount)
            .ThenBy(s => s.FamilyId, StringComparer.Ordinal))
        {
            if (liveActiveHunters >= _tuning.MaxActiveVendettaHunters)
                break;
            if (!IsOpenUnmediatedFeudKnownAt(standing.FamilyId, activeSite))
                continue;

            DaggerfallHouseholdScope scope = DaggerfallHouseholds.ScopeOf(standing.FamilyId);
            if (!CanHireHunters(scope, standing.MurderCount, settlementBlocks))
                continue;

            VendettaState state = GetOrCreate(standing.FamilyId);
            if (state.DispatchedWaveCount > 0
                && currentDay - state.LastDispatchDay < _tuning.VendettaHunterCooldownDays)
            {
                continue;
            }
            if (state.ActiveHunterIds.Any(id => actors.TryGet(id, out ActorState? a) && !a.IsDefeated))
                continue;

            (DaggerfallHostileSignalKind kind, int preferredMobileId, int waveCount, string squadTitle) =
                ResolveHunterTier(scope, standing.MurderCount, settlementBlocks);
            DaggerfallActorDefinition? enemyDef = ResolveCombatantDefinition(preferredMobileId);
            if (enemyDef is null)
                continue;

            int toSpawn = Math.Min(waveCount, _tuning.MaxActiveVendettaHunters - liveActiveHunters);
            int hunterLevel = Math.Max(1, playerLevel() + (scope == DaggerfallHouseholdScope.Dynasty ? 2 : scope == DaggerfallHouseholdScope.GreatHouse ? 1 : 0));
            string label = FormatHouseholdLabel(standing.FamilyId);
            ulong waveSeed = StableHash.NonZeroFnv1a64(string.Create(
                CultureInfo.InvariantCulture,
                $"daggerfall.vendetta-wave:{worldSocialSeed()}:{standing.FamilyId}:{state.DispatchedWaveCount}"));

            int placedForFamily = 0;
            for (int i = 0; i < toSpawn; i++)
            {
                string opKey = string.Create(
                    CultureInfo.InvariantCulture,
                    $"vendetta:{standing.FamilyId}:w{state.DispatchedWaveCount}:m{i}");
                if (trySpawnPose(opKey, pPos, MinHunterAmbushDistanceMeters, HunterAmbushDistanceMeters) is not { } spawnPose)
                    continue;

                long hunterId = spawnCombatant(enemyDef.Id.Value, spawnPose, hunterLevel);
                makeHostile(hunterId);
                state.ActiveHunterIds.Add(hunterId);
                spawnedIds.Add(hunterId);
                liveActiveHunters++;
                placedForFamily++;

                if (placedForFamily == 1 && _signaledThisScene.Add(hunterId))
                {
                    string gender = ((waveSeed >> 3) & 1UL) == 0UL ? "Male" : "Female";
                    string reason = BuildHunterReasonLine(kind, squadTitle, label);
                    EmitSignal(new DaggerfallHostileReasonSignal(
                        ActorId: hunterId,
                        SpeakerName: squadTitle,
                        Gender: gender,
                        FamilyId: standing.FamilyId,
                        HouseholdLabel: label,
                        Kind: kind,
                        ReasonLine: reason,
                        Position: spawnPose.Position));
                }
            }

            if (placedForFamily > 0)
            {
                state.LastDispatchDay = Math.Max(0, currentDay);
                state.DispatchedWaveCount++;
            }
        }

        return spawnedIds;
    }

    /// <summary>
    /// Resolves active unmediated blood feuds eligible to hire overland pursuit bands in <paramref name="region"/>
    /// for <see cref="DaggerfallHostileOverlandBands"/>.
    /// </summary>
    internal IReadOnlyList<DaggerfallHostileVendettaStanding> ResolveOverlandVendettas(int region, DaggerfallSiteRecord? referenceSite = null)
    {
        DaggerfallLocalHouseholdsSave captured = socialConsequences.Capture();
        if (captured.Standings.Length == 0)
            return [];

        List<DaggerfallHostileVendettaStanding> results = [];
        foreach (DaggerfallLocalHouseholdStandingSave standing in captured.Standings)
        {
            DaggerfallHouseholdScope scope = DaggerfallHouseholds.ScopeOf(standing.FamilyId);
            if (scope == DaggerfallHouseholdScope.None)
                continue;
            if (!socialConsequences.TryGetHouseholdSnapshot(standing.FamilyId, out DaggerfallHouseholdSnapshot snap)
                || !snap.FeudActive
                || snap.TruceMediated)
            {
                continue;
            }
            if (referenceSite is not null && !socialConsequences.IsFeudActiveAt(standing.FamilyId, referenceSite))
                continue;

            int settlementBlocks = referenceSite?.Exterior?.Blocks.Count ?? WealthyLocalBlockThreshold;
            if (scope == DaggerfallHouseholdScope.Local)
            {
                if (standing.Region != region)
                    continue;
                if (!CanHireHunters(scope, snap.MurderCount, settlementBlocks))
                    continue;
            }
            else if (scope == DaggerfallHouseholdScope.GreatHouse
                && DaggerfallHouseholds.TryParseRegion(standing.FamilyId, out int houseRegion)
                && houseRegion != region
                && snap.MurderCount < 2)
            {
                // Great House hunters pursue primarily within their province unless repeat murders escalate them bay-wide.
                continue;
            }

            (DaggerfallHostileSignalKind kind, int mobileId, int count, string squadTitle) =
                ResolveHunterTier(scope, snap.MurderCount, settlementBlocks);
            string label = FormatHouseholdLabel(standing.FamilyId);
            double speed = kind == DaggerfallHostileSignalKind.DarkBrotherhoodAssassin ? 2.55 : 2.3;
            results.Add(new DaggerfallHostileVendettaStanding(
                FamilyId: standing.FamilyId,
                HouseholdLabel: label,
                BandTitle: $"{label} {squadTitle}",
                PreferredMobileId: mobileId,
                MemberCount: count,
                SpeedPixelsPerHour: speed));
        }
        return results;
    }

    internal static bool CanHireHunters(DaggerfallHouseholdScope scope, int murderCount, int settlementBlocks) =>
        scope switch
        {
            DaggerfallHouseholdScope.Dynasty => true,
            DaggerfallHouseholdScope.GreatHouse => true,
            DaggerfallHouseholdScope.Local => settlementBlocks >= WealthyLocalBlockThreshold || murderCount >= 2,
            _ => false,
        };

    internal static (DaggerfallHostileSignalKind Kind, int PreferredMobileId, int MemberCount, string SquadTitle) ResolveHunterTier(
        DaggerfallHouseholdScope scope,
        int murderCount,
        int settlementBlocks)
    {
        return scope switch
        {
            DaggerfallHouseholdScope.Dynasty =>
                (DaggerfallHostileSignalKind.DarkBrotherhoodAssassin, DarkBrotherhoodAssassinMobileId, murderCount >= 2 ? 3 : 2, "Dark Brotherhood Assassins"),
            DaggerfallHouseholdScope.GreatHouse when murderCount >= 2 =>
                (DaggerfallHostileSignalKind.DarkBrotherhoodAssassin, DarkBrotherhoodAssassinMobileId, 2, "Dark Brotherhood Assassins"),
            DaggerfallHouseholdScope.GreatHouse =>
                (DaggerfallHostileSignalKind.BountyHunter, BountyHunterMobileId, 2, "Bounty Hunters"),
            _ =>
                (DaggerfallHostileSignalKind.HiredTough, HiredToughMobileId, settlementBlocks >= 8 || murderCount >= 2 ? 2 : 1, "Hired Toughs"),
        };
    }

    private bool IsOpenUnmediatedFeudKnownAt(string familyId, DaggerfallSiteRecord activeSite) =>
        socialConsequences.TryGetHouseholdSnapshot(familyId, out DaggerfallHouseholdSnapshot snap)
        && snap.FeudActive
        && !snap.TruceMediated
        && socialConsequences.IsFeudActiveAt(familyId, activeSite);

    private static bool IsBoldVendettaKin(long actorId, DaggerfallNpc npc, ulong seed)
    {
        if (npc.Role.Contains("knight", StringComparison.OrdinalIgnoreCase)
            || npc.Role.Contains("warrior", StringComparison.OrdinalIgnoreCase)
            || npc.Role.Contains("soldier", StringComparison.OrdinalIgnoreCase)
            || npc.Role.Contains("hunter", StringComparison.OrdinalIgnoreCase)
            || npc.Role.Contains("smith", StringComparison.OrdinalIgnoreCase)
            || npc.Role.Contains("noble", StringComparison.OrdinalIgnoreCase)
            || npc.Role.Contains("captain", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        ulong hash = StableHash.NonZeroFnv1a64(string.Create(
            CultureInfo.InvariantCulture,
            $"daggerfall.vendetta-bold:{seed}:{actorId}"));
        return (hash & 1UL) == 0UL;
    }

    private static string? ResolveActorFamilyId(ActorState actor, DaggerfallNpc npc)
    {
        if (actor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? overlay)
            && !string.IsNullOrWhiteSpace(overlay.FamilyId))
        {
            return overlay.FamilyId;
        }
        return npc.FamilyId;
    }

    private string FormatHouseholdLabel(string familyId) =>
        DaggerfallHouseholds.FormatLabel(
            familyId,
            definitions,
            names.Surname,
            (bank, id) => names.FirstName(bank, false, id),
            worldSocialSeed()) ?? "A grieving household";

    private static string BuildHunterReasonLine(DaggerfallHostileSignalKind kind, string squadTitle, string householdLabel) =>
        kind switch
        {
            DaggerfallHostileSignalKind.DarkBrotherhoodAssassin =>
                $"{squadTitle}: \"{householdLabel} paid the Dark Brotherhood in blood — the Night Mother claims your life!\"",
            DaggerfallHostileSignalKind.BountyHunter =>
                $"{squadTitle}: \"{householdLabel} put a blood bounty on your head — you will not walk away!\"",
            _ =>
                $"{squadTitle}: \"{householdLabel} hired us to settle their blood feud with steel!\"",
        };

    private DaggerfallActorDefinition? ResolveCombatantDefinition(int preferredMobileId)
    {
        DaggerfallActorDefinition? preferred = definitions.Actors.Values
            .FirstOrDefault(a => (a.Capabilities.Scaling == ActorScalingMode.CareerLeveled || (a.TemplateId is null && a.Kind == DaggerfallActorKinds.EnemyClass))
                && a.MobileId == preferredMobileId);
        if (preferred is not null)
            return preferred;
        return definitions.Actors.Values
            .Where(a => (a.Capabilities.Scaling == ActorScalingMode.CareerLeveled || (a.TemplateId is null && a.Kind == DaggerfallActorKinds.EnemyClass))
                && a.MobileId is not null)
            .OrderBy(a => a.MobileId)
            .FirstOrDefault();
    }

    private VendettaState GetOrCreate(string familyId)
    {
        if (!_entries.TryGetValue(familyId, out VendettaState? state))
        {
            state = new VendettaState(familyId);
            _entries[familyId] = state;
        }
        return state;
    }

    private void PruneDeadHunters()
    {
        foreach (VendettaState state in _entries.Values)
            state.ActiveHunterIds.RemoveWhere(id => actors.TryGet(id, out ActorState? a) && a.IsDefeated);
    }

    private int CountLiveActiveHunters()
    {
        int count = 0;
        foreach (VendettaState state in _entries.Values)
            count += state.ActiveHunterIds.Count(id => actors.TryGet(id, out ActorState? a) && !a.IsDefeated);
        return count;
    }

    private void EmitSignal(DaggerfallHostileReasonSignal signal)
    {
        setOutcome(signal.ReasonLine);
        HostilitySignaled?.Invoke(signal);
    }
}
