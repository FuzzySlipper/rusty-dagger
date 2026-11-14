using System.Numerics;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The activation contribution owners built without the session, each over its own inputs: a door, a portal, a pile
/// and a body, with what they reach beyond themselves (the lock, the crossing, the loot windows) recorded.
/// </summary>
public sealed class DaggerfallActivationContributionsTests
{
    private static readonly DaggerfallRdbDoorId DoorId = new("S0000007.RDB", 1, 1, 3);

    [Fact]
    public void A_dungeon_door_is_examined_opened_or_forced_and_its_actions_follow_a_started_motion()
    {
        using EntityDirectory entities = new();
        using DaggerfallDoorRuntime doors = new(entities, RandomMinimum.Create(), [new DaggerfallRdbDoorDefinition(DoorId, new Vector3(2, 0, -4),
            Vector3.Zero, new Vector3(-.5F, 0, -.1F), new Vector3(.5F, 2, .1F), DaggerfallDoorKind.Normal, StartingLockValue: 4)], "dungeon");
        List<DaggerfallRdbDoorId> triggered = [];
        List<DaggerfallActivationMode> forced = [];
        DaggerfallDoorActivationOwner owner = new(doors, triggered.Add,
            (_, mode) => { forced.Add(mode); return new(false, "The lock resists your pick."); },
            _ => null, (_, _) => null, _ => null, _ => null, _ => "A hard lock.", _ => { });
        DaggerfallActivationTarget target = Assert.Single(owner.DoorTargets());
        Assert.Equal(DaggerfallActivationTargetKind.Door, target.Kind);
        Assert.Null(target.Label);

        Assert.Equal("Pick the lock of", owner.PromptAction(new(DaggerfallActivationMode.Steal, target)));
        Assert.Null(owner.PromptAction(new(DaggerfallActivationMode.Grab, target)));
        Assert.Equal(new DaggerfallActivationOutcome(true, "You see a locked door. A hard lock."), owner.ActivateDoor(new(DaggerfallActivationMode.Info, target)));
        Assert.Equal(new DaggerfallActivationOutcome(false, "The door does not answer."), owner.ActivateDoor(new(DaggerfallActivationMode.Talk, target)));
        Assert.Equal(new DaggerfallActivationOutcome(false, "The door is locked."), owner.ActivateDoor(new(DaggerfallActivationMode.Grab, target)));
        Assert.Equal(new DaggerfallActivationOutcome(false, "The lock resists your pick."), owner.ActivateDoor(new(DaggerfallActivationMode.Steal, target)));
        Assert.Equal([DaggerfallActivationMode.Steal], forced);
        Assert.Empty(triggered);

        DaggerfallRdbDoorId openId = DoorId with { ModelIndex = 4 };
        using EntityDirectory openEntities = new();
        using DaggerfallDoorRuntime open = new(openEntities, RandomMinimum.Create(), [new DaggerfallRdbDoorDefinition(openId, new Vector3(2, 0, -4),
            Vector3.Zero, new Vector3(-.5F, 0, -.1F), new Vector3(.5F, 2, .1F), DaggerfallDoorKind.Normal)], "dungeon");
        DaggerfallDoorActivationOwner unlocked = new(open, triggered.Add, (_, _) => throw new InvalidOperationException("Nothing is forced."),
            _ => null, (_, _) => null, _ => null, _ => null, _ => "No lock.", _ => { });
        DaggerfallActivationTarget door = Assert.Single(unlocked.DoorTargets());
        Assert.Equal("Open", unlocked.PromptAction(new(DaggerfallActivationMode.Grab, door)));
        Assert.Equal(new DaggerfallActivationOutcome(true, "The door begins to open."), unlocked.ActivateDoor(new(DaggerfallActivationMode.Grab, door)));
        Assert.Equal([openId], triggered);
    }

    [Fact]
    public void A_portal_passes_through_the_crossing_and_refuses_force_and_talk()
    {
        using EntityDirectory entities = new();
        DaggerfallWorldProfileKey here = new(new DaggerfallSiteId(1, 2), DaggerfallWorldProfileKind.Dungeon, "dungeon");
        DaggerfallSitePortal exit = new("exit", new WorldPoint(4f, 0f, 4f), 1.5f, "outside");
        using DaggerfallSitePortalRuntime portals = new(entities, here, [exit]);
        DaggerfallWorldProfileKey outside = new(new DaggerfallSiteId(1, 2), DaggerfallWorldProfileKind.Exterior, "outside");
        List<(DaggerfallWorldProfileKey, DaggerfallSitePortal)> crossed = [];
        bool passes = true;
        DaggerfallPortalActivationOwner owner = new(portals, logical => logical == "outside" ? outside : throw new InvalidOperationException($"No profile '{logical}'."),
            (destination, portal) => { crossed.Add((destination, portal)); return passes; });
        DaggerfallActivationTarget target = Assert.Single(owner.PortalTargets());
        Assert.Equal("entrance", target.Label);

        Assert.Equal(new DaggerfallActivationOutcome(true, "You see an entrance."), owner.ActivatePortal(new(DaggerfallActivationMode.Info, target)));
        Assert.Equal(new DaggerfallActivationOutcome(false, "That entrance cannot be forced."), owner.ActivatePortal(new(DaggerfallActivationMode.Bash, target)));
        Assert.Equal(new DaggerfallActivationOutcome(false, "The entrance does not answer."), owner.ActivatePortal(new(DaggerfallActivationMode.Talk, target)));
        Assert.Empty(crossed);
        Assert.Equal(new DaggerfallActivationOutcome(true, "You pass through the entrance."), owner.ActivatePortal(new(DaggerfallActivationMode.Grab, target)));
        Assert.Equal([(outside, exit)], crossed);
        passes = false;
        Assert.Equal(new DaggerfallActivationOutcome(false, "The entrance cannot be used."), owner.ActivatePortal(new(DaggerfallActivationMode.Grab, target)));
    }

    [Fact]
    public void A_pile_and_a_body_are_described_refuse_a_bash_and_open_through_their_loot_window()
    {
        DaggerfallWorldProfileKey here = new(new DaggerfallSiteId(1, 2), DaggerfallWorldProfileKind.Dungeon, "dungeon");
        DaggerfallGroundContainer pile = new(here, 7, new EntityId(70), new WorldPoint(1f, 0f, 1f));
        List<long> opened = [];
        DaggerfallGroundActivationOwner ground = new(() => [pile], container => container.Position,
            id => { opened.Add(id); return new(true, "Dropped items."); });
        DaggerfallActivationTarget heap = Assert.Single(ground.ContainerTargets());
        Assert.Equal(Vector3.UnitY, heap.SurfaceNormal);
        Assert.Equal(new DaggerfallActivationOutcome(true, "You see dropped items."), ground.ActivateContainer(new(DaggerfallActivationMode.Info, heap)));
        Assert.Equal(new DaggerfallActivationOutcome(false, "Bash requires a door."), ground.ActivateContainer(new(DaggerfallActivationMode.Bash, heap)));
        Assert.Equal(new DaggerfallActivationOutcome(true, "Dropped items."), ground.ActivateContainer(new(DaggerfallActivationMode.Steal, heap)));
        Assert.Equal([7L], opened);

        using SocietyStage stage = new();
        long body = stage.Civilian();
        DaggerfallCorpseActivationOwner corpses = new(() => [body], id => $"body {id}", stage.Actors,
            actor => actor.Position, id => { opened.Add(id); return new(false, "Nothing to take."); });
        DaggerfallActivationTarget remains = Assert.Single(corpses.CorpseTargets());
        Assert.Equal(DaggerfallActivationTargetKind.Corpse, remains.Kind);
        Assert.Equal(new DaggerfallActivationOutcome(true, $"You see the remains of body {body}."), corpses.ActivateCorpse(new(DaggerfallActivationMode.Info, remains)));
        Assert.Equal(new DaggerfallActivationOutcome(false, "Bash requires a door."), corpses.ActivateCorpse(new(DaggerfallActivationMode.Bash, remains)));
        Assert.Equal(new DaggerfallActivationOutcome(false, "Nothing to take."), corpses.ActivateCorpse(new(DaggerfallActivationMode.Grab, remains)));
        Assert.Equal([7L, body], opened);
    }

    [Fact]
    public void A_body_or_a_person_with_no_known_sprite_is_sighted_where_it_stands()
    {
        using SocietyStage stage = new();
        long person = stage.Civilian();
        DaggerfallActivationSight sight = new(() => CombatCollaborators.Profile.Value, stage.Npcs, stage.Actors, stage.ActorDefinitions);
        ActorState actor = stage.Actors.Get(person);
        Assert.Equal(actor.Position, sight.Corpse(actor));
        Assert.Null(sight.PersonHeight(424242));
    }
}
