using WorldRpg.Kit.Ai;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Behavior;

/// <summary>
/// Developer overrides of enemy behaviour for one run (<c>ai.freeze</c>, <c>actor.passive</c>, <c>actor.force-state</c>,
/// <c>actor.target</c>, <c>actor.hostile</c>). <see cref="DaggerfallEnemyBehaviorModule"/> reads them at the inputs it
/// already takes: a freeze is a movement restriction, passive and a chosen target are target selection, a forced state
/// replaces the motor policy's maneuver, and forced hostility is what <c>ForcedHostile</c> does to perception and
/// pacification, without touching the actor's saved disposition. Nothing here is saved.
/// </summary>
internal sealed class DaggerfallEnemyOverrides
{
    /// <summary>The states <c>actor.force-state</c> can hold an enemy in.</summary>
    internal static readonly IReadOnlyList<PursuitState> ForcibleStates =
        [PursuitState.Chase, PursuitState.Retreat, PursuitState.Strafe, PursuitState.Attack, PursuitState.Search, PursuitState.Idle];

    private readonly HashSet<long> _passive = [];
    private readonly HashSet<long> _hostile = [];
    private readonly Dictionary<long, PursuitState> _forced = [];
    private readonly Dictionary<long, long> _targets = [];

    /// <summary>Every enemy holds still and starts no attack or spell, as a movement restriction does.</summary>
    internal bool Frozen { get; set; }

    internal bool IsPassive(long actorId) => _passive.Contains(actorId);
    internal bool IsHostile(long actorId) => _hostile.Contains(actorId);
    internal PursuitState? ForcedState(long actorId) => _forced.TryGetValue(actorId, out PursuitState state) ? state : null;
    internal long? Target(long actorId) => _targets.TryGetValue(actorId, out long target) ? target : null;

    internal void SetPassive(long actorId, bool on) => Toggle(_passive, actorId, on);
    internal void SetHostile(long actorId, bool on) => Toggle(_hostile, actorId, on);

    internal void Force(long actorId, PursuitState? state)
    {
        if (state is not { } value) _forced.Remove(actorId);
        else if (ForcibleStates.Contains(value)) _forced[actorId] = value;
        else throw new ArgumentOutOfRangeException(nameof(state), $"An enemy can't be held in {value}.");
    }

    internal void SetTarget(long actorId, long? target)
    {
        if (target is { } value) _targets[actorId] = value;
        else _targets.Remove(actorId);
    }

    /// <summary>The overrides on one actor, by command name, for inspection.</summary>
    internal IEnumerable<string> On(long actorId)
    {
        if (Frozen) yield return "ai.freeze";
        if (IsPassive(actorId)) yield return "passive";
        if (IsHostile(actorId)) yield return "hostile";
        if (ForcedState(actorId) is { } state) yield return $"force-state {state}";
        if (Target(actorId) is { } target) yield return $"target {target}";
    }

    private static void Toggle(HashSet<long> set, long actorId, bool on)
    {
        if (on) set.Add(actorId);
        else set.Remove(actorId);
    }
}
