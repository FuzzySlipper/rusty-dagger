using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Behavior;

/// <summary>
/// What an enemy's tactic is shown when pursuit asks how it moves this step, assembled by the behaviour module.
/// </summary>
/// <param name="Pursuit">What pursuit saw of the target: whether it is visible, how far, and the reach pursuit knows.</param>
/// <param name="Senses">The senses' judgement of this target where they judge it (the player's stealth, language and
/// illusions); null where the Engine's sight alone decides.</param>
/// <param name="Attack">The attack combat gives the actor at this distance (its reach, and whether it is a bow shot), or none.</param>
/// <param name="Motor">Own and target levels, and whether the target is paralysed, has its back turned or its guard open.</param>
/// <param name="Movement">How the actor moves at all (its medium and maneuver distances), before any maneuver is chosen.</param>
/// <param name="MaySearch">Whether it may go looking where the target was last seen: not while pacified.</param>
/// <param name="LaneClear">Whether a missile from where it stands would reach the target; swept only when read.</param>
/// <param name="Health">Its own health, as a share of its most.</param>
/// <param name="TargetHealth">The target's health, as a share of its most.</param>
/// <param name="Allies">Where the others fighting the same target stood at their last decision.</param>
/// <param name="GameMinute">The game minute this step falls in.</param>
internal readonly record struct DaggerfallEnemyMoveObservation(
    ActorState Actor,
    PursuitTarget Target,
    PursuitObservation Pursuit,
    DaggerfallEnemyPerceptionDecision? Senses,
    (double Reach, bool Ranged)? Attack,
    DaggerfallEnemyMotorContext Motor,
    PursuitPolicy Movement,
    bool MaySearch,
    Lazy<bool> LaneClear,
    ulong Generation,
    ulong Step,
    float DeltaSeconds)
{
    internal double Health { get; init; } = 1d;
    internal double TargetHealth { get; init; } = 1d;
    internal IReadOnlyList<WorldPoint> Allies { get; init; } = [];
    internal long GameMinute { get; init; }
}

/// <summary>
/// What an enemy's tactic is shown when it chooses this step's action, after pursuit has moved it.
/// </summary>
/// <param name="Visibility">The Engine's perception receipt pursuit admitted this step, or none.</param>
/// <param name="Senses">The senses' judgement of this target where they judge it; null where the Engine's sight alone decides.</param>
/// <param name="TargetRateOfApproach">How far the target closed on it since its last decision, in metres.</param>
/// <param name="ActionOpen">Whether the action door is still open for it in this Engine update: it opens once per update,
/// and a spell or strike admitted in an earlier step of the update has closed it.</param>
/// <param name="LaneClear">Whether a missile from where it stands now would reach the target; swept only when read.</param>
internal readonly record struct DaggerfallEnemyActionObservation(
    ActorState Actor,
    PursuitTarget Target,
    PerceptionReadoutResult? Visibility,
    DaggerfallEnemyPerceptionDecision? Senses,
    double TargetRateOfApproach,
    bool ActionOpen,
    Lazy<bool> LaneClear,
    ulong Generation,
    ulong Step,
    float DeltaSeconds);

/// <summary>
/// The action a tactic chose for this step: a spell to release first, and whether it strikes when no spell took the
/// step's action. The strike is made with <paramref name="Attack"/>, one of the attacks the enemy carries
/// (<see cref="Combat.DaggerfallEnemyAttackOption"/>), or when that is null with the attack combat gives it at this
/// distance (a bow in its band while arrows last, else the melee weapon: combat's EnemyAction).
/// </summary>
internal readonly record struct DaggerfallEnemyActionChoice(DaggerfallEnemySpellAttempt? Spell, bool Strike, string? Attack = null);

/// <summary>What became of a tactic's chosen action at the step's action door.</summary>
internal enum DaggerfallEnemyAdmission
{
    /// <summary>Nothing it chose was admitted.</summary>
    None,
    /// <summary>Its spell was released.</summary>
    Cast,
    /// <summary>Its strike began.</summary>
    Struck,
}

/// <summary>
/// One way an enemy decides in a fight: how it moves and which action it takes, step by step, from what the behaviour
/// module shows it. The module keeps everything shared (target selection, perception, pursuit, the action door and
/// its admission through casting and combat, and the developer's overrides); a tactic only chooses. It records its
/// gates as <see cref="DaggerfallDecisionGate"/>s into the list it is handed, which is null unless the actor is traced.
/// What it remembers lives in the actor's own components, transient and never saved.
/// </summary>
internal interface IDaggerfallEnemyTactic
{
    /// <summary>Whether it weighs a mobile record's authored tendencies; a record giving them to one that doesn't is refused.</summary>
    bool WeighsTendencies { get; }

    /// <summary>The movement intent: the maneuver, whether it may attack and at what reach.</summary>
    PursuitPolicy Move(DaggerfallEnemyMoveObservation observation, List<DaggerfallDecisionGate>? gates);

    /// <summary>The action it wants this step.</summary>
    DaggerfallEnemyActionChoice Act(DaggerfallEnemyActionObservation observation, List<DaggerfallDecisionGate>? gates);

    /// <summary>Tells it what became of the action it chose this step.</summary>
    void Admitted(ActorState actor, DaggerfallEnemyActionChoice choice, DaggerfallEnemyAdmission admission);
}

/// <summary>
/// The compiled tactics by the name a mobile's record gives (<see cref="DaggerfallMobileDefinition.Tactic"/>), and the
/// one that decides for each actor.
/// </summary>
/// <remarks>Tactics are compiled into the ruleset; content only names one. A record naming a tactic this ruleset does
/// not compile is refused at composition, since its creature would otherwise fight as another without saying so.</remarks>
internal sealed class DaggerfallEnemyTactics
{
    private readonly IReadOnlyDictionary<string, IDaggerfallEnemyTactic> _compiled;
    private readonly DaggerfallMobileCatalogSet _mobiles;
    private readonly Func<long, DaggerfallActorDefinition?> _definition;

    /// <param name="definition">Every live actor's definition by durable identity, or null.</param>
    internal DaggerfallEnemyTactics(IReadOnlyDictionary<string, IDaggerfallEnemyTactic> compiled, DaggerfallMobileCatalogSet mobiles,
        Func<long, DaggerfallActorDefinition?> definition)
    {
        _compiled = compiled ?? throw new ArgumentNullException(nameof(compiled));
        _mobiles = mobiles ?? throw new ArgumentNullException(nameof(mobiles));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        if (!compiled.ContainsKey(DaggerfallMobileDefinition.LegacyTactic))
            throw new InvalidOperationException($"The ruleset compiles no '{DaggerfallMobileDefinition.LegacyTactic}' tactic, which every mobile without one fights with.");
        foreach (DaggerfallMobileDefinition mobile in mobiles.Mobiles.Values)
        {
            if (!compiled.TryGetValue(mobile.Tactic, out IDaggerfallEnemyTactic? tactic))
                throw new InvalidOperationException($"Mobile {mobile.DonorId} ('{mobile.DonorName}') names tactic '{mobile.Tactic}', which this ruleset does not compile "
                    + $"({string.Join(", ", compiled.Keys.Order(StringComparer.Ordinal))}); it would fight as another without saying so.");
            if (mobile.Tendencies is not null && !tactic.WeighsTendencies)
                throw new InvalidOperationException($"Mobile {mobile.DonorId} ('{mobile.DonorName}') authors tendencies, which its tactic '{mobile.Tactic}' does not weigh; "
                    + "they would be ignored without saying so.");
        }
    }

    /// <summary>The tactic that decides for an actor: its mobile's, or the legacy tactic for an actor no mobile describes.</summary>
    internal IDaggerfallEnemyTactic For(long actorId) =>
        _compiled[_definition(actorId)?.MobileId is int mobileId && _mobiles.Mobiles.TryGetValue(mobileId, out DaggerfallMobileDefinition? mobile)
            ? mobile.Tactic : DaggerfallMobileDefinition.LegacyTactic];
}
