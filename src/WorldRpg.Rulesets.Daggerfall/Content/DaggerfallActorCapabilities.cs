namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>How combat, spells, and law enforcement may harm an actor, independent of gameplay kind.</summary>
internal enum ActorHarmPolicyKind
{
    /// <summary>Static shopkeepers, questors, and unique cast who cannot be targeted or damaged directly.</summary>
    Protected,

    /// <summary>Townsfolk and bystanders who can be struck (triggering assault/murder law) without active combat deflection.</summary>
    LawProtectedCivilian,

    /// <summary>Active fighters (monsters, enemy classes, promoted city watch) with full combat participation and deflection.</summary>
    Combatant,
}

/// <summary>Attached harm capability on every live actor in <c>EntityStore</c>.</summary>
internal sealed class ActorHarmPolicy(ActorHarmPolicyKind policy)
{
    internal ActorHarmPolicyKind Policy { get; private set; } = policy;

    internal bool IsProtected => Policy == ActorHarmPolicyKind.Protected;
    internal bool IsLawProtectedCivilian => Policy == ActorHarmPolicyKind.LawProtectedCivilian;
    internal bool IsCombatant => Policy == ActorHarmPolicyKind.Combatant;
    internal bool CanBeTargetedInCombat => Policy != ActorHarmPolicyKind.Protected;
    internal bool CanBeAffectedBySpells => Policy != ActorHarmPolicyKind.Protected;
    internal bool CanDeflectAttacks => Policy == ActorHarmPolicyKind.Combatant;

    internal void Set(ActorHarmPolicyKind policy) => Policy = policy;
}

/// <summary>How the player may interact with an actor.</summary>
internal enum ActorInteractionPolicyKind
{
    None,
    AmbientBark,
    FullDialogue,
}

/// <summary>Attached interaction capability on every live actor in <c>EntityStore</c>.</summary>
internal sealed class ActorInteractionPolicy(ActorInteractionPolicyKind policy)
{
    internal ActorInteractionPolicyKind Policy { get; private set; } = policy;

    internal bool CanTalk => Policy == ActorInteractionPolicyKind.FullDialogue;
    internal bool CanBark => Policy == ActorInteractionPolicyKind.AmbientBark;

    internal void Set(ActorInteractionPolicyKind policy) => Policy = policy;
}

/// <summary>
/// Content/tuning-shaped baseline profile used to materialize a real <c>StatsComponent</c> and default capability
/// policies on every live actor in <c>EntityStore</c>. Sparsity stays at the <c>ActorLedger</c>/save boundary.
/// </summary>
internal sealed record ArchetypeProfile(
    string Id,
    int BaseHealth,
    int BaseFatigue,
    int BaseMagicka,
    int BaseAttribute,
    ActorHarmPolicyKind DefaultHarm,
    ActorInteractionPolicyKind DefaultInteraction,
    bool Walks = true)
{
    internal static ArchetypeProfile Civilian { get; } = new(
        "civilian", BaseHealth: 1, BaseFatigue: 0, BaseMagicka: 0, BaseAttribute: 50,
        ActorHarmPolicyKind.LawProtectedCivilian, ActorInteractionPolicyKind.FullDialogue, Walks: true);

    internal static ArchetypeProfile StaticNpc { get; } = new(
        "static-npc", BaseHealth: 1, BaseFatigue: 0, BaseMagicka: 0, BaseAttribute: 50,
        ActorHarmPolicyKind.Protected, ActorInteractionPolicyKind.FullDialogue, Walks: false);

    internal static ArchetypeProfile Questor { get; } = new(
        "questor", BaseHealth: 1, BaseFatigue: 0, BaseMagicka: 0, BaseAttribute: 50,
        ActorHarmPolicyKind.Protected, ActorInteractionPolicyKind.FullDialogue, Walks: false);

    internal static ArchetypeProfile Guard { get; } = new(
        "guard", BaseHealth: 40, BaseFatigue: 80, BaseMagicka: 0, BaseAttribute: 55,
        ActorHarmPolicyKind.Combatant, ActorInteractionPolicyKind.FullDialogue, Walks: true);

    internal static ArchetypeProfile Combatant { get; } = new(
        "combatant", BaseHealth: 1, BaseFatigue: 0, BaseMagicka: 0, BaseAttribute: 50,
        ActorHarmPolicyKind.Combatant, ActorInteractionPolicyKind.None, Walks: true);
}

/// <summary>Attached social/quest overlay state on a live actor (HUD escort portrait, mute, archetype id, witness memory).</summary>
internal sealed class ActorOverlayComponent
{
    private readonly List<string> _witnessedCrimes = [];

    internal string? ArchetypeId { get; set; }
    internal bool Met { get; set; }
    internal bool IsMuted { get; set; }
    internal bool IsQuestor { get; set; }
    internal string? EscortPortraitMedia { get; set; }
    internal int EscortOrder { get; set; }
    internal IReadOnlyList<string> WitnessedCrimes => _witnessedCrimes;

    internal void RecordWitnessedCrime(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId)) return;
        if (!_witnessedCrimes.Contains(operationId, StringComparer.Ordinal))
            _witnessedCrimes.Add(operationId);
    }
}
