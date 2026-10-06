using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The construction a combat fact needs, in one place: a player and one opposing actor in an actor state,
/// the combat rules over them, and a fact buffer. A fact that cares about one rule — fatigue after an
/// accepted hit, say — starts here and asserts what it is about, so a change to what combat needs lands
/// in this file rather than in every fact that builds its own.
/// </summary>
/// <remarks>
/// The rules are built on the same collaborator graph a session composes: real targeting over the Engine
/// fakes, each actor's own inventory and equipment, and the item-condition service. Collaborators that
/// reach other session owners (skill uses, monster hit consequences, weapon poison) record what combat
/// asked of them, so a fact can assert on the request without composing the owner.
/// </remarks>
internal sealed class DaggerCombatFixture : IDisposable
{
    private readonly SpatialMovementSystem _spatial;

    internal DaggerCombatFixture(string sourceId, double playerHealth = 100d, double playerStamina = 600d)
    {
        Definitions = TestPayload.Definitions;
        Actors = new ActorsState();
        PlayerActorState player = Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), Stats(playerHealth, playerStamina), "health");
        ActorState source = Actors.CreateActor(2, new EntityTypeId(sourceId), Stats(100d, 600d), new ActorPose(new WorldPoint(1f, 0f, 0f), 0f), "health");
        Random = RandomMinimum.Create();
        Dictionary<long, DaggerfallActorDefinition> authored = new()
        {
            [DaggerfallActorIdentity.PlayerEntityId] = Definitions.RequireActor(new DaggerfallActorId("player")),
            [2] = Definitions.RequireActor(new DaggerfallActorId(sourceId)),
        };
        DaggerfallItemInstances itemInstances = new();
        MechanicsEquipmentCoordinator playerEquipment = CombatCollaborators.Equipment(Actors, player.Actor, Definitions, out MechanicsInventoryCoordinator playerInventory);
        MechanicsEquipmentCoordinator sourceEquipment = CombatCollaborators.Equipment(Actors, source.Actor, Definitions, out MechanicsInventoryCoordinator sourceInventory);
        DaggerfallItemConditionService itemCondition = new(Definitions, itemInstances,
            new DaggerfallEquipmentMoves(playerInventory, playerEquipment, Definitions, itemInstances: itemInstances));
        Rules = new DaggerCombatRules(
            Random, Actors, playerEquipment, id => id == 2 ? sourceInventory : playerInventory,
            itemInstances, Definitions, authored,
            CombatCollaborators.Targeting(Actors, authored, out _spatial),
            skillUses: SkillUses.Add, playerBiographyAvoidHit: () => 0,
            actorEquipment: id => id == 2 ? sourceEquipment : playerEquipment, itemCondition: itemCondition,
            rules: new CombatResolution(), adrenalineRush: _ => default, playerPosition: () => null, character: () => null,
            playerSwing: () => DaggerfallSwingDirection.None, coverBlocksShot: (_, _) => false, armorValueModifier: () => 0,
            deliverWeaponPoison: (target, poison) => WeaponPoisons.Add((target, poison)), attackChanceModifier: () => 0,
            transformActor: (_, target, _, _, _) => new DaggerfallWabbajackResult(DaggerfallWabbajackOutcome.InvalidTarget, target),
            magicDefense: _ => DaggerfallMagicDefense.None, physicalAttacksBlocked: _ => false,
            molagBalStrike: (_, _, _, _, _, _, _) => throw new InvalidOperationException("This fixture arms no Mace of Molag Bal."),
            itemStrike: (_, _, _, damage) => damage, monsterHit: MonsterHits.Add,
            actorGameplayActive: _ => true, actorTeam: id => authored.GetValueOrDefault(id)?.Team);
    }

    /// <summary>The skill uses combat recorded, in order.</summary>
    internal List<DaggerfallSkillUse> SkillUses { get; } = [];

    /// <summary>The monster hit consequences combat handed to their owner.</summary>
    internal List<DaggerfallMonsterHitExposure> MonsterHits { get; } = [];

    /// <summary>The weapon poison deliveries combat requested.</summary>
    internal List<(long Target, DaggerfallWeaponPoisonSource Poison)> WeaponPoisons { get; } = [];

    internal DaggerfallDefinitions Definitions { get; }

    internal ActorsState Actors { get; }

    /// <summary>The fixture's random service, which answers the minimum of every keyed window.</summary>
    internal IRandomService Random { get; }

    internal DaggerCombatRules Rules { get; }

    internal FactBuffer<IProductFact> Facts { get; } = new();

    internal double Health => Track(DaggerfallMechanicsIds.Health.Value);

    internal double Stamina => Track(DaggerfallMechanicsIds.Stamina.Value);

    internal List<IProductFact> Deliver()
    {
        List<IProductFact> delivered = [];
        Facts.Deliver(delivered.Add);
        return delivered;
    }

    internal static StatsComponent Stats(double health, double stamina)
    {
        Stat healthMaximum = new(1_000);
        Stat staminaMaximum = new(1_000);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value), healthMaximum);
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value), staminaMaximum);
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value), new Track(healthMaximum, health, 0d));
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value), new Track(staminaMaximum, stamina, 0d));
        return stats;
    }

    public void Dispose()
    {
        _spatial.Dispose();
        Actors.Dispose();
    }

    private double Track(string id) => Actors.Player.Stats.GetTrack(TrackId.Parse(id)).Current;
}
