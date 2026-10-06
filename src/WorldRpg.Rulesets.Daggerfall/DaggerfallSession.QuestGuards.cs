using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private DaggerfallQuestGuardSpawnState? SpawnQuestGuards(string operation, bool immediate, double elapsedSeconds,
        DaggerfallQuestGuardSpawnState? state)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (state is { Outcome: not DaggerfallQuestGuardSpawnOutcome.Pending }) return state;
        var location = _sites.ReadQuestLocation();
        if (state is not null && (state.Profile != _activeProfileKey || state.Location != location.ExteriorLocation?.Id))
            return state with { Remaining = 0, CandidateNpcs = [], Outcome = DaggerfallQuestGuardSpawnOutcome.LeftLocation };
        if (_activeProfileKey.Kind == DaggerfallWorldProfileKind.Dungeon)
            throw new NotSupportedException("City guards cannot be requested inside a dungeon; the request awaits an admitted settlement.");
        if (State.PlayerControl.Position is not WorldPoint player || State.Actors.Player.IsDefeated) return state;
        if (location.ExteriorLocation is not { } town)
            throw new NotSupportedException("City guards require an admitted settlement; the request cannot summon watchmen in wilderness.");
        if (state is null)
        {
            DaggerfallQuestGuardSpawnState Result(DaggerfallQuestGuardSpawnOutcome outcome, int count = 0, double delay = 0, long[]? candidates = null) =>
                new(_activeProfileKey, town.Id, delay, count, 0, candidates ?? [], outcome);
            if (State.Actors.All.Count(actor => !actor.IsDefeated && _roster.Definitions.GetValueOrDefault(actor.DurableId)?.MobileId == 146)
                > _tuning.QuestSpawning.MaximumActiveGuards) return Result(DaggerfallQuestGuardSpawnOutcome.ActiveLimit);
            int count = CrimeRoll(operation, "quest-guard-count", _tuning.Law.MinimumGuards, _tuning.Law.MaximumGuards);
            if (_activeProfileKey.Kind == DaggerfallWorldProfileKind.Interior)
            {
                int building = _sites.Projection.Inputs.InteriorBuilding?.BuildingType ?? -1;
                if (!(building == 15 || building is >= 17 and <= 20
                    || DaggerfallNpcServiceFacts.IsShop(building) && DaggerfallCrimePolicy.IsPublicEntryHour(building, _time.Calendar.Hour)))
                    throw new NotSupportedException("This building is not an open shop, tavern, or residence for city guard arrival.");
                state = Result(DaggerfallQuestGuardSpawnOutcome.Pending, count);
            }
            else
            {
                var candidates = State.Npcs.All.Where(npc => npc.Kind == DaggerfallNpcKind.Civilian && State.Npcs.IsGameplayActive(npc.DurableId)
                    && State.Actors.TryGet(npc.DurableId, out var actor) && !actor.IsDefeated
                    && Vector3.Distance(actor.Position.ToVector(), player.ToVector()) <= _tuning.QuestSpawning.GuardWitnessDistance).ToDictionary(npc => npc.DurableId);
                long[] convert;
                if (immediate)
                {
                    Vector3 forward = ActorHeading.Forward(State.PlayerControl.YawRadians);
                    float behind = MathF.Cos(_tuning.QuestSpawning.GuardConversionAngleDegrees * MathF.PI / 180f);
                    convert = candidates.Values.Where(npc => npc.Role == "guard" ||
                        Vector3.Dot(Vector3.Normalize(State.Actors.Get(npc.DurableId).Position.ToVector() - player.ToVector()), forward) <= behind
                            && CrimeRoll(operation, "quest-guard-convert:" + npc.DurableId, 1, 100) <= _tuning.QuestSpawning.GuardConversionChance)
                        .OrderByDescending(npc => npc.Role == "guard").ThenBy(npc => npc.DurableId).Select(npc => npc.DurableId).ToArray();
                    state = Result(DaggerfallQuestGuardSpawnOutcome.Pending, convert.Length == 0 ? count : convert.Length, candidates: convert);
                }
                else
                {
                    var witnesses = QueryCrimeWitnesses(actor => candidates.ContainsKey(actor.DurableId),
                        _tuning.QuestSpawning.GuardWitnessDistance, Math.Cos(_tuning.QuestSpawning.GuardWitnessAngleDegrees * Math.PI / 180));
                    if (witnesses.Query != DaggerfallCrimeWitnessQuery.CompletedWithWitnesses) return Result(DaggerfallQuestGuardSpawnOutcome.NoWitness);
                    convert = witnesses.WitnessActorIds.Where(id => candidates[id].Role == "guard").ToArray();
                    state = Result(DaggerfallQuestGuardSpawnOutcome.Pending, convert.Length == 0 ? count : convert.Length,
                        convert.Length == 0 ? CrimeRoll(operation, "quest-guard-delay", _tuning.Law.MinimumResponseSeconds, _tuning.Law.MaximumResponseSeconds) : 0, convert);
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
            return SpawnQuestGuards(operation, true, 0, null);
        }
        if (state.Remaining == 0) return state with { Outcome = DaggerfallQuestGuardSpawnOutcome.Complete };
        long? candidate = state.CandidateNpcs.Length > 0 ? state.CandidateNpcs[0] : null;
        ActorPose pose;
        if (candidate is { } npcId)
        {
            if (!State.Npcs.IsGameplayActive(npcId) || !State.Actors.TryGet(npcId, out var actor) || actor.IsDefeated)
                return state with { CandidateNpcs = state.CandidateNpcs[1..], Remaining = state.Remaining - 1 };
            pose = actor.Pose;
        }
        else if (!TryGuardArrivalPose(operation + "/quest/" + state.SpawnedCount, player, out pose)) return state;
        long guard = AdmitCityWatch(pose);
        if (candidate is { } converted && State.Npcs.Require(converted).Role == "guard") RetireNpcActor(converted);
        // The law owner receives actual watchmen only when a current case already exists.
        // Spawning never invents an accusation or a second legal response.
        if (CurrentLegalResponse is { Modal: false } response && response.Profile.Require() == _activeProfileKey)
            State.Crime.SetResponse(response with { Guards = [.. response.Guards, guard] });
        int remaining = state.Remaining - 1;
        return state with { Remaining = remaining, SpawnedCount = state.SpawnedCount + 1,
            CandidateNpcs = candidate is null ? [] : state.CandidateNpcs[1..],
            Outcome = remaining == 0 ? DaggerfallQuestGuardSpawnOutcome.Complete : DaggerfallQuestGuardSpawnOutcome.Pending };
    }
}
