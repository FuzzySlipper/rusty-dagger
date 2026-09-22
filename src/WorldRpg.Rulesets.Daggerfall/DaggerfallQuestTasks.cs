using System.Text.RegularExpressions;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The source-defined forms whose trigger state belongs to a quest instance.</summary>
internal enum DaggerfallQuestTaskKind { Headless, Standard, Variable, PersistUntil, Global }
internal enum DaggerfallQuestTaskOperationKind { When, DailyFrom, Start, Clear, Unset, StartClock, StopClock, Journal, RemoveJournal, Rumor, Prompt, PickOneOf, RunQuest, StartQuest, End, Unsupported }
internal enum DaggerfallQuestTaskConditionOperator { When, WhenNot, And, AndNot, Or, OrNot }

/// <summary>One durable trigger state. Operation completion aligns with the compiled source operation order.</summary>
internal sealed record DaggerfallQuestTaskOperationState(string? PickedTarget, string? ChildInstanceId);
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
    string[] Targets, DaggerfallQuestTaskCondition[] Conditions, int? MessageId, int? Step = null, string? MessageAlias = null, DaggerfallQuestPromptOption[]? PromptOptions = null);
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
    private static readonly Regex PickOneOf = Header("^pick\\s+one\\s+of\\s+(?<targets>[a-zA-Z0-9_.]+(?:\\s+[a-zA-Z0-9_.]+)+)$");
    private static readonly Regex RunQuest = Header("^run\\s+quest\\s+(?<quest>[a-zA-Z0-9_.]+)\\s+then\\s+(?<success>[a-zA-Z0-9_.]+)\\s+or\\s+(?<failure>[a-zA-Z0-9_.]+)$");
    private static readonly Regex StartQuest = Header("^start\\s+quest\\s+(?:(?<first>\\d+)\\s+(?<second>\\d+)|(?<quest>[a-zA-Z0-9_.]+))$");
    private static readonly Regex Journal = Header("^log\\s+(?<message>\\d+)\\s+step\\s+(?<step>\\d+)$");
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
        if (Rumor.Match(line) is { Success: true } rumor)
            return new(DaggerfallQuestTaskOperationKind.Rumor, sourceLine, line, [], [], Message(rumor.Groups["message"].Value));
        if (PickOneOf.Match(line) is { Success: true } pick)
            return new(DaggerfallQuestTaskOperationKind.PickOneOf, sourceLine, line, Symbols(pick.Groups["targets"].Value), [], null);
        if (RunQuest.Match(line) is { Success: true } run)
            return new(DaggerfallQuestTaskOperationKind.RunQuest, sourceLine, line,
                [run.Groups["quest"].Value, Canonical(run.Groups["success"].Value), Canonical(run.Groups["failure"].Value)], [], null);
        if (StartQuest.Match(line) is { Success: true } startQuest)
        {
            string target = startQuest.Groups["quest"].Success
                ? startQuest.Groups["quest"].Value
                : $"S{int.Parse(startQuest.Groups["first"].Value, System.Globalization.CultureInfo.InvariantCulture):0000000}";
            return new(DaggerfallQuestTaskOperationKind.StartQuest, sourceLine, line, [target], [], null);
        }
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
    string Pick(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation, int operationIndex, DaggerfallQuestTaskRuntimeState state);
    string? RunChild(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskDefinition task, DaggerfallQuestTaskOperation operation, int operationIndex, DaggerfallQuestTaskRuntimeState state);
    void Schedule(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation);
}

internal static class DaggerfallQuestTaskRunner
{
    internal static void Advance(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskProgram program, DaggerfallVariableStore variables, World.DaggerfallCalendar calendar, DaggerfallQuestMessages? messages = null, IDaggerfallQuestTaskLifecycle? lifecycle = null)
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
                            instance.Succeeded = false;
                            return;
                        }
                        if (!changed)
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = $"Quest action at line {operation.SourceLine} refers to missing clock '{operation.Targets.Single()}'.";
                            instance.Succeeded = false;
                            return;
                        }
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.Journal:
                        if (messages is null)
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = $"Quest journal action at line {operation.SourceLine} has no message owner.";
                            instance.Succeeded = false;
                            return;
                        }
                        messages.Log(instance, operation.MessageId!.Value, operation.Step!.Value);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.RemoveJournal:
                        if (messages is null)
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = $"Quest journal action at line {operation.SourceLine} has no message owner.";
                            instance.Succeeded = false;
                            return;
                        }
                        messages.RemoveLog(instance, operation.Step!.Value);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.Rumor:
                        if (messages is null)
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = $"Quest rumor action at line {operation.SourceLine} has no message owner.";
                            instance.Succeeded = false;
                            return;
                        }
                        messages.Rumor(instance, operation.MessageId!.Value);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.Prompt:
                        if (messages is null)
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = $"Quest prompt action at line {operation.SourceLine} has no message owner.";
                            instance.Succeeded = false;
                            return;
                        }
                        if (!messages.TryResolveMessage(instance, operation.MessageId, operation.MessageAlias, out int promptMessage, out string? promptDiagnostic))
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = $"Quest prompt action at line {operation.SourceLine}: {promptDiagnostic}";
                            instance.Succeeded = false;
                            return;
                        }
                        messages.Prompt(instance, promptMessage, PromptChoices(operation), task.Symbol, operationIndex);
                        return;
                    case DaggerfallQuestTaskOperationKind.PickOneOf:
                        if (lifecycle is null)
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = $"Quest pick-one-of action at line {operation.SourceLine} has no session lifecycle owner.";
                            instance.Succeeded = false;
                            return;
                        }
                        string picked = lifecycle.Pick(instance, operation, operationIndex, state);
                        Start(picked, states, indexes, program.Tasks, variables, instance.InstanceId, operation);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.StartQuest:
                        if (lifecycle is null)
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = $"Quest start action at line {operation.SourceLine} has no session lifecycle owner.";
                            instance.Succeeded = false;
                            return;
                        }
                        lifecycle.Schedule(instance, operation);
                        MarkCompleted(state, operationIndex);
                        break;
                    case DaggerfallQuestTaskOperationKind.RunQuest:
                        if (lifecycle is null)
                        {
                            instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                            instance.Outcome = $"Quest child action at line {operation.SourceLine} has no session lifecycle owner.";
                            instance.Succeeded = false;
                            return;
                        }
                        if (lifecycle.RunChild(instance, task, operation, operationIndex, state) is { } branch)
                        {
                            Start(branch, states, indexes, program.Tasks, variables, instance.InstanceId, operation);
                            MarkCompleted(state, operationIndex);
                        }
                        break;
                    case DaggerfallQuestTaskOperationKind.End:
                        MarkCompleted(state, operationIndex);
                        instance.Lifecycle = DaggerfallQuestLifecycle.Ended;
                        instance.Outcome = "end quest";
                        instance.Succeeded ??= false;
                        instance.TerminalMessageId = operation.MessageId;
                        if (operation.MessageId is { } messageId) messages?.Popup(instance, messageId);
                        return;
                    case DaggerfallQuestTaskOperationKind.Unsupported:
                        instance.Lifecycle = DaggerfallQuestLifecycle.Failed;
                        instance.Outcome = $"Unsupported quest action at line {operation.SourceLine}: {operation.Source}";
                        instance.Succeeded = false;
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

    private static void Rearm(DaggerfallQuestTaskRuntimeState state)
    {
        state.OperationCompleted = new bool[state.OperationCompleted.Length];
        state.OperationState = [.. Enumerable.Repeat(new DaggerfallQuestTaskOperationState(null, null), state.OperationState.Length)];
    }
    private static void MarkCompleted(DaggerfallQuestTaskRuntimeState state, int index) => state.OperationCompleted[index] = true;
}
