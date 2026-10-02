using System.Text.Json;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class ItemSoulEffectsSessionTests
{
    [Fact]
    public void Full_inventory_refuses_creation_without_item_or_identity_and_keeps_paid_selection_for_retry()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var coins = InventoryStackId.Parse("test.create.capacity");
        ulong amount = checked((ulong)s.State.Encumbrance.Read().RemainingClassicUnits);
        s.State.Inventory.Grant(new(new("gold-piece"), coins, amount));
        s.State.ItemInstances.RegisterDefaultStack(DaggerfallItemOwner.Player,
            s.State.Inventory.Read().Stacks.Single(value => value.Id == coins), TestPayload.Definitions.RequireItem(new("gold-piece")));
        Create(s, "full");
        var before = DaggerfallSavePayload.Read(s.CaptureSave());
        s.ChooseCreateItem("full", "weapon-113");
        Assert.NotNull(s.CreateItemView);
        Assert.Contains("cannot carry", s.Presentation.LastOutcome);
        Assert.DoesNotContain(s.State.ItemInstances.UniqueItems, value => value.Value.Conjuration is not null);
        var after = DaggerfallSavePayload.Read(s.CaptureSave());
        Assert.Equal(JsonSerializer.Serialize(before.Identities, DaggerfallSaveJsonContext.Default.DurableIdentityState),
            JsonSerializer.Serialize(after.Identities, DaggerfallSaveJsonContext.Default.DurableIdentityState));
        s.State.Inventory.Consume(new(coins, amount)); s.State.ItemInstances.RemoveStack(DaggerfallItemOwner.Player, coins);
        s.ChooseCreateItem("full", "weapon-113");
        Assert.Null(s.CreateItemView);
        Assert.Single(s.State.ItemInstances.UniqueItems, value => value.Value.Conjuration is not null);
    }

    [Fact]
    public void Soul_trap_zero_chance_attaches_through_real_delivery_and_humanoid_admission_does_not_attach()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        long target = s.SpawnActor("rat", new(new(1, 0, 1), 0));
        var spell = new DaggerfallSpellDefinition("test.trap", 1, false, "Soul Trap", 4, 1, 0, 0, [Setting(12, 0)]);
        var magicka = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); magicka.Maximum.BaseValue = 10000; magicka.SetCurrent(10000);
        var casting = new DaggerfallCasting(TestPayload.Definitions.Magic with { Spells = new Dictionary<string, DaggerfallSpellDefinition> { [spell.Key] = spell } }, s.State.Effects,
            id => id == 1 ? s.State.Actors.Player.Actor : s.State.Actors.TryGet(id, out var actor) ? actor.Actor : null,
            s.MagicProfile, _ => true, _ => { }, _ => { }, RandomMaximum.Create(), 1, playerKnowsSpell: _ => true);
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
        var bundle = casting.Release(1, true).Bundle!; casting.Deliver(bundle, [target]);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        Assert.Single(s.State.Effects.Active, effect => effect.Definition.Key == "soul-trap");
        long humanoid = s.SpawnActor(TestPayload.Definitions.Actors.Values.First(value => value.Kind == DaggerfallActorKinds.EnemyClass).Id.Value, new(new(2, 0, 2), 0));
        Trap(s, humanoid, 100);
        Assert.DoesNotContain(s.State.Effects.Active, effect => effect.Context.Target.Value == (ulong)humanoid);
    }
    [Fact]
    public void Conjured_item_transferred_to_inactive_site_expires_on_canonical_materialization_before_save_or_presentation()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        Create(s, "transfer"); s.ChooseCreateItem("transfer", "weapon-113");
        var item = Assert.Single(s.State.ItemInstances.UniqueItems, item => item.Value.Conjuration is not null);
        var live = s.State.Inventory.Read().UniqueItems.Single(value => s.State.Inventory.GetDurableItemId(value.Entity).Value == item.Key);
        s.State.Containers.Transfer(s.State.Actors.Player.Actor.Entity, s.State.Actors.Get(f.Enemy).Actor.Entity,
            new(new(live.Definition.Value), 1, UniqueEntityId: live.Entity.Value));
        s.State.ItemInstances.MoveUnique(item.Key, DaggerfallItemOwner.Actor(f.Enemy));
        var profiles = new DaggerfallSiteProfiles([f.Inputs, f.Castle]); s.AdmitSiteProfiles(profiles);
        Assert.True(s.TryTransitionTo(f.Castle.ProfileKey)); s.AdvanceElapsedTime(10 * 60);
        using var restored = f.Restore(profiles);
        Assert.True(restored.TryTransitionTo(f.Inputs.ProfileKey));
        Assert.False(restored.State.ItemInstances.ContainsUnique(item.Key));
        Assert.DoesNotContain(DaggerfallSavePayload.Read(restored.CaptureSave()).ActorInventories.SelectMany(value => value.Inventory.UniqueItems), value => value.EntityId == item.Key);
    }

    [Fact]
    public void A_lethal_real_melee_strike_reports_capture_and_creates_one_ordinary_death_and_reward()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        Trap(s, f.Enemy, 100); AddGem(s, f, "melee-gem");
        var health = s.State.Actors.Get(f.Enemy).Stats.GetTrack(TrackId.Parse("health")); health.SetCurrent(1);
        s.State.Kit.Rules.RegisterAction(s.DefinitionsByActor[1].ActionId!, new LethalStrike());
        s.ResolveExplicitMelee(new(1, f.Enemy, 1, 10000, .125)); f.Update();
        Assert.True(s.State.Actors.Get(f.Enemy).IsDefeated);
        Assert.DoesNotContain(s.State.Effects.Active, effect => effect.Definition.Key == "soul-trap");
        Assert.Contains("Soul trapped", s.Presentation.LastOutcome);
        Assert.Single(s.State.ItemInstances.UniqueItems, value => value.Value.CapturedSoulMobileId == 7);
        var saved = DaggerfallSavePayload.Read(s.CaptureSave()); Assert.Single(saved.Corpses, corpse => corpse.ActorId == f.Enemy);
        Assert.DoesNotContain(saved.ActiveEffects, effect => effect.EffectKey == "soul-trap");
        using var restored = f.Restore();
        Assert.DoesNotContain(restored.State.Effects.Active, effect => effect.Definition.Key == "soul-trap");
    }

    private sealed class LethalStrike : ICombatContribution
    {
        public void Hit(TryHitEvent hit) { hit.Hit = true; hit.Chance = 100; hit.Roll = 1; }
        public void Damage(DamageEvent damage) { damage.Damage = 1000; }
    }
    [Fact]
    public void Real_casting_pays_once_and_keeps_each_mandatory_selection_in_the_current_save()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        var spell = new DaggerfallSpellDefinition("test.create", 1, false, "Create Item", 4, 0, 0, 0, [Setting(2), Setting(2)]);
        var magicka = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); magicka.Maximum.BaseValue = 10000; magicka.SetCurrent(10000);
        var casting = new DaggerfallCasting(TestPayload.Definitions.Magic with { Spells = new Dictionary<string, DaggerfallSpellDefinition> { [spell.Key] = spell } }, s.State.Effects,
            id => id == 1 ? s.State.Actors.Player.Actor : null, s.MagicProfile, _ => true, _ => { }, _ => { }, RandomMaximum.Create(), 1, playerKnowsSpell: _ => true);
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
        var bundle = casting.Release(1, true).Bundle!; double paid = magicka.Current;
        casting.Deliver(bundle, [1]); Assert.NotNull(s.CreateItemView);
        Assert.Equal(10000 - bundle.Cost, paid);
        using var restored = f.Restore();
        restored.ChooseCreateItem(restored.CreateItemView!.Revision, "steel-113"); // invalid choice does not consume it
        Assert.NotNull(restored.CreateItemView);
        restored.ChooseCreateItem(restored.CreateItemView!.Revision, "weapon-113"); Assert.NotNull(restored.CreateItemView);
        restored.ChooseCreateItem(restored.CreateItemView!.Revision, "robes"); Assert.Null(restored.CreateItemView);
        Assert.Equal(paid, restored.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).Current);
    }

    [Fact]
    public void Soul_trap_runs_after_shield_and_extends_its_original_chance_through_save_and_restore()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        long target = s.SpawnActor("rat", new(new(1, 0, 1), 0)); Trap(s, target, 100);
        var actor = s.State.Actors.Get(target).Actor; var health = actor.Get<StatsComponent>().GetTrack(TrackId.Parse("health")); health.Maximum.BaseValue = 10; health.SetCurrent(10);
        s.State.Effects.Start(new("shield", "shield", "spell.shield", 1, target, "shield", "Magic", null, 1, 20,
            DaggerfallAlterationEffects.ShieldState(new(50, 50))));
        var shielded = new CombatResolution().ApplyToHealth(new(s.State.Actors.Player.Actor, actor, "test"), 20, 0, health).Result;
        Assert.False(shielded.Defeated); Assert.Equal(10d, health.Current);
        var trap = s.State.Effects.Active.Single(value => value.Definition.Key == "soul-trap");
        Assert.Equal(0, trap.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallSoulTrapState)!.Attempts);
        s.State.Effects.Start(new("extend", "soul-trap", "spell.other", 1, target, "soul-trap", "Magic", null, 1, 7,
            JsonSerializer.SerializeToElement(new DaggerfallSoulTrapState(new(Setting(12, 0), 1, 0, 100)), DaggerfallSaveJsonContext.Default.DaggerfallSoulTrapState)));
        Assert.Equal(16u, trap.Lifecycle.RemainingRounds);
        using var restored = f.Restore(); actor = restored.State.Actors.Get(target).Actor; health = actor.Get<StatsComponent>().GetTrack(TrackId.Parse("health"));
        var tethered = new CombatResolution().ApplyToHealth(new(restored.State.Actors.Player.Actor, actor, "terminal"), 100, 0, health, HealthApplicationMode.Terminal).Result;
        Assert.False(tethered.Defeated); Assert.Equal(1d, health.Current);
        var savedTrap = DaggerfallSavePayload.Read(restored.CaptureSave()).ActiveEffects.Single(value => value.EffectKey == "soul-trap");
        Assert.Equal(1, savedTrap.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallSoulTrapState)!.Attempts);
    }
    public static IEnumerable<object[]> CreateItemChoiceIndexes() => Enumerable.Range(0, 29).Select(index => new object[] { index });

    [Theory]
    [MemberData(nameof(CreateItemChoiceIndexes))]
    public void All_29_paid_choices_materialize_normal_items_with_transferable_lifetime_and_arrow_quantity(int index)
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        Create(s, $"create-{index}");
        var view = s.CreateItemView!; Assert.Equal(29, view.Options.Count);
        s.ChooseCreateItem("stale", view.Options[index].Id); Assert.NotNull(s.CreateItemView);
        f.Submit(new { action = "create-item-select", revision = view.Revision, key = view.Options[index].Id });
        Assert.Null(s.CreateItemView);
        if (index == 26)
        {
            var arrow = Assert.Single(s.State.Inventory.Read().Stacks, stack => stack.Id.Value == $"daggerfall.conjured.create-{index}");
            Assert.InRange(arrow.Quantity, 1UL, 20UL);
            Assert.NotNull(s.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player, arrow.Id).Conjuration);
        }
        else Assert.Single(s.State.ItemInstances.UniqueItems, item => item.Value.Conjuration?.Source == $"create-{index}");
        using var restored = f.Restore();
        Assert.Equal(index == 26 ? 0 : 1, restored.State.ItemInstances.UniqueItems.Count(item => item.Value.Conjuration is not null));
        if (index == 26) Assert.Single(restored.State.ItemInstances.StackItems, item => item.Metadata.Conjuration is not null);
    }

    [Fact]
    public void Paid_mandatory_choice_restores_without_recasting_and_expiry_removes_equipped_and_stacked_items()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        Create(s, "pending");
        using var restored = f.Restore();
        Assert.Equal("pending", restored.CreateItemView!.Revision);
        restored.ChooseCreateItem("pending", "steel-102");
        var item = Assert.Single(restored.State.ItemInstances.UniqueItems.Where(item => item.Value.Conjuration is not null));
        var live = restored.State.Inventory.Read().UniqueItems.Single(value => restored.State.Inventory.GetDurableItemId(value.Entity).Value == item.Key);
        restored.EquipmentMoves.MoveToSlot(new(live.Entity.Value, new(live.Definition.Value)), new("chest"));
        Create(restored, "arrows"); restored.ChooseCreateItem("arrows", "weapon-131");
        restored.AdvanceElapsedTime(10 * 60);
        Assert.DoesNotContain(restored.State.ItemInstances.UniqueItems, entry => entry.Value.Conjuration is not null);
        Assert.DoesNotContain(restored.State.Inventory.Read().Stacks, entry => entry.Id.Value == "daggerfall.conjured.arrows");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Soul_trap_success_without_empty_gem_vetoes_damage_or_terminal_death_then_fills_real_gem(bool terminal)
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        long target = s.SpawnActor("rat", new(new(1, 0, 1), 0));
        Trap(s, target, 100);
        var actor = s.State.Actors.Get(target).Actor;
        var health = actor.Get<StatsComponent>().GetTrack(TrackId.Parse("health")); health.Maximum.BaseValue = 30; health.SetCurrent(30);
        var rules = new CombatResolution();
        var denied = rules.ApplyToHealth(new(s.State.Actors.Player.Actor, actor, "test"), 100, 0, health,
            terminal ? HealthApplicationMode.Terminal : HealthApplicationMode.Damage).Result;
        Assert.False(denied.Defeated); Assert.Equal(1d, health.Current);
        AddGem(s, f, "first");
        var accepted = rules.ApplyToHealth(new(s.State.Actors.Player.Actor, actor, "test"), 100, 0, health).Result;
        Assert.True(accepted.Defeated);
        var gem = Assert.Single(s.State.ItemInstances.UniqueItems.Where(value => value.Value.CapturedSoulMobileId == 0));
        Assert.True(s.SoulGems.Consume(0)); Assert.False(s.State.ItemInstances.ContainsUnique(gem.Key));
        Assert.False(s.SoulGems.Consume(0));
    }

    [Fact]
    public void Trap_always_attaches_but_failed_death_chance_allows_death_and_expiry_detaches_policy()
    {
        using var f = new SanguineRoseSessionTests.Fixture(); var s = f.Session;
        long target = s.SpawnActor("rat", new(new(1, 0, 1), 0));
        Trap(s, target, 0);
        var actor = s.State.Actors.Get(target).Actor; var health = actor.Get<StatsComponent>().GetTrack(TrackId.Parse("health"));
        Assert.True(new CombatResolution().ApplyToHealth(new(s.State.Actors.Player.Actor, actor, "test"), 10000, 0, health).Result.Defeated);
        long next = s.SpawnActor("rat", new(new(2, 0, 2), 0)); Trap(s, next, 100);
        s.State.Effects.AdvanceElapsedRounds(10);
        actor = s.State.Actors.Get(next).Actor; health = actor.Get<StatsComponent>().GetTrack(TrackId.Parse("health"));
        Assert.True(new CombatResolution().ApplyToHealth(new(s.State.Actors.Player.Actor, actor, "test"), 10000, 0, health).Result.Defeated);
    }

    [Fact]
    public void Star_is_filled_first_but_ordinary_gem_consumed_first_and_current_soul_state_restores()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: "magic-item.0009"); var s = f.Session;
        AddGem(s, f, "ordinary");
        Assert.True(s.SoulGems.Capture(0)); Assert.Equal(0, s.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.True(s.SoulGems.Capture(0)); Assert.False(s.SoulGems.Capture(1));
        using var restored = f.Restore();
        Assert.True(restored.SoulGems.Consume(0)); Assert.Equal(0, restored.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.True(restored.SoulGems.Consume(0)); Assert.Null(restored.State.ItemInstances.RequireUnique(f.Source).CapturedSoulMobileId);
        Assert.True(restored.SoulGems.Capture(1, starOnly: true));
    }

    private static DaggerfallSpellEffectDefinition Setting(int type, int chance = 0) => new("effect", type, -1, 10, 0, 1, chance, 0, 1, 0, 0, 0, 0, 1);
    private static void Create(DaggerfallSession session, string instance) => session.State.Effects.Start(new(instance, "create-item", "spell.create", 1, 1,
        "create-item", "Magic", null, 1, 10, JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(Setting(2), 1, 0, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)));
    private static void Trap(DaggerfallSession session, long target, int chance) => session.State.Effects.Start(new($"trap-{target}", "soul-trap", "spell.trap", 1, target,
        "soul-trap", "Magic", null, 1, 10, JsonSerializer.SerializeToElement(new DaggerfallSoulTrapState(new(Setting(12, chance), 1, 0, 100)), DaggerfallSaveJsonContext.Default.DaggerfallSoulTrapState)));
    private static void AddGem(DaggerfallSession session, SanguineRoseSessionTests.Fixture f, string key)
    {
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random);
        var item = factory.Create(new("MiscItems", key, DaggerfallItemOwner.Player, TemplateIndex: 274));
        factory.Materialize(item, session.State.Inventory, session.State.ItemInstances, unique: session.UniqueItemAllocator.AllocateReference());
    }
}
