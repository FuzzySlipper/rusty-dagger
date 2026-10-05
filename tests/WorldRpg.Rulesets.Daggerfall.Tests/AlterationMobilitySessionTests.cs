using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class AlterationMobilitySessionTests
{
    [Theory]
    [InlineData(25, "slowfall")]
    [InlineData(27, "jumping")]
    [InlineData(28, "climbing")]
    [InlineData(30, "water-breathing")]
    public void Stock_and_constructed_spells_cast_extend_restore_and_expire_through_shared_calendar(int type, string effectKey)
    {
        using var f = new Fixture();
        Cast(f.Session, type);
        var effect = Assert.Single(f.Session.State.Effects.Active);
        Assert.Equal(effectKey, effect.Definition.Key);
        uint rounds = effect.Lifecycle.RemainingRounds!.Value;
        Cast(f.Session, type);
        Assert.Same(effect, Assert.Single(f.Session.State.Effects.Active));
        Assert.True(effect.Lifecycle.RemainingRounds > rounds);
        using var restored = new Fixture(f.Session.CaptureSave());
        Assert.Equal(effect.Lifecycle.RemainingRounds, Assert.Single(restored.Session.State.Effects.Active).Lifecycle.RemainingRounds);
        restored.Session.AdvanceElapsedTime((effect.Lifecycle.RemainingRounds!.Value + 1) * 60);
        Assert.DoesNotContain(restored.Session.State.Effects.Active, value => value.Definition.Key == effectKey);
        Assert.False(Capability(restored.Session, type));
    }

    [Fact]
    public void Jumping_updates_the_existing_Engine_jump_command_and_removal_restores_it()
    {
        using var f = new Fixture();
        f.Session.Update(new ProductUpdate(OuterUpdate(1), []));
        float initial = f.Spatial.StepRequests[^1].Config.Vertical.JumpSpeed;
        Cast(f.Session, 27);
        f.Session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Equal(initial + DaggerfallLocomotionTuning.Classic.JumpBaseSpeed * .6f, f.Spatial.StepRequests[^1].Config.Vertical.JumpSpeed, 4);
        f.Session.State.Effects.Cancel(Assert.Single(f.Session.State.Effects.Active).Context.Instance);
        f.Session.Update(new ProductUpdate(OuterUpdate(3), []));
        Assert.Equal(initial, f.Spatial.StepRequests[^1].Config.Vertical.JumpSpeed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Slowfall_uses_accepted_descent_and_releases_restored_fall_baseline_even_before_first_step(bool expireBeforeFirstStep)
    {
        using var f = new Fixture();
        Cast(f.Session, 25);
        var start = new WorldPoint(0, -20, 0);
        f.Session.State.PlayerControl.Restore(start, default(CharacterMotion) with
        { Grounded = false, PeakY = 1000, FallOriginY = 1000, ControlledVelocity = new Vector3(0, -10, 0) });
        f.Spatial.StepResult = Descending;
        f.Session.Update(new ProductUpdate(OuterUpdate(1), []));
        var request = f.Spatial.StepRequests[^1];
        Assert.Equal(0, request.Config.Vertical.Gravity);
        Assert.Equal(-DaggerfallLocomotionTuning.Classic.SlowfallDescentSpeed, request.Motion.ControlledVelocity.Y);
        using var restored = new Fixture(f.Session.CaptureSave());
        restored.Spatial.StepResult = Descending;
        if (!expireBeforeFirstStep) restored.Session.Update(new ProductUpdate(OuterUpdate(2), []));
        uint rounds = Assert.Single(restored.Session.State.Effects.Active).Lifecycle.RemainingRounds!.Value;
        restored.Session.AdvanceElapsedTime((rounds + 1) * 60);
        float expiryHeight = restored.Session.State.PlayerControl.Position!.Value.Y;
        double health = restored.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        restored.Spatial.StepResult = (step, result) => result with
        { Transform = result.Transform with { Translation = step.Position - Vector3.UnitY }, Motion = result.Motion with { Grounded = true } };
        restored.Session.Update(new ProductUpdate(OuterUpdate(3), []));
        var released = restored.Spatial.StepRequests[^1];
        Assert.True(released.Config.Vertical.Gravity > 0);
        Assert.Equal(expiryHeight, released.Motion.PeakY);
        Assert.Equal(health, restored.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void Slowfall_rejects_delivery_to_another_actor_without_granting_support()
    {
        using var f = new Fixture();
        var spell = TestPayload.Definitions.Magic.Spells.Values.First(value => value.RangeType == 0 && value.Effects.Count == 1 && value.Effects[0].Type == 25);
        f.Session.State.Character.LearnSpell(spell.Key);
        var mana = f.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        mana.Maximum.BaseValue = 10000; mana.SetCurrent(10000);
        Assert.Equal(DaggerfallCastOutcome.Ready, f.Session.ReadyPlayerSpell(spell.Key).Outcome);
        var bundle = f.Session.Casting.Release(1, true).Bundle!;
        Assert.Equal(DaggerfallCastOutcome.InvalidTarget, f.Session.Casting.Deliver(bundle, [2000]).Outcome);
        Assert.False(f.Session.State.Effects.GrantsSlowfall(2000));
        Assert.Empty(f.Session.State.Effects.Active);
    }

    [Fact]
    public void Water_breathing_restore_and_removal_reach_actual_submersion_consequences()
    {
        using var f = new Fixture(); Cast(f.Session, 30);
        f.Spatial.MovementFact = _ => new(CharacterMovementMode.Swimming, 1, true, false, false, false);
        f.Session.Update(new ProductUpdate(OuterUpdate(1) with { FixedDeltaSeconds = 10 }, []));
        Assert.True(f.Session.State.Swimming.HeadSubmerged);
        Assert.False(f.Session.State.Actors.Player.IsDefeated);
        using var restored = new Fixture(f.Session.CaptureSave());
        restored.Spatial.MovementFact = f.Spatial.MovementFact;
        restored.Session.State.Effects.Cancel(Assert.Single(restored.Session.State.Effects.Active).Context.Instance);
        restored.Session.Update(new ProductUpdate(OuterUpdate(2) with { FixedDeltaSeconds = 10 }, []));
        Assert.True(restored.Session.State.Actors.Player.IsDefeated);
    }

    [Fact]
    public void Climbing_spell_doubles_the_existing_climb_command_and_removes_cleanly()
    {
        using var f = new Fixture(minimumRandom: true);
        f.Spatial.FloorHit = request => request.Direction.Y == 0f
            ? default(SpatialHit) with { Present = true, Normal = Vector3.UnitZ } : default;
        f.Session.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        for (ulong step = 2; step <= 49; step++) f.Session.Update(new ProductUpdate(OuterUpdate(step), []));
        float ordinary = f.Spatial.StepRequests[^1].Motion.ControlledVelocity.Y;
        Assert.True(ordinary > 0);
        Cast(f.Session, 28);
        f.Session.Update(new ProductUpdate(OuterUpdate(50), []));
        Assert.Equal(ordinary * 2, f.Spatial.StepRequests[^1].Motion.ControlledVelocity.Y, 4);
        f.Session.State.Effects.Cancel(Assert.Single(f.Session.State.Effects.Active).Context.Instance);
        f.Session.Update(new ProductUpdate(OuterUpdate(51), []));
        Assert.Equal(ordinary, f.Spatial.StepRequests[^1].Motion.ControlledVelocity.Y);
    }

    private static CharacterStepReceipt Descending(CharacterStepRequest step, CharacterStepReceipt result) => result with
    { Transform = result.Transform with { Translation = step.Position + step.Motion.ControlledVelocity / 60 }, Motion = result.Motion with { Grounded = false } };

    private static bool Capability(DaggerfallSession s, int type) => type switch
    { 25 => s.State.Effects.GrantsSlowfall(1), 27 => s.State.Effects.EnhancesJumping(1), 28 => s.State.Effects.EnhancesClimbing(1), 30 => s.State.Effects.GrantsWaterBreathing(1), _ => false };

    private static void Cast(DaggerfallSession session, int type)
    {
        if (type == 28 && !session.State.Character.KnownSpells.Any(value => value.StartsWith("custom-spell.")))
            BuyClimbing(session);
        var spell = type == 28 ? Assert.Single(DaggerfallSavePayload.Read(session.CaptureSave()).CustomSpells)
            : TestPayload.Definitions.Magic.Spells.Values.First(value => value.RangeType == 0 && value.Effects.Count == 1 && value.Effects[0].Type == type);
        session.State.Character.LearnSpell(spell.Key);
        var magicka = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        magicka.Maximum.BaseValue = 10000; magicka.SetCurrent(10000);
        Assert.Equal(DaggerfallCastOutcome.Ready, session.ReadyPlayerSpell(spell.Key).Outcome);
        var result = session.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Contains(Assert.Single(result.Bundle!.Results).Outcome, new[] { DaggerfallCastOutcome.Applied, DaggerfallCastOutcome.Refreshed });
        Assert.True(Capability(session, type));
    }

    private static void BuyClimbing(DaggerfallSession game)
    {
        var option = Assert.Single(game.SpellMaker.Effects, effect => effect.Key == "climbing");
        game.State.Social.JoinGuild(40, 0);
        var site = game.Site.ActiveSite!;
        var npcSite = new DaggerfallNpcSite(site.Id.Region, site.Name, string.Empty);
        long npc = game.State.Npcs.RegisterStable(DaggerfallNpcKind.Static, "climb-maker", npcSite,
            new("Breton", "Male", 0, 0, 0, 64), "Spell maker", ["talk", "make-spells"]);
        var provider = new DaggerfallServiceProvider(npc, npcSite, "make-spells");
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, RandomMinimum.Create());
        foreach (var (group, template, quantity) in new[] { ("MiscItems", 132, 1) })
        {
            string key = $"climb-maker.{template}";
            factory.Materialize(factory.Create(new(group, key, DaggerfallItemOwner.Player, Quantity: (ulong)quantity, TemplateIndex: template)),
                game.State.Inventory, game.State.ItemInstances, InventoryStackId.Parse(key), template == 132 ? game.UniqueItemAllocator.AllocateReference() : null);
        }
        game.SpellMaker.SetDraft(new("Climbing", 4, 0, 1,
            [new("climbing", option.Type, option.SubType, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1)]));
        var quote = Assert.IsType<DaggerfallSpellConstructionQuote>(game.SpellMaker.Quote(provider));
        Assert.True(quote.Eligible, quote.Reason);
        const string gold = "climb-maker.gold";
        factory.Materialize(factory.Create(new("Currency", gold, DaggerfallItemOwner.Player, Quantity: (ulong)quote.Gold, TemplateIndex: 276)),
            game.State.Inventory, game.State.ItemInstances, InventoryStackId.Parse(gold));
        Assert.True(game.SpellMaker.Buy(provider, quote.Key, (ulong)quote.Gold, true).Accepted);
    }

    private sealed class Fixture : IDisposable
    {
        internal DaggerfallSession Session { get; }
        internal SpatialFake Spatial { get; }
        internal Fixture(RulesetSavePayload? save = null, bool minimumRandom = false)
        {
            var inputs = ReadInputs(TestData.RepositoryRoot);
            List<string> releases = [];
            ContentFake content = new(releases); PopulateContent(content, inputs);
            Spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
            var engine = EngineContextFake.Create(content, Spatial.Service, new AppearanceFake(releases), random: minimumRandom ? RandomMinimum.Create() : RandomMaximum.Create());
            var composition = new DaggerfallSessionComposition(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults);
            Session = save is null ? DaggerfallSession.StartNew(engine.Context, composition) : DaggerfallSession.Restore(engine.Context, composition, save);
        }
        public void Dispose() => Session.Dispose();
    }
}
