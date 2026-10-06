using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallHitConsequencesTests
{
    [Theory]
    [InlineData(0, 5, true, (int)DaggerfallMonsterHitConsequence.RatDisease)]
    [InlineData(0, 6, true, (int)DaggerfallMonsterHitConsequence.None)]
    [InlineData(3, 2, true, (int)DaggerfallMonsterHitConsequence.RatDisease)]
    [InlineData(3, 3, true, (int)DaggerfallMonsterHitConsequence.None)]
    [InlineData(17, 2, true, (int)DaggerfallMonsterHitConsequence.UndeadDisease)]
    [InlineData(17, 3, true, (int)DaggerfallMonsterHitConsequence.None)]
    [InlineData(19, 5, true, (int)DaggerfallMonsterHitConsequence.UndeadDisease)]
    [InlineData(19, 6, true, (int)DaggerfallMonsterHitConsequence.None)]
    [InlineData(9, 6000, true, (int)DaggerfallMonsterHitConsequence.Werewolf)]
    [InlineData(9, 6001, true, (int)DaggerfallMonsterHitConsequence.None)]
    [InlineData(14, 0, true, (int)DaggerfallMonsterHitConsequence.Wereboar)]
    [InlineData(14, 6000, false, (int)DaggerfallMonsterHitConsequence.None)]
    [InlineData(28, 6000, true, (int)DaggerfallMonsterHitConsequence.Vampire)]
    [InlineData(28, 6001, true, (int)DaggerfallMonsterHitConsequence.Plague)]
    [InlineData(30, 6000, false, (int)DaggerfallMonsterHitConsequence.Plague)]
    [InlineData(30, 20000, true, (int)DaggerfallMonsterHitConsequence.Plague)]
    [InlineData(30, 20001, true, (int)DaggerfallMonsterHitConsequence.None)]
    public void Natural_vectors_keep_inclusive_donor_bounds_and_vampire_single_draw(int mobile, int roll, bool player, int expectedValue)
    {
        int draws = 0;
        var selected = DaggerfallMonsterHitPolicy.Select(mobile, player, (low, high) => { draws++; Assert.InRange(roll, low, high); return roll; });
        Assert.Equal((DaggerfallMonsterHitConsequence)expectedValue, selected);
        Assert.Equal(1, draws);
    }

    [Theory]
    [InlineData(9, (int)DaggerfallInfectionKind.Werewolf)]
    [InlineData(14, (int)DaggerfallInfectionKind.Wereboar)]
    [InlineData(28, (int)DaggerfallInfectionKind.Vampire)]
    [InlineData(30, (int)DaggerfallInfectionKind.Vampire)]
    public void Real_natural_impacts_infect_once_and_source_retirement_and_save_do_not_cure(int mobile, int expectedKind)
    {
        using ConditionSessionFixture f = new();
        var s = f.Session;
        long enemy = Spawn(s, mobile);
        var facts = Facts(s);
        Assert.True(s.State.Kit.AttackExecution.Start(new(enemy, 1, 7, 13, .125, true), facts));
        Assert.Empty(s.State.Effects.Active);
        Impact(s, enemy);
        var infection = Assert.Single(s.State.Effects.Active);
        var state = DaggerfallTransformationInfectionPolicy.Read(infection);
        Assert.Equal((DaggerfallInfectionKind)expectedKind, state.Kind);
        Assert.Equal(enemy, state.OriginActorId);
        Assert.Null(infection.Context.Caster);
        Impact(s, enemy);
        Assert.Single(s.State.Effects.Active);
        s.BanishActor(enemy);
        Assert.Single(s.State.Effects.Active);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(state, DaggerfallTransformationInfectionPolicy.Read(Assert.Single(restored.State.Effects.Active)));
    }

    [Theory]
    [InlineData(0)] [InlineData(3)] [InlineData(17)] [InlineData(19)]
    public void Real_disease_hit_vectors_use_resistance_and_survive_source_retirement(int mobile)
    {
        var random = HitRandom.Create();
        using ConditionSessionFixture f = new(random: random);
        var s = f.Session;
        s.State.Actors.Player.Progression.AdvanceTo(0, 5);
        long enemy = Spawn(s, mobile);
        Assert.True(s.State.Kit.AttackExecution.Start(new(enemy, 1, 7, 13, .125, true), Facts(s)));
        Impact(s, enemy);
        Assert.NotEmpty(s.State.Effects.Active);
        Assert.All(s.State.Effects.Active, effect => { Assert.StartsWith("disease-", effect.Definition.Key); Assert.Null(effect.Context.Caster); });
        int count = s.State.Effects.Active.Count;
        Impact(s, enemy);
        Assert.Equal(count, s.State.Effects.Active.Count);
        s.BanishActor(enemy);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(count, restored.State.Effects.Active.Count);
    }

    [Theory]
    [InlineData(6)] [InlineData(20)]
    public void Real_spider_touch_uses_stock_identity66_and_common_casting_defenses(int mobile)
    {
        using ConditionSessionFixture f = new(random: HitRandom.Create());
        var s = f.Session;
        long enemy = Spawn(s, mobile);
        double magicka = s.State.Actors.Get(enemy).Stats.GetTrack(TrackId.Parse("magicka")).Current;
        Assert.True(s.State.Kit.AttackExecution.Start(new(enemy, 1, 7, 13, .125, true), Facts(s)));
        Impact(s, enemy);
        var paralysis = Assert.Single(s.State.Effects.Active);
        Assert.Equal("paralyze", paralysis.Definition.Key);
        Assert.Equal("spell.spell.062", paralysis.Context.Source.Key);
        Assert.True(s.State.Effects.ControlsFor(1).PhysicalAttacks);
        Assert.Equal(magicka, s.State.Actors.Get(enemy).Stats.GetTrack(TrackId.Parse("magicka")).Current);
        int sequence = checked((int)s.Casting.NextSequence);
        s.State.Kit.AttackExecution.Start(new(enemy, 1, 7, 30, .125, true), Facts(s));
        s.State.Kit.AttackExecution.ApplyImpacts([new(enemy, 1, 7, 30, false)], 7, Facts(s));
        Assert.Equal(sequence, s.Casting.NextSequence);
        Assert.Single(s.State.Effects.Active);
        s.State.Effects.Cancel(paralysis.Context.Instance);
        s.State.Actors.Player.Stats.GetStat(StatId.Parse("immunity-paralysis")).BaseValue = 1;
        var result = s.Casting.TriggerMonsterParalysis(enemy, 1);
        Assert.Equal(DaggerfallCastOutcome.Immune, Assert.Single(result.Bundle!.Results).Outcome);
        Assert.Empty(s.State.Effects.Active);
    }

    [Fact]
    public void Delayed_poison_spends_the_admitted_durable_weapon_even_after_swap_and_health_absorption()
    {
        using ConditionSessionFixture f = new();
        var s = f.Session;
        var equipment = s.State.Kit.Equipment;
        Assert.True(equipment.Read().TryGet(new("right-hand"), out var weapon));
        ulong id = equipment.GetDurableItemId(new EntityId(weapon.EntityId)).Value;
        s.State.ItemInstances.ReplaceUnique(id, s.State.ItemInstances.RequireUnique(id) with { PoisonVariant = 128 });
        s.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 80;
        long target = Spawn(s, 0);
        var shield = new AbsorbHealth();
        s.State.Actors.Get(target).Actor.Get<CombatContributions>().Rules.Add(shield);
        Assert.True(s.State.Kit.AttackExecution.Start(new(1, target, 7, 13, .125, true), Facts(s)));
        var prepared = Assert.IsType<DaggerCombatRules.DaggerfallPreparedAttack>(s.State.Actors.Player.Actor.Get<AttackState>().Pending!.Value.Attack);
        Assert.Equal(id, prepared.WeaponPoison!.ItemId);
        Assert.True(prepared.Outcome.Damage > 0);
        equipment.Unequip(weapon);
        var spare = equipment.Materialize(new(DurableIdentityKind.Item, 9001), new InventoryItemId("iron-longsword"));
        s.State.ItemInstances.RegisterUnique(9001, DaggerfallItemInstanceMetadata.Default(f.Definitions.RequireItem(new("iron-longsword")), DaggerfallItemOwner.Player) with { PoisonVariant = 131 });
        equipment.Equip(spare, [new("right-hand")]);
        double health = s.State.Actors.Get(target).Stats.GetTrack(TrackId.Parse("health")).Current;
        s.State.Kit.AttackExecution.ApplyImpacts([new(1, target, 7, 13, false), new(1, target, 7, 13, false)], 7, Facts(s));
        Assert.Null(s.State.ItemInstances.RequireUnique(id).PoisonVariant);
        Assert.Equal(131, s.State.ItemInstances.RequireUnique(9001).PoisonVariant);
        Assert.Equal(health, s.State.Actors.Get(target).Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Theory]
    [InlineData("miss")] [InlineData("zero")] [InlineData("material-refusal")]
    public void Refused_missed_and_zero_weapon_hits_keep_their_coating(string refusal)
    {
        using ConditionSessionFixture f = new();
        var s = f.Session;
        var equipment = s.State.Kit.Equipment;
        Assert.True(equipment.Read().TryGet(new("right-hand"), out var weapon));
        ulong id = equipment.GetDurableItemId(new EntityId(weapon.EntityId)).Value;
        s.State.ItemInstances.ReplaceUnique(id, s.State.ItemInstances.RequireUnique(id) with { PoisonVariant = 128 });
        s.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 80;
        long target = Spawn(s, 0);
        s.State.Actors.Player.Actor.Get<CombatContributions>().Rules.Add(new RefusePhysical(refusal));
        Assert.True(s.State.Kit.AttackExecution.Start(new(1, target, 7, 13, .125, true), Facts(s)));
        s.State.Kit.AttackExecution.ApplyImpacts([new(1, target, 7, 13, false)], 7, Facts(s));
        Assert.Equal(128, s.State.ItemInstances.RequireUnique(id).PoisonVariant);
    }

    [Fact]
    public void Each_natural_slot_uses_raw_damage_for_fatigue_before_a_lethal_aggregate_hit()
    {
        using DaggerCombatFixture f = new("nymph", playerHealth: 1, playerStamina: 1000);
        var request = new AttackRequest(2, 1, 7, 13, .125, true);
        var prepared = new DaggerCombatRules.DaggerfallPreparedAttack(.5,
            new(true, true, 5), DaggerfallStrikeFeedback.Unarmed,
            MonsterHits: [new(0, 2, DaggerfallMonsterHitConsequence.Fatigue), new(1, 3, DaggerfallMonsterHitConsequence.Fatigue)],
            Detail: new(0, 1, 100));
        f.Rules.Apply(request, prepared, f.Facts);
        var facts = f.Deliver();
        Assert.Equal(new[] { 256, 384 }, facts.OfType<FatigueAppliedFact>().Select(value => value.CalculatedFatigueLoss));
        Assert.Equal(360, f.Stamina);
        Assert.Equal(0, f.Health);
        Assert.IsType<FatigueAppliedFact>(facts[0]);
        Assert.IsType<FatigueAppliedFact>(facts[1]);
        Assert.Single(facts.OfType<ActorDiedFact>());
    }

    [Theory]
    [InlineData(7, 2, 5, true)]
    [InlineData(8, 2, 5, true)]
    [InlineData(12, 2, 5, true)]
    [InlineData(7, 1, 1, false)]
    [InlineData(8, 2, 6, false)]
    [InlineData(144, 1, 1, false)]
    [InlineData(144, 2, 5, true)]
    [InlineData(144, 2, 6, false)]
    [InlineData(139, 2, 60, true)]
    [InlineData(139, 2, 61, false)]
    public void Actual_equipped_spawn_has_ordinary_and_assassin_coatings_and_saves_one_dose(int mobile, int level, int chanceRoll, bool poisoned)
    {
        using ConditionSessionFixture f = new(random: HitRandom.Create(chanceRoll));
        var s = f.Session;
        s.State.Actors.Player.Progression.AdvanceTo(0, level);
        string definition = f.Definitions.Actors.Values.Single(value => value.MobileId == mobile).Id.Value;
        long actor = s.SpawnActor(definition, new(new(10, 0, 10), 0));
        var equipment = s.State.ActorInventories.EquipmentFor(actor);
        Assert.True(equipment.Read().TryGet(new("right-hand"), out var weapon));
        ulong id = equipment.GetDurableItemId(new EntityId(weapon.EntityId)).Value;
        Assert.Equal(poisoned ? 135 : (int?)null, s.State.ItemInstances.RequireUnique(id).PoisonVariant);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(s.State.ItemInstances.RequireUnique(id).PoisonVariant, restored.State.ItemInstances.RequireUnique(id).PoisonVariant);
        if (poisoned)
        {
            Assert.True(restored.State.Kit.AttackExecution.Start(new(actor, 1, 7, 13, .125, true), Facts(restored)));
            var prepared = Assert.IsType<DaggerCombatRules.DaggerfallPreparedAttack>(restored.State.Actors.Get(actor).Actor.Get<AttackState>().Pending!.Value.Attack);
            Assert.Equal(new DaggerfallWeaponPoisonSource(id, 135), prepared.WeaponPoison);
            Assert.True(prepared.Outcome.Damage > 0);
            Impact(restored, actor);
            Assert.Null(restored.State.ItemInstances.RequireUnique(id).PoisonVariant);
        }
    }

    [Fact]
    public void Accepted_weapon_poison_survives_source_weapon_and_actor_retirement_and_save()
    {
        using ConditionSessionFixture f = new(random: HitRandom.Create());
        var s = f.Session;
        s.State.Progression.AdvanceTo(0, 2);
        long actor = Spawn(s, 7);
        var equipment = s.State.ActorInventories.EquipmentFor(actor);
        Assert.True(equipment.Read().TryGet(new("right-hand"), out var weapon));
        ulong id = equipment.GetDurableItemId(new EntityId(weapon.EntityId)).Value;
        s.State.Actors.Player.Actor.Get<CombatContributions>().Rules.Add(new AbsorbHealth());
        Assert.True(s.State.Kit.AttackExecution.Start(new(actor, 1, 7, 13, .125, true), Facts(s)));
        Impact(s, actor);
        Assert.True(s.State.Poisons.IsAfflicted(s.State.Actors.Player.Actor));
        Assert.Null(s.State.ItemInstances.RequireUnique(id).PoisonVariant);
        var poison = Assert.Single(s.State.Effects.Active, effect => effect.Context.Source.Key == "poison");
        Assert.Null(poison.Context.Item);
        s.BanishActor(actor);
        Assert.False(s.State.ItemInstances.ContainsUnique(id));
        Assert.True(s.State.Poisons.IsAfflicted(s.State.Actors.Player.Actor));
        using var restored = f.Restore(s.CaptureSave());
        Assert.True(restored.State.Poisons.IsAfflicted(restored.State.Actors.Player.Actor));
    }

    [Fact]
    public void New_game_authored_Orc_has_a_saved_weapon_without_a_level_one_coating()
    {
        using ConditionSessionFixture f = new(random: HitRandom.Create());
        var equipment = f.Session.State.ActorInventories.EquipmentFor(2003);
        Assert.True(equipment.Read().TryGet(new("right-hand"), out var weapon));
        ulong id = equipment.GetDurableItemId(new EntityId(weapon.EntityId)).Value;
        Assert.Null(f.Session.State.ItemInstances.RequireUnique(id).PoisonVariant);
        using var restored = f.Restore(f.Session.CaptureSave());
        var restoredEquipment = restored.State.ActorInventories.EquipmentFor(2003);
        Assert.True(restoredEquipment.Read().TryGet(new("right-hand"), out var restoredWeapon));
        Assert.Equal(id, restoredEquipment.GetDurableItemId(new EntityId(restoredWeapon.EntityId)).Value);
        Assert.Null(restored.State.ItemInstances.RequireUnique(id).PoisonVariant);
    }

    [Fact]
    public void Authored_Privateers_Hold_Orc_generates_and_spends_one_coating_on_first_visit_then_restores_it()
    {
        string root = TestData.RepositoryRoot;
        var definitions = TestPayload.Definitions;
        var hold = TestSessions.ReadInputs(root);
        var castle = DaggerfallSiteContent.Read(TestSessions.FullContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        List<string> releases = [];
        ContentFake content = new(releases);
        TestSessions.PopulateContent(content, hold); TestSessions.PopulateContent(content, castle);
        var spatial = SpatialFake.Create(castle.SpatialArtifact.Sha256, releases);
        var random = HitRandom.Create();
        var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: random);
        var composition = new DaggerfallSessionComposition(definitions, castle, DaggerfallTuning.Defaults)
            { Profiles = new DaggerfallSiteProfiles([castle, hold]) };
        using var s = DaggerfallSession.StartNew(engine.Context, composition);
        s.State.Progression.AdvanceTo(0, 2);
        Assert.True(s.TryTransitionTo(hold.ProfileKey));
        const long orc = 2003;
        var equipment = s.State.ActorInventories.EquipmentFor(orc);
        Assert.True(equipment.Read().TryGet(new("right-hand"), out var weapon));
        ulong id = equipment.GetDurableItemId(new EntityId(weapon.EntityId)).Value;
        Assert.Equal(135, s.State.ItemInstances.RequireUnique(id).PoisonVariant);
        s.State.Actors.Player.Actor.Get<CombatContributions>().Rules.Add(new AbsorbHealth());
        Assert.True(s.State.Kit.AttackExecution.Start(new(orc, 1, 7, 13, .125, true), Facts(s)));
        var attack = Assert.IsType<DaggerCombatRules.DaggerfallPreparedAttack>(s.State.Actors.Get(orc).Actor.Get<AttackState>().Pending!.Value.Attack);
        Assert.Equal(new DaggerfallWeaponPoisonSource(id, 135), attack.WeaponPoison);
        Impact(s, orc);
        Assert.Null(s.State.ItemInstances.RequireUnique(id).PoisonVariant);
        using var restored = DaggerfallSession.Restore(engine.Context, composition, s.CaptureSave());
        Assert.True(restored.TryTransitionTo(castle.ProfileKey));
        Assert.True(restored.TryTransitionTo(hold.ProfileKey));
        var restoredEquipment = restored.State.ActorInventories.EquipmentFor(orc);
        Assert.True(restoredEquipment.Read().TryGet(new("right-hand"), out var restoredWeapon));
        Assert.Equal(id, restoredEquipment.GetDurableItemId(new EntityId(restoredWeapon.EntityId)).Value);
        Assert.Null(restored.State.ItemInstances.RequireUnique(id).PoisonVariant);
    }

    [Fact]
    public void Generated_coating_includes_the_first_classic_poison_variant()
    {
        using ConditionSessionFixture f = new(random: HitRandom.Create(poisonVariant: 128));
        f.Session.State.Progression.AdvanceTo(0, 2);
        long actor = Spawn(f.Session, 7);
        var equipment = f.Session.State.ActorInventories.EquipmentFor(actor);
        Assert.True(equipment.Read().TryGet(new("right-hand"), out var weapon));
        ulong id = equipment.GetDurableItemId(new EntityId(weapon.EntityId)).Value;
        Assert.Equal(128, f.Session.State.ItemInstances.RequireUnique(id).PoisonVariant);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(20)]
    public void Actual_player_poison_hit_uses_spawned_class_level_before_and_after_save(int level)
    {
        using ConditionSessionFixture f = new(random: HitRandom.Create());
        var s = f.Session;
        s.State.Progression.AdvanceTo(0, level);
        string definition = f.Definitions.Actors.Values.Single(value => value.MobileId == 144).Id.Value;
        long target = s.SpawnActor(definition, new(new(10, 0, 10), 0), level);
        s.State.Actors.Get(target).Actor.Get<CombatContributions>().Rules.Add(new AbsorbHealth());
        s.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 80;
        var equipment = s.State.Kit.Equipment;
        Assert.True(equipment.Read().TryGet(new("right-hand"), out var weapon));
        ulong id = equipment.GetDurableItemId(new EntityId(weapon.EntityId)).Value;
        void Strike(DaggerfallSession session, ulong step)
        {
            session.State.ItemInstances.ReplaceUnique(id, session.State.ItemInstances.RequireUnique(id) with { PoisonVariant = 128 });
            Assert.True(session.State.Kit.AttackExecution.Start(new(1, target, 7, step, .125, true), Facts(session)));
            session.State.Kit.AttackExecution.ApplyImpacts([new(1, target, 7, step, false)], 7, Facts(session));
            Assert.Null(session.State.ItemInstances.RequireUnique(id).PoisonVariant);
            Assert.Equal(level > 1, session.State.Poisons.IsAfflicted(session.State.Actors.Get(target).Actor));
        }
        Strike(s, 13);
        var save = s.CaptureSave();
        Assert.Equal(level, Assert.Single(DaggerfallSavePayload.Read(save).DynamicActors, actor => actor.EntityId == target).Level);
        using var restored = f.Restore(save);
        Assert.Equal(level, restored.DefinitionsByActor[target].Level);
        restored.State.Actors.Get(target).Actor.Get<CombatContributions>().Rules.Add(new AbsorbHealth());
        Strike(restored, 30);
    }

    [Fact]
    public void Zero_damage_natural_slots_still_draw_reflex_before_short_circuiting()
    {
        var random = HitRandom.Create();
        using ConditionSessionFixture f = new(random: random);
        long rat = Spawn(f.Session, 0);
        var proxy = (HitRandom)(object)random;
        proxy.Requests.Clear();
        Assert.True(f.Session.State.Kit.AttackExecution.Start(new(rat, 1, 7, 13, .125, true), Facts(f.Session)));
        foreach (int salt in Enumerable.Range(CombatRandomKey.MonsterReflexSaltBase, 3))
            Assert.Contains(proxy.Requests, request => request.Key == CombatRandomKey.For(7, 13, rat, 1, salt));
        foreach (int slot in new[] { 1, 2 })
            Assert.DoesNotContain(proxy.Requests, request => request.Key == CombatRandomKey.For(7, 13, rat, 1, CombatRandomKey.MonsterDamageSaltBase + slot));
    }

    private static long Spawn(DaggerfallSession s, int mobile) => s.SpawnActor(TestPayload.Definitions.Mobiles.Mobiles[mobile].Actor!, new(new(10, 0, 10), 0));
    private static FactBuffer<IProductFact> Facts(DaggerfallSession s) => (FactBuffer<IProductFact>)typeof(DaggerfallSession).GetField("_facts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
    private static void Impact(DaggerfallSession s, long enemy) => s.State.Kit.AttackExecution.ApplyImpacts([new(enemy, 1, 7, 13, false)], 7, Facts(s));
    private sealed class RefusePhysical(string reason) : ICombatContribution
    {
        public void Hit(TryHitEvent value) { if (reason == "miss") value.Hit = false; }
        public void Damage(DamageEvent value)
        {
            if (reason == "zero") value.Damage = 0;
            if (reason == "material-refusal") value.Allowed = false;
        }
    }
    private sealed class AbsorbHealth : ICombatContribution { public void Applying(ApplyHitEvent value) => value.Damage = 0; }
    public class HitRandom : DispatchProxy
    {
        private int _poisonRoll;
        private int _poisonVariant;
        internal List<KeyedRngRequest> Requests { get; } = [];
        internal static IRandomService Create(int poisonRoll = 1, int poisonVariant = 135)
        {
            var service = Create<IRandomService, HitRandom>();
            ((HitRandom)(object)service)._poisonRoll = poisonRoll;
            ((HitRandom)(object)service)._poisonVariant = poisonVariant;
            return service;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException();
            var request = (KeyedRngRequest)args![0]!;
            Requests.Add(request);
            long roll = request.Minimum;
            if (request.Key.EndsWith("poison-chance")) roll = _poisonRoll;
            else if (request.Key.EndsWith("poison-variant")) roll = _poisonVariant;
            else if (request.Scope == DaggerfallPoisonRandomKey.Scope || request.Scope == "dagger.disease.v1" && request.Key.StartsWith("resist:")
                || request.Scope == "daggerfall.casting.v1" && request.Key.EndsWith(":draw:2")) roll = request.Maximum;
            return new KeyedRngReceipt(Math.Clamp(roll, request.Minimum, request.Maximum));
        }
    }
}
