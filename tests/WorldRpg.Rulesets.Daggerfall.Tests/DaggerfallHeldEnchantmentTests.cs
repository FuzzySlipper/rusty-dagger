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
    public void A_worn_regeneration_heals_on_every_fourth_round_and_catches_up()
    {
        // The donor's RegensHealth restores one point every fourth magic round, one tick per worn
        // source, and a catch-up interval restores for every fourth round it covered.
        using Fixture fixture = new();
        int maximum = fixture.HealthMaximum();
        fixture.SetHealth(maximum - 10);
        fixture.EquipEnchanted("iron-cuirass", 8001, "magic-item.0051", equip: true);  // Lord's Mail, all the time
        fixture.Refresh();

        fixture.AdvanceRounds(firstMinuteIndex: 0, minutes: 3);
        Assert.Equal(maximum - 10, fixture.Health());

        fixture.AdvanceRounds(firstMinuteIndex: 3, minutes: 1);
        Assert.Equal(maximum - 9, fixture.Health());

        // Eight more rounds cover two fourth-round beats rather than one.
        fixture.AdvanceRounds(firstMinuteIndex: 4, minutes: 8);
        Assert.Equal(maximum - 7, fixture.Health());

        // The donor clamps at the maximum rather than overhealing.
        fixture.SetHealth(maximum - 1);
        fixture.AdvanceRounds(firstMinuteIndex: 12, minutes: 4);
        Assert.Equal(maximum, fixture.Health());
        fixture.AdvanceRounds(firstMinuteIndex: 16, minutes: 4);
        Assert.Equal(maximum, fixture.Health());

        fixture.Unequip(8001);
        fixture.Refresh();
        fixture.SetHealth(maximum - 4);
        fixture.AdvanceRounds(firstMinuteIndex: 20, minutes: 8);
        Assert.Equal(maximum - 4, fixture.Health());
    }

    [Fact]
    public void A_worn_regeneration_heals_only_under_the_condition_it_names()
    {
        using Fixture fixture = new();
        int maximum = fixture.HealthMaximum();
        fixture.SetHealth(maximum - 10);
        fixture.EquipEnchanted("iron-cuirass", 8002, "test.regen-sunlight", equip: true);
        fixture.EquipEnchanted("tower-shield", 8003, "test.regen-darkness", equip: true);
        fixture.Refresh();

        // Daylight outdoors: only the sunlight source ticks, so one point per fourth round.
        fixture.Sunlight = true;
        fixture.AdvanceRounds(firstMinuteIndex: 0, minutes: 4);
        Assert.Equal(maximum - 9, fixture.Health());

        // Darkness: only the darkness source ticks.
        fixture.Sunlight = false;
        fixture.AdvanceRounds(firstMinuteIndex: 4, minutes: 4);
        Assert.Equal(maximum - 8, fixture.Health());

        // Two sources whose condition holds at the same time tick twice in a round.
        fixture.EquipEnchanted("iron-longsword", 8004, "test.regen-sunlight", equip: true);
        fixture.Refresh();
        fixture.Sunlight = true;
        fixture.AdvanceRounds(firstMinuteIndex: 8, minutes: 4);
        Assert.Equal(maximum - 6, fixture.Health());
    }

    [Fact]
    public void The_donors_held_conditions_answer_for_every_param_they_name()
    {
        using Fixture fixture = new();
        // Seasons 0-3 each answer for the donor's own month table, where winter wraps the year end.
        foreach ((int param, int winter, int spring, int summer, int autumn) season in new[] { (0, 0, 2, 5, 8), (1, 1, 3, 6, 9), (2, 11, 4, 7, 10) })
        {
            fixture.Calendar = fixture.Calendar with { Month = season.spring };
            Assert.True(fixture.ConditionHolds(1), $"param 1 must hold in spring month {season.spring}");
            Assert.False(fixture.ConditionHolds(0));
            fixture.Calendar = fixture.Calendar with { Month = season.summer };
            Assert.True(fixture.ConditionHolds(2), $"param 2 must hold in summer month {season.summer}");
            Assert.False(fixture.ConditionHolds(1));
            fixture.Calendar = fixture.Calendar with { Month = season.autumn };
            Assert.True(fixture.ConditionHolds(3), $"param 3 must hold in autumn month {season.autumn}");
            Assert.False(fixture.ConditionHolds(2));
            fixture.Calendar = fixture.Calendar with { Month = season.winter };
            Assert.True(fixture.ConditionHolds(0), $"param 0 must hold in winter month {season.winter}");
            Assert.False(fixture.ConditionHolds(3));
        }

        // Lunar phases: 4 full, 5 half, 6 new on the donor's 32-day cycle. Masser's three-day offset
        // puts its full moon of year 405 month 0 on the 20th and Secunda's one-day-back offset on the
        // 24th, so those days and their neighbours pin both the cycle and the two offsets.
        fixture.Calendar = fixture.Calendar with { Year = 405, Month = 0, Day = 1 };
        Assert.False(fixture.ConditionHolds(4));
        fixture.Calendar = fixture.Calendar with { Day = 19 };
        Assert.False(fixture.ConditionHolds(4));
        fixture.Calendar = fixture.Calendar with { Day = 20 };
        Assert.True(fixture.ConditionHolds(4), "Masser's offset puts its full moon on day 20");
        fixture.Calendar = fixture.Calendar with { Day = 21 };
        Assert.False(fixture.ConditionHolds(4));
        fixture.Calendar = fixture.Calendar with { Day = 24 };
        Assert.True(fixture.ConditionHolds(4), "Secunda's offset puts its own full moon on day 24");
        fixture.Calendar = fixture.Calendar with { Day = 4 };
        Assert.True(fixture.ConditionHolds(6), "day 4 is the new moon on Masser's cycle");
        fixture.Calendar = fixture.Calendar with { Day = 13 };
        Assert.True(fixture.ConditionHolds(5), "day 13 is a half moon on Masser's cycle");

        // Nearby groups: 7 undead, 8 daedra, 9 humanoid, 10 animals, strictly inside the donor's 18m.
        fixture.Calendar = fixture.Calendar with { Day = 1 };
        fixture.Position = new WorldPoint(0f, 0f, 0f);
        fixture.Nearby = [new DaggerfallNearbyCreature(DaggerfallEnemyGroup.Undead, new WorldPoint(0f, 0f, 17f))];
        Assert.True(fixture.ConditionHolds(7));
        Assert.False(fixture.ConditionHolds(8));
        fixture.Nearby = [new DaggerfallNearbyCreature(DaggerfallEnemyGroup.Undead, new WorldPoint(0f, 0f, 18f))];
        Assert.False(fixture.ConditionHolds(7), "the donor keeps every object strictly inside the radius");
        fixture.Nearby = [new DaggerfallNearbyCreature(DaggerfallEnemyGroup.Undead, new WorldPoint(0f, 0f, 18.5f))];
        Assert.False(fixture.ConditionHolds(7));
        fixture.Nearby = [new DaggerfallNearbyCreature(DaggerfallEnemyGroup.Daedra, new WorldPoint(0f, 0f, 4f))];
        Assert.True(fixture.ConditionHolds(8));
        Assert.False(fixture.ConditionHolds(9));
        fixture.Nearby = [new DaggerfallNearbyCreature(DaggerfallEnemyGroup.Humanoid, new WorldPoint(0f, 0f, 4f))];
        Assert.True(fixture.ConditionHolds(9));
        fixture.Nearby = [new DaggerfallNearbyCreature(DaggerfallEnemyGroup.Animals, new WorldPoint(0f, 0f, 4f))];
        Assert.True(fixture.ConditionHolds(10));
        // The donor's own grouping leaves an ungrouped creature ungrouped rather than guessing.
        fixture.Nearby = [new DaggerfallNearbyCreature(DaggerfallEnemyGroup.None, new WorldPoint(0f, 0f, 4f))];
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
    public void A_worn_armor_enchantment_shifts_the_armor_value_the_donor_names()
    {
        // Ebony Mail and Auriel's Shield publish StrengthensArmor, whose documented shift is five
        // points of armor value. The donor sets one modifier rather than adding per item.
        using Fixture fixture = new();
        Assert.Equal(0, fixture.ArmorValueModifier);

        fixture.EquipEnchanted("iron-cuirass", 6001, "magic-item.0054", equip: true);
        fixture.Refresh();
        Assert.Equal(-5, fixture.ArmorValueModifier);

        fixture.EquipEnchanted("tower-shield", 6002, "magic-item.0055", equip: true);
        fixture.Refresh();
        Assert.Equal(-5, fixture.ArmorValueModifier);

        fixture.Unequip(6001);
        fixture.Unequip(6002);
        fixture.Refresh();
        Assert.Equal(0, fixture.ArmorValueModifier);
    }

    [Fact]
    public void A_worn_carry_allowance_enchantment_extends_the_one_encumbrance_maximum()
    {
        // The encumbrance owner answers with the strength formula scaled by whatever the worn items
        // allow, so a weight-allowance enchantment has one consumer rather than a second weight path.
        using Fixture fixture = new();
        long plain = fixture.MaximumCarryUnits();
        Assert.Equal(1d, fixture.CarryMultiplier);

        fixture.EquipEnchanted("iron-cuirass", 7001, "test.extra-weight", equip: true);
        fixture.Refresh();

        Assert.Equal(1.25d, fixture.CarryMultiplier);
        Assert.Equal((long)(plain * 1.25d), fixture.MaximumCarryUnits());

        fixture.Unequip(7001);
        fixture.Refresh();
        Assert.Equal(1d, fixture.CarryMultiplier);
        Assert.Equal(plain, fixture.MaximumCarryUnits());
    }

    [Fact]
    public void A_worn_talent_enchantment_sets_the_talent_the_combat_owner_reads()
    {
        using Fixture fixture = new();
        Assert.False(fixture.Talents.AdrenalineRush);

        fixture.EquipEnchanted("iron-cuirass", 7002, "test.talents", equip: true);
        fixture.Refresh();
        Assert.True(fixture.Talents.AdrenalineRush);

        fixture.Unequip(7002);
        fixture.Refresh();
        Assert.False(fixture.Talents.AdrenalineRush);
    }

    [Fact]
    public void A_worn_spell_point_enchantment_adds_its_points_only_under_its_condition()
    {
        using Fixture fixture = new();
        int plain = fixture.MagickaMaximum();

        fixture.EquipEnchanted("iron-cuirass", 7003, "test.spell-points", equip: true);
        fixture.Calendar = fixture.Calendar with { Month = 3 };   // a spring month: the condition holds
        fixture.Refresh();
        Assert.Equal(plain + 75, fixture.MagickaMaximum());

        fixture.Calendar = fixture.Calendar with { Month = 0 };   // a winter month: it does not
        fixture.Refresh();
        Assert.Equal(plain, fixture.MagickaMaximum());

        fixture.Calendar = fixture.Calendar with { Month = 3 };
        fixture.Refresh();
        Assert.Equal(plain + 75, fixture.MagickaMaximum());

        fixture.Unequip(7003);
        fixture.Refresh();
        Assert.Equal(plain, fixture.MagickaMaximum());
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
        private bool _sunlight;
        private DaggerfallCalendar _calendar = new(405, 0, 1, 12, 0, 0);

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
            _held = new DaggerfallHeldEnchantments(_equipment, _instances, MagicItems, player.Stats, _actors.Entities, PlayerEntity,
                () => _calendar, () => _positions.TryGetValue(DaggerfallActorIdentity.PlayerEntityId, out WorldPoint position) ? position : new WorldPoint(0f, 0f, 0f),
                () => _nearby, () => _sunlight);
            _encumbrance = new DaggerfallEncumbrancePolicy(coordinator, player.Stats, () => _held.CarryMultiplier);
        }

        internal EntityId PlayerEntity { get; }

        internal DaggerfallCalendar Calendar { get => _calendar; set => _calendar = value; }

        internal WorldPoint Position { set => _positions[DaggerfallActorIdentity.PlayerEntityId] = value; }

        internal IReadOnlyList<DaggerfallNearbyCreature> Nearby { set => _nearby = value; }

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
            // Wear it where its own classification allows, preferring a hand that is still free.
            string[] candidates = definition.Armor is not null
                ? ["chest-armor", "legs-armor", "head", "feet", "gloves"]
                : definition.Shield is not null ? ["left-hand"] : ["right-hand", "left-hand"];
            string slot = candidates.First(candidate => !taken.Contains(candidate));
            _equipment.Equip(item, [new WorldRpg.Kit.Inventory.EquipmentSlotId(slot)]);
        }

        internal void Unequip(ulong uniqueId)
        {
            WorldRpg.Kit.Inventory.UniqueInventoryItem item = _equipment.Read().Assignments.Single(assignment =>
                _equipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == uniqueId).Item;
            _equipment.Unequip(item);
        }

        internal void Refresh() => _held.Refresh();

        internal void AdvanceRounds(long firstMinuteIndex, int minutes) => _held.AdvanceRounds(firstMinuteIndex, minutes);

        internal bool Sunlight { set => _sunlight = value; }

        internal int Health() => checked((int)_actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current);

        internal int HealthMaximum() => checked((int)_actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Maximum.Value);

        internal void SetHealth(int value) =>
            _actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).SetCurrent(value, clamp: true);

        internal double CarryMultiplier => _held.CarryMultiplier;

        internal DaggerfallHeldTalents Talents => _held.Talents;

        internal int MagickaMaximum() => _actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value)).ValueInt;

        internal bool ConditionHolds(int param) => _held.ConditionHolds(param);

        internal int ArmorValueModifier => _held.ArmorValueModifier;

        internal int Skill(string skill) => _actors.Player.Stats.GetStat(StatId.Parse(skill)).ValueInt;

        internal IReadOnlyList<StatSource> Sources(string skill) => _actors.Player.Stats.GetStat(StatId.Parse(skill)).Sources;

        internal long MaximumCarryUnits() => _encumbrance.Read().MaximumClassicUnits;

        private static DaggerfallDefinitions Definitions { get; } = DaggerfallBaseContent.Read(
            File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));

        /// <summary>
        /// The published magic items plus the item-maker payloads the corpus cannot wear yet: the donor
        /// enumerates those as settings rather than as pre-generated items, and Den #8583 publishes
        /// them. Authoring them here is what lets each payload's own dispatch be asserted.
        /// </summary>
        private static Dictionary<string, DaggerfallMagicItemDefinition> MagicItems { get; } = Published()
            .Concat(Authored())
            .ToDictionary(item => item.Key, StringComparer.Ordinal);

        private static IEnumerable<DaggerfallMagicItemDefinition> Published() => Definitions.Magic.MagicItems.Values;

        private static IEnumerable<DaggerfallMagicItemDefinition> Authored()
        {
            yield return Setting("test.spell-points", 3, 1);      // ExtraSpellPts during spring
            yield return Setting("test.extra-weight", 7, 0);      // IncreasedWeightAllowance one quarter
            yield return Setting("test.talents", 13, 2);          // ImprovesTalents adrenaline rush
            yield return Setting("test.regen-sunlight", 5, 1);    // RegensHealth in sunlight
            yield return Setting("test.regen-darkness", 5, 2);    // RegensHealth in darkness
        }

        private static DaggerfallMagicItemDefinition Setting(string key, int type, int param) => new(
            key, 0L, key, 0, 0, 0, 0, 0, 0,
            [new DaggerfallMagicEnchantmentDefinition($"{key}.enchantment.1", type, param, "authored", null, false)]);

        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
        }

        public void Dispose() => _actors.Dispose();
    }
}
