using System.Numerics;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The event delivered to one normalized dungeon action.  <see cref="ActionObject"/>
/// is the internal event used by an action link; the other values are external
/// interactions with the authored object.
/// </summary>
internal enum DaggerfallDungeonActionEvent : byte
{
    ActionObject = 0,
    Direct = 1,
    WalkOn = 2,
    WalkInto = 3,
    Attack = 4,
    Door = 5,
}

/// <summary>Known source trigger flags. The numeric values are the Arena2 values.</summary>
internal enum DaggerfallDungeonTriggerFlag : uint
{
    None = 0x00,
    Collision01 = 0x01,
    Direct = 0x02,
    Collision03 = 0x03,
    Attack = 0x05,
    Direct6 = 0x06,
    MultiTrigger = 0x08,
    Collision09 = 0x09,
    Door = 0x0A,
}

/// <summary>Known action flags from the RDB action resource.</summary>
internal enum DaggerfallDungeonActionFlag : byte
{
    Translation = 0x01,
    Rotation = 0x02,
    PositiveX = 0x03,
    NegativeX = 0x04,
    PositiveZ = 0x05,
    NegativeZ = 0x06,
    PositiveY = 0x07,
    NegativeY = 0x08,
    CastSpell = 0x09,
    ShowText = 0x0B,
    ShowTextWithInput = 0x0C,
    Teleport = 0x0E,
    LockDoor = 0x10,
    UnlockDoor = 0x11,
    OpenDoor = 0x12,
    CloseDoor = 0x14,
    Hurt21 = 0x15,
    Hurt22 = 0x16,
    Hurt23 = 0x17,
    Hurt24 = 0x18,
    Hurt25 = 0x19,
    Poison = 0x1A,
    DrainMagicka = 0x1C,
    Dialogue = 0x1D,
    Activate = 0x1E,
    SetGlobalVar = 0x1F,
    DoorText = 0x63,
}

/// <summary>What happened to one action during one admitted dispatch.</summary>
internal enum DaggerfallDungeonActionOutcome
{
    Applied,
    AppliedWithoutChange,
    RejectedTrigger,
    Cooldown,
    CycleSuppressed,
    MissingTarget,
    NoAction,
    UnsupportedAction,
    InvalidVariable,
    RejectedOperation,
    AwaitingAnswer,
}

/// <summary>
/// One normalized action node.  The raw values remain alongside the resolved link
/// so diagnostics and later action-family tasks never have to reconstruct source
/// parameters from a product policy decision.
/// </summary>
internal sealed record DaggerfallDungeonActionDefinition(
    string Id,
    int SourceOffset,
    uint TriggerFlag,
    byte ActionFlag,
    byte Axis,
    ushort Duration,
    ushort Magnitude,
    int NextObjectOffset,
    string? NextActionId,
    string? DoorId = null,
    bool IsFlat = false,
    double CooldownSeconds = 0d,
    byte SoundIndex = 0,
    Vector3? SourcePosition = null,
    byte RawIndex = 0)
{
    internal DaggerfallDungeonActionDefinition Validate(IReadOnlySet<string> actionIds)
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("A dungeon action requires a stable id.", nameof(Id));
        if (SourceOffset <= 0) throw new ArgumentOutOfRangeException(nameof(SourceOffset));
        if (SourcePosition is Vector3 position && (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)))
            throw new ArgumentOutOfRangeException(nameof(SourcePosition));
        if (NextActionId is not null)
        {
            if (string.IsNullOrWhiteSpace(NextActionId)) throw new ArgumentException("An action link cannot have an empty target id.", nameof(NextActionId));
            if (!actionIds.Contains(NextActionId)) throw new ArgumentException($"Dungeon action '{Id}' links to unknown action '{NextActionId}'.", nameof(NextActionId));
            if (NextObjectOffset <= 0) throw new ArgumentException($"Dungeon action '{Id}' resolves a target while preserving non-link source offset {NextObjectOffset}.", nameof(NextActionId));
        }

        if (!double.IsFinite(CooldownSeconds) || CooldownSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(CooldownSeconds));
        return this;
    }
}

/// <summary>One durable action node state, relative to the graph's admitted timeline.</summary>
internal sealed record DaggerfallDungeonActionNodeSave(
    string Id,
    ulong ActivationCount,
    double RemainingCooldownSeconds)
{
    internal DaggerfallDungeonActionNodeSave Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("A saved dungeon action state requires an id.", nameof(Id));
        if (!double.IsFinite(RemainingCooldownSeconds) || RemainingCooldownSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(RemainingCooldownSeconds));
        return this;
    }
}

/// <summary>Persistent state for one loaded normalized dungeon action graph.</summary>
internal sealed record DaggerfallDungeonActionGraphSnapshot(
    string ProfileId,
    DaggerfallDungeonActionNodeSave[] Nodes)
{
    internal DaggerfallDungeonActionGraphSnapshot Validate()
    {
        if (string.IsNullOrWhiteSpace(ProfileId)) throw new ArgumentException("A dungeon action graph snapshot requires its profile id.", nameof(ProfileId));
        ArgumentNullException.ThrowIfNull(Nodes);
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DaggerfallDungeonActionNodeSave node in Nodes)
        {
            node.Validate();
            if (!ids.Add(node.Id)) throw new ArgumentException($"Dungeon action snapshot repeats '{node.Id}'.", nameof(Nodes));
        }

        return this;
    }
}

/// <summary>One action result and any diagnostic produced while attempting it.</summary>
internal sealed record DaggerfallDungeonActionExecution(
    string ActionId,
    DaggerfallDungeonActionOutcome Outcome,
    bool VariableChanged = false,
    string? Diagnostic = null);

/// <summary>
/// Result of one externally requested action dispatch. Entries are in deterministic
/// donor order: linked targets run before the current node's own operation, matching
/// <c>DaggerfallAction.Play</c>, which calls <c>ActivateNext</c> first.
/// </summary>
internal sealed record DaggerfallDungeonActionDispatch(
    string RootActionId,
    DaggerfallDungeonActionEvent Event,
    IReadOnlyList<DaggerfallDungeonActionExecution> Executions)
{
    internal bool Applied => Executions.Any(entry => entry.Outcome is DaggerfallDungeonActionOutcome.Applied or DaggerfallDungeonActionOutcome.AppliedWithoutChange);
    internal bool HasUnsupportedAction => Executions.Any(entry => entry.Outcome == DaggerfallDungeonActionOutcome.UnsupportedAction);
}

/// <summary>
/// Daggerfall's bounded normalized dungeon action graph. It owns only trigger
/// admission, linked ordering and durable trigger state; action families such as
/// motion, text, damage and magic add their named product handlers in later tasks.
/// </summary>
internal sealed class DaggerfallDungeonActionGraph
{
    private readonly string _profileId;
    private readonly DaggerfallVariableStore _variables;
    private readonly Func<DaggerfallDungeonActionDefinition, DaggerfallDungeonActionExecution?>? _executeFamilyAction;
    private readonly IReadOnlyDictionary<string, DaggerfallDungeonActionDefinition> _definitions;
    private readonly Dictionary<string, NodeState> _states;
    private readonly HashSet<string> _dispatching = new(StringComparer.Ordinal);
    internal DaggerfallDungeonActionGraph(
        string profileId,
        IEnumerable<DaggerfallDungeonActionDefinition> definitions,
        DaggerfallVariableStore variables,
        DaggerfallDungeonActionGraphSnapshot? restored = null,
        Func<DaggerfallDungeonActionDefinition, DaggerfallDungeonActionExecution?>? executeFamilyAction = null)
    {
        if (string.IsNullOrWhiteSpace(profileId)) throw new ArgumentException("A dungeon action graph requires its profile id.", nameof(profileId));
        ArgumentNullException.ThrowIfNull(definitions);
        _variables = variables ?? throw new ArgumentNullException(nameof(variables));
        _executeFamilyAction = executeFamilyAction;
        _profileId = profileId;

        DaggerfallDungeonActionDefinition[] ordered = definitions
            .Select(definition => definition ?? throw new ArgumentException("A dungeon action definition cannot be null.", nameof(definitions)))
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DaggerfallDungeonActionDefinition definition in ordered)
        {
            if (!ids.Add(definition.Id)) throw new ArgumentException($"Dungeon action id '{definition.Id}' is repeated.", nameof(definitions));
        }

        foreach (DaggerfallDungeonActionDefinition definition in ordered)
        {
            definition.Validate(ids);
        }

        _definitions = ordered.ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        _states = ordered.ToDictionary(definition => definition.Id, static _ => new NodeState(), StringComparer.Ordinal);
        if (restored is not null)
        {
            Restore(restored);
        }
    }

    internal string ProfileId => _profileId;

    internal IReadOnlyList<DaggerfallDungeonActionDefinition> Definitions =>
        _definitions.Values.OrderBy(definition => definition.Id, StringComparer.Ordinal).ToArray();

    /// <summary>Advances per-node retrigger cooldowns inside the admitted product update.</summary>
    internal void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0d) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        foreach (NodeState state in _states.Values)
        {
            state.RemainingCooldownSeconds = Math.Max(0d, state.RemainingCooldownSeconds - deltaSeconds);
        }
    }

    /// <summary>Dispatches one externally observed event to a stable action identity.</summary>
    internal DaggerfallDungeonActionDispatch Trigger(string actionId, DaggerfallDungeonActionEvent @event)
    {
        if (!_definitions.ContainsKey(actionId)) throw new KeyNotFoundException($"Dungeon action '{actionId}' is not loaded in graph '{_profileId}'.");
        if (!Enum.IsDefined(@event)) throw new ArgumentOutOfRangeException(nameof(@event));
        List<DaggerfallDungeonActionExecution> executions = [];
        Dispatch(actionId, @event, executions);
        return new(actionId, @event, executions);
    }

    /// <summary>Continues an admitted input-text link after its answer was accepted.</summary>
    internal DaggerfallDungeonActionDispatch ContinueAcceptedAnswer(string actionId)
    {
        if (!_definitions.TryGetValue(actionId, out DaggerfallDungeonActionDefinition? definition)
            || definition.ActionFlag != (byte)DaggerfallDungeonActionFlag.ShowTextWithInput)
            throw new ArgumentException($"'{actionId}' is not an admitted input-text action.", nameof(actionId));
        List<DaggerfallDungeonActionExecution> executions = [];
        if (definition.NextObjectOffset > 0)
        {
            if (definition.NextActionId is null)
                executions.Add(new(actionId, DaggerfallDungeonActionOutcome.MissingTarget,
                    Diagnostic: $"Dungeon answer '{actionId}' preserves next-object offset {definition.NextObjectOffset} without an admitted target."));
            else
            {
                _dispatching.Add(actionId);
                try { Dispatch(definition.NextActionId, DaggerfallDungeonActionEvent.ActionObject, executions); }
                finally { _dispatching.Remove(actionId); }
            }
        }
        return new(actionId, DaggerfallDungeonActionEvent.ActionObject, executions);
    }

    /// <summary>
    /// Dispatches the action attached to one normalized model placement. The normalized map keeps
    /// the source placement id as <c>model/&lt;block&gt;/&lt;index&gt;</c>, while action records use
    /// <c>action/&lt;block&gt;/model-&lt;index&gt;</c>; this is the one stable identity conversion used by
    /// Engine hit callers. Flat actions are admitted as Engine-owned Resource trigger entities by
    /// <see cref="DaggerfallDungeonActionTriggerRuntime"/> and do not use source-point guessing here.
    /// </summary>
    internal DaggerfallDungeonActionDispatch? TriggerForPlacement(string placementId, DaggerfallDungeonActionEvent @event)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placementId);
        string? actionId = PlacementActionId(placementId);
        return actionId is not null && _definitions.ContainsKey(actionId)
            ? Trigger(actionId, @event)
            : null;
    }

    /// <summary>Dispatches all actions authored for one normalized action-door identity in source order.</summary>
    internal IReadOnlyList<DaggerfallDungeonActionDispatch> TriggerForDoor(DaggerfallRdbDoorId door, DaggerfallDungeonActionEvent @event)
    {
        DaggerfallDoorIdentity.Validate(door);
        string doorId = DoorSourceId(door);
        return _definitions.Values
            .Where(definition => StringComparer.Ordinal.Equals(definition.DoorId, doorId))
            .OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .Select(definition => Trigger(definition.Id, @event))
            .ToArray();
    }

    /// <summary>Converts a runtime door identity to the normalized source id carried by action records.</summary>
    internal static string DoorSourceId(DaggerfallRdbDoorId door)
    {
        DaggerfallDoorIdentity.Validate(door);
        string source = door.SourceKey[..^".RDB".Length].ToLowerInvariant();
        return $"door/{source}-rdb/{door.BlockX}/{door.BlockZ}/{door.ModelIndex}";
    }

    internal DaggerfallDungeonActionGraphSnapshot Capture()
    {
        DaggerfallDungeonActionNodeSave[] nodes = _definitions.Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id =>
            {
                NodeState state = _states[id];
                return new DaggerfallDungeonActionNodeSave(id, state.ActivationCount, state.RemainingCooldownSeconds);
            })
            .ToArray();
        return new(_profileId, nodes);
    }

    internal IReadOnlyDictionary<string, (ulong ActivationCount, double RemainingCooldownSeconds)> State =>
        _states.ToDictionary(
            pair => pair.Key,
            pair => (pair.Value.ActivationCount, pair.Value.RemainingCooldownSeconds),
            StringComparer.Ordinal);

    private void Dispatch(string actionId, DaggerfallDungeonActionEvent @event, ICollection<DaggerfallDungeonActionExecution> executions)
    {
        DaggerfallDungeonActionDefinition definition = _definitions[actionId];
        NodeState state = _states[actionId];
        if (_dispatching.Contains(actionId))
        {
            executions.Add(new(actionId, DaggerfallDungeonActionOutcome.CycleSuppressed,
                Diagnostic: $"Dungeon action link cycle re-entered '{actionId}' in graph '{_profileId}'."));
            return;
        }

        if (state.RemainingCooldownSeconds > 0d)
        {
            executions.Add(new(actionId, DaggerfallDungeonActionOutcome.Cooldown,
                Diagnostic: $"Dungeon action '{actionId}' remains on cooldown for {state.RemainingCooldownSeconds:0.###} seconds."));
            return;
        }

        if (!Matches(definition, @event, out string? triggerDiagnostic))
        {
            executions.Add(new(actionId, DaggerfallDungeonActionOutcome.RejectedTrigger, Diagnostic: triggerDiagnostic));
            return;
        }

        _dispatching.Add(actionId);
        try
        {
            state.ActivationCount = checked(state.ActivationCount + 1);
            state.RemainingCooldownSeconds = definition.CooldownSeconds;

            if (definition.ActionFlag == (byte)DaggerfallDungeonActionFlag.ShowTextWithInput)
            {
                // The donor's answer dialog suspends the linked action. Its continuation is
                // dispatched once by ContinueAcceptedAnswer, never by another activation.
                executions.Add(Apply(definition, missingTarget: false));
                return;
            }

            // The donor activates the next object before running this node's delegate.
            // A missing normalized target remains an explicit failure, never a silent end.
            bool missingTarget = false;
            if (definition.NextObjectOffset > 0)
            {
                if (definition.NextActionId is null)
                {
                    missingTarget = true;
                    executions.Add(new(actionId, DaggerfallDungeonActionOutcome.MissingTarget,
                        Diagnostic: $"Dungeon action '{actionId}' preserves next-object offset {definition.NextObjectOffset} but has no admitted target."));
                }
                else
                {
                    Dispatch(definition.NextActionId, DaggerfallDungeonActionEvent.ActionObject, executions);
                }
            }

            executions.Add(Apply(definition, missingTarget));
        }
        finally
        {
            _dispatching.Remove(actionId);
        }
    }

    private DaggerfallDungeonActionExecution Apply(DaggerfallDungeonActionDefinition definition, bool missingTarget)
    {
        if (definition.ActionFlag == 0)
        {
            return new(definition.Id, DaggerfallDungeonActionOutcome.NoAction,
                Diagnostic: $"Dungeon action '{definition.Id}' declares source action flag 0 (None); no operation was admitted.");
        }

        if (definition.ActionFlag == (byte)DaggerfallDungeonActionFlag.Activate)
        {
            if (missingTarget || definition.NextObjectOffset < 0)
            {
                return new(definition.Id, DaggerfallDungeonActionOutcome.MissingTarget,
                    Diagnostic: $"Dungeon action '{definition.Id}' is Activate but has no resolved next action target.");
            }

            // Arena2 uses an absolute object offset of zero as the null link. The donor's
            // Activate delegate then has no work after Play's empty ActivateNext call.
            if (definition.NextObjectOffset == 0)
                return new(definition.Id, DaggerfallDungeonActionOutcome.AppliedWithoutChange,
                    Diagnostic: $"Dungeon action '{definition.Id}' has the source null-link offset 0.");

            return new(definition.Id, DaggerfallDungeonActionOutcome.Applied);
        }

        if (definition.ActionFlag == (byte)DaggerfallDungeonActionFlag.SetGlobalVar)
        {
            try
            {
                bool changed = _variables.Write(new DaggerfallVariableAddress(DaggerfallVariableScope.Global, 0, definition.Axis), true);
                return new(definition.Id,
                    changed ? DaggerfallDungeonActionOutcome.Applied : DaggerfallDungeonActionOutcome.AppliedWithoutChange,
                    VariableChanged: changed);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return new(definition.Id, DaggerfallDungeonActionOutcome.InvalidVariable,
                    Diagnostic: $"Dungeon action '{definition.Id}' cannot set global variable {definition.Axis}: {exception.Message}");
            }
        }

        if (_executeFamilyAction?.Invoke(definition) is { } familyResult)
            return familyResult;

        if (!Enum.IsDefined((DaggerfallDungeonActionFlag)definition.ActionFlag))
        {
            return new(definition.Id, DaggerfallDungeonActionOutcome.UnsupportedAction,
                Diagnostic: $"Dungeon action '{definition.Id}' has unknown source action flag {definition.ActionFlag}; parameters were retained but no operation was claimed.");
        }

        return new(definition.Id, DaggerfallDungeonActionOutcome.UnsupportedAction,
            Diagnostic: $"Dungeon action '{definition.Id}' uses supported source flag {definition.ActionFlag}, but its action-family owner has not admitted execution yet.");
    }

    private void Restore(DaggerfallDungeonActionGraphSnapshot snapshot)
    {
        snapshot.Validate();
        if (!string.Equals(snapshot.ProfileId, _profileId, StringComparison.Ordinal))
            throw new ArgumentException($"Dungeon action snapshot belongs to profile '{snapshot.ProfileId}', not '{_profileId}'.", nameof(snapshot));

        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (DaggerfallDungeonActionNodeSave saved in snapshot.Nodes)
        {
            if (!_states.TryGetValue(saved.Id, out NodeState? state))
                throw new ArgumentException($"Dungeon action snapshot names unknown action '{saved.Id}'.", nameof(snapshot));
            if (!seen.Add(saved.Id))
                throw new ArgumentException($"Dungeon action snapshot repeats '{saved.Id}'.", nameof(snapshot));
            state.ActivationCount = saved.ActivationCount;
            state.RemainingCooldownSeconds = saved.RemainingCooldownSeconds;
        }

        if (seen.Count != _states.Count)
        {
            string missing = _states.Keys.Where(id => !seen.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).First();
            throw new ArgumentException($"Dungeon action snapshot omits loaded action '{missing}'.", nameof(snapshot));
        }
    }

    private static bool Matches(DaggerfallDungeonActionDefinition definition, DaggerfallDungeonActionEvent @event, out string? diagnostic)
    {
        uint triggerFlag = definition.TriggerFlag;
        diagnostic = null;
        if (@event == DaggerfallDungeonActionEvent.ActionObject)
        {
            // DaggerfallAction.Receive bypasses the authored trigger check for a
            // linked action object, including nodes marked None.
            return true;
        }

        // Action-door records pack the starting-lock selector into the upper
        // bits of the same source word. The low trigger nibble is still the
        // donor trigger value; retain the raw word in content while matching
        // that source convention here.
        // The upper lock selector is a source convention of action-door records. An arbitrary
        // non-door trigger with a high raw value remains unknown and must not become a valid low
        // nibble merely because its bits happen to resemble a known trigger.
        uint effectiveTriggerFlag = definition.DoorId is not null && triggerFlag > 0x0A
            ? triggerFlag & 0x0F
            : triggerFlag;
        bool matches = effectiveTriggerFlag switch
        {
            (uint)DaggerfallDungeonTriggerFlag.None => false,
            (uint)DaggerfallDungeonTriggerFlag.Collision01 => @event == DaggerfallDungeonActionEvent.WalkOn,
            (uint)DaggerfallDungeonTriggerFlag.Direct or (uint)DaggerfallDungeonTriggerFlag.Direct6 => @event == DaggerfallDungeonActionEvent.Direct,
            (uint)DaggerfallDungeonTriggerFlag.Collision03 => @event == DaggerfallDungeonActionEvent.WalkInto,
            (uint)DaggerfallDungeonTriggerFlag.Attack => @event == DaggerfallDungeonActionEvent.Attack,
            (uint)DaggerfallDungeonTriggerFlag.MultiTrigger => @event is DaggerfallDungeonActionEvent.Direct or DaggerfallDungeonActionEvent.Attack or DaggerfallDungeonActionEvent.WalkInto,
            (uint)DaggerfallDungeonTriggerFlag.Collision09 => @event is DaggerfallDungeonActionEvent.Direct or DaggerfallDungeonActionEvent.WalkInto,
            (uint)DaggerfallDungeonTriggerFlag.Door => @event == DaggerfallDungeonActionEvent.Door,
            _ => false,
        };
        if (!matches)
        {
            diagnostic = Enum.IsDefined((DaggerfallDungeonTriggerFlag)effectiveTriggerFlag) && effectiveTriggerFlag == triggerFlag
                ? $"Dungeon action trigger flag {triggerFlag} does not accept event '{@event}'."
                : $"Dungeon action has unknown source trigger flag {triggerFlag}; event '{@event}' was rejected.";
        }

        return matches;
    }

    private sealed class NodeState
    {
        internal ulong ActivationCount { get; set; }
        internal double RemainingCooldownSeconds { get; set; }
    }

    private static string? PlacementActionId(string placementId)
    {
        if (!placementId.StartsWith("model/", StringComparison.Ordinal)) return null;
        int separator = placementId.LastIndexOf('/');
        if (separator <= "model/".Length || separator == placementId.Length - 1) return null;
        return $"action/{placementId["model/".Length..separator]}/model-{placementId[(separator + 1)..]}";
    }
}
