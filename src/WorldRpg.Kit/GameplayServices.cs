using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Targeting;

namespace WorldRpg.Kit;

/// <summary>Explicit named gameplay composition, with state reached through its canonical owners.</summary>
public sealed class GameplayServices<TFact>(ActorsState actors, TargetingService targeting,
    IAttackCapabilities<TFact> attacks, AttackExecution<TFact> execution, CombatResolution rules,
    MechanicsInventoryCoordinator inventory, MechanicsEquipmentCoordinator equipment) where TFact : IWorldRpgFact
{
    public ActorsState Actors { get; } = actors;
    public TargetingService Targeting { get; } = targeting;
    public IAttackCapabilities<TFact> Attacks { get; } = attacks;
    public AttackExecution<TFact> AttackExecution { get; } = execution;
    public CombatResolution Rules { get; } = rules;
    public MechanicsInventoryCoordinator Inventory { get; } = inventory;
    public MechanicsEquipmentCoordinator Equipment { get; } = equipment;
}
