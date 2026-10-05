using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestLocationRead(DaggerfallSiteProfile Profile, DaggerfallSiteRecord? ExteriorLocation, int? DungeonType);
internal sealed record DaggerfallQuestLocationTransition(int? Current, int? Previous)
{
    internal void Validate()
    {
        foreach (int? kind in new[] { Current, Previous })
            if (kind is { } value && !Enum.IsDefined((DaggerfallSiteKind)value)) throw new ArgumentException("Quest exterior transition has an unknown location kind.");
    }
    internal DaggerfallQuestLocationTransition Observe(int? current) => current == Current ? this : new(current, Current);
}

internal static partial class DaggerfallQuestTaskCompiler
{
    private static readonly System.Text.RegularExpressions.Regex PcAt = Header(@"^pc\s+at\s+(?<any>any\s+)?(?<place>[a-zA-Z0-9_.-]+)\s+(?:set|do)\s+(?<task>[a-zA-Z0-9_.-]+)(?:\s+saying\s+(?<message>\d+))?$");
    private static readonly System.Text.RegularExpressions.Regex PcTransition = Header(@"^when\s+pc\s+(?<verb>enters|exits)\s+(?<place>[a-zA-Z0-9_.-]+)$");
    private static DaggerfallQuestTaskOperation? CompileWorldTrigger(string line, int sourceLine)
    {
        if (PcAt.Match(line) is { Success: true } at)
            return new(at.Groups["any"].Success ? DaggerfallQuestTaskOperationKind.PcAtAny : DaggerfallQuestTaskOperationKind.PcAt, sourceLine, line,
                [Canonical(at.Groups["place"].Value), Canonical(at.Groups["task"].Value)], [], at.Groups["message"].Success ? Step(at.Groups["message"].Value, sourceLine) : null);
        if (PcTransition.Match(line) is { Success: true } transition)
            return new(transition.Groups["verb"].Value.Equals("enters", StringComparison.OrdinalIgnoreCase) ? DaggerfallQuestTaskOperationKind.WhenPcEnters : DaggerfallQuestTaskOperationKind.WhenPcExits,
                sourceLine, line, [Canonical(transition.Groups["place"].Value)], [], null);
        return null;
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private Func<DaggerfallQuestLocationRead>? _worldRead;
    internal void BindWorldRead(Func<DaggerfallQuestLocationRead> read) => _worldRead = read;
    private void InitializeWorldTriggers(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskProgram program)
    {
        for (int task = 0; task < program.Tasks.Count; task++)
            for (int op = 0; op < program.Tasks[task].Operations.Count; op++)
                if (program.Tasks[task].Operations[op].Kind is DaggerfallQuestTaskOperationKind.WhenPcEnters or DaggerfallQuestTaskOperationKind.WhenPcExits)
                    instance.Tasks[task].OperationState[op] = new(null, null) { Location = new(WorldRead().ExteriorLocation is { } site ? (int)site.Kind : null, null) };
    }
    private DaggerfallQuestLocationRead WorldRead() => (_worldRead ?? throw new InvalidOperationException("Quest world reader has not been composed."))();
    bool IDaggerfallQuestTaskLifecycle.PlayerAt(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        var current = WorldRead();
        if (operation.Kind == DaggerfallQuestTaskOperationKind.PcAt) return IsAtPlace(instance, operation.Targets[0]);
        var place = WorldPlace(operation);
        return place.P1 switch
        {
            0 => current.Profile.ProfileKind == DaggerfallWorldProfileKind.Interior && current.Profile.InteriorBuilding is { } building
                && DaggerfallQuestPlaceAllocator.BuildingMatches(building.BuildingType, building.FactionId, place.P2, place.P3),
            1 => current.Profile.ProfileKind == DaggerfallWorldProfileKind.Dungeon && (place.P2 == -1 || current.DungeonType == place.P2),
            _ => throw new NotSupportedException($"PcAt any '{place.Name}' at line {operation.SourceLine} requires a building or dungeon table discriminator, observed p1={place.P1}."),
        };
    }
    private DaggerfallQuestPlace WorldPlace(DaggerfallQuestTaskOperation operation)
    {
        try { return _definitions.QuestSources.Tables.Places.Resolve(operation.Targets[0]); }
        catch (KeyNotFoundException) { throw new NotSupportedException($"Place type '{operation.Targets[0]}' is absent from the admitted quest table."); }
    }
    bool IDaggerfallQuestTaskLifecycle.WorldTransition(DaggerfallQuestTaskOperation operation, DaggerfallQuestTaskRuntimeState state, int operationIndex)
    {
        var place = WorldPlace(operation);
        if (place.P1 != 2) throw new NotSupportedException($"Quest exterior transition '{place.Name}' at line {operation.SourceLine} requires p1=2, observed {place.P1}.");
        int? kind = WorldRead().ExteriorLocation is { } location ? (int)location.Kind : null;
        var saved = state.OperationState[operationIndex];
        var observed = saved.Location is { } previous ? previous.Observe(kind) : new DaggerfallQuestLocationTransition(kind, null);
        state.OperationState[operationIndex] = saved with { Location = observed };
        int? target = operation.Kind == DaggerfallQuestTaskOperationKind.WhenPcEnters ? observed.Current : observed.Previous;
        return target is { } actual && (place.P2 == -1 || place.P2 == actual);
    }
}
