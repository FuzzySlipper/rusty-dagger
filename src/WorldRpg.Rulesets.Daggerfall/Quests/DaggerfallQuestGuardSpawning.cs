using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallQuestGuardSpawnOutcome { Pending, Complete, NoWitness, ActiveLimit, LeftLocation }
/// <summary>Only pending request policy lives here; admitted guard identities live in the canonical roster/save.</summary>
internal sealed record DaggerfallQuestGuardSpawnState(DaggerfallWorldProfileKey Profile, DaggerfallSiteId Location, double DelaySeconds,
    int Remaining, int SpawnedCount, long[] CandidateNpcs, DaggerfallQuestGuardSpawnOutcome Outcome)
{
    internal DaggerfallQuestGuardSpawnState Copy() { Validate(); return this with { CandidateNpcs = [.. CandidateNpcs] }; }
    internal void Validate()
    {
        Profile.Validate();
        if (!double.IsFinite(DelaySeconds) || DelaySeconds < 0 || Remaining < 0 || SpawnedCount < 0 || !Enum.IsDefined(Outcome)
            || CandidateNpcs is null || CandidateNpcs.Any(id => id <= 0) || CandidateNpcs.Distinct().Count() != CandidateNpcs.Length
            || CandidateNpcs.Length > Remaining || Outcome != DaggerfallQuestGuardSpawnOutcome.Pending && Remaining != 0)
            throw new ArgumentException("Quest guard request has invalid pending placement or completion state.");
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private Func<string, bool, double, DaggerfallQuestGuardSpawnState?, DaggerfallQuestGuardSpawnState?>? _spawnQuestGuards;
    internal void BindGuardSpawning(Func<string, bool, double, DaggerfallQuestGuardSpawnState?, DaggerfallQuestGuardSpawnState?> spawn) => _spawnQuestGuards = spawn;
    bool IDaggerfallQuestTaskLifecycle.SpawnGuards(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation,
        DaggerfallQuestTaskRuntimeState state, int index, double elapsedSeconds)
    {
        var result = (_spawnQuestGuards ?? throw new InvalidOperationException("No canonical guard request owner is composed."))(
            instance.InstanceId + "/" + state.Symbol + "/" + index, operation.Step == 1, elapsedSeconds, state.OperationState[index].GuardSpawn);
        state.OperationState[index] = state.OperationState[index] with { GuardSpawn = result, UnavailableReason = null };
        return result is not null && result.Outcome != DaggerfallQuestGuardSpawnOutcome.Pending;
    }
}

/// <summary>
/// The city guards a quest calls: how many come and from where (watchmen converted from civilians who saw the player, or
/// arrivals after the report travels), each admitted to the watch and joined to a legal response already under way.
/// </summary>
/// <remarks>Where the player is, the active profile and its interior change with the site, so they are read as they are
/// asked; a converted guard civilian is retired through Actors' civilians, handed in.</remarks>
internal sealed class DaggerfallQuestGuards(ActorsState actors, DaggerfallNpcRegistry npcs, PlayerControlState playerControl, DaggerfallCrimeState crime,
    IReadOnlyDictionary<long, DaggerfallActorDefinition> actorDefinitions, DaggerfallWorldTime time, DaggerfallCrimes crimes, DaggerfallLaw law,
    DaggerfallQuestSpawningTuning spawning, DaggerfallLawTuning lawTuning, Func<DaggerfallQuestLocationRead> questLocation,
    Func<DaggerfallWorldProfileKey> activeProfile, Func<DaggerfallSiteProfile> profile, Action<long> retireCivilian)
{
    internal DaggerfallQuestGuardSpawnState? Spawn(string operation, bool immediate, double elapsedSeconds,
        DaggerfallQuestGuardSpawnState? state)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (state is { Outcome: not DaggerfallQuestGuardSpawnOutcome.Pending }) return state;
        var location = questLocation();
        if (state is not null && (state.Profile != activeProfile() || state.Location != location.ExteriorLocation?.Id))
            return state with { Remaining = 0, CandidateNpcs = [], Outcome = DaggerfallQuestGuardSpawnOutcome.LeftLocation };
        if (activeProfile().Kind == DaggerfallWorldProfileKind.Dungeon)
            throw new NotSupportedException("City guards cannot be requested inside a dungeon; the request awaits an admitted settlement.");
        if (playerControl.Position is not WorldPoint player || actors.Player.IsDefeated) return state;
        if (location.ExteriorLocation is not { } town)
            throw new NotSupportedException("City guards require an admitted settlement; the request cannot summon watchmen in wilderness.");
        if (state is null)
        {
            DaggerfallQuestGuardSpawnState Result(DaggerfallQuestGuardSpawnOutcome outcome, int count = 0, double delay = 0, long[]? candidates = null) =>
                new(activeProfile(), town.Id, delay, count, 0, candidates ?? [], outcome);
            if (actors.All.Count(actor => !actor.IsDefeated && actorDefinitions.GetValueOrDefault(actor.DurableId)?.MobileId == 146)
                > spawning.MaximumActiveGuards) return Result(DaggerfallQuestGuardSpawnOutcome.ActiveLimit);
            int count = crimes.Roll(operation, "quest-guard-count", lawTuning.MinimumGuards, lawTuning.MaximumGuards);
            if (activeProfile().Kind == DaggerfallWorldProfileKind.Interior)
            {
                int building = profile().InteriorBuilding?.BuildingType ?? -1;
                if (!(building == 15 || building is >= 17 and <= 20
                    || DaggerfallNpcServiceFacts.IsShop(building) && DaggerfallBuildingEntryPolicy.IsOpenHour(building, time.Calendar.Hour)))
                    throw new NotSupportedException("This building is not an open shop, tavern, or residence for city guard arrival.");
                state = Result(DaggerfallQuestGuardSpawnOutcome.Pending, count);
            }
            else
            {
                var candidates = npcs.All.Where(npc => npc.Kind == DaggerfallNpcKind.Civilian && npcs.IsGameplayActive(npc.DurableId)
                    && actors.TryGet(npc.DurableId, out var actor) && !actor.IsDefeated
                    && Vector3.Distance(actor.Position.ToVector(), player.ToVector()) <= spawning.GuardWitnessDistance).ToDictionary(npc => npc.DurableId);
                long[] convert;
                if (immediate)
                {
                    Vector3 forward = ActorHeading.Forward(playerControl.YawRadians);
                    float behind = MathF.Cos(spawning.GuardConversionAngleDegrees * MathF.PI / 180f);
                    convert = candidates.Values.Where(npc => npc.Role == "guard" ||
                        Vector3.Dot(Vector3.Normalize(actors.Get(npc.DurableId).Position.ToVector() - player.ToVector()), forward) <= behind
                            && crimes.Roll(operation, "quest-guard-convert:" + npc.DurableId, 1, 100) <= spawning.GuardConversionChance)
                        .OrderByDescending(npc => npc.Role == "guard").ThenBy(npc => npc.DurableId).Select(npc => npc.DurableId).ToArray();
                    state = Result(DaggerfallQuestGuardSpawnOutcome.Pending, convert.Length == 0 ? count : convert.Length, candidates: convert);
                }
                else
                {
                    var witnesses = crimes.QueryWitnesses(actor => candidates.ContainsKey(actor.DurableId),
                        spawning.GuardWitnessDistance, Math.Cos(spawning.GuardWitnessAngleDegrees * Math.PI / 180));
                    if (witnesses.Query != DaggerfallCrimeWitnessQuery.CompletedWithWitnesses) return Result(DaggerfallQuestGuardSpawnOutcome.NoWitness);
                    convert = witnesses.WitnessActorIds.Where(id => candidates[id].Role == "guard").ToArray();
                    state = Result(DaggerfallQuestGuardSpawnOutcome.Pending, convert.Length == 0 ? count : convert.Length,
                        convert.Length == 0 ? crimes.Roll(operation, "quest-guard-delay", lawTuning.MinimumResponseSeconds, lawTuning.MaximumResponseSeconds) : 0, convert);
                    if (state.DelaySeconds > 0) return state;
                }
            }
        }
        else if (state.DelaySeconds > 0)
        {
            state = state with { DelaySeconds = Math.Max(0, state.DelaySeconds - elapsedSeconds) };
            if (state.DelaySeconds > 0) return state;
            // PlayerEntity's countdown re-enters the immediate request: population may have
            // changed while the report travelled, so conversions precede random fallback.
            return Spawn(operation, true, 0, null);
        }
        if (state.Remaining == 0) return state with { Outcome = DaggerfallQuestGuardSpawnOutcome.Complete };
        long? candidate = state.CandidateNpcs.Length > 0 ? state.CandidateNpcs[0] : null;
        ActorPose pose;
        if (candidate is { } npcId)
        {
            if (!npcs.IsGameplayActive(npcId) || !actors.TryGet(npcId, out var actor) || actor.IsDefeated)
                return state with { CandidateNpcs = state.CandidateNpcs[1..], Remaining = state.Remaining - 1 };
            pose = actor.Pose;
        }
        else if (!law.TryGuardArrivalPose(operation + "/quest/" + state.SpawnedCount, player, out pose)) return state;
        long guard = law.AdmitCityWatch(pose);
        if (candidate is { } converted && npcs.Require(converted).Role == "guard") retireCivilian(converted);
        // The law owner receives actual watchmen only when a current case already exists.
        // Spawning never invents an accusation or a second legal response.
        if (law.Current is { Modal: false } response && response.Profile.Require() == activeProfile())
            crime.SetResponse(response with { Guards = [.. response.Guards, guard] });
        int remaining = state.Remaining - 1;
        return state with { Remaining = remaining, SpawnedCount = state.SpawnedCount + 1,
            CandidateNpcs = candidate is null ? [] : state.CandidateNpcs[1..],
            Outcome = remaining == 0 ? DaggerfallQuestGuardSpawnOutcome.Complete : DaggerfallQuestGuardSpawnOutcome.Pending };
    }
}
