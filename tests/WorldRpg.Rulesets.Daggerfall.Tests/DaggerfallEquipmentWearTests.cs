using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Physical-hit equipment wear at the one admitted application boundary. Classic wears equipment only
/// for a weapon strike, charges the weapon first, then the shield covering the struck body part or that
/// part's armour, and every condition change comes from one accepted hit receipt.
/// </summary>
public sealed class DaggerfallEquipmentWearTests
{
    private const long Enemy = 2;

    [Fact]
    public void A_landed_weapon_strike_wears_the_swingers_weapon_and_the_armour_that_covered_the_body_part()
    {
        using WearFixture fixture = new();
        fixture.EquipEnemyWeapon("iron-longsword", 9001);
        // A body roll of 9 is the classic table's chest, where this player wears the iron cuirass.
        fixture.Script(body: 9, critical: 50, hit: 1, damage: 15);
        int enemyWeapon = fixture.Condition(9001);
        int playerArmour = fixture.Condition(1003);
        int playerWeapon = fixture.Condition(1001);

        IReadOnlyList<IProductFact> facts = fixture.RunEnemyAttack();

        // Ten percent of the accepted damage, rounded to nearest, charged on both items without a floor
        // roll: the swing's own weapon and the armour that covered the struck body part. The accepted
        // hit is 13, whose ten percent rounds up to one unit; stated as a literal rather than read back
        // from the formula the rules call, so a change to the donor's rounding fails here too.
        Assert.Equal(13, AppliedDamage(facts));
        const int units = 1;
        Assert.Equal(enemyWeapon - units, fixture.Condition(9001));
        Assert.Equal(playerArmour - units, fixture.Condition(1003));
        Assert.Equal(playerWeapon, fixture.Condition(1001));
        EquipmentWornFact[] worn = [.. facts.OfType<EquipmentWornFact>()];
        Assert.Equal(2, worn.Length);
        Assert.All(worn, fact => Assert.False(fact.Broken));
        Assert.Equal([Enemy, DaggerfallActorIdentity.PlayerEntityId], worn.Select(fact => fact.OwnerActorId));
        Assert.Equal([units, units], worn.Select(fact => fact.PreviousCondition - fact.CurrentCondition));
    }

    [Fact]
    public void An_unarmed_natural_attack_wears_nothing_at_all()
    {
        // The donor reaches its whole wear block from the branch that has a weapon in hand, so a rat's
        // natural attack never damages the armour it lands on.
        using WearFixture fixture = new();
        fixture.ReplaceActor(Enemy, fixture.Definitions.RequireActor(new DaggerfallActorId("rat")) with
        {
            Attacks = [new DaggerfallAttackRange(1, 8), new(0, 0), new(0, 0)],
        });
        fixture.ScriptMonster(body: 9, reflex: 50, critical: 50, hit: 1, damage: 8);
        int playerArmour = fixture.Condition(1003);
        int playerWeapon = fixture.Condition(1001);

        IReadOnlyList<IProductFact> facts = fixture.RunEnemyAttack();

        Assert.Empty(facts.OfType<EquipmentWornFact>());
        Assert.Equal(playerArmour, fixture.Condition(1003));
        Assert.Equal(playerWeapon, fixture.Condition(1001));
    }

    [Fact]
    public void A_miss_and_a_material_refusal_leave_every_item_at_its_condition()
    {
        using WearFixture fixture = new();
        fixture.EquipEnemyWeapon("iron-longsword", 9001);
        fixture.Script(body: 3, critical: 50, hit: 99);
        int enemyWeapon = fixture.Condition(9001);
        int playerArmour = fixture.Condition(1003);

        IReadOnlyList<IProductFact> missed = fixture.RunEnemyAttack();

        Assert.Empty(missed.OfType<EquipmentWornFact>());
        Assert.Equal(playerArmour, fixture.Condition(1003));
        Assert.Equal(enemyWeapon, fixture.Condition(9001));

        // The player's iron longsword cannot cut a target whose minimum material is silver: the attempt
        // is refused before damage, so neither weapon nor armour wears.
        using WearFixture immune = new();
        immune.ReplaceActor(Enemy, immune.Definitions.RequireActor(new DaggerfallActorId("thief")) with
        {
            ActionId = "enemy-class-equipped-melee",
            MinimumMaterial = "silver",
        });
        immune.Script(body: 3, critical: 50, hit: 1);
        int immuneWeapon = immune.Condition(1001);

        IReadOnlyList<IProductFact> refused = immune.RunPlayerAttack();

        Assert.Contains(refused, fact => fact is AttackRejectedFact { Reason: AttackRejection.InsufficientWeaponMaterial });
        Assert.Empty(refused.OfType<EquipmentWornFact>());
        Assert.Equal(immuneWeapon, immune.Condition(1001));
    }

    [Fact]
    public void A_blow_below_five_damage_takes_one_unit_only_when_the_floor_roll_grants_it()
    {
        // Damage below five scales to zero, so the classic one-in-five floor decides: a roll of 20 or
        // less takes one unit, a roll of 21 leaves that item whole.
        using WearFixture worn = new();
        worn.EquipEnemyWeapon("iron-longsword", 9001);
        worn.Script(body: 9, critical: 50, hit: 1, damage: 4, wornWeaponRoll: 20, wornArmourRoll: 21);
        int enemyWeapon = worn.Condition(9001);
        int playerArmour = worn.Condition(1003);

        IReadOnlyList<IProductFact> facts = worn.RunEnemyAttack();

        Assert.Equal(enemyWeapon - 1, worn.Condition(9001));
        Assert.Equal(playerArmour, worn.Condition(1003));
        EquipmentWornFact single = Assert.Single(facts.OfType<EquipmentWornFact>());
        Assert.Equal(Enemy, single.OwnerActorId);
        Assert.Equal(1, single.PreviousCondition - single.CurrentCondition);

        using WearFixture spared = new();
        spared.EquipEnemyWeapon("iron-longsword", 9001);
        spared.Script(body: 9, critical: 50, hit: 1, damage: 4, wornWeaponRoll: 21, wornArmourRoll: 21);
        int sparedWeapon = spared.Condition(9001);

        IReadOnlyList<IProductFact> sparedFacts = spared.RunEnemyAttack();

        Assert.Equal(sparedWeapon, spared.Condition(9001));
        Assert.Empty(sparedFacts.OfType<EquipmentWornFact>());
    }

    [Fact]
    public void A_covering_shield_wears_instead_of_the_armour_on_the_struck_body_part()
    {
        using WearFixture fixture = new();
        fixture.EquipEnemyWeapon("iron-longsword", 9001);
        // The tower shield covers the left arm (body 2) and the player wears no left-arm armour, so the
        // shield is the only defence that can wear.
        fixture.EquipPlayerItem("tower-shield", 2001, "left-hand");
        // A body roll of 6 is the classic table's left arm, which the tower shield covers.
        fixture.Script(body: 6, critical: 50, hit: 1, damage: 15);
        int shield = fixture.Condition(2001);
        int cuirass = fixture.Condition(1003);
        int enemyWeapon = fixture.Condition(9001);

        IReadOnlyList<IProductFact> facts = fixture.RunEnemyAttack();

        // The covering shield takes the defence's wear, and it does not excuse the swing's own weapon:
        // one accepted hit still costs both items their single unit.
        Assert.Equal(13, AppliedDamage(facts));
        const int units = 1;
        Assert.Equal(shield - units, fixture.Condition(2001));
        Assert.Equal(cuirass, fixture.Condition(1003));
        Assert.Equal(enemyWeapon - units, fixture.Condition(9001));
        EquipmentWornFact[] worn = [.. facts.OfType<EquipmentWornFact>()];
        Assert.Equal(2, worn.Length);
        Assert.Equal([Enemy, DaggerfallActorIdentity.PlayerEntityId], worn.Select(fact => fact.OwnerActorId));
        Assert.Contains(worn, fact => fact.DurableItemId == 2001 && fact.OwnerActorId == DaggerfallActorIdentity.PlayerEntityId);
    }

    [Fact]
    public void A_broken_pair_of_greaves_reports_the_donors_plural_from_the_wear_path()
    {
        using WearFixture fixture = new();
        fixture.EquipEnemyWeapon("iron-longsword", 9001);
        // The instance a materialized pair of greaves really is, in the slot the classic legs roll
        // wears: neither the item identifier nor the authored lookup states the plural here, only the
        // native template the definition carries.
        fixture.EquipPlayerItem("template-104-iron", 4001, "legs-armor");
        fixture.SetCondition(fixture.PlayerItem(4001), 1);
        // A body roll of 17 is the classic table's legs.
        fixture.Script(body: 17, critical: 50, hit: 1, damage: 15);

        IReadOnlyList<IProductFact> facts = fixture.RunEnemyAttack();

        EquipmentWornFact broken = Assert.Single(facts.OfType<EquipmentWornFact>(), fact => fact.Broken);
        Assert.True(broken.PluralBreak, "the donor's greaves break in the plural");
        // The line is the donor's published plural message with the item substituted.
        PresentationState presentation = new(string.Empty);
        DaggerfallOutcomePresentation outcome = new(presentation,
            new Dictionary<long, DaggerfallActorDefinition> { [Enemy] = Definitions.RequireActor(new DaggerfallActorId("rat")) },
            text: Definitions.Text);
        outcome.React(new AttackHitFact(Enemy, DaggerfallActorIdentity.PlayerEntityId, 5, 5, 3, EnemyAttack: true, 1, 1));
        outcome.React(broken);
        Assert.EndsWith("template-104-iron have broken.", presentation.LastOutcome);
    }

    [Fact]
    public void A_hit_that_breaks_a_player_item_unequips_it_once_and_reports_one_break()
    {
        using WearFixture fixture = new();
        fixture.EquipEnemyWeapon("iron-longsword", 9001);
        fixture.Script(body: 9, critical: 50, hit: 1, damage: 15);
        UniqueItem cuirass = fixture.PlayerItem(1003);
        fixture.SetCondition(cuirass, 1);

        IReadOnlyList<IProductFact> facts = fixture.RunEnemyAttack();

        Assert.DoesNotContain(fixture.PlayerEquipment.Read().Assignments, assignment => assignment.Item.EntityId == cuirass.EntityId);
        EquipmentWornFact broken = Assert.Single(facts.OfType<EquipmentWornFact>(), fact => fact.Broken);
        Assert.Equal(1, broken.PreviousCondition);
        Assert.Equal(0, broken.CurrentCondition);
        Assert.Equal(0, fixture.Condition(1003));

        // A second accepted hit finds the worn-out armour gone from the struck body part, so it breaks
        // nothing again: exactly one break came out of the item that reached zero.
        fixture.Script(body: 9, critical: 50, hit: 1, damage: 15);
        int weaponBefore = fixture.Condition(9001);

        // A later admitted step clears the enemy's swing cooldown.
        IReadOnlyList<IProductFact> second = fixture.RunEnemyAttack(step: 30);

        Assert.Equal(weaponBefore - DaggerfallFormulaPolicy.ConditionDamageScale(AppliedDamage(second)), fixture.Condition(9001));
        Assert.DoesNotContain(second.OfType<EquipmentWornFact>(), fact => fact.OwnerActorId == DaggerfallActorIdentity.PlayerEntityId);
        Assert.Equal(0, fixture.Condition(1003));
    }

    [Fact]
    public void A_starting_loadout_item_carries_the_condition_its_template_and_material_author()
    {
        using WearFixture fixture = new();
        DaggerfallDefinitions definitions = fixture.Definitions;

        int longsword = definitions.AuthoredMaximumCondition(definitions.RequireItem(new DaggerfallItemId("iron-longsword")));
        int dagger = definitions.AuthoredMaximumCondition(definitions.RequireItem(new DaggerfallItemId("iron-dagger")));
        int cuirass = definitions.AuthoredMaximumCondition(definitions.RequireItem(new DaggerfallItemId("iron-cuirass")));
        int shield = definitions.AuthoredMaximumCondition(definitions.RequireItem(new DaggerfallItemId("buckler")));
        int coin = definitions.AuthoredMaximumCondition(definitions.RequireItem(new DaggerfallItemId("gold-piece")));

        // The authored condition is the factory's own material adjustment of the same native template,
        // so a starting item and a created item of one template and material cannot disagree.
        Assert.Equal(
            DaggerfallItemMaterialPolicy.Apply(definitions.ItemTemplateCatalog.Templates[120], "iron").MaximumCondition,
            longsword);
        Assert.True(longsword > 100 && dagger > 0 && cuirass > 100);
        Assert.Equal(longsword, fixture.Condition(1001));
        Assert.Equal(cuirass, fixture.Condition(1003));
        Assert.Equal(dagger, fixture.Condition(1002));
        // A shield's authored definition carries no material, so the donor's un-scaled native template
        // condition stands for it; a coin has no native wear template and keeps the registry's unit.
        Assert.Equal(definitions.ItemTemplateCatalog.Templates[109].HitPoints, shield);
        Assert.Equal(1, coin);
    }

    [Fact]
    public void The_wear_decision_follows_the_donor_order_and_its_amounts_stay_in_classic_bounds()
    {
        Assert.Equal(DaggerfallStruckEquipment.None, DaggerfallFormulaPolicy.DamageEquipment(weaponStrike: false, shieldCoversStruckBodyPart: false, armourAtStruckBodyPart: true));
        Assert.Equal(DaggerfallStruckEquipment.None, DaggerfallFormulaPolicy.DamageEquipment(weaponStrike: false, shieldCoversStruckBodyPart: true, armourAtStruckBodyPart: false));
        Assert.Equal(DaggerfallStruckEquipment.Weapon, DaggerfallFormulaPolicy.DamageEquipment(weaponStrike: true, shieldCoversStruckBodyPart: false, armourAtStruckBodyPart: false));
        Assert.Equal(DaggerfallStruckEquipment.Armour, DaggerfallFormulaPolicy.DamageEquipment(weaponStrike: true, shieldCoversStruckBodyPart: false, armourAtStruckBodyPart: true));
        Assert.Equal(DaggerfallStruckEquipment.Shield, DaggerfallFormulaPolicy.DamageEquipment(weaponStrike: true, shieldCoversStruckBodyPart: true, armourAtStruckBodyPart: true));

        Assert.Equal(0, DaggerfallFormulaPolicy.ConditionDamageScale(0));
        Assert.Equal(0, DaggerfallFormulaPolicy.ConditionDamageScale(4));
        Assert.Equal(1, DaggerfallFormulaPolicy.ConditionDamageScale(5));
        Assert.Equal(1, DaggerfallFormulaPolicy.ConditionDamageScale(9));
        Assert.Equal(2, DaggerfallFormulaPolicy.ConditionDamageScale(15));
        Assert.Equal(0, DaggerfallFormulaPolicy.ApplyConditionDamageThroughPhysicalHit(4, 21));
        Assert.Equal(1, DaggerfallFormulaPolicy.ApplyConditionDamageThroughPhysicalHit(4, 20));
        Assert.Equal(2, DaggerfallFormulaPolicy.ApplyConditionDamageThroughPhysicalHit(15, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallFormulaPolicy.ApplyConditionDamageThroughPhysicalHit(4, 101));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallFormulaPolicy.ConditionDamageScale(-1));
    }

    [Fact]
    public void The_break_the_hit_caused_is_what_the_outcome_line_reports()
    {
        PresentationState presentation = new(string.Empty);
        DaggerfallOutcomePresentation outcome = new(presentation,
            new Dictionary<long, DaggerfallActorDefinition> { [Enemy] = Definitions.RequireActor(new DaggerfallActorId("rat")) },
            text: Definitions.Text);
        outcome.React(new AttackHitFact(Enemy, DaggerfallActorIdentity.PlayerEntityId, 5, 5, 3, EnemyAttack: true, 1, 1));

        // The line is the donor's own published message with the item substituted for its placeholder.
        outcome.React(new EquipmentWornFact(DaggerfallActorIdentity.PlayerEntityId, 1001, "iron-longsword", false, 2, 0, Broken: true, 1, 1));
        Assert.Equal("rat hit you for 5 damage; iron-longsword has broken.", presentation.LastOutcome);

        // The plural set is the donor's authored template rule, carried by the fact: greaves break in
        // the plural whatever the identifier they are materialized under looks like.
        outcome.React(new EquipmentWornFact(DaggerfallActorIdentity.PlayerEntityId, 2001, "template-104-iron", true, 4, 0, Broken: true, 1, 1));
        Assert.EndsWith("template-104-iron have broken.", presentation.LastOutcome);
        // Only a break is reported: condition that merely fell is the weapon wear the player never saw.
        outcome.React(new EquipmentWornFact(DaggerfallActorIdentity.PlayerEntityId, 1001, "iron-longsword", false, 10, 7, Broken: false, 1, 1));
        Assert.EndsWith("template-104-iron have broken.", presentation.LastOutcome);
    }

    /// <summary>The health the hit actually took, which is the amount classic scales equipment wear from.</summary>
    private static int AppliedDamage(IReadOnlyList<IProductFact> facts) =>
        (int)Math.Truncate(Assert.Single(facts.OfType<AttackHitFact>()).ActualHealthLost);

    private static DaggerfallDefinitions Definitions => _definitions ??= DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));
    private static DaggerfallDefinitions? _definitions;

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }

    /// <summary>
    /// One staged wear arena: the loadout player with authored item conditions, a brigand (entity 2) that
    /// wears whatever weapon a test hands it, the real condition service, and a scripted keyed random.
    /// </summary>
    private sealed class WearFixture : IDisposable
    {
        private readonly ActorsState _actors;
        private readonly DaggerfallItemInstances _itemInstances = new();
        private readonly Dictionary<long, DaggerfallActorDefinition> _authored = [];
        private readonly Dictionary<long, MechanicsEquipmentCoordinator> _actorEquipment = [];
        private readonly ScriptedRandom _scripted = (ScriptedRandom)(object)DispatchProxy.Create<IRandomService, ScriptedRandom>();
        private readonly IRandomService _random;
        private readonly DaggerCombatRules _combat;
        private readonly DaggerfallEquipmentMoves _playerMoves;
        private readonly DaggerfallItemConditionService _itemCondition;

        internal DaggerfallDefinitions Definitions { get; }
        internal MechanicsEquipmentCoordinator PlayerEquipment { get; }

        internal WearFixture()
        {
            Definitions = DaggerfallEquipmentWearTests.Definitions;
            DaggerfallActorDefinition playerDefinition = Definitions.RequireActor(new DaggerfallActorId("player"));
            _random = (IRandomService)(object)_scripted;
            _actors = new ActorsState();
            PlayerActorState player = _actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId(playerDefinition.Id.Value),
                new DaggerfallMechanicsState().CreateStats(playerDefinition, DaggerfallPlayerVitals.Initial(playerDefinition.Stats, Definitions.Catalogs.RequireCareer("class00"))), "health");
            PlayerEquipment = BuildEquipment(player.Actor.Entity, player.Actor, out MechanicsInventoryCoordinator playerInventory);
            _playerMoves = new DaggerfallEquipmentMoves(playerInventory, PlayerEquipment, Definitions, itemInstances: _itemInstances);
            _itemCondition = new DaggerfallItemConditionService(Definitions, _itemInstances, _playerMoves);
            foreach (DaggerfallLoadoutEntry entry in playerDefinition.Loadout.Where(entry => entry.UniqueEntityId is not null))
            {
                DaggerfallItemDefinition definition = Definitions.RequireItem(entry.ItemId);
                int condition = Definitions.AuthoredMaximumCondition(definition);
                _itemInstances.RegisterUnique(entry.UniqueEntityId!.Value,
                    DaggerfallItemInstanceMetadata.Default(definition, DaggerfallItemOwner.Player) with
                    {
                        CurrentCondition = condition,
                        MaximumCondition = condition,
                    });
                UniqueItem item = PlayerEquipment.Materialize(
                    new DurableIdentityReference(DurableIdentityKind.Item, entry.UniqueEntityId.Value), new InventoryItemId(entry.ItemId.Value));
                if (entry.EquipSlot is DaggerfallEquipmentSlotId slot)
                    PlayerEquipment.Equip(item, [new SlotId(slot.Value)]);
            }

            DaggerfallActorDefinition brigand = Definitions.RequireActor(new DaggerfallActorId("thief")) with { ActionId = "enemy-class-equipped-melee" };
            ActorState enemy = _actors.CreateActor(Enemy, new EntityTypeId("brigand"),
                new DaggerfallMechanicsState().CreateStats(brigand, new DaggerfallVitalValues(200, 100, 0)), new ActorPose(new WorldPoint(1f, 0f, 0f), 0f), "health");
            _authored[DaggerfallActorIdentity.PlayerEntityId] = playerDefinition;
            _authored[Enemy] = brigand;
            _actorEquipment[Enemy] = BuildEquipment(enemy.Actor.Entity, enemy.Actor, out _);
            _combat = new DaggerCombatRules(_random, _actors, PlayerEquipment, _ => null, _itemInstances, Definitions, _authored, null!,
                actorEquipment: id => _actorEquipment.TryGetValue(id, out MechanicsEquipmentCoordinator? coordinator) ? coordinator : PlayerEquipment,
                itemCondition: _itemCondition, playerPosition: () => new WorldPoint(0f, 0f, 0f));
        }

        internal void Script(int body, int critical, int hit, int? damage = null, int? wornWeaponRoll = null, int? wornArmourRoll = null)
        {
            List<int> draws = [body, critical, hit];
            if (damage is int rolled) draws.Add(rolled);
            if (wornWeaponRoll is int weapon) draws.Add(weapon);
            if (wornArmourRoll is int armour) draws.Add(armour);
            _scripted.Feed(draws);
        }

        internal void ScriptMonster(int body, int reflex, int critical, int hit, int damage)
        {
            _scripted.Feed([body, reflex, critical, hit, damage]);
        }

        internal void ReplaceActor(long entityId, DaggerfallActorDefinition definition) => _authored[entityId] = definition;

        internal void EquipEnemyWeapon(string itemId, ulong uniqueId) => Equip(_actorEquipment[Enemy], DaggerfallItemOwner.Actor(Enemy), itemId, uniqueId, "right-hand");

        internal void EquipPlayerItem(string itemId, ulong uniqueId, string slot) => Equip(PlayerEquipment, DaggerfallItemOwner.Player, itemId, uniqueId, slot);

        private void Equip(MechanicsEquipmentCoordinator equipment, DaggerfallItemOwner owner, string itemId, ulong uniqueId, string slot)
        {
            DaggerfallItemDefinition definition = Definitions.RequireItem(new DaggerfallItemId(itemId));
            int condition = Definitions.AuthoredMaximumCondition(definition);
            _itemInstances.RegisterUnique(uniqueId, DaggerfallItemInstanceMetadata.Default(definition, owner) with
            {
                CurrentCondition = condition,
                MaximumCondition = condition,
            });
            equipment.Equip(
                equipment.Materialize(new DurableIdentityReference(DurableIdentityKind.Item, uniqueId), new InventoryItemId(itemId)),
                [new SlotId(slot)]);
        }

        internal int Condition(ulong durableItemId) => _itemInstances.RequireUnique(durableItemId).CurrentCondition;

        internal UniqueItem PlayerItem(ulong durableItemId) =>
            PlayerEquipment.Read().Assignments.Single(assignment =>
                PlayerEquipment.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value == durableItemId).Item;

        internal void SetCondition(UniqueItem item, int condition)
        {
            ulong durable = PlayerEquipment.GetDurableItemId(new EntityId(item.EntityId)).Value;
            _itemInstances.ReplaceUnique(durable, _itemInstances.RequireUnique(durable) with { CurrentCondition = condition });
        }

        internal IReadOnlyList<IProductFact> RunPlayerAttack()
        {
            FactBuffer<IProductFact> facts = new();
            Assert.True(_combat.Execution.Start(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, Enemy, 5, 9, .125d, Delayed: false), facts));
            return Delivered(facts);
        }

        internal IReadOnlyList<IProductFact> RunEnemyAttack(ulong step = 9)
        {
            FactBuffer<IProductFact> facts = new();
            AttackRequest request = new(Enemy, DaggerfallActorIdentity.PlayerEntityId, 5, step, .125d, Delayed: true);
            Assert.True(_combat.Execution.Start(request, facts));
            _combat.Execution.ApplyImpacts([new AttackImpactNotice(Enemy, DaggerfallActorIdentity.PlayerEntityId, 5, step, Expired: false)], 5, facts);
            return Delivered(facts);
        }

        private static IReadOnlyList<IProductFact> Delivered(FactBuffer<IProductFact> facts)
        {
            List<IProductFact> collected = [];
            facts.Deliver(collected.Add);
            return collected;
        }

        public void Dispose() => _actors.Dispose();

        private MechanicsEquipmentCoordinator BuildEquipment(EntityId owner, Actor actor, out MechanicsInventoryCoordinator inventory)
        {
            InventoryStore world = new();
            world.RegisterInventory(new InventoryState(owner));
            world.RegisterEquipment(new EquipmentState(owner));
            InventoryComponent inventoryComponent = new(world, owner);
            EquipmentComponent equipment = new(world, owner);
            actor.Add(inventoryComponent);
            actor.Add(equipment);
            var items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values).ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            var slots = Definitions.EquipmentSlots.Values.ToDictionary(slot => new SlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
            inventory = new MechanicsInventoryCoordinator(inventoryComponent, _actors.Entities, items);
            return new MechanicsEquipmentCoordinator(inventoryComponent, equipment, _actors.Entities, items, slots);
        }
    }

    private class ScriptedRandom : DispatchProxy
    {
        private readonly List<int> _values = [];
        private int _next;

        internal void Feed(IEnumerable<int> values) => _values.AddRange(values);

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            if (_next >= _values.Count) throw new InvalidOperationException($"The scripted attack reached draw {_next + 1} with no scripted value.");
            int value = _values[_next++];
            if (value < request.Minimum || value > request.Maximum)
                throw new InvalidOperationException($"Scripted draw {value} is outside [{request.Minimum}, {request.Maximum}].");
            return new KeyedRngReceipt(value);
        }
    }
}
