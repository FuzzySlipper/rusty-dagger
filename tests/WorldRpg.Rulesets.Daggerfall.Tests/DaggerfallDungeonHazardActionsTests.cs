using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDungeonHazardActionsTests
{
    [Fact]
    public void Hurt21_keeps_the_donor_twentieth_activation_gate_and_random_bounds()
    {
        using EntityDirectory entities = new();
        Actor source = ActorWithVitals(entities, 1, health: 100, magicka: 20);
        Actor target = ActorWithVitals(entities, 2, health: 100, magicka: 20);
        FixedRandom random = FixedRandom.Create(7);
        DaggerfallDungeonActionDefinition action = Action("hurt21", DaggerfallDungeonActionFlag.Hurt21, magnitude: 3);
        CombatResolution combat = new();

        DaggerfallDungeonHazardActionResult skipped = DaggerfallDungeonHazardActions.Execute(
            action,
            Context(source, target, targetLevel: 2, activationCount: 19),
            combat,
            random.Service)!;

        Assert.Equal(DaggerfallDungeonActionOutcome.AppliedWithoutChange, skipped.Execution.Outcome);
        Assert.Equal(100, Health(target).ValueInt);
        Assert.Empty(random.Requests);

        DaggerfallDungeonHazardActionResult applied = DaggerfallDungeonHazardActions.Execute(
            action,
            Context(source, target, targetLevel: 2, activationCount: 20, actionIndex: 8),
            combat,
            random.Service)!;

        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, applied.Execution.Outcome);
        Assert.Equal(14d, applied.HealthDamage!.Value.ActualHealthLost);
        Assert.Equal(86, Health(target).ValueInt);
        KeyedRngRequest request = Assert.Single(random.Requests);
        Assert.Equal(3, request.Minimum);
        Assert.Equal(7, request.Maximum); // DFU's Random.Range upper bound is exclusive.
        Assert.Contains("activation:20", request.Key, StringComparison.Ordinal);
        Assert.Equal(100, Health(source).ValueInt); // The selected target, not the source, is hurt.
    }

    [Fact]
    public void Hurt21_uses_the_donor_Index_carried_by_normalized_SoundIndex()
    {
        using EntityDirectory entities = new();
        Actor source = ActorWithVitals(entities, 1, health: 100, magicka: 20);
        Actor target = ActorWithVitals(entities, 2, health: 100, magicka: 20);
        FixedRandom random = FixedRandom.Create(7);

        DaggerfallDungeonHazardActionResult result = DaggerfallDungeonHazardActions.Execute(
            Action("hurt21", DaggerfallDungeonActionFlag.Hurt21, magnitude: 3, soundIndex: 8),
            Context(source, target, targetLevel: 1, activationCount: 20),
            new CombatResolution(),
            random.Service)!;

        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, result.Execution.Outcome);
        Assert.Equal(7d, result.HealthDamage!.Value.ActualHealthLost);
        Assert.Equal(93, Health(target).ValueInt);
        KeyedRngRequest request = Assert.Single(random.Requests);
        Assert.Equal(3, request.Minimum);
        Assert.Equal(7, request.Maximum);
    }

    [Fact]
    public void Hurt21_reversed_random_bounds_keep_the_donor_draw_interval()
    {
        using EntityDirectory entities = new();
        Actor source = ActorWithVitals(entities, 1, health: 100, magicka: 20);
        Actor target = ActorWithVitals(entities, 2, health: 100, magicka: 20);
        FixedRandom random = FixedRandom.Create(5);

        DaggerfallDungeonHazardActionResult result = DaggerfallDungeonHazardActions.Execute(
            Action("hurt21-reversed", DaggerfallDungeonActionFlag.Hurt21, magnitude: 8, soundIndex: 3),
            Context(source, target, targetLevel: 1, activationCount: 20),
            new CombatResolution(), random.Service)!;

        Assert.Equal(5d, result.HealthDamage!.Value.ActualHealthLost);
        KeyedRngRequest request = Assert.Single(random.Requests);
        Assert.Equal(4, request.Minimum);
        Assert.Equal(8, request.Maximum);
    }

    [Theory]
    [InlineData((byte)0x16, true, (byte)9, (ushort)4, 3, 12)]
    [InlineData((byte)0x17, false, (byte)5, (ushort)99, 3, 15)]
    [InlineData((byte)0x18, false, (byte)5, (ushort)99, 3, 15)]
    [InlineData((byte)0x19, false, (byte)5, (ushort)99, 3, 15)]
    public void Hurt22_through_25_preserve_flat_and_raw_axis_parameter_shapes(
        byte flag,
        bool isFlat,
        byte axis,
        ushort magnitude,
        int level,
        int expectedDamage)
    {
        using EntityDirectory entities = new();
        Actor source = ActorWithVitals(entities, 1, health: 100, magicka: 20);
        Actor target = ActorWithVitals(entities, 2, health: 100, magicka: 20);

        DaggerfallDungeonHazardActionResult result = DaggerfallDungeonHazardActions.Execute(
            Action($"hurt-{flag}", (DaggerfallDungeonActionFlag)flag, axis, magnitude, isFlat),
            Context(source, target, level, activationCount: 1),
            new CombatResolution(),
            FixedRandom.Create(1).Service)!;

        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, result.Execution.Outcome);
        Assert.Equal(expectedDamage, result.HealthDamage!.Value.CalculatedDamage);
        Assert.Equal((double)expectedDamage, result.HealthDamage.Value.ActualHealthLost);
        Assert.Equal(100 - expectedDamage, Health(target).ValueInt);
    }

    [Fact]
    public void Hurt_actions_use_shared_combat_application_for_mitigation_and_lethal_transition()
    {
        using EntityDirectory entities = new();
        Actor source = ActorWithVitals(entities, 1, health: 100, magicka: 20);
        Actor target = ActorWithVitals(entities, 2, health: 10, magicka: 20);
        CombatContributions contributions = new();
        contributions.Rules.Add(new ReduceDamage(3));
        target.Add(contributions);

        DaggerfallDungeonHazardActionResult mitigated = DaggerfallDungeonHazardActions.Execute(
            Action("hurt22", DaggerfallDungeonActionFlag.Hurt22, magnitude: 8, isFlat: true),
            Context(source, target, targetLevel: 1, activationCount: 1),
            new CombatResolution(),
            FixedRandom.Create(1).Service)!;

        Assert.Equal(8, mitigated.HealthDamage!.Value.CalculatedDamage);
        Assert.Equal(5d, mitigated.HealthDamage.Value.ActualHealthLost);
        Assert.Equal(5, Health(target).ValueInt);

        DaggerfallDungeonHazardActionResult lethal = DaggerfallDungeonHazardActions.Execute(
            Action("hurt22-lethal", DaggerfallDungeonActionFlag.Hurt22, magnitude: 8, isFlat: true),
            Context(source, target, targetLevel: 1, activationCount: 2),
            new CombatResolution(),
            FixedRandom.Create(1).Service)!;

        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, lethal.Execution.Outcome);
        Assert.True(lethal.HealthDamage!.Value.Defeated);
        Assert.Equal(5d, lethal.HealthDamage.Value.ActualHealthLost);
        Assert.Equal(0, Health(target).ValueInt);

        DaggerfallDungeonHazardActionResult noSecondDeath = DaggerfallDungeonHazardActions.Execute(
            Action("hurt22-after-death", DaggerfallDungeonActionFlag.Hurt22, magnitude: 8, isFlat: true),
            Context(source, target, targetLevel: 1, activationCount: 3),
            new CombatResolution(),
            FixedRandom.Create(1).Service)!;

        Assert.Equal(DaggerfallDungeonActionOutcome.AppliedWithoutChange, noSecondDeath.Execution.Outcome);
        Assert.False(noSecondDeath.HealthDamage!.Value.Defeated);
        Assert.Equal(0d, noSecondDeath.HealthDamage.Value.ActualHealthLost);
    }

    [Theory]
    [InlineData(true, (byte)9, (ushort)5, 3, 0, 3)]
    [InlineData(false, (byte)2, (ushort)99, 3, 1, 2)]
    public void DrainMagicka_uses_flat_or_axis_parameter_and_saturates_the_guarded_track(
        bool isFlat,
        byte axis,
        ushort magnitude,
        int startingMagicka,
        int expectedRemaining,
        int expectedLoss)
    {
        using EntityDirectory entities = new();
        Actor source = ActorWithVitals(entities, 1, health: 100, magicka: 20);
        Actor target = ActorWithVitals(entities, 2, health: 100, magicka: startingMagicka);

        DaggerfallDungeonHazardActionResult result = DaggerfallDungeonHazardActions.Execute(
            Action("drain", DaggerfallDungeonActionFlag.DrainMagicka, axis, magnitude, isFlat),
            Context(source, target, targetLevel: 1, activationCount: 1),
            new CombatResolution(),
            FixedRandom.Create(1).Service)!;

        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, result.Execution.Outcome);
        Assert.Equal((double)expectedLoss, result.MagickaLost);
        Assert.Equal(expectedRemaining, Magicka(target).ValueInt);
    }

    [Fact]
    public void DrainMagicka_at_zero_vitals_is_an_accepted_without_change_result()
    {
        using EntityDirectory entities = new();
        Actor source = ActorWithVitals(entities, 1, health: 100, magicka: 20);
        Actor target = ActorWithVitals(entities, 2, health: 100, magicka: 0);

        DaggerfallDungeonHazardActionResult result = DaggerfallDungeonHazardActions.Execute(
            Action("drain-zero", DaggerfallDungeonActionFlag.DrainMagicka, magnitude: 5, isFlat: true),
            Context(source, target, targetLevel: 1, activationCount: 1),
            new CombatResolution(),
            FixedRandom.Create(1).Service)!;

        Assert.Equal(DaggerfallDungeonActionOutcome.AppliedWithoutChange, result.Execution.Outcome);
        Assert.Equal(0d, result.MagickaLost);
        Assert.Equal(0, Magicka(target).ValueInt);
    }

    [Fact]
    public void Graph_cooldown_blocks_repeated_hurt_until_the_admitted_advance()
    {
        using EntityDirectory entities = new();
        Actor source = ActorWithVitals(entities, 1, health: 100, magicka: 20);
        Actor target = ActorWithVitals(entities, 2, health: 100, magicka: 20);
        CombatResolution combat = new();
        FixedRandom random = FixedRandom.Create(1);
        DaggerfallDungeonActionDefinition action = Action("cooldown-hurt", DaggerfallDungeonActionFlag.Hurt22, magnitude: 2, isFlat: true)
            with { CooldownSeconds = 2d };
        ulong familyActivations = 0;
        DaggerfallDungeonActionGraph graph = new(
            "hazard-profile",
            [action],
            new DaggerfallVariableStore(new Dictionary<string, int>()),
            executeFamilyAction: definition =>
            {
                familyActivations++;
                return DaggerfallDungeonHazardActions.Execute(
                    definition,
                    Context(source, target, targetLevel: 1, activationCount: familyActivations),
                    combat,
                    random.Service)?.Execution;
            });

        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, graph.Trigger(action.Id, DaggerfallDungeonActionEvent.Direct).Executions.Single().Outcome);
        Assert.Equal(98, Health(target).ValueInt);

        Assert.Equal(DaggerfallDungeonActionOutcome.Cooldown, graph.Trigger(action.Id, DaggerfallDungeonActionEvent.Direct).Executions.Single().Outcome);
        Assert.Equal(98, Health(target).ValueInt);
        Assert.Equal(1UL, familyActivations);

        graph.Advance(2d);
        Assert.Equal(DaggerfallDungeonActionOutcome.Applied, graph.Trigger(action.Id, DaggerfallDungeonActionEvent.Direct).Executions.Single().Outcome);
        Assert.Equal(96, Health(target).ValueInt);
        Assert.Equal(2UL, familyActivations);
    }

    private static DaggerfallDungeonHazardExecutionContext Context(
        Actor source,
        Actor target,
        int targetLevel,
        ulong activationCount,
        int? actionIndex = null) =>
        new(source, target, targetLevel, activationCount, actionIndex);

    private static DaggerfallDungeonActionDefinition Action(
        string id,
        DaggerfallDungeonActionFlag flag,
        byte axis = 0,
        ushort magnitude = 0,
        bool isFlat = false,
        byte soundIndex = 0) =>
        new(id, 1, (uint)DaggerfallDungeonTriggerFlag.Direct, (byte)flag, axis, 0, magnitude, -1, null,
            IsFlat: isFlat, SoundIndex: soundIndex);

    private static Actor ActorWithVitals(EntityDirectory entities, long id, double health, double magicka)
    {
        EntityId entity = entities.Create(ActorsState.Identity(id), new EntityTypeId("hazard-test"));
        Actor actor = new(entities.Store, entity);
        StatsComponent stats = new();
        stats.AddTrack(TrackId.Parse("health"), new Track(100d, health));
        stats.AddTrack(TrackId.Parse("magicka"), new Track(20d, magicka));
        actor.Add(stats);
        return actor;
    }

    private static Track Health(Actor actor) => actor.Get<StatsComponent>().GetTrack(TrackId.Parse("health"));
    private static Track Magicka(Actor actor) => actor.Get<StatsComponent>().GetTrack(TrackId.Parse("magicka"));

    private sealed class ReduceDamage(int amount) : ICombatContribution
    {
        public void Applying(ApplyHitEvent interaction) => interaction.Damage = Math.Max(0, interaction.Damage - amount);
    }

    private class FixedRandom : DispatchProxy
    {
        private long _value;
        internal IRandomService Service { get; private set; } = null!;
        internal List<KeyedRngRequest> Requests { get; } = [];

        internal static FixedRandom Create(long value)
        {
            IRandomService service = DispatchProxy.Create<IRandomService, FixedRandom>();
            FixedRandom proxy = (FixedRandom)(object)service;
            proxy.Service = service;
            proxy._value = value;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            Requests.Add(request);
            return new KeyedRngReceipt(Math.Clamp(_value, request.Minimum, request.Maximum));
        }
    }
}
