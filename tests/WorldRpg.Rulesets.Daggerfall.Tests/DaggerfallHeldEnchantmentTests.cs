using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The enchantments a worn item holds: what they contribute while it is equipped, what leaves with it,
/// and the donor conditions that gate the ones which only apply some of the time.
/// </summary>
public sealed class DaggerfallHeldEnchantmentTests
{
    [Fact]
    public void A_worn_items_skill_enchantment_raises_the_skill_the_donor_names()
    {
        // Chrysamere's published enchantment is EnhancesSkill with the classic param for long blade, and
        // the donor's amount is a flat 15 that stacks over items.
        using Fixture fixture = new();
        int baseSkill = fixture.Skill("long-blade");

        fixture.EquipEnchanted("iron-longsword", 5001, "magic-item.0050", equip: true);
        fixture.Refresh();
        Assert.Equal(baseSkill + 15, fixture.Skill("long-blade"));

        fixture.EquipEnchanted("iron-saber", 5002, "magic-item.0050", equip: true);
        fixture.Refresh();
        Assert.Equal(baseSkill + 30, fixture.Skill("long-blade"));

        // Refreshing again changes nothing: the contribution is one source per item, not one per refresh.
        fixture.Refresh();
        Assert.Equal(baseSkill + 30, fixture.Skill("long-blade"));

        fixture.Unequip(5002);
        fixture.Refresh();
        Assert.Equal(baseSkill + 15, fixture.Skill("long-blade"));

        fixture.Unequip(5001);
        fixture.Refresh();
        Assert.Equal(baseSkill, fixture.Skill("long-blade"));
    }

    [Fact]
    public void A_held_contribution_is_a_source_the_save_owner_can_rebind()
    {
        // Held enchantments apply as effect-scoped stat sources, which is the identity kind the stats
        // save captures and rebinds; a private identity kind would make a worn item unsavable.
        using Fixture fixture = new();
        fixture.EquipEnchanted("iron-longsword", 5001, "magic-item.0050", equip: true);
        fixture.Refresh();

        StatSource source = Assert.Single(fixture.Sources("long-blade"));
        Assert.Equal(fixture.PlayerEntity, Assert.IsType<EffectSourceIdentity>(source.Identity).Entity);
        Assert.Equal("daggerfall.held.magic-item.0050.enchantment.4", source.Definition.Value);
    }

    [Fact]
    public void The_donors_held_conditions_answer_for_every_param_they_name()
    {
        using Fixture fixture = new();
        // Seasons: 0 winter, 1 spring, 2 summer, 3 autumn, off by one month apiece.
        fixture.Calendar = fixture.Calendar with { Month = 11 };  // Evening Star, winter
        Assert.True(fixture.ConditionHolds(0));
        Assert.False(fixture.ConditionHolds(1));
        fixture.Calendar = fixture.Calendar with { Month = 1 };   // First Seed, spring
        Assert.True(fixture.ConditionHolds(1));
        Assert.False(fixture.ConditionHolds(0));

        // Lunar phases: 4 full, 5 half, 6 new. The donor aligns Masser's full moon with a three-day
        // offset over a 32-day cycle, so the day answering 4 is whatever that cycle says.
        fixture.Calendar = fixture.Calendar with { Year = 405, Month = 0, Day = 1 };
        int fullMoonDay = Enumerable.Range(1, 30).First(day =>
        {
            fixture.Calendar = fixture.Calendar with { Day = day };
            return fixture.ConditionHolds(4);
        });
        Assert.True(fullMoonDay > 0);
        Assert.False(fixture.ConditionHolds(6) && fixture.ConditionHolds(4));

        // Nearby groups: 7 undead, 8 daedra, 9 humanoid, 10 animals, inside the donor's 18m radius.
        fixture.Calendar = fixture.Calendar with { Day = 1 };
        fixture.Position = new WorldPoint(0f, 0f, 0f);
        fixture.Nearby = [new DaggerfallNearbyCreature(15, new WorldPoint(0f, 0f, 17f))];  // skeletal warrior
        Assert.True(fixture.ConditionHolds(7));
        Assert.False(fixture.ConditionHolds(8));
        fixture.Nearby = [new DaggerfallNearbyCreature(15, new WorldPoint(0f, 0f, 18.5f))];
        Assert.False(fixture.ConditionHolds(7));
        fixture.Nearby = [new DaggerfallNearbyCreature(26, new WorldPoint(0f, 0f, 4f))];   // fire daedra
        Assert.True(fixture.ConditionHolds(8));
        Assert.False(fixture.ConditionHolds(9));
        fixture.Nearby = [new DaggerfallNearbyCreature(7, new WorldPoint(0f, 0f, 4f))];    // orc
        Assert.True(fixture.ConditionHolds(9));
        fixture.Nearby = [new DaggerfallNearbyCreature(0, new WorldPoint(0f, 0f, 4f))];    // rat
        Assert.True(fixture.ConditionHolds(10));
        // The donor's own grouping leaves an ungrouped mobile ungrouped rather than guessing.
        fixture.Nearby = [new DaggerfallNearbyCreature(35, new WorldPoint(0f, 0f, 4f))];   // fire atronach
        Assert.False(fixture.ConditionHolds(9));
        Assert.False(fixture.ConditionHolds(10));
    }

    [Fact]
    public void The_donors_held_allowance_and_talent_tables_are_the_ones_wired()
    {
        Assert.Equal(1.25d, DaggerfallHeldEnchantments.WeightMultiplier(0));
        Assert.Equal(1.5d, DaggerfallHeldEnchantments.WeightMultiplier(1));
        Assert.Equal(1d, DaggerfallHeldEnchantments.WeightMultiplier(7));
        Assert.Equal(DaggerfallHeldEnchantments.DaggerfallHeldTalentKind.AcuteHearing, DaggerfallHeldEnchantments.Talent(0));
        Assert.Equal(DaggerfallHeldEnchantments.DaggerfallHeldTalentKind.Athleticism, DaggerfallHeldEnchantments.Talent(1));
        Assert.Equal(DaggerfallHeldEnchantments.DaggerfallHeldTalentKind.AdrenalineRush, DaggerfallHeldEnchantments.Talent(2));
        Assert.Equal(DaggerfallHeldEnchantments.DaggerfallHeldTalentKind.None, DaggerfallHeldEnchantments.Talent(3));
    }

    [Fact]
    public void A_carry_allowance_enchantment_extends_the_one_encumbrance_maximum()
    {
        // The encumbrance owner answers with the strength formula scaled by whatever the worn items
        // allow, so a weight-allowance enchantment has one consumer rather than a second weight path.
        using Fixture fixture = new();
        long plain = fixture.MaximumCarryUnits();

        fixture.CarryMultiplier = 1.25d;
        Assert.Equal((long)(plain * 1.25d), fixture.MaximumCarryUnits());

        fixture.CarryMultiplier = 1.5d;
        Assert.Equal((long)(plain * 1.5d), fixture.MaximumCarryUnits());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ActorsState _actors = new();
        private readonly DaggerfallItemInstances _instances = new();
        private readonly MechanicsEquipmentCoordinator _equipment;
        private readonly DaggerfallEncumbrancePolicy _encumbrance;
        private readonly DaggerfallHeldEnchantments _held;
        private readonly Dictionary<ulong, WorldPoint> _positions = [];
        private IReadOnlyList<DaggerfallNearbyCreature> _nearby = [];
        private DaggerfallCalendar _calendar = new(405, 0, 1, 12, 0, 0);
        private double _carryMultiplier = 1d;

        internal Fixture()
        {
            DaggerfallActorDefinition playerDefinition = Definitions.RequireActor(new DaggerfallActorId("player"));
            PlayerActorState player = _actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId(playerDefinition.Id.Value),
                new DaggerfallMechanicsState().CreateStats(playerDefinition, DaggerfallPlayerVitals.Initial(playerDefinition.Stats, Definitions.Catalogs.RequireCareer("class00"))), "health");
            PlayerEntity = player.Actor.Entity;
            InventoryStore world = new();
            world.RegisterInventory(new InventoryState(PlayerEntity, [new InventoryCapacityLimit(DaggerActorFactory.ClassicWeightMetric, ulong.MaxValue)]));
            world.RegisterEquipment(new EquipmentState(PlayerEntity));
            InventoryComponent inventory = new(world, PlayerEntity);
            EquipmentComponent equipment = new(world, PlayerEntity);
            player.Actor.Add(inventory);
            player.Actor.Add(equipment);
            Dictionary<InventoryItemId, ItemDefinition> items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values)
                .ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            Dictionary<WorldRpg.Kit.Inventory.EquipmentSlotId, EquipmentSlotDefinition> slots = Definitions.EquipmentSlots.Values
                .ToDictionary(slot => new WorldRpg.Kit.Inventory.EquipmentSlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
            MechanicsInventoryCoordinator coordinator = new(inventory, _actors.Entities, items);
            _equipment = new MechanicsEquipmentCoordinator(inventory, equipment, _actors.Entities, items, slots);
            _held = new DaggerfallHeldEnchantments(_equipment, _instances, Definitions, player.Stats, _actors.Entities, PlayerEntity,
                () => _calendar, () => _positions.TryGetValue(DaggerfallActorIdentity.PlayerEntityId, out WorldPoint position) ? position : new WorldPoint(0f, 0f, 0f),
                () => _nearby);
            _encumbrance = new DaggerfallEncumbrancePolicy(coordinator, player.Stats, () => _carryMultiplier);
        }

        internal EntityId PlayerEntity { get; }

        internal DaggerfallCalendar Calendar { get => _calendar; set => _calendar = value; }

        internal WorldPoint Position { set => _positions[DaggerfallActorIdentity.PlayerEntityId] = value; }

        internal IReadOnlyList<DaggerfallNearbyCreature> Nearby { set => _nearby = value; }

        internal double CarryMultiplier { set => _carryMultiplier = value; }

        internal void EquipEnchanted(string itemId, ulong uniqueId, string enchantment, bool equip = false)
        {
            DaggerfallItemDefinition definition = Definitions.RequireItem(new DaggerfallItemId(itemId));
            int condition = Definitions.AuthoredMaximumCondition(definition);
            _instances.RegisterUnique(uniqueId, DaggerfallItemInstanceMetadata.Default(definition, DaggerfallItemOwner.Player) with
            {
                CurrentCondition = condition,
                MaximumCondition = condition,
                Enchantment = enchantment,
            });
            WorldRpg.Kit.Inventory.UniqueInventoryItem item = _equipment.Materialize(
                new DurableIdentityReference(DurableIdentityKind.Item, uniqueId), new InventoryItemId(itemId));
            if (!equip) return;
            // The slots are the item's own: a two-handed weapon needs both hands, anything else the
            // first free hand, so two enchanted items can be worn at once.
            if (definition.Weapon?.Handedness == "both")
            {
                _equipment.Equip(item, [new WorldRpg.Kit.Inventory.EquipmentSlotId("right-hand"), new WorldRpg.Kit.Inventory.EquipmentSlotId("left-hand")]);
                return;
            }
            HashSet<string> taken = [.. _equipment.Read().Assignments.Select(assignment => assignment.Slot.Value)];
            string hand = taken.Contains("right-hand") ? "left-hand" : "right-hand";
            _equipment.Equip(item, [new WorldRpg.Kit.Inventory.EquipmentSlotId(hand)]);
        }

        internal void Unequip(ulong uniqueId)
        {
            WorldRpg.Kit.Inventory.UniqueInventoryItem item = _equipment.Read().Assignments.Single(assignment =>
                _equipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == uniqueId).Item;
            _equipment.Unequip(item);
        }

        internal void Refresh() => _held.Refresh();

        internal bool ConditionHolds(int param) => _held.ConditionHolds(param);

        internal int Skill(string skill) => _actors.Player.Stats.GetStat(StatId.Parse(skill)).ValueInt;

        internal IReadOnlyList<StatSource> Sources(string skill) => _actors.Player.Stats.GetStat(StatId.Parse(skill)).Sources;

        internal long MaximumCarryUnits() => _encumbrance.Read().MaximumClassicUnits;

        private static DaggerfallDefinitions Definitions { get; } = DaggerfallBaseContent.Read(
            File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));

        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
        }

        public void Dispose() => _actors.Dispose();
    }
}
