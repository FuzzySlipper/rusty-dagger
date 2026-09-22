using System.Text.RegularExpressions;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The source-defined forms whose trigger state belongs to a quest instance.</summary>
internal enum DaggerfallQuestTaskKind { Headless, Standard, Variable, PersistUntil, Global }
internal enum DaggerfallQuestTaskOperationKind { When, DailyFrom, Start, Clear, Unset, StartClock, StopClock, End, Unsupported }
internal enum DaggerfallQuestTaskConditionOperator { When, WhenNot, And, AndNot, Or, OrNot }

/// <summary>One durable trigger state. Operation completion aligns with the compiled source operation order.</summary>
internal sealed record DaggerfallQuestTaskState(string Symbol, DaggerfallQuestTaskKind Kind, bool IsSet, bool WasSet, bool IsDropped, bool[] OperationCompleted);

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
    }

    internal string Symbol { get; }
    internal DaggerfallQuestTaskKind Kind { get; }
    internal bool IsSet { get; set; }
    internal bool WasSet { get; set; }
    internal bool IsDropped { get; set; }
    internal bool[] OperationCompleted { get; set; }
    internal DaggerfallQuestTaskState Capture() => new(Symbol, Kind, IsSet, WasSet, IsDropped, [.. OperationCompleted]);
}

internal sealed record DaggerfallQuestTaskCondition(DaggerfallQuestTaskConditionOperator Operator, string Symbol);
internal sealed record DaggerfallQuestTaskOperation(DaggerfallQuestTaskOperationKind Kind, int SourceLine, string Source,
    string[] Targets, DaggerfallQuestTaskCondition[] Conditions, int? MessageId);
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
    private static readonly Regex StandardHeader = Header("^(?<symbol>[a-zA-Z0-9_.]+)\\s+task:$");
    private static readonly Regex VariableHeader = Header("^variable\\s+(?<symbol>[a-zA-Z0-9_.]+)$");
    private static readonly Regex PersistHeader = Header("^until\\s+(?<symbol>[a-zA-Z0-9_.]+)\\s+performed:$");
    private static readonly Regex GlobalHeader = Header("^(?<global>[a-zA-Z0-9_.]+)\\s+(?<symbol>[a-zA-Z0-9_.]+)$");
    private static readonly Regex Start = Header("^(?:start\\s+task|setvar)\\s+(?<symbol>[a-zA-Z0-9_.]+)$");
    private static readonly Regex Clear = Header("^clear\\s+(?<symbols>[a-zA-Z0-9_.]+(?:\\s+[a-zA-Z0-9_.]+)*)$");
    private static readonly Regex Unset = Header("^unset\\s+(?<symbols>[a-zA-Z0-9_.]+(?:\\s+[a-zA-Z0-9_.]+)*)$");
    private static readonly Regex End = Header("^end\\s+quest(?:\\s+saying\\s+(?<message>\\d+))?$");
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

    internal static DaggerfallQuestTaskState[] InitialState(DaggerfallQuestTaskProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return [.. program.Tasks.Select(task => new DaggerfallQuestTaskState(task.Symbol, task.Kind,
            task.Kind is DaggerfallQuestTaskKind.Headless or DaggerfallQuestTaskKind.PersistUntil, false, false,
            new bool[task.Operations.Count]))];
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
            if (!string.Equals(state.Symbol, task.Symbol, StringComparison.Ordinal) || state.Kind != task.Kind || state.OperationCompleted is null || state.OperationCompleted.Length != task.Operations.Count)
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
    private static string Canonical(string value) => DaggerfallQuestInstanceSave.Canonical(value, "quest task");
    private static string Trim(string value) => value.Trim();
    private static Regex Header(string expression) => new(expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

/// <summary>Runs only the retained source-order task transitions over one mutable active quest instance.</summary>
internal static class DaggerfallQuestTaskRunner
{
    internal static void Advance(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskProgram program, DaggerfallVariableStore variables, World.DaggerfallCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(variables);
        DaggerfallQuestTaskRuntimeState[] states = instance.Tasks;
        IReadOnlyDictionary<string, int> indexes = program.TaskIndexes;

        for (int index = 0; index < program.Tasks.Count; index++)
        {
            DaggerfallQuestTaskDefinition task = program.Tasks[index];
            DaggerfallQuestTaskRuntimeState state = states[index];
            ReadGlobal(task, state, variables);
            bool ranPrimaryAlwaysOn = false;
            for (int operationIndex = 0; operationIndex < task.Operations.Count; operationIndex++)
            {
                DaggerfallQuestTaskOperation operation = task.Operations[operationIndex];
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

                if (!state.IsSet || state.OperationCompleted[operationIndex]) continue;
                switch (operation.Kind)
                {
                    case DaggerfallQuestTaskOperationKind.Start:
                        foreach (string target in operation.Targets) Start(target, states, indexes, program.Tasks, variables, instance.InstanceId, operation);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.Clear:
                        // The donor marks clear complete first so a self-clear rearms it instead of overwriting the rearm.
                        MarkCompleted(state, operationIndex);
                        foreach (string target in operation.Targets) Clear(target, states, indexes, program.Tasks, variables, instance.InstanceId, operation);
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
                            return;
                        }
                        if (!changed)
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = $"Quest action at line {operation.SourceLine} refers to missing clock '{operation.Targets.Single()}'.";
                            return;
                        }
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.End:
                        MarkCompleted(state, operationIndex);
                        instance.Lifecycle = DaggerfallQuestLifecycle.Ended;
                        instance.Outcome = "end quest";
                        instance.TerminalMessageId = operation.MessageId;
                        return;
                    case DaggerfallQuestTaskOperationKind.Unsupported:
                        instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                        instance.Outcome = $"Unsupported quest action at line {operation.SourceLine}: {operation.Source}";
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
                    return;
                }
                if (target) Clear(task, state, variables);
                else Rearm(state);
            }
            state.WasSet = state.IsSet;
        }
    }

    internal static void TriggerClockDeadline(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskProgram program,
        DaggerfallVariableStore variables, string symbol)
    {
        if (!program.TaskIndexes.TryGetValue(symbol, out int index)) return;
        Set(program.Tasks[index], instance.Tasks[index], true, variables);
    }

    private static void Start(string symbol, DaggerfallQuestTaskRuntimeState[] states, IReadOnlyDictionary<string, int> indexes, IReadOnlyList<DaggerfallQuestTaskDefinition> tasks,
        DaggerfallVariableStore variables, string instanceId, DaggerfallQuestTaskOperation operation)
    {
        int index = Require(symbol, indexes, instanceId, operation);
        Set(tasks[index], states[index], true, variables);
    }

    private static void Clear(string symbol, DaggerfallQuestTaskRuntimeState[] states, IReadOnlyDictionary<string, int> indexes, IReadOnlyList<DaggerfallQuestTaskDefinition> tasks,
        DaggerfallVariableStore variables, string instanceId, DaggerfallQuestTaskOperation operation)
    {
        int index = Require(symbol, indexes, instanceId, operation);
        Clear(tasks[index], states[index], variables);
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

    private static void Clear(DaggerfallQuestTaskDefinition task, DaggerfallQuestTaskRuntimeState state, DaggerfallVariableStore variables)
    {
        state.IsSet = false;
        Rearm(state);
        WriteGlobal(task, false, variables);
    }

    private static void Rearm(DaggerfallQuestTaskRuntimeState state) => state.OperationCompleted = new bool[state.OperationCompleted.Length];
    private static void MarkCompleted(DaggerfallQuestTaskRuntimeState state, int index) => state.OperationCompleted[index] = true;
}
