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
    private readonly List<DaggerfallEncounterResolution> _resolved = [];

    internal IReadOnlyList<DaggerfallEncounterResolution> Resolved => _resolved;

    internal DaggerfallEncounterResolution Select(DaggerfallEncounterRequest request, ulong generation, string profileId, ActorPose pose)
    {
        request.Validate();
        if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ulong sequence = checked((ulong)_resolved.Count + 1UL);

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
        _resolved.Add(result);
        return result;
    }

    internal IReadOnlyList<long> MaterializePending(string activeProfileId, Func<string, ActorPose, int, long> spawn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeProfileId);
        ArgumentNullException.ThrowIfNull(spawn);
        List<long> created = [];
        for (int index = 0; index < _resolved.Count; index++)
        {
            DaggerfallEncounterResolution current = _resolved[index];
            if (current.ActorDefinition is not string definition || current.SpawnedActorId is not null
                || !StringComparer.Ordinal.Equals(current.ProfileId, activeProfileId)) continue;
            long actorId = spawn(definition, current.Pose, current.Request.PlayerLevel);
            _resolved[index] = current with { SpawnedActorId = actorId };
            created.Add(actorId);
        }
        return created;
    }

    internal DaggerfallEncounterRuntimeSave Capture() => new([.. _resolved]);

    internal void Restore(DaggerfallEncounterRuntimeSave saved, IReadOnlyDictionary<long, string> liveActors) =>
        Restore(saved, liveActors, new HashSet<long>());

    /// <summary>
    /// Restores encounter relationships while allowing a selected actor to have been retired
    /// after its corpse/identity lifecycle completed.  A tombstoned identity remains a valid
    /// historical SpawnedActorId, but it can never be materialized again.
    /// </summary>
    internal void Restore(DaggerfallEncounterRuntimeSave saved, IReadOnlyDictionary<long, string> liveActors,
        IReadOnlySet<long> tombstonedActors)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(liveActors);
        ArgumentNullException.ThrowIfNull(tombstonedActors);
        saved.Validate();
        if (_resolved.Count != 0) throw new InvalidOperationException("Encounter runtime restores only into a fresh session.");
        ulong expectedSequence = 1;
        foreach (DaggerfallEncounterResolution value in saved.Resolved.OrderBy(value => value.Sequence))
        {
            if (value.Sequence != expectedSequence++)
                throw new ArgumentException("Saved encounter sequences must remain contiguous from one.");
            ValidateRestored(value, liveActors, tombstonedActors);
            _resolved.Add(value);
        }
    }

    private void ValidateRestored(DaggerfallEncounterResolution value, IReadOnlyDictionary<long, string> liveActors,
        IReadOnlySet<long> tombstonedActors)
    {
        value.Validate();
        if (value.Choice.Context != value.Request.Context)
            throw new ArgumentException("A saved encounter choice must retain its request context.");
        if (!float.IsFinite(value.Pose.Position.X) || !float.IsFinite(value.Pose.Position.Y)
            || !float.IsFinite(value.Pose.Position.Z) || !float.IsFinite(value.Pose.HeadingYawRadians))
            throw new ArgumentException("A saved encounter pose must be finite.");

        DaggerfallEncounterChoice choice = value.Choice;
        if (choice.MobileId is null)
        {
            ValidateEmptyChoice(value.Request, choice);
            return;
        }

        if (choice.Reason is not null || choice.Table is not int table || choice.Entry is not int entry
            || choice.LevelPercentile is not int percentile || choice.ChanceRoll is not 0
            || choice.ChanceDenominator != DaggerfallEncounterPolicy.DenominatorFor(value.Request.Context))
            throw new ArgumentException("A saved selected encounter must retain its complete canonical selection facts.");
        if (table != DaggerfallEncounterPolicy.TableFor(value.Request) || entry is < 0 or >= 20)
            throw new ArgumentException("A saved encounter table choice does not match its request.");
        (int minimum, int maximum) = DaggerfallEncounterPolicy.LevelBand(percentile, value.Request.PlayerLevel);
        if (entry < minimum || entry > maximum || _definitions.Encounters.Tables[table][entry] != choice.MobileId.Value)
            throw new ArgumentException("A saved encounter entry does not match its canonical table or level band.");

        DaggerfallMobileDefinition mobile = _definitions.Mobiles.Mobiles.TryGetValue(choice.MobileId.Value, out DaggerfallMobileDefinition? found)
            ? found : throw new ArgumentException($"Saved encounter selects unpublished mobile {choice.MobileId.Value}.");
        string expectedActor = DaggerfallEncounterActors.ActorFor(mobile).Value;
        if (!StringComparer.Ordinal.Equals(value.ActorDefinition, expectedActor))
            throw new ArgumentException("A saved encounter actor does not match its selected mobile.");
        _ = _definitions.RequireActor(new DaggerfallActorId(expectedActor));
        if (value.SpawnedActorId is long spawned)
        {
            if (liveActors.TryGetValue(spawned, out string? liveDefinition))
            {
                if (!StringComparer.Ordinal.Equals(liveDefinition, expectedActor))
                    throw new ArgumentException("A saved spawned encounter actor does not match its canonical dynamic actor definition.");
            }
            else if (!tombstonedActors.Contains(spawned))
            {
                throw new ArgumentException("A saved spawned encounter actor is neither live nor a known tombstone.");
            }
        }
    }

    private static void ValidateEmptyChoice(DaggerfallEncounterRequest request, DaggerfallEncounterChoice choice)
    {
        if (choice.Table is not null || choice.Entry is not null || choice.LevelPercentile is not null)
            throw new ArgumentException("An empty encounter outcome cannot carry table-selection facts.");
        if (request.Context == DaggerfallEncounterContext.Dungeon && !request.EnemyAlert)
        {
            if (choice.Reason != "dungeon-not-alert" || choice.ChanceRoll is not null || choice.ChanceDenominator is not null)
                throw new ArgumentException("A non-alert dungeon encounter must retain the canonical empty result.");
            return;
        }

        int denominator = DaggerfallEncounterPolicy.DenominatorFor(request.Context);
        if (choice.Reason != "source-chance-missed" || choice.ChanceDenominator != denominator
            || choice.ChanceRoll is not int chance || chance is < 1 || chance >= denominator)
            throw new ArgumentException("A saved empty encounter does not match the canonical source chance result.");
    }
}

/// <summary>One selected result, either pending actor materialization or bound to the actor it created.</summary>
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

internal sealed record DaggerfallEncounterRuntimeSave(DaggerfallEncounterResolution[] Resolved)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Resolved);
        HashSet<(ulong Generation, ulong Sequence)> identities = [];
        foreach (DaggerfallEncounterResolution resolution in Resolved)
        {
            ArgumentNullException.ThrowIfNull(resolution);
            resolution.Validate();
            if (!identities.Add((resolution.Generation, resolution.Sequence)))
                throw new ArgumentException("Saved encounter resolutions must have distinct generation and sequence identities.");
        }
    }
}
