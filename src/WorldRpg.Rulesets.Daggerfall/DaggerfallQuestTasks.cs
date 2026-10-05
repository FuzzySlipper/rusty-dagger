using System.Text.RegularExpressions;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The source-defined forms whose trigger state belongs to a quest instance.</summary>
internal enum DaggerfallQuestTaskKind { Headless, Standard, Variable, PersistUntil, Global }
internal enum DaggerfallQuestTaskOperationKind { When, CreateNpc, HideNpc, RestoreNpc, DestroyNpc, WhenNpcAvailable, ClickedNpc, ClickedFoe, PcAt, PcAtAny, WhenPcEnters, WhenPcExits, DailyFrom, LevelCompleted, WhenAttributeLevel, WhenSkillLevel, Start, Clear, Unset, StartClock, StopClock, Journal, RemoveJournal, JournalNote, Say, Rumor, Prompt, PickOneOf, RunQuest, StartQuest, CureLycanthropy, TrainPc, GetItem, HaveItem, TakeItem, MakePermanent, ReservePlace, PlaceFoe, PlaceItem, PlaceNpc, AddQuestor, DropQuestor, AddFace, DropFace, MuteNpc, InjuredFoe, KilledFoe, KillFoe, RemoveFoe, End, Unsupported }
internal enum DaggerfallQuestTaskConditionOperator { When, WhenNot, And, AndNot, Or, OrNot }

/// <summary>One durable trigger state. Operation completion aligns with the compiled source operation order.</summary>
internal sealed record DaggerfallQuestTaskOperationState(string? PickedTarget, string? ChildInstanceId)
{
    public DaggerfallQuestNpcAvailability? NpcAvailability { get; init; }
    public DaggerfallQuestLocationTransition? Location { get; init; }
    public string? UnavailableReason { get; init; }
}
internal sealed record DaggerfallQuestTaskState(string Symbol, DaggerfallQuestTaskKind Kind, bool IsSet, bool WasSet, bool IsDropped, bool[] OperationCompleted)
{
    /// <summary>Receipts for operations whose result must remain stable across a current-schema save.</summary>
    [System.Text.Json.Serialization.JsonRequired]
    public DaggerfallQuestTaskOperationState[] OperationState { get; init; } = [];
}

/// <summary>Mutable runtime state for one compiled task; save records are created only at capture boundaries.</summary>
internal sealed class DaggerfallQuestTaskRuntimeState
{
    internal DaggerfallQuestTaskRuntimeState(DaggerfallQuestTaskState saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        Symbol = saved.Symbol;
        Kind = saved.Kind;
        IsSet = saved.IsSet;
        WasSet = saved.WasSet;
        IsDropped = saved.IsDropped;
        OperationCompleted = [.. saved.OperationCompleted];
        OperationState = [.. saved.OperationState];
    }

    internal string Symbol { get; }
    internal DaggerfallQuestTaskKind Kind { get; }
    internal bool IsSet { get; set; }
    internal bool WasSet { get; set; }
    internal bool IsDropped { get; set; }
    internal bool[] OperationCompleted { get; set; }
    internal DaggerfallQuestTaskOperationState[] OperationState { get; set; }
    internal DaggerfallQuestTaskState Capture() => new(Symbol, Kind, IsSet, WasSet, IsDropped, [.. OperationCompleted]) { OperationState = [.. OperationState] };
}

internal sealed record DaggerfallQuestTaskCondition(DaggerfallQuestTaskConditionOperator Operator, string Symbol);
internal sealed record DaggerfallQuestTaskOperation(DaggerfallQuestTaskOperationKind Kind, int SourceLine, string Source,
    string[] Targets, DaggerfallQuestTaskCondition[] Conditions, int? MessageId, int? Step = null, string? MessageAlias = null, DaggerfallQuestPromptOption[]? PromptOptions = null,
    int? MarkerIndex = null, DaggerfallQuestMarkerPreference MarkerPreference = DaggerfallQuestMarkerPreference.Default);
internal sealed record DaggerfallQuestTaskDefinition(string Symbol, DaggerfallQuestTaskKind Kind, string? PersistUntilTarget,
    string? GlobalName, IReadOnlyList<DaggerfallQuestTaskOperation> Operations);
internal sealed class DaggerfallQuestTaskProgram
{
    internal DaggerfallQuestTaskProgram(IReadOnlyList<DaggerfallQuestTaskDefinition> tasks)
    {
        Tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        TaskIndexes = tasks.Select((task, index) => (task.Symbol, index)).ToDictionary(value => value.Symbol, value => value.index, StringComparer.Ordinal);
    }

    internal IReadOnlyList<DaggerfallQuestTaskDefinition> Tasks { get; }
    internal IReadOnlyDictionary<string, int> TaskIndexes { get; }
}

/// <summary>Compiles the retained task forms into concrete ruleset operations; it has no runtime registry.</summary>
internal static partial class DaggerfallQuestTaskCompiler
{
    private static readonly Regex ReservePlace = Header(@"^create\s+npc\s+at\s+(?<place>[a-zA-Z0-9_.-]+)$");
    private static readonly Regex PlaceActor = Header(@"^place\s+(?<kind>foe|npc)\s+(?<symbol>[a-zA-Z0-9_.-]+)\s+at\s+(?<place>[a-zA-Z0-9_.-]+)(?:\s+marker\s+(?<marker>\d+))?$");
    private static readonly Regex PlaceItem = Header(@"^place\s+item\s+(?<symbol>[a-zA-Z0-9_.-]+)\s+at\s+(?<place>[a-zA-Z0-9_.-]+)(?:\s+(?:(?<markerKind>marker|questmarker)\s+(?<marker>\d+)|(?<any>anymarker)))?$");
    private static readonly Regex GetItem = Header(@"^get\s+item\s+(?<symbol>[a-zA-Z0-9_.-]+)(?:\s+from\s+[a-zA-Z0-9_.-]+)?(?:\s+saying\s+(?<message>\d+))?$");
    private static readonly Regex TakeItem = Header(@"^take\s+(?<symbol>[a-zA-Z0-9_.-]+)\s+from\s+pc(?:\s+saying\s+(?<message>\d+))?$");
    private static readonly Regex HaveItem = Header(@"^have\s+(?<symbol>[a-zA-Z0-9_.-]+)\s+set\s+(?<task>[a-zA-Z0-9_.-]+)$");
    private static readonly Regex Permanent = Header(@"^make\s+(?<symbol>[a-zA-Z0-9_.-]+)\s+permanent$");
    private static readonly Regex GiveNotification = Header(@"^give\s+pc\s+(?<symbol>[a-zA-Z0-9_.-]+)(?:\s+notify\s+(?<message>[a-zA-Z0-9_.-]+))?$");
    private static readonly Regex StandardHeader = Header("^(?<symbol>[a-zA-Z0-9_.]+)\\s+task:$");
    private static readonly Regex VariableHeader = Header("^variable\\s+(?<symbol>[a-zA-Z0-9_.]+)$");
    private static readonly Regex PersistHeader = Header("^until\\s+(?<symbol>[a-zA-Z0-9_.]+)\\s+performed:$");
    private static readonly Regex GlobalHeader = Header("^(?<global>[a-zA-Z0-9_.]+)\\s+(?<symbol>[a-zA-Z0-9_.]+)$");
    private static readonly Regex Start = Header("^(?:start\\s+task|setvar)\\s+(?<symbol>[a-zA-Z0-9_.]+)$");
    private static readonly Regex InjuredFoe = Header(@"^injured\s+(?<symbol>[a-zA-Z0-9_.-]+)(?:\s+saying\s+(?<message>\d+))?$");
    private static readonly Regex KilledFoe = Header(@"^killed\s+(?:(?<count>\d+)\s+)?(?<symbol>[a-zA-Z0-9_.-]+)(?:\s+saying\s+(?<message>\d+))?$");
    private static readonly Regex CommandFoe = Header(@"^(?<verb>kill|remove)\s+foe\s+(?<symbol>[a-zA-Z0-9_.-]+)$");
    private static readonly Regex Questor = Header("^(?<verb>add|drop)\\s+(?<symbol>[a-zA-Z0-9_.-]+)\\s+as\\s+questor$");
    private static readonly Regex Face = Header("^(?<verb>add|drop)\\s+(?<foe>foe\\s+)?(?<symbol>[a-zA-Z0-9_.-]+)\\s+face(?:\\s+saying\\s+(?<message>[0-9]+))?$");
    private static readonly Regex Mute = Header("^mute\\s+npc\\s+(?<symbol>[a-zA-Z0-9_.-]+)$");
    private static readonly Regex Clear = Header("^clear\\s+(?<symbols>[a-zA-Z0-9_.]+(?:\\s+[a-zA-Z0-9_.]+)*)$");
    private static readonly Regex Unset = Header("^unset\\s+(?<symbols>[a-zA-Z0-9_.]+(?:\\s+[a-zA-Z0-9_.]+)*)$");
    private static readonly Regex End = Header("^end\\s+quest(?:\\s+saying\\s+(?<message>\\d+))?$");
    private static readonly Regex PickOneOf = Header("^pick\\s+one\\s+of\\s+(?<targets>[a-zA-Z0-9_.]+(?:\\s+[a-zA-Z0-9_.]+)+)$");
    private static readonly Regex RunQuest = Header("^run\\s+quest\\s+(?<quest>[a-zA-Z0-9_.]+)\\s+then\\s+(?<success>[a-zA-Z0-9_.]+)\\s+or\\s+(?<failure>[a-zA-Z0-9_.]+)$");
    private static readonly Regex StartQuest = Header("^start\\s+quest\\s+(?:(?<first>\\d+)\\s+(?<second>\\d+)|(?<quest>[a-zA-Z0-9_.]+))$");
    private static readonly Regex LevelCompleted = Header("^level\\s+(?<minimum>\\d+)\\s+completed$");
    private static readonly Regex WhenAttributeLevel = Header("^when\\s+attribute\\s+(?<attribute>\\w+)\\s+is\\s+at\\s+least\\s+(?<minimum>\\d+)$");
    private static readonly Regex WhenSkillLevel = Header("^when\\s+skill\\s+(?<skill>\\w+)\\s+is\\s+at\\s+least\\s+(?<minimum>\\d+)$");
    private static readonly Regex TrainPc = Header("^train\\s+pc\\s+(?<skill>\\w+)$");
    private static readonly Regex Journal = Header("^log\\s+(?<message>\\d+)\\s+(?:step\\s+)?(?<step>\\d+)$");
    private static readonly Regex JournalNote = Header(@"^journal\s+note\s+(?<message>\d+)$");
    private static readonly Regex Say = Header(@"^say\s+(?<message>[a-zA-Z0-9_.]+)$");
    private static readonly Regex RemoveJournal = Header("^remove\\s+log\\s+step\\s+(?<step>\\d+)$");
    private static readonly Regex Rumor = Header("^rumor\\s+mill\\s+(?<message>\\d+)$");
    private static string ButtonLabel(int id) => id switch
    {
        0 => "Accept", 1 => "Reject", 2 => "Cancel", 3 => "Yes", 4 => "No", 5 => "OK", 6 => "Male", 7 => "Female",
        8 => "Add", 9 => "Delete", 10 => "Edit", 11 => "Counter", 12 => "12 months", 13 => "36 months", 14 => "Copy",
        15 => "Guilty", 16 => "Not guilty", 17 => "Debate", 18 => "Lie", 19 => "Anchor", 20 => "Teleport",
        _ => throw new ArgumentException($"Quest prompt button {id} requires its source label."),
    };
    private static readonly Regex PromptMulti = Header(@"^promptmulti\s+(?<message>\d+)(?<options>(?:\s+\d+(?::[a-zA-Z0-9]+)?\s+[a-zA-Z0-9_.]+){2,4})$");
    private static readonly Regex PromptOption = Header(@"(?<id>\d+)(?::(?<label>[a-zA-Z0-9]+))?\s+(?<target>[a-zA-Z0-9_.]+)");
    private static readonly Regex Prompt = Header("^prompt\\s+(?<message>[a-zA-Z0-9.]+)\\s+yes\\s+(?<yes>[a-zA-Z0-9_.]+)\\s+no\\s+(?<no>[a-zA-Z0-9_.]+)$");
    private static readonly Regex Timer = Header("^(?<start>start)\\s+timer\\s+(?<symbol>[a-zA-Z0-9_.-]+)$|^stop\\s+timer\\s+(?<stop>[a-zA-Z0-9_.-]+)$");
    private static readonly Regex Daily = Header("^daily\\s+from\\s+(?<fromHour>\\d+):(?<fromMinute>\\d+)\\s+to\\s+(?<toHour>\\d+):(?<toMinute>\\d+)$");
    private static readonly Regex WhenTerm = new("(?<operator>when not|when|and not|and|or not|or)\\s+(?<symbol>[a-zA-Z0-9_.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static DaggerfallQuestTaskProgram Compile(DaggerfallQuestSourceDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        List<DaggerfallQuestTaskDefinition> tasks = [];
        foreach (DaggerfallQuestBlockDefinition block in source.Blocks)
        {
            if (block.Lines.Count == 0 || !IsTaskBlock(block.Kind)) continue;
            tasks.Add(CompileBlock(block));
        }

        HashSet<string> symbols = new(StringComparer.Ordinal);
        foreach (DaggerfallQuestTaskDefinition task in tasks)
            if (!symbols.Add(task.Symbol)) throw new ArgumentException($"Quest source '{source.SourceFile}' declares task '{task.Symbol}' more than once.");
        return new(tasks);
    }

    /// <summary>Assesses executable task bodies using the same compiler and operation kinds the runner advances.</summary>
    internal static IReadOnlyList<DaggerfallQuestDiagnosticDefinition> Assess(DaggerfallQuestSourceDefinition source)
    {
        try
        {
            DaggerfallQuestTaskProgram program = Compile(source);
            return [.. program.Tasks.SelectMany(task => task.Operations)
                .Where(operation => operation.Kind == DaggerfallQuestTaskOperationKind.Unsupported)
                .Select(operation => new DaggerfallQuestDiagnosticDefinition(operation.SourceLine, operation.Source, "No current quest task runner operation supports this action."))];
        }
        catch (ArgumentException exception)
        {
            return [new DaggerfallQuestDiagnosticDefinition(1, source.SourceFile, exception.Message)];
        }
    }

    internal static DaggerfallQuestTaskState[] InitialState(DaggerfallQuestTaskProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return [.. program.Tasks.Select(task => new DaggerfallQuestTaskState(task.Symbol, task.Kind,
            task.Kind is DaggerfallQuestTaskKind.Headless or DaggerfallQuestTaskKind.PersistUntil, false, false,
            new bool[task.Operations.Count]) { OperationState = [.. Enumerable.Repeat(new DaggerfallQuestTaskOperationState(null, null), task.Operations.Count)] })];
    }

    internal static void ValidateState(DaggerfallQuestTaskProgram program, IReadOnlyList<DaggerfallQuestTaskState> states, string sourceFile)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(states);
        if (states.Count != program.Tasks.Count)
            throw new ArgumentException($"Quest source '{sourceFile}' task state count is incompatible with its compiled blocks.");
        for (int index = 0; index < program.Tasks.Count; index++)
        {
            DaggerfallQuestTaskState state = states[index] ?? throw new ArgumentException($"Quest source '{sourceFile}' has null task state.");
            DaggerfallQuestTaskDefinition task = program.Tasks[index];
            if (!string.Equals(state.Symbol, task.Symbol, StringComparison.Ordinal) || state.Kind != task.Kind || state.OperationCompleted is null || state.OperationCompleted.Length != task.Operations.Count
                || state.OperationState is null || state.OperationState.Length != task.Operations.Count || state.OperationState.Any(value => value is null))
                throw new ArgumentException($"Quest source '{sourceFile}' task state '{state.Symbol}' does not match the compiled task at position {index}.");
        }
    }

    private static bool IsTaskBlock(string kind) => kind.Equals("headless", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("task", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("variable", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("global", StringComparison.OrdinalIgnoreCase);

    private static DaggerfallQuestTaskDefinition CompileBlock(DaggerfallQuestBlockDefinition block)
    {
        string header = Trim(block.Lines[0]);
        DaggerfallQuestTaskKind kind;
        string symbol;
        string? target = null;
        string? global = null;
        int start;
        if (block.Kind.Equals("headless", StringComparison.OrdinalIgnoreCase))
        {
            kind = DaggerfallQuestTaskKind.Headless;
            symbol = $"headless.{block.FirstLine}";
            start = 0;
        }
        else if (PersistHeader.Match(header) is { Success: true } persist)
        {
            kind = DaggerfallQuestTaskKind.PersistUntil;
            symbol = $"persist.{block.FirstLine}";
            target = Canonical(persist.Groups["symbol"].Value);
            start = 1;
        }
        else if (block.Kind.Equals("variable", StringComparison.OrdinalIgnoreCase) && VariableHeader.Match(header) is { Success: true } variable)
        {
            kind = DaggerfallQuestTaskKind.Variable;
            symbol = Canonical(variable.Groups["symbol"].Value);
            start = 1;
        }
        else if (block.Kind.Equals("global", StringComparison.OrdinalIgnoreCase) && GlobalHeader.Match(header) is { Success: true } globalHeader)
        {
            kind = DaggerfallQuestTaskKind.Global;
            global = globalHeader.Groups["global"].Value;
            symbol = Canonical(globalHeader.Groups["symbol"].Value);
            start = 1;
        }
        else if (StandardHeader.Match(header) is { Success: true } standard)
        {
            kind = DaggerfallQuestTaskKind.Standard;
            symbol = Canonical(standard.Groups["symbol"].Value);
            start = 1;
        }
        else
        {
            throw new ArgumentException($"Quest task block at source line {block.FirstLine} has an unsupported header '{header}'.");
        }

        List<DaggerfallQuestTaskOperation> operations = [];
        for (int offset = start; offset < block.Lines.Count; offset++)
        {
            string line = Trim(block.Lines[offset]);
            if (line.Length == 0) continue;
            operations.Add(CompileOperation(line, checked(block.FirstLine + offset)));
        }
        return new(symbol, kind, target, global, operations);
    }

    private static DaggerfallQuestTaskOperation CompileOperation(string line, int sourceLine)
    {
        if (CompileNpcLifecycle(line, sourceLine) is { } npcLifecycle) return npcLifecycle;
        if (CompileActorClick(line, sourceLine) is { } click) return click;
        if (CompileWorldTrigger(line, sourceLine) is { } worldTrigger) return worldTrigger;
        if (ReservePlace.Match(line) is { Success: true } reserve)
            return new(DaggerfallQuestTaskOperationKind.ReservePlace, sourceLine, line, [Canonical(reserve.Groups["place"].Value)], [], null);
        foreach (var pattern in new[] { PlaceActor, PlaceItem })
            if (pattern.Match(line) is { Success: true } placement)
            {
                var kind = pattern == PlaceItem ? DaggerfallQuestTaskOperationKind.PlaceItem
                    : placement.Groups["kind"].Value.Equals("foe", StringComparison.OrdinalIgnoreCase) ? DaggerfallQuestTaskOperationKind.PlaceFoe : DaggerfallQuestTaskOperationKind.PlaceNpc;
                int? marker = placement.Groups["marker"].Success ? Step(placement.Groups["marker"].Value, sourceLine) : null;
                var preference = placement.Groups["any"].Success ? DaggerfallQuestMarkerPreference.Any
                    : placement.Groups["markerKind"].Value.Equals("questmarker", StringComparison.OrdinalIgnoreCase) ? DaggerfallQuestMarkerPreference.QuestSpawn : DaggerfallQuestMarkerPreference.Default;
                return new(kind, sourceLine, line, [Canonical(placement.Groups["symbol"].Value), Canonical(placement.Groups["place"].Value)], [], null,
                    MarkerIndex: marker, MarkerPreference: preference);
            }
        foreach (var (pattern, kind) in new[] { (GetItem, DaggerfallQuestTaskOperationKind.GetItem), (TakeItem, DaggerfallQuestTaskOperationKind.TakeItem), (Permanent, DaggerfallQuestTaskOperationKind.MakePermanent) })
            if (pattern.Match(line) is { Success: true } item)
                return new(kind, sourceLine, line, [Canonical(item.Groups["symbol"].Value)], [], item.Groups["message"].Success ? Message(item.Groups["message"].Value) : null);
        if (CommandFoe.Match(line) is { Success: true } commandFoe)
            return new(commandFoe.Groups["verb"].Value.Equals("kill", StringComparison.OrdinalIgnoreCase) ? DaggerfallQuestTaskOperationKind.KillFoe : DaggerfallQuestTaskOperationKind.RemoveFoe,
                sourceLine, line, [Canonical(commandFoe.Groups["symbol"].Value)], [], null);
        if (InjuredFoe.Match(line) is { Success: true } injuredFoe)
            return new(DaggerfallQuestTaskOperationKind.InjuredFoe, sourceLine, line, [Canonical(injuredFoe.Groups["symbol"].Value)], [], injuredFoe.Groups["message"].Success ? Message(injuredFoe.Groups["message"].Value) : null);
        if (KilledFoe.Match(line) is { Success: true } killedFoe && (!killedFoe.Groups["message"].Success || killedFoe.Groups["count"].Success))
            return new(DaggerfallQuestTaskOperationKind.KilledFoe, sourceLine, line, [Canonical(killedFoe.Groups["symbol"].Value)], [], killedFoe.Groups["message"].Success ? Message(killedFoe.Groups["message"].Value) : null,
                Step: killedFoe.Groups["count"].Success ? Math.Max(1, Step(killedFoe.Groups["count"].Value, sourceLine)) : 1);
        if (Questor.Match(line) is { Success: true } questor)
            return new(questor.Groups["verb"].Value.Equals("add", StringComparison.OrdinalIgnoreCase) ? DaggerfallQuestTaskOperationKind.AddQuestor : DaggerfallQuestTaskOperationKind.DropQuestor,
                sourceLine, line, [Canonical(questor.Groups["symbol"].Value)], [], null);
        if (Face.Match(line) is { Success: true } face && (face.Groups["verb"].Value.Equals("add", StringComparison.OrdinalIgnoreCase) || !face.Groups["message"].Success))
            return new(face.Groups["verb"].Value.Equals("add", StringComparison.OrdinalIgnoreCase) ? DaggerfallQuestTaskOperationKind.AddFace : DaggerfallQuestTaskOperationKind.DropFace,
                sourceLine, line, [Canonical(face.Groups["symbol"].Value), face.Groups["foe"].Success ? "foe" : "person"], [], face.Groups["message"].Success ? Message(face.Groups["message"].Value) : null);
        if (Mute.Match(line) is { Success: true } mute)
            return new(DaggerfallQuestTaskOperationKind.MuteNpc, sourceLine, line, [Canonical(mute.Groups["symbol"].Value)], [], null);
        if (HaveItem.Match(line) is { Success: true } have)
            return new(DaggerfallQuestTaskOperationKind.HaveItem, sourceLine, line, [Canonical(have.Groups["symbol"].Value), Canonical(have.Groups["task"].Value)], [], null);
        // #8133 owns execution. Retain its exact item/message operands now for named resolution.
        if (GiveNotification.Match(line) is { Success: true } give)
            return new(DaggerfallQuestTaskOperationKind.Unsupported, sourceLine, line, [Canonical(give.Groups["symbol"].Value)], [], null,
                MessageAlias: give.Groups["message"].Success ? give.Groups["message"].Value : null);
        if (LevelCompleted.Match(line) is { Success: true } level)
            return new(DaggerfallQuestTaskOperationKind.LevelCompleted, sourceLine, line, [level.Groups["minimum"].Value], [], null);
        if (WhenAttributeLevel.Match(line) is { Success: true } attribute)
            return new(DaggerfallQuestTaskOperationKind.WhenAttributeLevel, sourceLine, line, [attribute.Groups["attribute"].Value, attribute.Groups["minimum"].Value], [], null);
        if (WhenSkillLevel.Match(line) is { Success: true } skill)
            return new(DaggerfallQuestTaskOperationKind.WhenSkillLevel, sourceLine, line, [skill.Groups["skill"].Value, skill.Groups["minimum"].Value], [], null);
        if (TryCompileWhen(line, sourceLine, out DaggerfallQuestTaskOperation? condition)) return condition!;
        if (Start.Match(line) is { Success: true } start)
            return new(DaggerfallQuestTaskOperationKind.Start, sourceLine, line, [Canonical(start.Groups["symbol"].Value)], [], null);
        if (Clear.Match(line) is { Success: true } clear)
            return new(DaggerfallQuestTaskOperationKind.Clear, sourceLine, line, Symbols(clear.Groups["symbols"].Value), [], null);
        if (Unset.Match(line) is { Success: true } unset)
            return new(DaggerfallQuestTaskOperationKind.Unset, sourceLine, line, Symbols(unset.Groups["symbols"].Value), [], null);
        if (Timer.Match(line) is { Success: true } timer)
            return new(timer.Groups["start"].Success ? DaggerfallQuestTaskOperationKind.StartClock : DaggerfallQuestTaskOperationKind.StopClock, sourceLine, line, [Canonical(timer.Groups["start"].Success ? timer.Groups["symbol"].Value : timer.Groups["stop"].Value)], [], null);
        if (Daily.Match(line) is { Success: true } daily)
            return new(DaggerfallQuestTaskOperationKind.DailyFrom, sourceLine, line,
                [DailyMinute(daily.Groups["fromHour"].Value, daily.Groups["fromMinute"].Value, sourceLine).ToString(System.Globalization.CultureInfo.InvariantCulture),
                 DailyMinute(daily.Groups["toHour"].Value, daily.Groups["toMinute"].Value, sourceLine).ToString(System.Globalization.CultureInfo.InvariantCulture)], [], null);
        if (Journal.Match(line) is { Success: true } journal)
            return new(DaggerfallQuestTaskOperationKind.Journal, sourceLine, line, [], [], Message(journal.Groups["message"].Value), Step(journal.Groups["step"].Value, sourceLine));
        if (RemoveJournal.Match(line) is { Success: true } removeJournal)
            return new(DaggerfallQuestTaskOperationKind.RemoveJournal, sourceLine, line, [], [], null, Step(removeJournal.Groups["step"].Value, sourceLine));
        if (JournalNote.Match(line) is { Success: true } note)
            return new(DaggerfallQuestTaskOperationKind.JournalNote, sourceLine, line, [], [], Message(note.Groups["message"].Value));
        if (Say.Match(line) is { Success: true } say)
        {
            string token = say.Groups["message"].Value;
            int? message = int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed) ? Message(token) : null;
            return new(DaggerfallQuestTaskOperationKind.Say, sourceLine, line, [], [], message, MessageAlias: message is null ? token : null);
        }
        if (Rumor.Match(line) is { Success: true } rumor)
            return new(DaggerfallQuestTaskOperationKind.Rumor, sourceLine, line, [], [], Message(rumor.Groups["message"].Value));
        if (PickOneOf.Match(line) is { Success: true } pick)
            return new(DaggerfallQuestTaskOperationKind.PickOneOf, sourceLine, line, Symbols(pick.Groups["targets"].Value), [], null);
        if (RunQuest.Match(line) is { Success: true } run)
            return new(DaggerfallQuestTaskOperationKind.RunQuest, sourceLine, line,
                [run.Groups["quest"].Value, Canonical(run.Groups["success"].Value), Canonical(run.Groups["failure"].Value)], [], null);
        if (line.Equals("cure lycanthropy", StringComparison.OrdinalIgnoreCase))
            return new(DaggerfallQuestTaskOperationKind.CureLycanthropy, sourceLine, line, [], [], null);
        if (StartQuest.Match(line) is { Success: true } startQuest)
        {
            string target = startQuest.Groups["quest"].Success
                ? startQuest.Groups["quest"].Value
                : $"S{int.Parse(startQuest.Groups["first"].Value, System.Globalization.CultureInfo.InvariantCulture):0000000}";
            return new(DaggerfallQuestTaskOperationKind.StartQuest, sourceLine, line, [target], [], null);
        }
        if (TrainPc.Match(line) is { Success: true } train)
            return new(DaggerfallQuestTaskOperationKind.TrainPc, sourceLine, line, [train.Groups["skill"].Value], [], null);
        if (PromptMulti.Match(line) is { Success: true } multi)
        {
            DaggerfallQuestPromptOption[] options = [.. PromptOption.Matches(multi.Groups["options"].Value).Select(match =>
            {
                int id = int.Parse(match.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture);
                string label = id <= 20 ? ButtonLabel(id) : match.Groups["label"].Success ? match.Groups["label"].Value : ButtonLabel(id);
                return new DaggerfallQuestPromptOption(id, label, Canonical(match.Groups["target"].Value));
            })];
            if (options.Select(option => option.Id).Distinct().Count() != options.Length)
                throw new ArgumentException($"Quest prompt at line {sourceLine} repeats a choice id.");
            return new(DaggerfallQuestTaskOperationKind.Prompt, sourceLine, line, options.Select(option => option.Target).ToArray(), [],
                Message(multi.Groups["message"].Value), PromptOptions: options);
        }
        if (Prompt.Match(line) is { Success: true } prompt)
        {
            string token = prompt.Groups["message"].Value;
            int? message = int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed) && parsed > 0 ? parsed : null;
            return new(DaggerfallQuestTaskOperationKind.Prompt, sourceLine, line,
                [Canonical(prompt.Groups["yes"].Value), Canonical(prompt.Groups["no"].Value)], [], message, null, message is null ? token : null);
        }
        if (End.Match(line) is { Success: true } end)
        {
            int? message = end.Groups["message"].Success ? int.Parse(end.Groups["message"].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
            return new(DaggerfallQuestTaskOperationKind.End, sourceLine, line, [], [], message == 0 ? null : message);
        }
        return new(DaggerfallQuestTaskOperationKind.Unsupported, sourceLine, line, [], [], null);
    }

    private static bool TryCompileWhen(string line, int sourceLine, out DaggerfallQuestTaskOperation? operation)
    {
        MatchCollection matches = WhenTerm.Matches(line);
        bool hasUnexpectedText = matches.Count == 0 || matches[0].Index != 0 || matches[^1].Index + matches[^1].Length != line.Length;
        for (int index = 1; !hasUnexpectedText && index < matches.Count; index++)
        {
            int gapStart = matches[index - 1].Index + matches[index - 1].Length;
            hasUnexpectedText = !string.IsNullOrWhiteSpace(line[gapStart..matches[index].Index]);
        }
        if (hasUnexpectedText)
        {
            operation = null;
            return false;
        }
        if (!matches[0].Groups["operator"].Value.StartsWith("when", StringComparison.OrdinalIgnoreCase))
        {
            operation = null;
            return false;
        }
        DaggerfallQuestTaskCondition[] conditions = [.. matches.Select(match => new DaggerfallQuestTaskCondition(ParseOperator(match.Groups["operator"].Value), Canonical(match.Groups["symbol"].Value)))];
        operation = new(DaggerfallQuestTaskOperationKind.When, sourceLine, line, [], conditions, null);
        return true;
    }

    private static DaggerfallQuestTaskConditionOperator ParseOperator(string value) => value.ToLowerInvariant() switch
    {
        "when" => DaggerfallQuestTaskConditionOperator.When,
        "when not" => DaggerfallQuestTaskConditionOperator.WhenNot,
        "and" => DaggerfallQuestTaskConditionOperator.And,
        "and not" => DaggerfallQuestTaskConditionOperator.AndNot,
        "or" => DaggerfallQuestTaskConditionOperator.Or,
        "or not" => DaggerfallQuestTaskConditionOperator.OrNot,
        _ => throw new ArgumentException($"Quest task condition names unsupported operator '{value}'."),
    };

    private static string[] Symbols(string value) => [.. value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Canonical)];
    private static int DailyMinute(string hourText, string minuteText, int sourceLine)
    {
        int hour = int.Parse(hourText, System.Globalization.CultureInfo.InvariantCulture);
        int minute = int.Parse(minuteText, System.Globalization.CultureInfo.InvariantCulture);
        if (hour >= World.DaggerfallCalendar.HoursPerDay || minute >= World.DaggerfallCalendar.MinutesPerHour)
            throw new ArgumentException($"Quest daily window at source line {sourceLine} has an invalid hour or minute.");
        return (hour * World.DaggerfallCalendar.MinutesPerHour) + minute;
    }
    private static int Message(string value) => int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int message) && message > 0
        ? message : throw new ArgumentException($"Quest message at source line has invalid id '{value}'.");
    private static int Step(string value, int sourceLine) => int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int step) && step >= 0
        ? step : throw new ArgumentException($"Quest journal operation at source line {sourceLine} has invalid step '{value}'.");
    private static string Canonical(string value) => DaggerfallQuestInstanceSave.Canonical(value, "quest task");
    private static string Trim(string value) => value.Trim();
    private static Regex Header(string expression) => new(expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

/// <summary>Runs only the retained source-order task transitions over one mutable active quest instance.</summary>
internal interface IDaggerfallQuestTaskLifecycle
{
    void NpcCommand(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation, string task, int index) => throw new NotSupportedException("No quest NPC lifecycle owner is composed.");
    bool NpcAvailable(DaggerfallQuestTaskOperation operation, DaggerfallQuestTaskRuntimeState state, int index) => throw new NotSupportedException("No quest NPC availability owner is composed.");
    DaggerfallQuestClickResult ActorClick(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation) => throw new NotSupportedException("No quest actor interaction owner is composed.");
    void FinishTaskInteractions(DaggerfallQuestRuntimeInstance instance) { }
    bool PlayerAt(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation) => throw new NotSupportedException("No quest world reader is composed.");
    bool WorldTransition(DaggerfallQuestTaskOperation operation, DaggerfallQuestTaskRuntimeState state, int operationIndex) => throw new NotSupportedException("No quest world reader is composed.");
    bool FoeTrigger(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation) => throw new NotSupportedException("No quest foe lifecycle owner is composed.");
    void FoeCommand(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation) => throw new NotSupportedException("No quest foe lifecycle owner is composed.");
    void NpcOverlay(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation) => throw new NotSupportedException("This lifecycle does not own NPC overlays.");
    void RearmMute(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation) => throw new NotSupportedException("This lifecycle does not own mute rearm.");
    void PlaceResource(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation, string task, int operationIndex);
    bool HaveItem(DaggerfallQuestRuntimeInstance instance, string symbol);
    DaggerfallQuestItemResult ItemAction(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation);
    bool IsLevelCompleted(int minimum);
    bool IsAttributeAtLeast(string attribute, int minimum);
    bool IsSkillAtLeast(string skill, int minimum);
    bool CureLycanthropy() => throw new NotSupportedException("No permanent curse owner is composed.");
    void Train(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation);
    void JournalNote(DaggerfallQuestRuntimeInstance instance, int messageId, string task, int operationIndex);
    string Pick(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation, int operationIndex, DaggerfallQuestTaskRuntimeState state);
    string? RunChild(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskDefinition task, DaggerfallQuestTaskOperation operation, int operationIndex, DaggerfallQuestTaskRuntimeState state);
    void Schedule(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation);
}

internal static class DaggerfallQuestTaskRunner
{
    // Donor QuestMachine.QuestMessages.QuestComplete. TrainPc presents this fixed reward message.
    private const int QuestCompleteMessageId = 1004;

    internal static void Advance(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskProgram program, DaggerfallVariableStore variables, World.DaggerfallCalendar calendar, DaggerfallQuestMessages messages, IDaggerfallQuestTaskLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(lifecycle);
        DaggerfallQuestTaskRuntimeState[] states = instance.Tasks;
        IReadOnlyDictionary<string, int> indexes = program.TaskIndexes;

        for (int index = 0; index < program.Tasks.Count; index++)
        {
            DaggerfallQuestTaskDefinition task = program.Tasks[index];
            DaggerfallQuestTaskRuntimeState state = states[index];
            ReadGlobal(task, state, variables);
            try
            {
            bool ranPrimaryAlwaysOn = false;
            for (int operationIndex = 0; operationIndex < task.Operations.Count; operationIndex++)
            {
                DaggerfallQuestTaskOperation operation = task.Operations[operationIndex];
                if (operation.Kind is DaggerfallQuestTaskOperationKind.ClickedNpc or DaggerfallQuestTaskOperationKind.ClickedFoe)
                {
                    if (state.IsSet || state.IsDropped) continue;
                    var click = lifecycle.ActorClick(instance, operation);
                    if (click.Otherwise is { } otherwise) Start(otherwise, states, indexes, program.Tasks, variables, instance.InstanceId, operation);
                    Set(task, state, click.Triggered, variables);
                    if (click.Triggered && click.MessageId is { } clickMessage) messages.Popup(instance, clickMessage);
                    continue;
                }
                if (operation.Kind == DaggerfallQuestTaskOperationKind.WhenNpcAvailable)
                {
                    Set(task, state, lifecycle.NpcAvailable(operation, state, operationIndex), variables);
                    continue;
                }
                if (operation.Kind == DaggerfallQuestTaskOperationKind.DailyFrom)
                {
                    int current = (calendar.Hour * 60) + calendar.Minute;
                    int start = int.Parse(operation.Targets[0], System.Globalization.CultureInfo.InvariantCulture);
                    int end = int.Parse(operation.Targets[1], System.Globalization.CultureInfo.InvariantCulture);
                    Set(task, state, current >= start && current <= end, variables);
                    continue;
                }
                if (operation.Kind == DaggerfallQuestTaskOperationKind.When)
                {
                    bool triggered = CheckCondition(operation.Conditions, states, indexes, program.Tasks, variables);
                    if (!ranPrimaryAlwaysOn)
                    {
                        Set(task, state, triggered, variables);
                        ranPrimaryAlwaysOn = true;
                    }
                    else if (triggered)
                    {
                        Set(task, state, true, variables);
                    }
                    continue;
                }
                if (operation.Kind is DaggerfallQuestTaskOperationKind.LevelCompleted or DaggerfallQuestTaskOperationKind.WhenAttributeLevel or DaggerfallQuestTaskOperationKind.WhenSkillLevel)
                {
                    IDaggerfallQuestTaskLifecycle owner = lifecycle;
                    bool triggered = operation.Kind switch
                    {
                        DaggerfallQuestTaskOperationKind.LevelCompleted => owner.IsLevelCompleted(Requirement(operation, 0)),
                        DaggerfallQuestTaskOperationKind.WhenAttributeLevel => owner.IsAttributeAtLeast(operation.Targets[0], Requirement(operation, 1)),
                        DaggerfallQuestTaskOperationKind.WhenSkillLevel => owner.IsSkillAtLeast(operation.Targets[0], Requirement(operation, 1)),
                        _ => false,
                    };
                    Set(task, state, triggered, variables);
                    continue;
                }

                if (operation.Kind is DaggerfallQuestTaskOperationKind.InjuredFoe or DaggerfallQuestTaskOperationKind.KilledFoe)
                {
                    bool triggered = lifecycle.FoeTrigger(instance, operation);
                    Set(task, state, triggered, variables);
                    if (triggered && !state.OperationCompleted[operationIndex])
                    {
                        if (operation.MessageId is > 0 and var saying) messages.Popup(instance, saying);
                        MarkCompleted(state, operationIndex);
                    }
                    continue;
                }

                if (operation.Kind is DaggerfallQuestTaskOperationKind.WhenPcEnters or DaggerfallQuestTaskOperationKind.WhenPcExits)
                {
                    try
                    {
                        bool enteredOrExited = lifecycle.WorldTransition(operation, state, operationIndex);
                        if (!state.IsSet) Set(task, state, enteredOrExited, variables);
                    }
                    catch (NotSupportedException unsupported) { DiagnoseWorldAction(state, operationIndex, operation, unsupported.Message); }
                    continue;
                }
                if (operation.Kind is DaggerfallQuestTaskOperationKind.PcAt or DaggerfallQuestTaskOperationKind.PcAtAny)
                {
                    if (!state.IsSet) continue;
                    bool atPlace;
                    try { atPlace = lifecycle.PlayerAt(instance, operation); }
                    catch (NotSupportedException unsupported) { DiagnoseWorldAction(state, operationIndex, operation, unsupported.Message); continue; }
                    if (atPlace)
                    {
                        Start(operation.Targets[1], states, indexes, program.Tasks, variables, instance.InstanceId, operation);
                        if (!state.OperationCompleted[operationIndex] && operation.MessageId is > 0 and var locationMessage) messages.Popup(instance, locationMessage);
                        MarkCompleted(state, operationIndex);
                    }
                    else Clear(operation.Targets[1], states, indexes, program.Tasks, variables, instance.InstanceId, operation, mute => lifecycle.RearmMute(instance, mute));
                    continue; // Ongoing source read even after the first saying message.
                }

                if (!state.IsSet || state.OperationCompleted[operationIndex]) continue;
                switch (operation.Kind)
                {
                    case DaggerfallQuestTaskOperationKind.CreateNpc:
                    case DaggerfallQuestTaskOperationKind.HideNpc:
                    case DaggerfallQuestTaskOperationKind.RestoreNpc:
                    case DaggerfallQuestTaskOperationKind.DestroyNpc:
                        try { lifecycle.NpcCommand(instance, operation, task.Symbol, operationIndex); MarkCompleted(state, operationIndex); }
                        catch (NotSupportedException unsupported) { DiagnoseWorldAction(state, operationIndex, operation, unsupported.Message); }
                        break;
                    case DaggerfallQuestTaskOperationKind.KillFoe:
                    case DaggerfallQuestTaskOperationKind.RemoveFoe:
                        lifecycle.FoeCommand(instance, operation);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.AddQuestor:
                    case DaggerfallQuestTaskOperationKind.DropQuestor:
                    case DaggerfallQuestTaskOperationKind.AddFace:
                    case DaggerfallQuestTaskOperationKind.DropFace:
                    case DaggerfallQuestTaskOperationKind.MuteNpc:
                        lifecycle.NpcOverlay(instance, operation);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.ReservePlace:
                    case DaggerfallQuestTaskOperationKind.PlaceFoe:
                    case DaggerfallQuestTaskOperationKind.PlaceItem:
                    case DaggerfallQuestTaskOperationKind.PlaceNpc:
                        lifecycle.PlaceResource(instance, operation, task.Symbol, operationIndex);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.HaveItem:
                        if (lifecycle.HaveItem(instance, operation.Targets[0])) Start(operation.Targets[1], states, indexes, program.Tasks, variables, instance.InstanceId, operation);
                        break; // Donor Have is ongoing, including after its first successful read.
                    case DaggerfallQuestTaskOperationKind.GetItem:
                    case DaggerfallQuestTaskOperationKind.TakeItem:
                    case DaggerfallQuestTaskOperationKind.MakePermanent:
                        // Resolve optional text before mutating inventory; missing references are diagnosed.
                        if (operation.MessageId is int itemMessage && !messages.TryResolveMessage(instance, itemMessage, null, out _, out var diagnostic))
                            throw new ArgumentException($"Quest item action at line {operation.SourceLine}: {diagnostic}");
                        var result = lifecycle.ItemAction(instance, operation);
                        if (result == DaggerfallQuestItemResult.Unavailable) throw new InvalidOperationException($"Quest item '{operation.Targets[0]}' is unavailable at line {operation.SourceLine}.");
                        if (result != DaggerfallQuestItemResult.NotCarried && operation.MessageId is int message) messages.Popup(instance, message);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.Start:
                        foreach (string target in operation.Targets) Start(target, states, indexes, program.Tasks, variables, instance.InstanceId, operation);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.Clear:
                        // The donor marks clear complete first so a self-clear rearms it instead of overwriting the rearm.
                        MarkCompleted(state, operationIndex);
                        foreach (string target in operation.Targets) Clear(target, states, indexes, program.Tasks, variables, instance.InstanceId, operation, mute => lifecycle.RearmMute(instance, mute));
                        break;
                    case DaggerfallQuestTaskOperationKind.Unset:
                        MarkCompleted(state, operationIndex);
                        foreach (string target in operation.Targets) Unset(target, states, indexes, program.Tasks, variables, instance.InstanceId, operation);
                        break;
                    case DaggerfallQuestTaskOperationKind.StartClock:
                    case DaggerfallQuestTaskOperationKind.StopClock:
                        bool changed;
                        try
                        {
                            changed = operation.Kind == DaggerfallQuestTaskOperationKind.StartClock ? instance.StartClock(operation.Targets.Single()) : instance.StopClock(operation.Targets.Single());
                        }
                        catch (NotSupportedException exception)
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = exception.Message;
                            instance.Succeeded = false;
                            instance.PendingEndPasses = 0;
                            instance.TerminalMessageId = null;
                            return;
                        }
                        if (!changed)
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = $"Quest action at line {operation.SourceLine} refers to missing clock '{operation.Targets.Single()}'.";
                            instance.Succeeded = false;
                            instance.PendingEndPasses = 0;
                            instance.TerminalMessageId = null;
                            return;
                        }
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.Journal:
                        messages.Log(instance, operation.MessageId!.Value, operation.Step!.Value);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.RemoveJournal:
                        messages.RemoveLog(instance, operation.Step!.Value);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.JournalNote:
                        lifecycle.JournalNote(instance, operation.MessageId!.Value, task.Symbol, operationIndex);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.Say:
                        if (!messages.TryResolveMessage(instance, operation.MessageId, operation.MessageAlias, out int sayMessage, out string? sayDiagnostic))
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = $"Quest say action at line {operation.SourceLine}: {sayDiagnostic}";
                            instance.Succeeded = false;
                            instance.PendingEndPasses = 0;
                            instance.TerminalMessageId = null;
                            return;
                        }
                        messages.Popup(instance, sayMessage);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.Rumor:
                        messages.Rumor(instance, operation.MessageId!.Value);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.Prompt:
                        if (!messages.TryResolveMessage(instance, operation.MessageId, operation.MessageAlias, out int promptMessage, out string? promptDiagnostic))
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = $"Quest prompt action at line {operation.SourceLine}: {promptDiagnostic}";
                            instance.Succeeded = false;
                            instance.PendingEndPasses = 0;
                            instance.TerminalMessageId = null;
                            return;
                        }
                        messages.Prompt(instance, promptMessage, PromptChoices(operation), task.Symbol, operationIndex);
                        return;
                    case DaggerfallQuestTaskOperationKind.PickOneOf:
                        string picked = lifecycle.Pick(instance, operation, operationIndex, state);
                        Start(picked, states, indexes, program.Tasks, variables, instance.InstanceId, operation);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.StartQuest:
                        lifecycle.Schedule(instance, operation);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.CureLycanthropy:
                        if (!lifecycle.CureLycanthropy())
                            return;
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.TrainPc:
                        lifecycle.Train(instance, operation);
                        messages.TryPopup(instance, QuestCompleteMessageId);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.RunQuest:
                        if (lifecycle.RunChild(instance, task, operation, operationIndex, state) is { } branch)
                        {
                            Start(branch, states, indexes, program.Tasks, variables, instance.InstanceId, operation);
                            MarkCompleted(state, operationIndex);
                        }
                        break;
                    case DaggerfallQuestTaskOperationKind.End:
                        MarkCompleted(state, operationIndex);
                        if (instance.PendingEndPasses == 0) instance.PendingEndPasses = 2;
                        instance.TerminalMessageId = operation.MessageId;
                        if (operation.MessageId is { } messageId) messages.Popup(instance, messageId);
                        return;
                    case DaggerfallQuestTaskOperationKind.Unsupported:
                        instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                        instance.Outcome = $"Unsupported quest action at line {operation.SourceLine}: {operation.Source}";
                        instance.Succeeded = false;
                        instance.PendingEndPasses = 0;
                        instance.TerminalMessageId = null;
                        return;
                    default:
                        throw new InvalidOperationException($"Quest task operation '{operation.Kind}' cannot run.");
                }
            }

            if (task.Kind == DaggerfallQuestTaskKind.PersistUntil)
            {
                if (task.PersistUntilTarget is null || !TryRead(task.PersistUntilTarget, states, indexes, program.Tasks, variables, out bool target))
                {
                    instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                    instance.Outcome = $"Quest task '{task.Symbol}' names missing persisted-until target '{task.PersistUntilTarget}'.";
                    instance.Succeeded = false;
                    instance.PendingEndPasses = 0;
                    instance.TerminalMessageId = null;
                    return;
                }
                if (target) Clear(task, state, variables, mute => lifecycle.RearmMute(instance, mute));
                else Rearm(task, state, mute => lifecycle.RearmMute(instance, mute));
            }
            state.WasSet = state.IsSet;
            }
            finally { lifecycle.FinishTaskInteractions(instance); }
        }
    }

    internal static void TriggerClockDeadline(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskProgram program,
        DaggerfallVariableStore variables, string symbol)
    {
        if (!program.TaskIndexes.TryGetValue(symbol, out int index)) return;
        Set(program.Tasks[index], instance.Tasks[index], true, variables);
    }

    /// <summary>Completes the prompt operation recorded by the UI and starts exactly its selected task.</summary>
    internal static Action PrepareChoice(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskProgram program,
        DaggerfallVariableStore variables, DaggerfallQuestPromptSave prompt, DaggerfallQuestChoiceSave choice,
        Func<DaggerfallQuestTaskOperation, int>? resolvePromptMessage = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(variables);
        (DaggerfallQuestTaskRuntimeState state, DaggerfallQuestTaskOperation operation) = ValidateChoice(instance, program, choice, prompt, resolvePromptMessage);
        int target = Require(choice.Target, program.TaskIndexes, instance.InstanceId, operation);
        if (program.Tasks[target].Kind == DaggerfallQuestTaskKind.Global)
            _ = variables.ReadGlobal(program.Tasks[target].GlobalName!);
        return () =>
        {
            MarkCompleted(state, prompt.OperationIndex);
            Start(choice.Target, instance.Tasks, program.TaskIndexes, program.Tasks, variables, instance.InstanceId, operation);
            if (instance.PendingEndPasses > 0) instance.PendingEndPasses = 2;
        };
    }

    /// <summary>Validates a persisted pending prompt against its compiled operation without consuming it.</summary>
    internal static void ValidatePrompt(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskProgram program, DaggerfallQuestPromptSave prompt,
        Func<DaggerfallQuestTaskOperation, int>? resolvePromptMessage = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(prompt);
        (_, DaggerfallQuestTaskOperation operation) = PromptOperation(instance, program, prompt.TaskSymbol, prompt.OperationIndex);
        ValidatePromptSource(operation, prompt.MessageId, resolvePromptMessage);
        if (!PromptChoices(operation).SequenceEqual(prompt.Options))
            throw new ArgumentException("Quest prompt does not match its source operation.");
    }

    /// <summary>Validates a persisted answer before it is admitted as a choice receipt.</summary>
    internal static void ValidateChoice(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskProgram program, DaggerfallQuestChoiceSave choice,
        Func<DaggerfallQuestTaskOperation, int>? resolvePromptMessage = null)
    {
        ArgumentNullException.ThrowIfNull(choice);
        (_, DaggerfallQuestTaskOperation operation) = PromptOperation(instance, program, choice.TaskSymbol, choice.OperationIndex);
        ValidatePromptSource(operation, choice.MessageId, resolvePromptMessage);
        if (choice.Occurrence < 0 || !PromptChoices(operation).Any(option => option.Id == choice.ChoiceId && option.Target == choice.Target))
            throw new ArgumentException("Quest choice does not match its source operation.");
    }

    private static (DaggerfallQuestTaskRuntimeState State, DaggerfallQuestTaskOperation Operation) ValidateChoice(
        DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskProgram program, DaggerfallQuestChoiceSave choice, DaggerfallQuestPromptSave prompt,
        Func<DaggerfallQuestTaskOperation, int>? resolvePromptMessage)
    {
        if (choice.TaskSymbol != prompt.TaskSymbol || choice.OperationIndex != prompt.OperationIndex
            || choice.Occurrence != prompt.Occurrence || choice.MessageId != prompt.MessageId)
            throw new ArgumentException("Quest prompt receipt does not match its pending operation.");
        ValidatePrompt(instance, program, prompt, resolvePromptMessage);
        (DaggerfallQuestTaskRuntimeState state, DaggerfallQuestTaskOperation operation) = PromptOperation(instance, program, prompt.TaskSymbol, prompt.OperationIndex);
        if (state.OperationCompleted[prompt.OperationIndex]) throw new InvalidOperationException("Quest prompt operation was already completed.");
        if (!prompt.Options.Any(option => option.Id == choice.ChoiceId && option.Target == choice.Target))
            throw new ArgumentException("Quest prompt choice target does not match its pending operation.");
        return (state, operation);
    }

    internal static DaggerfallQuestPromptOption[] PromptChoices(DaggerfallQuestTaskOperation operation) => operation.PromptOptions
        ?? [new(3, "Yes", operation.Targets[0]), new(4, "No", operation.Targets[1])];

    private static void ValidatePromptSource(DaggerfallQuestTaskOperation operation, int messageId,
        Func<DaggerfallQuestTaskOperation, int>? resolvePromptMessage)
    {
        if (messageId <= 0) throw new ArgumentException("Quest prompt has an invalid message id.");
        int? direct = operation.MessageId;
        int? expected = direct ?? (resolvePromptMessage is null ? null : resolvePromptMessage(operation));
        if (expected is not null && expected != messageId)
            throw new ArgumentException("Quest prompt message does not match its source operation.");
    }

    private static (DaggerfallQuestTaskRuntimeState State, DaggerfallQuestTaskOperation Operation) PromptOperation(
        DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskProgram program, string taskSymbol, int operationIndex)
    {
        if (!program.TaskIndexes.TryGetValue(taskSymbol, out int taskIndex))
            throw new ArgumentException($"Quest prompt refers to missing task '{taskSymbol}'.");
        DaggerfallQuestTaskRuntimeState state = instance.Tasks[taskIndex];
        if (operationIndex < 0 || operationIndex >= state.OperationCompleted.Length)
            throw new ArgumentException($"Quest prompt has invalid operation {operationIndex}.");
        DaggerfallQuestTaskOperation operation = program.Tasks[taskIndex].Operations[operationIndex];
        if (operation.Kind != DaggerfallQuestTaskOperationKind.Prompt)
            throw new ArgumentException("Quest prompt does not refer to a prompt operation.");
        return (state, operation);
    }

    private static void Start(string symbol, DaggerfallQuestTaskRuntimeState[] states, IReadOnlyDictionary<string, int> indexes, IReadOnlyList<DaggerfallQuestTaskDefinition> tasks,
        DaggerfallVariableStore variables, string instanceId, DaggerfallQuestTaskOperation operation)
    {
        int index = Require(symbol, indexes, instanceId, operation);
        Set(tasks[index], states[index], true, variables);
    }

    private static void Clear(string symbol, DaggerfallQuestTaskRuntimeState[] states, IReadOnlyDictionary<string, int> indexes, IReadOnlyList<DaggerfallQuestTaskDefinition> tasks,
        DaggerfallVariableStore variables, string instanceId, DaggerfallQuestTaskOperation operation, Action<DaggerfallQuestTaskOperation> rearmMute)
    {
        int index = Require(symbol, indexes, instanceId, operation);
        Clear(tasks[index], states[index], variables, rearmMute);
    }

    private static void Unset(string symbol, DaggerfallQuestTaskRuntimeState[] states, IReadOnlyDictionary<string, int> indexes, IReadOnlyList<DaggerfallQuestTaskDefinition> tasks,
        DaggerfallVariableStore variables, string instanceId, DaggerfallQuestTaskOperation operation)
    {
        int index = Require(symbol, indexes, instanceId, operation);
        DaggerfallQuestTaskRuntimeState state = states[index];
        state.IsSet = false;
        state.IsDropped = true;
        WriteGlobal(tasks[index], false, variables);
    }

    private static int Require(string symbol, IReadOnlyDictionary<string, int> indexes, string instanceId, DaggerfallQuestTaskOperation operation) =>
        indexes.TryGetValue(symbol, out int index) ? index : throw new ArgumentException($"Quest instance '{instanceId}' action at line {operation.SourceLine} refers to missing task '{symbol}'.");

    private static int Requirement(DaggerfallQuestTaskOperation operation, int target) =>
        int.TryParse(operation.Targets[target], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value) && value >= 0
            ? value : throw new ArgumentException($"Quest condition at line {operation.SourceLine} has an invalid minimum value.");

    private static bool CheckCondition(IReadOnlyList<DaggerfallQuestTaskCondition> conditions, DaggerfallQuestTaskRuntimeState[] states,
        IReadOnlyDictionary<string, int> indexes, IReadOnlyList<DaggerfallQuestTaskDefinition> tasks, DaggerfallVariableStore variables)
    {
        bool left = false;
        bool right = false;
        for (int index = 0; index < conditions.Count; index++)
        {
            DaggerfallQuestTaskCondition condition = conditions[index];
            if (!TryRead(condition.Symbol, states, indexes, tasks, variables, out bool set)) return false;
            right = condition.Operator is DaggerfallQuestTaskConditionOperator.WhenNot or DaggerfallQuestTaskConditionOperator.AndNot or DaggerfallQuestTaskConditionOperator.OrNot ? !set : set;
            switch (condition.Operator)
            {
                case DaggerfallQuestTaskConditionOperator.When:
                case DaggerfallQuestTaskConditionOperator.WhenNot:
                    if (conditions.Count == 1 && !right) return false;
                    break;
                case DaggerfallQuestTaskConditionOperator.And:
                case DaggerfallQuestTaskConditionOperator.AndNot:
                    if (!left || !right)
                    {
                        if (index < conditions.Count - 1) right = false;
                        else return false;
                    }
                    else
                    {
                        right = true;
                    }
                    if (right && index < conditions.Count - 1 && conditions[index + 1].Operator is DaggerfallQuestTaskConditionOperator.Or or DaggerfallQuestTaskConditionOperator.OrNot)
                        return true;
                    break;
                case DaggerfallQuestTaskConditionOperator.Or:
                case DaggerfallQuestTaskConditionOperator.OrNot:
                    if (!left && !right)
                    {
                        if (index < conditions.Count - 1) right = false;
                        else return false;
                    }
                    else
                    {
                        right = true;
                    }
                    break;
                default:
                    return false;
            }
            left = right;
        }
        return true;
    }

    private static bool TryRead(string symbol, DaggerfallQuestTaskRuntimeState[] states, IReadOnlyDictionary<string, int> indexes,
        IReadOnlyList<DaggerfallQuestTaskDefinition> tasks, DaggerfallVariableStore variables, out bool value)
    {
        if (!indexes.TryGetValue(symbol, out int index)) { value = false; return false; }
        value = tasks[index].Kind == DaggerfallQuestTaskKind.Global ? variables.ReadGlobal(tasks[index].GlobalName!) : states[index].IsSet;
        return true;
    }

    private static void ReadGlobal(DaggerfallQuestTaskDefinition task, DaggerfallQuestTaskRuntimeState state, DaggerfallVariableStore variables)
    {
        if (task.Kind == DaggerfallQuestTaskKind.Global) state.IsSet = variables.ReadGlobal(task.GlobalName!);
    }

    private static void WriteGlobal(DaggerfallQuestTaskDefinition task, bool value, DaggerfallVariableStore variables)
    {
        if (task.Kind == DaggerfallQuestTaskKind.Global) variables.WriteGlobal(task.GlobalName!, value);
    }

    private static void Set(DaggerfallQuestTaskDefinition task, DaggerfallQuestTaskRuntimeState state, bool value, DaggerfallVariableStore variables)
    {
        if (state.IsDropped)
        {
            state.IsSet = false;
            return;
        }
        state.IsSet = value;
        WriteGlobal(task, value, variables);
    }

    private static void Clear(DaggerfallQuestTaskDefinition task, DaggerfallQuestTaskRuntimeState state, DaggerfallVariableStore variables, Action<DaggerfallQuestTaskOperation> rearmMute)
    {
        state.IsSet = false;
        Rearm(task, state, rearmMute);
        WriteGlobal(task, false, variables);
    }

    private static void Rearm(DaggerfallQuestTaskDefinition task, DaggerfallQuestTaskRuntimeState state, Action<DaggerfallQuestTaskOperation> rearmMute)
    {
        for (int index = 0; index < task.Operations.Count; index++)
        {
            if (PersistsAcrossRearm(task.Operations[index])) continue;
            if (task.Operations[index].Kind == DaggerfallQuestTaskOperationKind.MuteNpc && state.OperationCompleted[index]) rearmMute(task.Operations[index]);
            state.OperationCompleted[index] = false;
            state.OperationState[index] = new(null, null);
        }
    }

    // These donor actions opt out of rearm.  RunQuest also retains its live child id while waiting.
    private static bool PersistsAcrossRearm(DaggerfallQuestTaskOperation operation) => operation.Kind is
        DaggerfallQuestTaskOperationKind.WhenNpcAvailable or DaggerfallQuestTaskOperationKind.PcAt or DaggerfallQuestTaskOperationKind.PcAtAny or DaggerfallQuestTaskOperationKind.WhenPcEnters or DaggerfallQuestTaskOperationKind.WhenPcExits or
        DaggerfallQuestTaskOperationKind.StartQuest or DaggerfallQuestTaskOperationKind.RunQuest or DaggerfallQuestTaskOperationKind.TrainPc
        or DaggerfallQuestTaskOperationKind.Say or DaggerfallQuestTaskOperationKind.JournalNote or DaggerfallQuestTaskOperationKind.AddFace;
    private static void DiagnoseWorldAction(DaggerfallQuestTaskRuntimeState state, int index, DaggerfallQuestTaskOperation operation, string reason)
    {
        string diagnostic = $"Quest world action at line {operation.SourceLine} is unavailable: {reason}";
        if (state.OperationState[index].UnavailableReason != diagnostic) System.Diagnostics.Trace.TraceWarning(diagnostic);
        state.OperationState[index] = state.OperationState[index] with { UnavailableReason = diagnostic };
    }

    private static void MarkCompleted(DaggerfallQuestTaskRuntimeState state, int index) => state.OperationCompleted[index] = true;
}
