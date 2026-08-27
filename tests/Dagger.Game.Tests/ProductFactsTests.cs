using Rusty.Engine;
using Xunit;
using RustyDagger.Game.Content;
using RustyDagger.Game.Daggerfall;
using RustyDagger.Game.Daggerfall.Content;
using RustyDagger.Game.Facts;
using RustyDagger.Game.Modules.Actors;
using RustyDagger.Game.Modules.Combat;
using RustyDagger.Game.Modules.Encounters;
using RustyDagger.Game.Modules.Equipment;
using RustyDagger.Game.Modules.Inventory;
using RustyDagger.Game.Modules.Loot;
using RustyDagger.Game.Modules.PlayerControl;
using RustyDagger.Game.Modules.Presentation;
using RustyDagger.Game.Modules.Progression;

namespace Dagger.Game.Tests;

public sealed class ProductFactsTests
{
    [Fact]
    public void LateUpdate_delivers_a_stable_buffer_and_defers_reaction_facts()
    {
        ProductFactBuffer buffer = new();
        List<string> delivered = [];
        buffer.Append(new ActorDiedFact(2000, 16, 7));

        buffer.Deliver(fact =>
        {
            delivered.Add(fact.GetType().Name);
            buffer.Append(new LootAwardedFact(2000, "gold-piece", 2, 7));
        });

        Assert.Equal(["ActorDiedFact"], delivered);
        buffer.Deliver(fact => delivered.Add(fact.GetType().Name));
        Assert.Equal(["ActorDiedFact", "LootAwardedFact"], delivered);
    }

    [Fact]
    public void Death_reactions_award_loot_and_experience_once()
    {
        DaggerfallState state = CreateSkeletalState();
        Assert.True(state.Actors.TryGet(2000, out ActorState actor));
        actor.ApplyDamage(1_000);
        InventoryState inventory = state.Inventory;
        ProgressionState progression = state.Progression;
        ProductFactBuffer facts = new();
        LootReaction loot = new(inventory, new MaximumRandom());
        ProgressionReaction experience = new(progression);
        ActorDiedFact death = new(actor.EntityId, 66, 7);

        loot.React(death, state, facts);
        experience.React(death, state, facts);
        loot.React(death, state, facts);
        experience.React(death, state, facts);

        Assert.Single(inventory.Items, item => item.ItemId == "gold-piece");
        Assert.Equal(DaggerfallDefinitions.SkeletalWarrior.ExperienceReward, progression.Experience);
    }

    [Fact]
    public void Keyed_rng_labels_are_step_scoped_and_stable()
    {
        Assert.Equal("step:7:hit:2000", CombatKeys.PlayerHit(7, 2000));
        Assert.Equal("step:7:damage:2000", CombatKeys.PlayerDamage(7, 2000));
        Assert.Equal("step:7:enemy-hit:2000", CombatKeys.EnemyHit(7, 2000));
        Assert.Equal("step:7:loot:2000:H", LootRandomKey.For(7, 2000, "H"));
    }

    [Fact]
    public void Death_presentation_preserves_damage_experience_and_loot_wording()
    {
        DaggerfallState state = CreateSkeletalState();
        PresentationState presentation = new();
        PresentationReaction reaction = new(presentation);

        reaction.React(new ActorDiedFact(2000, 16, 7), state);
        reaction.React(new LootAwardedFact(2000, "gold-piece", 10, 7), state);

        Assert.Equal("Defeated skeletal-warrior for 16 damage; gained 450 XP; looted 10 gold-piece", presentation.LastOutcome);
    }

    [Fact]
    public void Enemy_hit_publishes_accepted_nonfatal_player_damage_before_its_raw_damage_outcome()
    {
        DaggerfallState state = CreateSkeletalState();
        ProductFactBuffer facts = new();
        CombatModule combat = new(CombatTuning.Defaults);

        combat.TryEnemyMelee(state, new EnemyHitRandom(), facts);

        List<IProductFact> delivered = Deliver(facts);
        PlayerDamagedFact damage = Assert.IsType<PlayerDamagedFact>(delivered[0]);
        Assert.Equal(15, damage.Amount);
        Assert.IsType<AttackHitFact>(delivered[1]);
        Assert.DoesNotContain(delivered, fact => fact is PlayerDiedFact);
        Assert.Equal(70, state.Actors.Player.Health);
    }

    [Fact]
    public void Enemy_hit_publishes_one_player_death_with_the_final_applied_damage()
    {
        DaggerfallState state = CreateSkeletalState();
        ProductFactBuffer facts = new();
        CombatModule combat = new(CombatTuning.Defaults);
        EnemyHitRandom random = new();

        for (int hit = 0; hit < 6; hit++)
        {
            combat.TryEnemyMelee(state, random, facts);
            combat.AdvanceCooldowns(state.Actors, 2f);
        }

        List<IProductFact> delivered = Deliver(facts);
        Assert.Equal(0, state.Actors.Player.Health);
        Assert.Equal(6, delivered.OfType<PlayerDamagedFact>().Count());
        PlayerDiedFact death = Assert.Single(delivered.OfType<PlayerDiedFact>());
        Assert.Equal(10, death.AppliedDamage);
        Assert.Equal(6, delivered.OfType<AttackHitFact>().Count());
    }

    private static DaggerfallState CreateSkeletalState()
    {
        AuthoredActor skeletal = new(2000, "enemy-skeletalwarrior-1", new WorldPoint(0, 0, 0), null);
        ActorsState actors = new(DaggerfallDefinitions.Player, [skeletal]);
        InventoryState inventory = new([]);
        return new DaggerfallState(new PlayerControlState(new WorldPoint(0, 0, 0)), actors, inventory, new EquipmentState(DaggerfallDefinitions.IronLongsword), new CombatState(), new EncounterState(), new ProgressionState());
    }

    private static List<IProductFact> Deliver(ProductFactBuffer facts)
    {
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        return delivered;
    }

    private sealed class MaximumRandom : IRandomService
    {
        public KeyedRngReceipt DrawKeyed(KeyedRngRequest request) => new(request.Maximum);
        public Rng CreateScoped(ScopedRngCreateRequest request) => throw new NotSupportedException();
        public Rng ForkScoped(ScopedRngForkRequest request) => throw new NotSupportedException();
        public RngValue NextU64(Rng rng) => throw new NotSupportedException();
        public RngValue NextBoundedU32(ScopedRngBoundedRequest request) => throw new NotSupportedException();
        public RngValue NextBool(Rng rng) => throw new NotSupportedException();
    }

    private sealed class EnemyHitRandom : IRandomService
    {
        public KeyedRngReceipt DrawKeyed(KeyedRngRequest request) => new(request.Maximum == 100 ? request.Minimum : request.Maximum);
        public Rng CreateScoped(ScopedRngCreateRequest request) => throw new NotSupportedException();
        public Rng ForkScoped(ScopedRngForkRequest request) => throw new NotSupportedException();
        public RngValue NextU64(Rng rng) => throw new NotSupportedException();
        public RngValue NextBoundedU32(ScopedRngBoundedRequest request) => throw new NotSupportedException();
        public RngValue NextBool(Rng rng) => throw new NotSupportedException();
    }
}
