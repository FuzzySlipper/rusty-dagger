using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
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
        // The default mage uses its career's 25 + 6 starting health maximum, not the
        // former provisional endurance-derived maximum.
        f.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(21);
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
        Assert.Equal((21L, 31L), Resource(sheet, "health"));
        Assert.Equal((5_760L, 5_760L), Resource(sheet, "stamina"));
        Assert.Equal((100L, 100L), Resource(sheet, "magicka"));
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

    [Fact]
    public void Sheet_distinguishes_live_from_permanent_values_and_reads_current_affiliation_standing()
    {
        using Fixture f = new();
        DaggerfallCharacterState character = new(f.Definitions, f.Player.Stats, f.PlayerDefinition);
        long strengthBefore = checked((long)f.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value)).BaseValue);
        DaggerfallStatModifiers.AdjustPermanent(f.Player.Stats, DaggerfallMechanicsIds.Strength, 4);
        _ = DaggerfallStatModifiers.ApplyMod(f.Player.Stats, DaggerfallMechanicsIds.Strength, -7);
        DaggerfallStatModifiers.AdjustPermanent(f.Player.Stats, DaggerfallMechanicsIds.ResistanceFire, 25);
        _ = DaggerfallStatModifiers.ApplyMod(f.Player.Stats, DaggerfallMechanicsIds.ResistanceFire, -10);
        f.Progression.AdvanceTo(750, 2);

        DaggerfallSocialState social = new(f.Definitions.Factions);
        DaggerfallFactionDefinition faction = f.Definitions.Factions.Factions[15];
        _ = social.JoinGuild(faction.Id, currentDay: 3);
        _ = social.PromoteGuild(faction.Id, currentDay: 4);
        _ = social.ChangeFactionReputation(faction.Id, 6);
        DaggerfallSkillUseReactions skills = new(f.Progression, f.Player.Stats, f.Definitions, () => character.Career);
        DaggerfallLevelProgress expectedProgress = skills.ReadLevelProgress();
        DaggerfallCharacterPresentation presentation = new(f.Definitions, character, f.PlayerDefinition, f.Equipment, social, skills);

        CharacterSheetPresentation sheet = presentation.Read(f.Player, f.Progression);

        Assert.Equal((strengthBefore - 3, strengthBefore + 4), Value(sheet.Attributes, "strength"));
        Assert.Equal((15L, 25L), Value(sheet.Resistances, "resistance-fire"));
        Assert.Equal((2, 750), (sheet.Progression.Level, sheet.Progression.Experience));
        Assert.Equal((expectedProgress.CurrentSkillSum, expectedProgress.NextLevelSkillSum, expectedProgress.PendingLevelUp),
            (sheet.Progression.SkillProgress, sheet.Progression.NextLevelSkillProgress, sheet.Progression.PendingLevelUp));
        CharacterAffiliationPresentation affiliation = Assert.Single(sheet.Affiliations);
        Assert.Equal(faction.Name, affiliation.Faction);
        Assert.Equal(1, affiliation.Rank);
        Assert.Equal(social.FactionReputation(faction.Id), affiliation.Reputation);
    }

    [Fact]
    public void Sheet_reuses_identification_and_condition_presentation_for_equipped_magic_items()
    {
        using Fixture f = new();
        foreach (WorldRpg.Kit.Inventory.UniqueInventoryItem equipped in f.Equipment.Read().Assignments.Select(assignment => assignment.Item).Distinct())
            f.Equipment.Unequip(equipped);

        const string magicKey = "magic-item.0010";
        DaggerfallMagicItemDefinition magic = f.Definitions.Magic.MagicItems[magicKey];
        string itemId = DaggerfallMagicItemIds.For("template-113-iron", magicKey);
        WorldRpg.Kit.Inventory.UniqueInventoryItem item = f.Equipment.Materialize(
            new DurableIdentityReference(DurableIdentityKind.Item, 700), new InventoryItemId(itemId));
        f.Equipment.Equip(item, [new SlotId("right-hand")]);

        DaggerfallItemInstances instances = new();
        DaggerfallEquipmentMoves moves = new(f.Inventory, f.Equipment, f.Definitions, itemInstances: instances);
        DaggerfallItemConditionService condition = new(f.Definitions, instances, moves);
        DaggerfallInventoryPresentation inventory = new(moves, f.Definitions, new Dictionary<string, string>());
        inventory.UseItemValuation(new DaggerfallItemValuation(f.Definitions), instances, DaggerfallItemOwner.Player,
            entity => f.Actors.Entities.IdentityOf(new EntityId(entity)).Value);
        inventory.UseItemCondition(condition);
        instances.RegisterUnique(700, new DaggerfallItemInstanceMetadata(itemId, "iron", 0, 2, 2, Identified: false,
            Stolen: false, QuestId: null, QuestItemSymbol: null, magicKey, DaggerfallItemOwner.Player));
        f.Presentation.UseItemPresentation(inventory);

        CharacterEquipmentPresentation unknown = Assert.Single(f.Presentation.Read(f.Player, f.Progression).Equipment);
        Assert.False(unknown.Identified);
        Assert.Equal(f.Definitions.RequireItem(new DaggerfallItemId(itemId)).Template!.Name, unknown.Label);
        Assert.Contains("Unidentified magical item", unknown.Details, StringComparison.Ordinal);

        _ = condition.Identify(item);
        _ = condition.Damage(item, 1);
        CharacterEquipmentPresentation identified = Assert.Single(f.Presentation.Read(f.Player, f.Progression).Equipment);
        Assert.True(identified.Identified);
        Assert.Equal(magic.Name.Replace("%it", f.Definitions.RequireItem(new DaggerfallItemId(itemId)).Template!.Name, StringComparison.Ordinal), identified.Label);
        Assert.Equal((1, 2, 50), (identified.Condition!.Current, identified.Condition.Maximum, identified.Condition.Percentage));
        Assert.Contains("Condition: 1/2 (50%)", identified.Details, StringComparison.Ordinal);
    }

    private static (long Current, long Maximum) Resource(CharacterSheetPresentation sheet, string id)
    {
        CharacterResourcePresentation resource = sheet.Resources.Single(value => value.Id == id);
        return (resource.Current, resource.Maximum);
    }

    private static (long Value, long Permanent) Value(IEnumerable<CharacterStatPresentation> values, string id)
    {
        CharacterStatPresentation value = values.Single(value => value.Id == id);
        return (value.Value, value.Permanent);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly MechanicsEquipmentCoordinator Equipment;
        internal readonly MechanicsInventoryCoordinator Inventory;
        internal readonly ActorsState Actors;
        internal readonly DaggerfallDefinitions Definitions;
        internal readonly DaggerfallActorDefinition PlayerDefinition;
        internal readonly PlayerActorState Player;
        internal readonly ProgressionState Progression = new();
        internal readonly DaggerfallCharacterPresentation Presentation;

        internal Fixture()
        {
            Definitions = TestPayload.Definitions;
            PlayerDefinition = Definitions.RequireActor(new DaggerfallActorId("player"));
            var items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values).ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            var slots = Definitions.EquipmentSlots.Values.ToDictionary(slot => new SlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
            Actors = new ActorsState();
            Player = Actors.CreatePlayer(1, new EntityTypeId(PlayerDefinition.Id.Value),
                new DaggerfallMechanicsState().CreateStats(PlayerDefinition, DaggerfallPlayerVitals.Initial(PlayerDefinition.Stats, Definitions.Catalogs.RequireCareer("class00"))), "health");
            EntityId owner = Player.Actor.Entity;
            InventoryStore world = new();
            world.RegisterInventory(new InventoryState(owner));
            world.RegisterEquipment(new EquipmentState(owner));
            InventoryComponent inventory = new(world, owner);
            EquipmentComponent equipmentComponent = new(world, owner);
            Player.Actor.Add(inventory);
            Player.Actor.Add(equipmentComponent);
            Inventory = new MechanicsInventoryCoordinator(inventory, Actors.Entities, items);
            Equipment = new MechanicsEquipmentCoordinator(inventory, equipmentComponent, Actors.Entities, items, slots);
            foreach (DaggerfallLoadoutEntry entry in PlayerDefinition.Loadout.Where(entry => entry.UniqueEntityId is not null))
            {
                WorldRpg.Kit.Inventory.UniqueInventoryItem item = Equipment.Materialize(
                    new DurableIdentityReference(DurableIdentityKind.Item, entry.UniqueEntityId!.Value),
                    new InventoryItemId(entry.ItemId.Value));
                if (entry.EquipSlot is DaggerfallEquipmentSlotId slot)
                    Equipment.Equip(item, [new SlotId(slot.Value)]);
            }
            Presentation = new DaggerfallCharacterPresentation(Definitions, PlayerDefinition, Equipment);
        }

        public void Dispose()
        {
            Actors.Dispose();
        }
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
