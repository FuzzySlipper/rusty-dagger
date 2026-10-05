using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestFoeSpawn(int IntervalSeconds, int? MaximumGroups, int Chance, bool Send);
internal sealed record DaggerfallQuestFoeSpawnState(long LastAttemptSeconds, int Attempts, int CompletedGroups,
    int PendingRemaining, DaggerfallWorldProfileKey? PendingProfile, bool MessageSent);

internal static partial class DaggerfallQuestTaskCompiler
{
    private static readonly System.Text.RegularExpressions.Regex SpawnFoe = Header(@"^(?<verb>create\s+foe|send)\s+(?<foe>[a-zA-Z0-9_.-]+)\s+every\s+(?<minutes>\d+)\s+minutes(?:\s+(?:(?<count>\d+)\s+times|(?<forever>indefinitely)))?\s+with\s+(?<chance>\d+)%\s+success(?:\s+msg\s+(?<message>\d+))?$");
    private static DaggerfallQuestTaskOperation? CompileSpawning(string line, int sourceLine)
    {
        if (line.Equals("spawncityguards", StringComparison.OrdinalIgnoreCase) || line.Equals("spawncityguards immediate", StringComparison.OrdinalIgnoreCase))
            return new(DaggerfallQuestTaskOperationKind.SpawnCityGuards, sourceLine, line, [], [], null, Step: line.EndsWith(" immediate", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        if (SpawnFoe.Match(line) is not { Success: true } match) return null;
        bool send = match.Groups["verb"].Value.Equals("send", StringComparison.OrdinalIgnoreCase);
        if (!send && !match.Groups["count"].Success && !match.Groups["forever"].Success)
            throw new ArgumentException($"Create foe at line {sourceLine} requires a count or indefinitely.");
        int chance = Step(match.Groups["chance"].Value, sourceLine);
        if (chance > 100) throw new ArgumentException($"Quest spawn chance at line {sourceLine} exceeds 100 percent.");
        int? count = match.Groups["count"].Success ? Step(match.Groups["count"].Value, sourceLine) : null;
        if (send && count == 0) count = null; // Source send's omitted/zero count means indefinite.
        return new(DaggerfallQuestTaskOperationKind.CreateFoe, sourceLine, line, [Canonical(match.Groups["foe"].Value)], [],
            match.Groups["message"].Success ? Step(match.Groups["message"].Value, sourceLine) : null,
            FoeSpawn: new(checked(Step(match.Groups["minutes"].Value, sourceLine) * 60), count, chance, send));
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private Func<string, DaggerfallQuestFoeSelection, bool, long?>? _spawnQuestFoe;
    internal void BindFoeSpawning(Func<string, DaggerfallQuestFoeSelection, bool, long?> spawn) => _spawnQuestFoe = spawn;

    void IDaggerfallQuestTaskLifecycle.SpawnFoes(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation,
        DaggerfallQuestTaskRuntimeState task, int index, DaggerfallCalendar calendar)
    {
        var specification = operation.FoeSpawn!;
        var resource = FoeResource(instance, operation);
        long now = calendar.ToAbsoluteSeconds();
        string key = instance.InstanceId + "/" + task.Symbol + "/" + index;
        var retained = task.OperationState[index].FoeSpawn;
        var state = retained is { LastAttemptSeconds: >= 0 } ? retained : new(now - _random.DrawKeyed(new(0, "daggerfall.quest.spawn",
            key + "/initial-delay", 0, Math.Max(0, specification.IntervalSeconds - 1))).Value, 0, 0, 0, null, retained?.MessageSent ?? false);
        var location = (_worldRead ?? throw new InvalidOperationException("Quest spawning requires the current admitted location."))();
        if (state.PendingProfile is { } prior && prior != location.Profile.ProfileKey)
            state = state with { PendingRemaining = 0, PendingProfile = null };
        if (specification.MaximumGroups is { } maximum && state.CompletedGroups >= maximum)
        { task.OperationState[index] = task.OperationState[index] with { FoeSpawn = state }; return; }
        if (state.PendingRemaining == 0 && now >= state.LastAttemptSeconds + specification.IntervalSeconds)
        {
            state = state with { LastAttemptSeconds = now, Attempts = checked(state.Attempts + 1) };
            int roll = checked((int)_random.DrawKeyed(new(0, "daggerfall.quest.spawn", key + "/chance/" + state.Attempts, 1, 100)).Value);
            if (roll <= specification.Chance && !resource.IsHidden)
                state = state with { PendingRemaining = resource.SelectedFoe!.Count, PendingProfile = location.Profile.ProfileKey };
        }
        task.OperationState[index] = task.OperationState[index] with { FoeSpawn = state };
        if (state.PendingRemaining == 0 || resource.IsHidden || specification.Send && location.ExteriorLocation is null) return;
        // Resolve the first-spawn message before any physical mutation. Later delivery uses
        // the committed actor binding, and does not repeat on another group or a loaded save.
        if (!state.MessageSent && operation.MessageId is { } message
            && !Messages.TryResolveMessage(instance, message, null, out _, out var diagnostic))
            throw new ArgumentException($"Quest spawn at line {operation.SourceLine}: {diagnostic}");
        long? actor = (_spawnQuestFoe ?? throw new InvalidOperationException("No canonical quest foe admission owner is composed."))(
            key + "/" + state.Attempts + "/" + (resource.SelectedFoe!.Count - state.PendingRemaining), resource.SelectedFoe, specification.Send);
        if (actor is not { } id) return;
        SetResource(instance.InstanceId, resource with { Binding = DaggerfallQuestResourceBinding.Actors([.. resource.Binding.ActorIds, id]) });
        int remaining = state.PendingRemaining - 1;
        bool notify = !state.MessageSent && operation.MessageId is not null;
        state = state with { PendingRemaining = remaining, PendingProfile = remaining == 0 ? null : state.PendingProfile,
            CompletedGroups = state.CompletedGroups + (remaining == 0 ? 1 : 0), MessageSent = state.MessageSent || notify };
        task.OperationState[index] = task.OperationState[index] with { FoeSpawn = state };
        if (notify) Messages.Popup(instance, operation.MessageId!.Value);
    }
}
