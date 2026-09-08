using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallCharacterPresentationTests
{
    [Fact]
    public void Sheet_reads_current_modeled_values_resources_progression_and_actual_equipment()
    {
        using Fixture f = new();
        f.Player.Mechanics.SetTrack(TrackId.Parse("health"), new ExactValue(42));
        f.Progression.AdvanceTo(250, 2);

        CharacterSheetPresentation sheet = f.Presentation.Read(f.Player, f.Progression);

        Assert.Equal("Player", sheet.Name);
        Assert.Equal(["strength", "intelligence", "willpower", "agility", "endurance", "personality", "speed", "luck", "reflexes"], sheet.Attributes.Select(value => value.Id));
        Assert.Equal(50, sheet.Attributes.Single(value => value.Id == "strength").Value);
        Assert.Equal(2, sheet.Attributes.Single(value => value.Id == "reflexes").Value);
        Assert.Equal(35, sheet.Skills.Length);
        Assert.Equal("medical", sheet.Skills[0].Id);
        Assert.Equal(0, sheet.Skills.Single(value => value.Id == "etiquette").Value);
        Assert.Equal(30, sheet.Skills.Single(value => value.Id == "backstabbing").Value);
        Assert.Equal(60, sheet.Skills.Single(value => value.Id == "long-blade").Value);
        Assert.Equal((42L, 85L), Resource(sheet, "health"));
        Assert.Equal((90L, 90L), Resource(sheet, "stamina"));
        Assert.Equal((50L, 50L), Resource(sheet, "magicka"));
        Assert.Equal(2, sheet.Progression.Level);
        Assert.Equal(250, sheet.Progression.Experience);
        Assert.Collection(sheet.Equipment,
            item =>
            {
                Assert.Equal("Iron Cuirass", item.Label);
                Assert.Equal(["Chest Armor"], item.Slots);
                Assert.Equal("Armor material: Iron", item.Details);
            },
            item =>
            {
                Assert.Equal("Iron Longsword", item.Label);
                Assert.Equal(["Right Hand"], item.Slots);
                Assert.Equal("Attack 2–16; Iron; Long Blade", item.Details);
            });
    }

    private static (long Current, long Maximum) Resource(CharacterSheetPresentation sheet, string id)
    {
        CharacterResourcePresentation resource = sheet.Resources.Single(value => value.Id == id);
        return (resource.Current, resource.Maximum);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly MechanicsEquipmentCoordinator equipment;
        internal readonly PlayerActorState Player;
        internal readonly ProgressionState Progression = new();
        internal readonly DaggerfallCharacterPresentation Presentation;

        internal Fixture()
        {
            DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));
            DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
            var items = definitions.Items.Values.ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerfallSession.ToManagedItem);
            var slots = definitions.EquipmentSlots.Values.ToDictionary(slot => new SlotId(slot.Id.Value), DaggerfallSession.ToManagedSlot);
            EntityId owner = new(1);
            InventoryWorld world = new();
            world.RegisterInventory(new InventoryState(owner));
            world.RegisterEquipment(new EquipmentState(owner));
            equipment = new MechanicsEquipmentCoordinator(world, owner, items, slots);
            foreach (DaggerfallLoadoutEntry entry in player.Loadout.Where(entry => entry.UniqueEntityId is not null))
            {
                WorldRpg.Kit.Inventory.UniqueInventoryItem item = equipment.Materialize(new UniqueItemMaterialization("test", entry.UniqueEntityId!.Value, new InventoryItemId(entry.ItemId.Value)));
                if (entry.EquipSlot is DaggerfallEquipmentSlotId slot)
                    equipment.Equip(item, [new SlotId(slot.Value)], new EquipmentChange("test", "test"));
            }
            Player = new PlayerActorState(new DaggerfallMechanicsState().CreateActor(player, player.PlayerInitialVitals, 1), "health");
            Presentation = new DaggerfallCharacterPresentation(definitions, player, equipment);
        }

        public void Dispose()
        {
            Player.Dispose();
            equipment.Dispose();
        }
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
