using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Targeting;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
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
        out SpatialMovementSystem spatial) => Targeting(actors, authored, out spatial, out _);

    /// <summary>The same targeting, also handing back the spatial fake that answers its queries.</summary>
    internal static TargetingService Targeting(ActorsState actors, IReadOnlyDictionary<long, DaggerfallActorDefinition> authored,
        out SpatialMovementSystem spatial, out SpatialFake fake)
    {
        DaggerfallSiteProfile inputs = Profile.Value;
        List<string> releases = [];
        fake = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial = new SpatialMovementSystem(fake.Service, new ContentFake(releases),
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

    /// <summary>Where a fixture's actors strike from: the player from its eye facing +X, a site actor from near the top of its body along its heading.</summary>
    internal static DaggerfallStrikePose? StrikePose(ActorsState actors, long actor, WorldPoint? playerPosition) => actor == DaggerfallActorIdentity.PlayerEntityId
        ? new((playerPosition ?? new WorldPoint(0f, 0f, 0f)).ToVector() + new Vector3(0f, 1.65f, 0f), Vector3.UnitX)
        : actors.TryGet(actor, out ActorState site) ? new(site.Position.ToVector() + new Vector3(0f, 1.62f, 0f), ActorHeading.Forward(site.HeadingYawRadians)) : null;

    /// <summary>Actor contact over a fixture's actor bodies, answered by the spatial system's fake.</summary>
    internal static ActorContact Contact(ActorsState actors, SpatialMovementSystem spatial, Func<WorldPoint?> playerPosition) =>
        new(actors, spatial, () => new PlayerControlState(playerPosition() ?? new WorldPoint(0f, 0f, 0f), 0f, 0f), DaggerfallSession.ActorContactBody, _ => true);

    /// <summary>Lands the enemy blows a session's delivery holds for their wind-up, as the admitted steps after the frame would.</summary>
    internal static void LandWindUps(DaggerfallSession session, ulong generation, ulong frameStep)
    {
        DaggerCombatRules combat = (DaggerCombatRules)typeof(DaggerfallSession).GetField("_combat", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
        FactBuffer<IProductFact> facts = (FactBuffer<IProductFact>)typeof(DaggerfallSession).GetField("_facts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
        // Past any tuned wind-up at the eighth-second steps these facts admit their attacks with.
        combat.LandDueImpacts(generation, checked(frameStep + 16), facts);
    }

    /// <summary>
    /// Rules over a running session's own actors, inventories, item instances and item-condition service,
    /// for a fact that drives one combat path directly with the session's default draws, its own
    /// targeting, and the spatial system whose fake answers its contact queries.
    /// </summary>
    internal static DaggerCombatRules SessionRules(DaggerfallSession session, DaggerfallDefinitions definitions,
        IReadOnlyDictionary<long, DaggerfallActorDefinition> authored, TargetingService targeting, SpatialMovementSystem spatial,
        Func<long, bool>? physicalAttacksBlocked = null, Func<long, bool>? actorGameplayActive = null,
        IRandomService? random = null, Action<DaggerfallSkillUse>? skillUses = null, Func<WorldPoint?>? playerPosition = null)
    {
        IRandomService draws = random ?? SessionRandom.Create();
        Func<WorldPoint?> position = playerPosition ?? (() => session.State.PlayerControl.Position);
        DaggerfallComposedSeams seams = DaggerfallSeamSelection.Default.Build(new DaggerfallSeamInputs(definitions, () => null, draws,
            id => session.State.ActorInventories.EquipmentFor(id).Read(),
            id => id == DaggerfallActorIdentity.PlayerEntityId ? session.State.Actors.Player.Stats : session.State.Actors.TryGet(id, out ActorState actor) ? actor.Stats : null,
            () => 0), DaggerfallContactTuning.Default);
        return new(draws, session.State.Actors, session.State.Equipment, session.State.ActorInventories.InventoryFor,
            session.State.ItemInstances, definitions, authored, targeting,
            skillUses: skillUses ?? (_ => { }), actorEquipment: session.State.ActorInventories.EquipmentFor,
            itemCondition: session.ItemCondition, rules: new CombatResolution(),
            playerPosition: position, character: () => null,
            deliverWeaponPoison: (_, _) => throw new InvalidOperationException("This fact coats no weapon."),
            attackChanceModifier: () => 0,
            transformActor: (_, target, _, _, _) => new DaggerfallWabbajackResult(DaggerfallWabbajackOutcome.InvalidTarget, target),
            magicDefense: _ => DaggerfallMagicDefense.None, physicalAttacksBlocked: physicalAttacksBlocked ?? (_ => false),
            molagBalStrike: (_, _, _, _, _, _, _) => throw new InvalidOperationException("This fact arms no Mace of Molag Bal."),
            itemStrike: (_, _, _, damage) => damage, monsterHit: _ => { },
            actorGameplayActive: actorGameplayActive ?? (_ => true), actorTeam: id => authored.GetValueOrDefault(id)?.Team,
            mitigation: seams.PhysicalMitigation, delivery: seams.PhysicalDelivery, attackTuning: DaggerfallAttackTuning.Default,
            contact: Contact(session.State.Actors, spatial, position), strikePose: actor => StrikePose(session.State.Actors, actor, position()));
    }
}
