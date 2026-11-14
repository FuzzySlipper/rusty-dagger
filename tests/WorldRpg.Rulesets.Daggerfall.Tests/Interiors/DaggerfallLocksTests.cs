using System.Numerics;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallLocks"/> built without the session, over Society's stage (the player, skill uses, the site and a
/// keyed roll), an unarmed player's equipment and one exterior building door; then the session's <c>ForceLock</c> order:
/// the lock, Society's notice, the incident.
/// </summary>
public sealed class DaggerfallLocksTests
{
    [Fact]
    public void A_failed_pick_of_a_buildings_door_is_remembered_used_and_noticed_in_the_lists_order()
    {
        using SocietyStage stage = new(roll: 100);
        DaggerfallLocks locks = new(stage.Actors, stage.SkillUses, Unarmed(stage), stage.Sites, stage.Definitions, stage.Random.Service);
        DaggerfallSiteBuildingId building = stage.Site.Exterior!.Buildings.Keys.First();
        DaggerfallRdbDoorDefinition definition = Door(lockValue: 4) with { LockSurface = DaggerfallLockInteractionSurface.Exterior, ExteriorBuilding = building };
        using EntityDirectory store = new();
        using DaggerfallDoorRuntime doors = new(store, RandomMinimum.Create(), [definition], "exterior");
        DaggerfallWorldProfileKey active = DaggerfallWorldProfileIds.Exterior(stage.Site.Id);
        int usesBefore = stage.SkillUses.ReadSkillProgress(DaggerfallSkills.Lockpicking).Uses;
        List<DaggerfallLockIncident> incidents = [];
        locks.Incident += incidents.Add;

        DaggerfallLockAttempt attempt = locks.Force(doors, definition.Id, DaggerfallActivationMode.Steal, active);
        Assert.Equal(DaggerfallLockInteractionStatus.Failed, attempt.Decision.Status);
        Assert.Equal(new DaggerfallActivationOutcome(false, "The lock resists your pick."), attempt.Outcome);
        Assert.Equal(4, doors.Read(definition.Id).LockValue);
        Assert.NotNull(stage.Sites.FailedBuildingPick(stage.Site.Id, building));
        Assert.True(stage.SkillUses.ReadSkillProgress(DaggerfallSkills.Lockpicking).Uses > usesBefore);
        Assert.Empty(incidents);

        stage.Crimes.ObserveLock(attempt.Decision, attempt.Operation, doors.IdentityOf(definition.Id), stage.Site.Id.Region);
        DaggerfallCrimeAttemptSave failed = Assert.Single(stage.Crime.Attempts);
        Assert.Equal(DaggerfallCrimeAttemptOutcome.Failed, failed.Outcome);
        locks.Publish(attempt);
        DaggerfallLockIncident incident = Assert.Single(incidents);
        Assert.Equal((definition.Id, DaggerfallLockInteractionKind.Lockpick, DaggerfallLockInteractionStatus.Failed),
            (incident.Door, incident.Kind, incident.Status));

        // The same skill against the same building's lock is no new attempt.
        Assert.Equal(DaggerfallLockInteractionStatus.DuplicateAttempt,
            locks.Force(doors, definition.Id, DaggerfallActivationMode.Steal, active).Decision.Status);
    }

    [Fact]
    public void A_bash_takes_the_players_strength_and_says_what_the_door_did()
    {
        using SocietyStage stage = new();
        DaggerfallLocks locks = new(stage.Actors, stage.SkillUses, Unarmed(stage), stage.Sites, stage.Definitions, stage.Random.Service);
        DaggerfallRdbDoorDefinition definition = Door(lockValue: 0);
        using EntityDirectory store = new();
        using DaggerfallDoorRuntime doors = new(store, RandomMinimum.Create(), [definition], "dungeon");

        DaggerfallLockAttempt attempt = locks.Force(doors, definition.Id, DaggerfallActivationMode.Bash,
            DaggerfallWorldProfileIds.Exterior(stage.Site.Id));
        Assert.Equal(DaggerfallLockInteractionKind.Bash, attempt.Decision.Kind);
        Assert.Equal(attempt.Operation == DaggerfallDoorOperationResult.Started, attempt.Outcome.Applied);
        Assert.Equal(attempt.Outcome.Applied ? "The door begins to open." : "The door resists your blow.", attempt.Outcome.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => locks.Force(doors, definition.Id, DaggerfallActivationMode.Talk,
            DaggerfallWorldProfileIds.Exterior(stage.Site.Id)));
    }

    [Fact]
    public void A_locks_difficulty_is_worded_from_the_players_chance_and_magic_holds_its_own_words()
    {
        using SocietyStage stage = new();
        DaggerfallLocks locks = new(stage.Actors, stage.SkillUses, Unarmed(stage), stage.Sites, stage.Definitions, stage.Random.Service);
        using EntityDirectory store = new();
        using DaggerfallDoorRuntime doors = new(store, RandomMinimum.Create(), [Door(lockValue: 4), Door(lockValue: 25, model: 4)], "dungeon");

        string held = locks.DescribeDifficulty(doors.Read(Door(lockValue: 25, model: 4).Id));
        string locked = locks.DescribeDifficulty(doors.Read(Door(lockValue: 4).Id));
        Assert.Equal(string.Concat(stage.Definitions.Text.Require(new(DaggerfallTextKind.Internal, "magicLock")).TextRuns), held);
        Assert.False(string.IsNullOrWhiteSpace(locked));
        Assert.NotEqual(held, locked);
    }

    private static DaggerfallRdbDoorDefinition Door(int lockValue, int model = 3) => new(
        new DaggerfallRdbDoorId("S0000007.RMB", 1, 1, model), new Vector3(2, 0, -4), new Vector3(0, 90, 0),
        new Vector3(-.5F, 0, -.1F), new Vector3(.5F, 2, .1F), DaggerfallDoorKind.Normal, lockValue);

    /// <summary>The stage's player with an empty pack and nothing worn.</summary>
    private static MechanicsEquipmentCoordinator Unarmed(SocietyStage stage)
    {
        InventoryStore store = new();
        EntityId player = stage.Actors.Player.Actor.Entity;
        store.RegisterInventory(new InventoryState(player, [new InventoryCapacityLimit(DaggerfallEncumbrancePolicy.ClassicWeightMetric, ulong.MaxValue)]));
        store.RegisterEquipment(new EquipmentState(player));
        return new(new InventoryComponent(store, player), new EquipmentComponent(store, player), stage.Actors.Entities,
            new Dictionary<InventoryItemId, ItemDefinition>(), new Dictionary<KitEquipmentSlotId, EquipmentSlotDefinition>());
    }
}
