using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Targeting;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The real collaborators a directly built <see cref="DaggerCombatRules"/> needs and a combat fixture
/// does not otherwise compose: targeting over the Engine fakes and an actor's own inventory and
/// equipment. Fixtures that build the rules without a session take them from here rather than
/// passing null for a dependency the rules may later start to use.
/// </summary>
internal static class CombatCollaborators
{
    private static readonly SharedFixture<DaggerfallSiteProfile> Profile = new(() => TestSessions.ReadInputs(TestData.RepositoryRoot));

    /// <summary>
    /// Targeting over the perception and spatial fakes, with the session's own policy on the committed
    /// start site. The returned spatial system is the caller's to dispose.
    /// </summary>
    internal static TargetingService Targeting(ActorsState actors, IReadOnlyDictionary<long, DaggerfallActorDefinition> authored,
        out SpatialMovementSystem spatial)
    {
        DaggerfallSiteProfile inputs = Profile.Value;
        List<string> releases = [];
        spatial = new SpatialMovementSystem(SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new ContentFake(releases),
            null, DaggerfallTuning.Defaults.Spatial);
        return new TargetingService(PerceptionFake.Create().Service, spatial, actors,
            new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting, () => inputs));
    }

    /// <summary>An actor's inventory and equipment over its own Engine inventory store, as the actor factory attaches them.</summary>
    internal static MechanicsEquipmentCoordinator Equipment(ActorsState actors, Actor actor, DaggerfallDefinitions definitions,
        out MechanicsInventoryCoordinator inventory)
    {
        EntityId owner = actor.Entity;
        InventoryStore world = new();
        world.RegisterInventory(new InventoryState(owner));
        world.RegisterEquipment(new EquipmentState(owner));
        InventoryComponent inventoryComponent = new(world, owner);
        EquipmentComponent equipment = new(world, owner);
        actor.Add(inventoryComponent);
        actor.Add(equipment);
        var items = definitions.Items.Values.Concat(definitions.TemplateItems.Values)
            .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
        var slots = definitions.EquipmentSlots.Values.ToDictionary(slot => new WorldRpg.Kit.Inventory.EquipmentSlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
        inventory = new MechanicsInventoryCoordinator(inventoryComponent, actors.Entities, items);
        return new MechanicsEquipmentCoordinator(inventoryComponent, equipment, actors.Entities, items, slots);
    }

    /// <summary>
    /// Rules over a running session's own actors, inventories, item instances and item-condition service,
    /// for a fact that drives one combat path directly with minimum draws and its own targeting.
    /// </summary>
    internal static DaggerCombatRules SessionRules(DaggerfallSession session, DaggerfallDefinitions definitions,
        IReadOnlyDictionary<long, DaggerfallActorDefinition> authored, TargetingService targeting,
        Func<long, bool>? physicalAttacksBlocked = null, Func<long, bool>? actorGameplayActive = null,
        IRandomService? random = null, Action<DaggerfallSkillUse>? skillUses = null, Func<WorldPoint?>? playerPosition = null) =>
        new(random ?? RandomMinimum.Create(), session.State.Actors, session.State.Equipment, session.State.ActorInventories.InventoryFor,
            session.State.ItemInstances, definitions, authored, targeting,
            skillUses: skillUses ?? (_ => { }), playerBiographyAvoidHit: () => 0, actorEquipment: session.State.ActorInventories.EquipmentFor,
            itemCondition: session.ItemCondition, rules: new CombatResolution(), adrenalineRush: _ => default,
            playerPosition: playerPosition ?? (() => null), character: () => null, playerSwing: () => DaggerfallSwingDirection.None,
            coverBlocksShot: (_, _) => false, armorValueModifier: () => 0,
            deliverWeaponPoison: (_, _) => throw new InvalidOperationException("This fact coats no weapon."),
            attackChanceModifier: () => 0,
            transformActor: (_, target, _, _, _) => new DaggerfallWabbajackResult(DaggerfallWabbajackOutcome.InvalidTarget, target),
            magicDefense: _ => DaggerfallMagicDefense.None, physicalAttacksBlocked: physicalAttacksBlocked ?? (_ => false),
            molagBalStrike: (_, _, _, _, _, _, _) => throw new InvalidOperationException("This fact arms no Mace of Molag Bal."),
            itemStrike: (_, _, _, damage) => damage, monsterHit: _ => { },
            actorGameplayActive: actorGameplayActive ?? (_ => true), actorTeam: id => authored.GetValueOrDefault(id)?.Team);
}
