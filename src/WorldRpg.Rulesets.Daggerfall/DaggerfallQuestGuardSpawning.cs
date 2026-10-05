using WorldRpg.Rulesets.Daggerfall.Content;

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
