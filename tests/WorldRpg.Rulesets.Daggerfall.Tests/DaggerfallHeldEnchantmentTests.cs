using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
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

        // The donor reads its round count before the round it serves, so the first round after a load is
        // itself a beat and the next one comes three rounds later.
        fixture.AdvanceRounds(minutes: 1);
        Assert.Equal(maximum - 9, fixture.Health());

        fixture.AdvanceRounds(minutes: 3);
        Assert.Equal(maximum - 9, fixture.Health());

        fixture.AdvanceRounds(minutes: 1);
        Assert.Equal(maximum - 8, fixture.Health());

        // Eight rounds from a count of five carry two beats, at counts eight and twelve.
        fixture.AdvanceRounds(minutes: 8);
        Assert.Equal(maximum - 6, fixture.Health());

        // The donor clamps at the maximum rather than overhealing.
        fixture.SetHealth(maximum - 1);
        fixture.AdvanceRounds(minutes: 4);
        Assert.Equal(maximum, fixture.Health());
        fixture.AdvanceRounds(minutes: 4);
        Assert.Equal(maximum, fixture.Health());

        fixture.Unequip(8001);
        fixture.Refresh();
        fixture.SetHealth(maximum - 4);
        fixture.AdvanceRounds(minutes: 8);
        Assert.Equal(maximum - 4, fixture.Health());
    }

    [Fact]
    public void Sunlight_is_the_donors_reading_of_daytime_outside_every_structure()
    {
        // The donor reads daytime while the player is not inside anything, so a building is darkness by
        // day exactly as a dungeon is, and prison is darkness whatever the sky says.
        Assert.True(DaggerfallHeldEnchantments.InSunlight(isDay: true, insideStructure: false, inPrison: false));
        Assert.False(DaggerfallHeldEnchantments.InSunlight(isDay: false, insideStructure: false, inPrison: false));
        Assert.False(DaggerfallHeldEnchantments.InSunlight(isDay: true, insideStructure: true, inPrison: false));
        Assert.False(DaggerfallHeldEnchantments.InSunlight(isDay: true, insideStructure: false, inPrison: true));
    }

    [Fact]
    public void A_regeneration_outside_the_donors_params_never_heals()
    {
        using Fixture fixture = new();
        int maximum = fixture.HealthMaximum();
        fixture.SetHealth(maximum - 10);
        // The owner's invalid-param path: no published item and no setting carries this payload, so it
        // is authored here to prove an unnamed param contributes nothing rather than healing always.
        fixture.EquipAuthored("iron-cuirass", 9001, type: 5, param: 9, equip: true);
        fixture.Refresh();

        fixture.Sunlight = true;
        fixture.AdvanceRounds(minutes: 40);
        Assert.Equal(maximum - 10, fixture.Health());

        fixture.Sunlight = false;
        fixture.AdvanceRounds(minutes: 40);
        Assert.Equal(maximum - 10, fixture.Health());
    }

    [Fact]
    public void A_very_long_interval_regenerates_no_further_than_the_effect_cap()
    {
        // The donor bounds elapsed catch-up, and the effect lifecycle's cap is that same bound, so a
        // held payload cannot out-heal the effects running beside it.
        using Fixture fixture = new();
        int maximum = fixture.HealthMaximum();
        fixture.EquipEnchanted("iron-cuirass", 9002, "magic-item.0051", equip: true);
        fixture.Refresh();

        // The bound is the donor's own two days of minutes, and it is the lifecycle's bound, not a
        // second number: doubling it or changing the shared constant must fail here.
        Assert.Equal(2880u, DaggerfallEffectLifecycle.MaximumElapsedCatchupRounds);
        fixture.SetHealth(1);
        fixture.AdvanceRounds(minutes: int.MaxValue);
        Assert.Equal(maximum, fixture.Health());

        fixture.SetHealth(1);
        fixture.AdvanceRounds(minutes: 100_000);
        Assert.Equal(maximum, fixture.Health());
    }

    [Fact]
    public void A_worn_regeneration_heals_only_under_the_condition_it_names()
    {
        using Fixture fixture = new();
        int maximum = fixture.HealthMaximum();
        fixture.SetHealth(maximum - 10);
        fixture.EquipEnchanted("iron-cuirass", 8002, SettingKey(5, 1), equip: true);
        fixture.EquipEnchanted("tower-shield", 8003, SettingKey(5, 2), equip: true);
        fixture.Refresh();

        // Daylight outdoors: only the sunlight source ticks, so one point per fourth round.
        fixture.Sunlight = true;
        fixture.AdvanceRounds(minutes: 4);
        Assert.Equal(maximum - 9, fixture.Health());

        // Darkness: only the darkness source ticks.
        fixture.Sunlight = false;
        fixture.AdvanceRounds(minutes: 4);
        Assert.Equal(maximum - 8, fixture.Health());

        // Two sources whose condition holds at the same time tick twice in a round.
        fixture.EquipEnchanted("iron-longsword", 8004, SettingKey(5, 1), equip: true);
        fixture.Refresh();
        fixture.Sunlight = true;
        fixture.AdvanceRounds(minutes: 4);
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

        fixture.EquipEnchanted("iron-cuirass", 7001, SettingKey(7, 0), equip: true);
        fixture.Refresh();

        Assert.Equal(1.25d, fixture.CarryMultiplier);
        Assert.Equal((long)(plain * 1.25d), fixture.MaximumCarryUnits());

        fixture.Unequip(7001);
        fixture.Refresh();
        Assert.Equal(1d, fixture.CarryMultiplier);
        Assert.Equal(plain, fixture.MaximumCarryUnits());
    }

    [Fact]
    public void A_setting_applied_by_the_item_maker_reaches_the_held_contribution()
    {
        // The whole loop in one fact: the item maker puts a weight-allowance setting on a plain item, the
        // player wears it, and the carry maximum the encumbrance owner answers with grows by a quarter.
        using Fixture fixture = new();
        long plain = fixture.MaximumCarryUnits();

        // A daedric longsword: iron carries no enchantment power, so the item maker refuses it, as
        // classic did, and a one-handed blade keeps the fixture's single-slot bookkeeping.
        fixture.EnchantAndWear("template-120-daedric", 9101, type: 7, param: 0);
        fixture.Refresh();

        Assert.Equal(1.25d, fixture.CarryMultiplier);
        Assert.Equal((long)(plain * 1.25d), fixture.MaximumCarryUnits());

        fixture.Unequip(9101);
        fixture.Refresh();
        Assert.Equal(1d, fixture.CarryMultiplier);
        Assert.Equal(plain, fixture.MaximumCarryUnits());
    }

    [Fact]
    public void A_setting_applied_by_the_item_maker_raises_the_skill_it_names()
    {
        // The skill payload the corpus' own artifacts carry, proven through the item maker instead of a
        // directly registered instance: the classic param names long blade, the donor's amount is 15.
        using Fixture fixture = new();
        int baseSkill = fixture.Skill("long-blade");

        fixture.EnchantAndWear("template-120-daedric", 9102, type: 10, param: 29);
        fixture.Refresh();
        Assert.Equal(baseSkill + 15, fixture.Skill("long-blade"));

        fixture.Unequip(9102);
        fixture.Refresh();
        Assert.Equal(baseSkill, fixture.Skill("long-blade"));
    }

    [Fact]
    public void A_conditional_spell_point_setting_reaches_the_worn_contribution()
    {
        // The conditional payload of #8583's requirement, through the real action: a during-spring
        // setting adds its 75 points in a spring month and nothing outside one.
        using Fixture fixture = new();
        int plain = fixture.MagickaMaximum();

        fixture.EnchantAndWear("template-120-daedric", 9103, type: 3, param: 1);
        fixture.Calendar = fixture.Calendar with { Month = 3 };
        fixture.Refresh();
        Assert.Equal(plain + 75, fixture.MagickaMaximum());

        fixture.Calendar = fixture.Calendar with { Month = 0 };
        fixture.Refresh();
        Assert.Equal(plain, fixture.MagickaMaximum());

        fixture.Unequip(9103);
        fixture.Refresh();
        Assert.Equal(plain, fixture.MagickaMaximum());
    }

    [Fact]
    public void A_worn_talent_enchantment_sets_the_talent_the_combat_owner_reads()
    {
        using Fixture fixture = new();
        Assert.False(fixture.Talents.AdrenalineRush);

        fixture.EquipEnchanted("iron-cuirass", 7002, SettingKey(13, 2), equip: true);
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

        fixture.EquipEnchanted("iron-cuirass", 7003, SettingKey(3, 1), equip: true);
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

    [Fact]
    public void The_item_makers_settings_are_the_donors_own_table()
    {
        // The donor enumerates these from each effect class rather than publishing them with its magic
        // items, so a worn item can hold a payload no published item carries. Counts, costs and params
        // are the donor's: EnhancesSkill prices all 35 skills alike, ExtraSpellPts runs seasons at 500
        // then moons at 200 then creature groups at 700-1000, weight is 400/600, talents 500/600/600,
        // regeneration 4000/3000/3000 for always/sunlight/darkness, StrengthensArmor and RepairsObjects
        // are single settings at param -1, and the detriments are priced negatively: ItemDeteriorates
        // -3000/-1500/-500, UserTakesDamage -6000/-1000, WeakensArmor -700.
        Assert.Equal(35 + 11 + 2 + 3 + 3 + 1 + 1 + 1 + 3 + 2, DaggerfallEnchantmentSettings.All.Count);
        Assert.All(DaggerfallEnchantmentSettings.All, setting => Assert.Equal(setting.Key, $"enchantment.{setting.Type}.{setting.Param}"));

        Assert.Equal(900, SettingCost(10, 29));                       // long blade, the donor's flat price
        Assert.Equal(500, SettingCost(3, 0));                         // during winter
        Assert.Equal(200, SettingCost(3, 5));                         // during half moon
        Assert.Equal(1000, SettingCost(3, 10));                       // near animals
        Assert.Equal(400, SettingCost(7, 0));
        Assert.Equal(600, SettingCost(7, 1));
        Assert.Equal(500, SettingCost(13, 0));                        // improved acute hearing
        Assert.Equal(600, SettingCost(13, 2));                        // improved adrenaline rush
        Assert.Equal(4000, SettingCost(5, 0));                        // regeneration all the time
        Assert.Equal(3000, SettingCost(5, 2));                        // regeneration in darkness
        Assert.Equal(700, SettingCost(12, -1));                       // strengthened armor
        Assert.Equal(900, SettingCost(8, -1));                        // repairs objects
        Assert.Equal(-700, SettingCost(24, -1));                      // weakens armor, a detriment
        Assert.Equal(-3000, SettingCost(16, 0));                      // deteriorates all the time
        Assert.Equal(-500, SettingCost(16, 2));                       // deteriorates in holy places
        Assert.Equal(-6000, SettingCost(17, 0));                      // damages its wearer in sunlight
        Assert.Equal(-1000, SettingCost(17, 1));                      // damages its wearer in holy places

        // Each setting resolves to the effect shape the held owner applies, and an unknown key does not.
        DaggerfallEnchantmentSetting setting = DaggerfallEnchantmentSettings.All.Single(candidate => candidate.Type == 3 && candidate.Param == 7);
        DaggerfallMagicEnchantmentDefinition effect = DaggerfallEnchantmentSettings.ToEffect(setting);
        Assert.Equal(3, effect.Type);
        Assert.Equal(7, effect.Param);
        Assert.Equal("near-undead", effect.ParamMeaning);
        Assert.False(DaggerfallEnchantmentSettings.TryResolve("enchantment.5.9", out _));
        Assert.False(DaggerfallEnchantmentSettings.TryResolve("magic-item.0050", out _));
    }

    [Fact]
    public void The_enchantment_cost_table_prices_every_item_maker_setting()
    {
        // The cost policy's non-spell table used to know three payloads; it now answers from the settings
        // catalog for every payload the item maker offers, which is what lets the enchant action price
        // one. The two published-only payloads stay in the policy.
        static int Cost(int type, int param)
        {
            Assert.True(DaggerfallMagicCostPolicy.TryGetNonSpellEnchantmentCost(
                new DaggerfallMagicEnchantmentDefinition("test", type, param, "test", null, false), out int cost));
            return cost;
        }

        Assert.Equal(2000, Cost(6, 0));      // VampiricEffect at range
        Assert.Equal(1000, Cost(6, 1));      // VampiricEffect.WhenStrikes
        Assert.Equal(1500, Cost(9, -1));     // AbsorbsSpells
        Assert.Equal(900, Cost(10, 29));     // EnhancesSkill, long blade
        Assert.Equal(900, Cost(10, 7));      // EnhancesSkill, the donor prices every skill alike
        Assert.Equal(4000, Cost(5, 0));      // RegensHealth all the time
        Assert.Equal(700, Cost(12, -1));     // StrengthensArmor
        Assert.Equal(600, Cost(13, 2));      // ImprovesTalents adrenaline rush
        Assert.Equal(-3000, Cost(16, 0));    // ItemDeteriorates all the time, a detriment
        Assert.False(DaggerfallMagicCostPolicy.TryGetNonSpellEnchantmentCost(
            new DaggerfallMagicEnchantmentDefinition("test", 99, 0, "test", null, false), out _));
    }

    [Fact]
    public void The_settings_table_validates_and_a_broken_row_is_reported()
    {
        // Every load reads the compiled table through this, so the checks have to be the ones that would
        // catch a transcription: a key that does not name its own type and param, an undefined classic
        // type, a param below the donor's sentinel, an empty meaning, a zero cost or a repeated pair.
        Assert.Empty(DaggerfallEnchantmentSettings.Validate(DaggerfallEnchantmentSettings.All));

        DaggerfallEnchantmentSetting sound = DaggerfallEnchantmentSettings.All[0];
        string[] problems = [.. DaggerfallEnchantmentSettings.Validate(
        [
            sound with { Key = "wrong" },
            sound with { Type = 99 },
            sound with { Param = -2 },
            sound with { Meaning = " " },
            sound with { Cost = 0 },
            sound,
            sound,
        ])];

        Assert.Contains(problems, problem => problem.Contains("does not name type", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("does not define", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("below the donor's single-setting sentinel", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("has no param meaning", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("costs nothing", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("repeats type", StringComparison.Ordinal));
    }

    /// <summary>The donor's own item-maker settings, addressed by the key a worn item carries.</summary>
    private static int SettingCost(int type, int param) =>
        DaggerfallEnchantmentSettings.All.Single(setting => setting.Type == type && setting.Param == param).Cost;

    private static string SettingKey(int type, int param) =>
        DaggerfallEnchantmentSettings.All.Single(setting => setting.Type == type && setting.Param == param).Key;

    private sealed class Fixture : IDisposable
    {
        private readonly ActorsState _actors = new();
        private readonly DaggerfallItemInstances _instances = new();
        private readonly MechanicsEquipmentCoordinator _equipment;
        private readonly DaggerfallEncumbrancePolicy _encumbrance;
        private readonly DaggerfallHeldEnchantments _held;
        private readonly DaggerfallItemConditionService _conditions;
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
            DaggerfallEquipmentMoves moves = new(coordinator, _equipment, Definitions, itemInstances: _instances);
            _conditions = new DaggerfallItemConditionService(Definitions, _instances, moves);
            _held = new DaggerfallHeldEnchantments(_equipment, _instances, MagicItems, player.Stats, _actors.Entities, PlayerEntity,
                () => _calendar, () => _positions.TryGetValue(DaggerfallActorIdentity.PlayerEntityId, out WorldPoint position) ? position : new WorldPoint(0f, 0f, 0f),
                () => _nearby, () => _sunlight);
            _encumbrance = new DaggerfallEncumbrancePolicy(coordinator, player.Stats, () => _held.CarryMultiplier);
        }

        internal EntityId PlayerEntity { get; }

        private Dictionary<string, DaggerfallMagicItemDefinition> MagicItems => Published.Concat(Authored)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

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

        internal void EquipAuthored(string itemId, ulong uniqueId, int type, int param, bool equip = false)
        {
            string key = $"authored.{type}.{param}";
            Authored[key] = new DaggerfallMagicItemDefinition(key, 0L, key, 0, 0, 0, 0, 0, 0,
                [new DaggerfallMagicEnchantmentDefinition($"{key}.enchantment.1", type, param, "authored", null, false)]);
            EquipEnchanted(itemId, uniqueId, key, equip);
        }

        /// <summary>Puts a setting on a player item through the real item-maker action, then wears it.</summary>
        internal void EnchantAndWear(string itemId, ulong uniqueId, int type, int param)
        {
            DaggerfallItemDefinition definition = Definitions.RequireItem(new DaggerfallItemId(itemId));
            int condition = Definitions.AuthoredMaximumCondition(definition);
            _instances.RegisterUnique(uniqueId, DaggerfallItemInstanceMetadata.Default(definition, DaggerfallItemOwner.Player) with
            {
                CurrentCondition = condition,
                MaximumCondition = condition,
            });
            WorldRpg.Kit.Inventory.UniqueInventoryItem item = _equipment.Materialize(
                new DurableIdentityReference(DurableIdentityKind.Item, uniqueId), new InventoryItemId(itemId));
            _conditions.Enchant(item, SettingKey(type, param));
            // Wear it where its own handedness allows, as the other fixtures do.
            _equipment.Equip(item, definition.Weapon?.Handedness == "both"
                ? [new WorldRpg.Kit.Inventory.EquipmentSlotId("right-hand"), new WorldRpg.Kit.Inventory.EquipmentSlotId("left-hand")]
                : [new WorldRpg.Kit.Inventory.EquipmentSlotId("right-hand")]);
        }

        internal void Unequip(ulong uniqueId)
        {
            WorldRpg.Kit.Inventory.UniqueInventoryItem item = _equipment.Read().Assignments.Single(assignment =>
                _equipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == uniqueId).Item;
            _equipment.Unequip(item);
        }

        internal void Refresh() => _held.Refresh();

        internal void AdvanceRounds(int minutes) => _held.AdvanceRounds(minutes);

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

        /// <summary>Payloads authored by a fact that needs one no published item or setting carries.</summary>
        private readonly Dictionary<string, DaggerfallMagicItemDefinition> Authored = new(StringComparer.Ordinal);

        /// <summary>The published magic items a worn item can name.</summary>
        private static Dictionary<string, DaggerfallMagicItemDefinition> Published { get; } =
            Definitions.Magic.MagicItems.Values.ToDictionary(item => item.Key, StringComparer.Ordinal);



        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
        }

        public void Dispose() => _actors.Dispose();
    }
}
