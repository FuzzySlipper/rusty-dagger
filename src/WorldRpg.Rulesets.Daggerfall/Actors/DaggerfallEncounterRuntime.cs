using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Encounters;

/// <summary>
/// Durable encounter selection between an elapsed-time consequence and dynamic actor materialization.
/// </summary>
/// <remarks>
/// Selection is committed before the actor exists. A save in that interval restores the exact result,
/// rather than drawing again, and materialization delegates to the existing dynamic actor/corpse lifecycle.
/// </remarks>
internal sealed class DaggerfallEncounterRuntime(DaggerfallDefinitions definitions, IRandomService random)
{
    private const ulong Seed = 0;
    private const string Scope = "daggerfall.encounter.v1";
    private readonly DaggerfallDefinitions _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly IRandomService _random = random ?? throw new ArgumentNullException(nameof(random));
    private readonly List<DaggerfallEncounterResolution> _pending = [];
    private readonly HashSet<string> _populatedDungeons = new(StringComparer.Ordinal);
    private ulong _nextSequence = 1UL;

    internal bool DungeonPopulated(string profile) => _populatedDungeons.Contains(profile);
    internal void MarkDungeonPopulated(string profile) => _populatedDungeons.Add(profile);

    /// <summary>Pre-materialization pending encounter selections awaiting placement.</summary>
    internal IReadOnlyList<DaggerfallEncounterResolution> Pending => _pending;

    internal DaggerfallEncounterResolution Select(DaggerfallEncounterRequest request, ulong generation, string profileId, ActorPose pose)
    {
        request.Validate();
        if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ulong sequence = checked(_nextSequence++);

        DaggerfallEncounterChoice choice = DaggerfallEncounterPolicy.Choose(_definitions.Encounters, request, (roll, minimum, maximum) =>
            checked((int)_random.DrawKeyed(new KeyedRngRequest(Seed, Scope, $"generation:{generation}:sequence:{sequence}:{roll}", minimum, maximum)).Value));
        DaggerfallActorId? actor = null;
        if (choice.MobileId is int mobileId)
        {
            DaggerfallMobileDefinition mobile = _definitions.Mobiles.Mobiles.TryGetValue(mobileId, out DaggerfallMobileDefinition? found)
                ? found : throw new InvalidOperationException($"Encounter table selected unpublished mobile {mobileId}.");
            actor = DaggerfallEncounterActors.ActorFor(mobile);
            _ = _definitions.RequireActor(actor.Value);
        }

        DaggerfallEncounterResolution result = new(generation, sequence, profileId, request, choice, actor?.Value, pose, SpawnedActorId: null);
        if (actor is not null) _pending.Add(result);
        return result;
    }

    /// <summary>
    /// Admits one member of a band met on the expedition map: the foe its table gave when it was staged, kept for the
    /// profile the player enters, where it is placed with its band as it was staged.
    /// </summary>
    internal DaggerfallEncounterResolution Stage(DaggerfallEncounterRequest request, DaggerfallEncounterChoice choice,
        DaggerfallEncounterBand band, ulong generation, string profileId, ActorPose pose)
    {
        request.Validate();
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(band);
        if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        DaggerfallMobileDefinition mobile = choice.MobileId is int id && _definitions.Mobiles.Mobiles.TryGetValue(id, out DaggerfallMobileDefinition? found)
            ? found : throw new InvalidOperationException($"A band names unpublished mobile {choice.MobileId}.");
        DaggerfallActorId actor = DaggerfallEncounterActors.ActorFor(mobile);
        _ = _definitions.RequireActor(actor);
        DaggerfallEncounterResolution result = new(generation, checked(_nextSequence++), profileId, request, choice, actor.Value, pose,
            SpawnedActorId: null) { Band = band };
        _pending.Add(result);
        return result;
    }

    /// <summary>
    /// Offers each pending selection of the active profile to <paramref name="spawn"/>, which places and
    /// creates its actor or returns null when no placement was found this step. Once materialized, the actor
    /// lives solely in <c>EntityStore</c>/<see cref="DaggerfallActorLedger"/> and leaves the pending queue.
    /// </summary>
    internal IReadOnlyList<long> MaterializePending(string activeProfileId, Func<DaggerfallEncounterResolution, long?> spawn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeProfileId);
        ArgumentNullException.ThrowIfNull(spawn);
        List<long> created = [];
        for (int index = 0; index < _pending.Count;)
        {
            DaggerfallEncounterResolution current = _pending[index];
            if (current.ActorDefinition is null || current.SpawnedActorId is not null
                || !StringComparer.Ordinal.Equals(current.ProfileId, activeProfileId))
            {
                index++;
                continue;
            }
            if (spawn(current) is not long actorId)
            {
                index++;
                continue;
            }
            _pending.RemoveAt(index);
            created.Add(actorId);
        }
        return created;
    }

    internal DaggerfallActorId? SelectDungeonEnemy(DaggerfallSiteRecord site, DaggerfallDungeonEnemyPlacement marker, int playerLevel)
    {
        int table = marker.Underwater ? 19 : site.DungeonType;
        if (table is < 0 or > 19) throw new ArgumentException($"Dungeon '{site.Name}' has no encounter table {table}.");
        int Draw(string key, int min, int max) => checked((int)_random.DrawKeyed(new KeyedRngRequest(
            checked((ulong)site.MapId), "daggerfall.dungeon.population", key, min, max)).Value);
        int slot = marker.EncounterSlot == 0 ? Draw($"marker:{marker.Id}:slot", 1, 6) : marker.EncounterSlot;
        string selection = $"table:{table}:slot:{slot}";
        // Every marker with the same non-zero slot shares a selection throughout this location.
        int percentile = Draw(selection + ":level-band", 1, 100);
        var (min, max) = DaggerfallEncounterPolicy.LevelBand(percentile, playerLevel);
        int entry = Draw(selection + ":table-entry", min, max);
        int mobileId = _definitions.Encounters.Tables[table][entry];
        var mobile = _definitions.Mobiles.Mobiles.TryGetValue(mobileId, out var found)
            ? found : throw new InvalidOperationException($"Dungeon encounter selected unpublished mobile {mobileId}.");
        if (mobileId is 11 or 41 or 42 && !marker.AquaticSpawnAllowed) return null;
        var actor = DaggerfallEncounterActors.ActorFor(mobile);
        _ = _definitions.RequireActor(actor);
        return actor;
    }

    /// <summary>Whether a selected encounter of the active profile is still waiting for a placement.</summary>
    internal bool HasPending(string activeProfileId) => _pending.Any(current => current.ActorDefinition is not null
        && current.SpawnedActorId is null && StringComparer.Ordinal.Equals(current.ProfileId, activeProfileId));

    /// <summary>Pending spawns share the active profile's local frame with current actors.</summary>
    internal void RebasePending(string activeProfileId, Vector3 delta)
    {
        for (int index = 0; index < _pending.Count; index++)
        {
            DaggerfallEncounterResolution current = _pending[index];
            if (current.SpawnedActorId is not null || current.ActorDefinition is null
                || !StringComparer.Ordinal.Equals(current.ProfileId, activeProfileId)) continue;
            _pending[index] = current with { Pose = new ActorPose(
                DaggerfallSceneOrigin.Shift(current.Pose.Position, delta), current.Pose.HeadingYawRadians) };
        }
    }

    internal DaggerfallEncounterRuntimeSave Capture() => new([.. _pending])
    {
        PopulatedDungeons = [.. _populatedDungeons.Order(StringComparer.Ordinal)],
        NextSequence = _nextSequence,
    };

    /// <summary>Restores strictly pre-materialization pending encounter selections and dungeon population flags.</summary>
    internal void Restore(DaggerfallEncounterRuntimeSave saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        saved.Validate();
        _populatedDungeons.UnionWith(saved.PopulatedDungeons);
        if (_pending.Count != 0) throw new InvalidOperationException("Encounter runtime restores only into a fresh session.");
        ulong maxSequence = 0;
        foreach (DaggerfallEncounterResolution value in saved.Pending.OrderBy(value => value.Sequence))
        {
            ValidateRestoredPending(value);
            _pending.Add(value);
            if (value.Sequence > maxSequence) maxSequence = value.Sequence;
        }
        _nextSequence = Math.Max(saved.NextSequence, checked(maxSequence + 1UL));
    }

    private void ValidateRestoredPending(DaggerfallEncounterResolution value)
    {
        value.Validate();
        if (value.SpawnedActorId is not null || value.Choice.MobileId is null || string.IsNullOrWhiteSpace(value.ActorDefinition))
            throw new ArgumentException("Saved encounters may retain only pre-materialization pending outcomes.");
        if (value.Choice.Context != value.Request.Context)
            throw new ArgumentException("A saved encounter choice must retain its request context.");
        if (!float.IsFinite(value.Pose.Position.X) || !float.IsFinite(value.Pose.Position.Y)
            || !float.IsFinite(value.Pose.Position.Z) || !float.IsFinite(value.Pose.HeadingYawRadians))
            throw new ArgumentException("A saved encounter pose must be finite.");

        DaggerfallEncounterChoice choice = value.Choice;
        bool conditionBand = value.Band is not null
            && DaggerfallWorldEncounters.ExtractConditionTokenId(choice.Reason) is not null
            && choice.Table is null
            && choice.Entry is null;
        if (!conditionBand)
        {
            // A band from the expedition map was picked from its table without the source's spawn chance.
            bool chanceKept = value.Band is null
                ? choice.ChanceRoll is 0 && choice.ChanceDenominator == DaggerfallEncounterPolicy.DenominatorFor(value.Request.Context)
                : choice.ChanceRoll is null && choice.ChanceDenominator is null;
            if (choice.Reason is not null || choice.Table is not int table || choice.Entry is not int entry
                || choice.LevelPercentile is not int percentile || !chanceKept)
                throw new ArgumentException("A saved selected encounter must retain its complete canonical selection facts.");
            if (table != DaggerfallEncounterPolicy.TableFor(value.Request) || entry is < 0 or >= 20)
                throw new ArgumentException("A saved encounter table choice does not match its request.");
            (int minimum, int maximum) = DaggerfallEncounterPolicy.LevelBand(percentile, value.Request.PlayerLevel);
            if (entry < minimum || entry > maximum || _definitions.Encounters.Tables[table][entry] != choice.MobileId.Value)
                throw new ArgumentException("A saved encounter entry does not match its canonical table or level band.");
        }

        DaggerfallMobileDefinition mobile = _definitions.Mobiles.Mobiles.TryGetValue(choice.MobileId.Value, out DaggerfallMobileDefinition? found)
            ? found : throw new ArgumentException($"Saved encounter selects unpublished mobile {choice.MobileId.Value}.");
        string expectedActor = DaggerfallEncounterActors.ActorFor(mobile).Value;
        if (!StringComparer.Ordinal.Equals(value.ActorDefinition, expectedActor))
            throw new ArgumentException("A saved encounter actor does not match its selected mobile.");
        _ = _definitions.RequireActor(new DaggerfallActorId(expectedActor));
    }
}

/// <summary>
/// One pre-materialization encounter selection awaiting placement. Its pose is the player's where the
/// encounter was selected; the actor itself is placed around the player when it appears, after which
/// ownership moves to <c>EntityStore</c> and <see cref="DaggerfallActorLedger"/>.
/// </summary>
internal sealed record DaggerfallEncounterResolution(
    ulong Generation,
    ulong Sequence,
    string ProfileId,
    DaggerfallEncounterRequest Request,
    DaggerfallEncounterChoice Choice,
    string? ActorDefinition,
    ActorPose Pose,
    long? SpawnedActorId)
{
    /// <summary>The band from the expedition map this member belongs to, or null for an ordinary encounter.</summary>
    public DaggerfallEncounterBand? Band { get; init; }

    internal void Validate()
    {
        if (Generation == 0 || Sequence == 0) throw new ArgumentOutOfRangeException(nameof(Generation));
        if (string.IsNullOrWhiteSpace(ProfileId)) throw new ArgumentException("An encounter outcome must retain its originating world profile.");
        (Request ?? throw new ArgumentNullException(nameof(Request))).Validate();
        ArgumentNullException.ThrowIfNull(Choice);
        if (Choice.MobileId is null && (ActorDefinition is not null || SpawnedActorId is not null))
            throw new ArgumentException("An empty encounter outcome cannot carry an actor.");
        if (Choice.MobileId is not null && string.IsNullOrWhiteSpace(ActorDefinition))
            throw new ArgumentException("A selected encounter mobile must carry its resolved actor definition.");
        if (SpawnedActorId is <= 0) throw new ArgumentOutOfRangeException(nameof(SpawnedActorId));
    }
}

internal sealed record DaggerfallEncounterRuntimeSave(DaggerfallEncounterResolution[] Pending)
{
    [System.Text.Json.Serialization.JsonRequired] public string[] PopulatedDungeons { get; init; } = [];
    public ulong NextSequence { get; init; } = 1UL;
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Pending);
        ArgumentNullException.ThrowIfNull(PopulatedDungeons);
        if (NextSequence == 0) throw new ArgumentOutOfRangeException(nameof(NextSequence));
        if (PopulatedDungeons.Any(string.IsNullOrWhiteSpace) || PopulatedDungeons.Distinct(StringComparer.Ordinal).Count() != PopulatedDungeons.Length)
            throw new ArgumentException("Populated dungeon identities must be distinct and non-blank.");
        HashSet<(ulong Generation, ulong Sequence)> identities = [];
        foreach (DaggerfallEncounterResolution resolution in Pending)
        {
            ArgumentNullException.ThrowIfNull(resolution);
            resolution.Validate();
            if (resolution.SpawnedActorId is not null || resolution.Choice.MobileId is null || string.IsNullOrWhiteSpace(resolution.ActorDefinition))
                throw new ArgumentException("Saved encounters may retain only pre-materialization pending outcomes.");
            if (!identities.Add((resolution.Generation, resolution.Sequence)))
                throw new ArgumentException("Saved encounter resolutions must have distinct generation and sequence identities.");
        }
    }
}

/// <summary>How a band met on the expedition map stands when the player enters among it.</summary>
internal enum DaggerfallEncounterStaging
{
    /// <summary>It caught the party: placed as an encounter is, and already set on the player.</summary>
    Caught,

    /// <summary>Camped: its members together, facing one another.</summary>
    Camp,

    /// <summary>On the move: its members in file, facing their way.</summary>
    Travelling,
}

/// <summary>A band's identity among the resolutions it became, how it was staged, and the way it faced (radians).</summary>
internal sealed record DaggerfallEncounterBand(int Id, DaggerfallEncounterStaging Staging, float HeadingYawRadians);

