using System.Reflection;
using System.Text;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.Targeting;
using Xunit;

namespace WorldRpg.Rulesets.Canary;

public sealed class CanaryRulesetTests
{
    private const string TuningPayload =
        """{"label":"single-room","hitChance":60,"strikeCooldownSeconds":0.5,"rallyRounds":2,"rallyMight":3,"messageSeconds":2}""";

    [Fact]
    public void Host_runs_admitted_updates_and_the_world_steps_only_after_begin()
    {
        CanaryRuleset ruleset = new();
        CanarySession session;
        using (WorldRpgProduct product = Product(ruleset))
        {
            session = Assert.IsType<CanarySession>(ruleset.CreatedSession);
            product.Start();
            int publishedAtStart = session.InitialPublishCount;
            Assert.Equal(ProductMode.Title, session.Mode);

            // The entry screen holds the world: an admitted update takes no step.
            Assert.Equal(ProductUpdateResult.None, product.Update(Update(1)));
            Assert.Equal(0u, session.AppliedStepCount);

            // The begin action is recognized by the session, but the Host leaves the entry screen
            // after that update ran, so the slice that asked for play still takes no step.
            product.Update(Update(2, Action("begin")));
            Assert.Equal(ProductMode.Playing, session.Mode);
            Assert.Equal(0u, session.AppliedStepCount);

            // An admitted update publishes through the session update, never by re-publishing
            // initial state: the initial count is unchanged by Update.
            product.Update(Update(3));
            Assert.Equal(publishedAtStart, session.InitialPublishCount);
            Assert.Equal(1u, session.AppliedStepCount);
            Assert.NotEqual(0, session.Hud.Nodes.Length);
            Assert.False(session.IsDisposed);
        }

        Assert.True(session.IsDisposed);
    }

    [Fact]
    public void Host_attach_republishes_the_current_session_without_restarting_or_after_shutdown()
    {
        CanaryRuleset ruleset = new();
        WorldRpgProduct product = Product(ruleset);
        CanarySession session = Assert.IsType<CanarySession>(ruleset.CreatedSession);

        product.Start();
        int publishedAtStart = session.InitialPublishCount;
        product.Attach();

        // Attach republishes the current session for the newly attached client.
        Assert.Equal(publishedAtStart + 1, session.InitialPublishCount);
        Assert.False(session.IsDisposed);

        product.Shutdown();
        product.Attach();

        // After shutdown there is nothing to republish to and no restart.
        Assert.Equal(publishedAtStart + 1, session.InitialPublishCount);
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public void The_session_reads_its_tuning_payload_and_refuses_a_value_outside_its_range()
    {
        CanaryRuleset ruleset = new();
        using WorldRpgProduct product = Product(ruleset);

        CanaryTuning tuning = ruleset.CreatedSession!.Tuning;
        Assert.Equal(new CanaryTuning("single-room", 60, .5d, 2, 3, 2d), tuning);
        Assert.Throws<InvalidDataException>(() => CanaryTuning.Parse(Encoding.UTF8.GetBytes(TuningPayload.Replace("\"hitChance\":60", "\"hitChance\":0"))));
    }

    [Fact]
    public void Strikes_hit_on_a_high_roll_and_defeat_through_the_vitality_track()
    {
        CanaryRuleset ruleset = new();
        using WorldRpgProduct product = Begun(ruleset);
        CanarySession session = ruleset.CreatedSession!;
        Track vitality = session.Track(session.Intruder.Actor, "vitality");

        // Kit no longer compares the pair: a roll of 10 under a chance of 60 misses here.
        product.Update(Update(9, Action("strike")));
        CanaryMissedFact missed = Assert.IsType<CanaryMissedFact>(Assert.Single(session.Delivered));
        Assert.Equal((10, 60), (missed.Roll, missed.Chance));
        Assert.Equal(12d, vitality.Current);

        // Still inside the cooldown the strike is refused by Kit attack execution.
        product.Update(Update(20, Action("strike")));
        Assert.Equal(AttackRefusal.Cooldown, Assert.IsType<CanaryRefusedFact>(session.Delivered[^1]).Reason);

        // A roll of 80 reaches the chance and the warden's might lands on vitality.
        product.Update(Update(79, Action("strike")));
        CanaryStruckFact struck = Assert.IsType<CanaryStruckFact>(session.Delivered[^1]);
        Assert.Equal((80, 60, 10, 10d, false), (struck.Roll, struck.Chance, struck.Damage, struck.VitalityLost, struck.Defeated));
        Assert.Equal(2d, vitality.Current);
        Assert.Equal("Struck for 10.", session.Presentation.LastOutcome);

        product.Update(Update(159, Action("strike")));
        CanaryStruckFact lethal = Assert.IsType<CanaryStruckFact>(session.Delivered[^1]);
        Assert.True(lethal.Defeated);
        Assert.Equal(2d, lethal.VitalityLost);
        Assert.True(session.Intruder.IsDefeated);

        // A defeated target is refused before any rule runs, and a direct application to an
        // already-defeated vitality track removes nothing and defeats nobody a second time.
        product.Update(Update(299, Action("strike")));
        Assert.Equal(AttackRefusal.TargetDefeated, Assert.IsType<CanaryRefusedFact>(session.Delivered[^1]).Reason);
        ApplyHitEvent repeated = session.Combat.ApplyToHealth(new CombatParticipants(session.Warden.Actor, session.Intruder.Actor, "strike"), 5, vitality);
        Assert.False(repeated.Defeated);
        Assert.Equal(0d, repeated.ActualHealthLost);
    }

    [Fact]
    public void Canary_actors_carry_only_the_kit_components_the_ruleset_opted_into()
    {
        CanaryRuleset ruleset = new();
        using WorldRpgProduct product = Product(ruleset);
        CanarySession session = ruleset.CreatedSession!;

        Assert.True(session.Warden.Actor.TryGet<AttackState>(out _));
        Assert.False(session.Warden.Actor.TryGet<TargetingComponent>(out _));
        Assert.False(session.Warden.Actor.TryGet<ProgressionState>(out _));
        Assert.False(session.Intruder.Actor.TryGet<TargetingComponent>(out _));
        Assert.Empty(session.Attacks.CaptureCooldowns(null, null));
    }

    [Fact]
    public void A_timed_rally_contributes_for_its_rounds_and_holds_while_the_world_is_held()
    {
        CanaryRuleset ruleset = new();
        using WorldRpgProduct product = Product(ruleset);
        product.Start();
        CanarySession session = ruleset.CreatedSession!;
        Stat might = session.Warden.Stats.GetStat(StatId.Parse("might"));
        Track focus = session.Track(session.Warden.Actor, "focus");

        var rally = session.Rally();
        Assert.Equal("rally", rally.Context.Source.Key);
        Assert.Equal((ushort)1, rally.Effect.Stacks);
        Assert.Equal(13d, might.Value);

        // At the entry screen the rally neither ticks nor expires.
        product.Update(Update(1));
        Assert.Equal(2u, Assert.Single(session.Rallies).RemainingRounds);
        Assert.Equal(0d, focus.Current);

        product.Update(Update(2, Action("begin")));
        product.Update(Update(3));
        Assert.Equal(1u, Assert.Single(session.Rallies).RemainingRounds);
        Assert.Equal(1d, focus.Current);

        product.Update(Update(4));
        Assert.Empty(session.Rallies);
        Assert.Empty(session.Warden.Effects.Effects);
        Assert.Equal(2d, focus.Current);
        Assert.Equal(10d, might.Value);
        Assert.Equal("rally-1", Assert.IsType<CanaryRallyEndedFact>(Assert.Single(session.Delivered)).Instance);
    }

    [Fact]
    public void The_weapon_slot_takes_a_spear_without_any_currency_and_its_reach_joins_the_strike()
    {
        CanaryRuleset ruleset = new();
        using WorldRpgProduct product = Begun(ruleset);
        CanarySession session = ruleset.CreatedSession!;

        var spear = session.EquipSpear(500, reach: 2);
        var assignment = Assert.Single(session.Equipment.Read().Assignments);
        Assert.Equal("weapon", assignment.Slot.Value);
        Assert.Equal(spear, assignment.Item);
        Assert.False(session.Scenario.HasCurrency);
        Assert.Empty(session.Equipment.Inventory.Stacks);
        Assert.Equal(500UL, session.Equipment.GetDurableItemId(new Rusty.Engine.Entities.EntityId(spear.EntityId)).Value);

        // Might 10 plus the spear's reach 2 meets the intruder's whole vitality of 12.
        product.Update(Update(99, Action("strike")));
        CanaryStruckFact struck = Assert.IsType<CanaryStruckFact>(session.Delivered[^1]);
        Assert.Equal(12, struck.Damage);
        Assert.True(struck.Defeated);
        Assert.Equal("The intruder falls.", session.Presentation.LastOutcome);
    }

    private static WorldRpgProduct Product(CanaryRuleset ruleset) =>
        new(new ProductCreateContext(EngineContextDouble.Create(), CanaryContent(), EmptyInputConfiguration()), ruleset, CanaryRuleset.Bundle);

    private static WorldRpgProduct Begun(CanaryRuleset ruleset)
    {
        WorldRpgProduct product = Product(ruleset);
        product.Start();
        Assert.Equal(ProductModeChangeOutcome.Applied, product.Begin().Outcome);
        Assert.Equal(ProductMode.Playing, ruleset.CreatedSession!.Mode);
        return product;
    }

    private static ProductUpdate Update(ulong step, params ProductInputEvent[] input) =>
        new(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, step, 60, 1, 0, 1d / 60d), input);

    private static ProductInputEvent Action(string action) =>
        new ProductInputEvent(InputEventKind.DirectDigital, InputEdge.None, InputDevice.None, InputChannel.None,
            InputAxis.None, KeyboardControl.None, PointerButton.None, ControllerButton.None, ControllerAxis.None,
            InputClearReason.None, InputValueKind.ProductPayload, InputPhase.DirectUi, InputProvenance.DirectUi,
            default, default, default, 0F, 0F, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty) with
        {
            PayloadContract = Encoding.UTF8.GetBytes(CanarySession.ActionContract),
            PayloadData = Encoding.UTF8.GetBytes($$"""{"action":"{{action}}"}"""),
        };

    private static ProductInputConfiguration EmptyInputConfiguration() => new(
        default,
        default,
        ReadOnlyMemory<ProductInputDescriptor>.Empty,
        ReadOnlyMemory<ProductInputMapping>.Empty);

    private static ProductContent CanaryContent() => new(new ProductContentFile[]
    {
        File("worldrpg/bundles/canary.single-room.bundle.json", """{"kind":"worldrpg.game-bundle","schemaVersion":1,"id":"canary.single-room","version":1,"ruleset":"canary","contentPacks":[{"id":"canary.single-room","version":1}],"tuning":{"id":"canary.single-room","version":1}}"""),
        File("worldrpg/content-packs/canary.single-room.pack.json", """{"kind":"worldrpg.content-pack","schemaVersion":1,"id":"canary.single-room","version":1,"ruleset":"canary","role":"canary.world","dependencies":[],"payload":"worldrpg/payloads/canary.single-room.json"}"""),
        File("worldrpg/payloads/canary.single-room.json", """{"room":"observatory"}"""),
        File("worldrpg/tuning/canary.single-room.tuning.json", """{"kind":"worldrpg.tuning-profile","schemaVersion":1,"id":"canary.single-room","version":1,"ruleset":"canary","payload":"worldrpg/tuning-payloads/canary.single-room.json"}"""),
        File("worldrpg/tuning-payloads/canary.single-room.json", TuningPayload),
    });

    private static ProductContentFile File(string path, string value) => new(Encoding.UTF8.GetBytes(path), Encoding.UTF8.GetBytes(value));

    private class EngineContextDouble : DispatchProxy
    {
        internal static IEngineContext Create() => DispatchProxy.Create<IEngineContext, EngineContextDouble>();

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new NotSupportedException($"Canary session does not use Engine service {method?.Name}.");
    }
}
