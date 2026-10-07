using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallCureEffectsTests
{
    [Theory]
    [InlineData("spell.059", 0)] [InlineData("spell.015", 1)]
    [InlineData("spell.058", 1)] [InlineData(CureParalysisKey, 2)]
    public void Published_cures_report_no_match_after_real_payment_and_do_not_leave_active_effects(string key, int subtype)
    {
        using Fixture f = new(); var s = f.Session;
        List<DaggerfallEffectOutcome> outcomes = []; s.State.Effects.Completed += outcomes.Add;
        double before = Magicka(s).Current;
        var bundle = Cast(s, key);
        Assert.Equal(DaggerfallCastOutcome.NoMatch, Assert.Single(bundle.Results).Outcome);
        Assert.Null(Assert.Single(bundle.Results).Instance);
        Assert.True(Magicka(s).Current < before);
        Assert.Empty(s.State.Effects.Active);
        Assert.Contains(outcomes, outcome => outcome.Kind == DaggerfallEffectOutcomeKind.NoMatch && outcome.TargetId == 1);
        double after = Magicka(s).Current;
        Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered, s.Casting.Deliver(bundle, [1]).Outcome);
        Assert.Equal(after, Magicka(s).Current);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Empty(restored.State.Effects.Active); Assert.Equal(after, Magicka(restored).Current);
        Assert.Equal(subtype, CureDefinitions.Value.Magic.Spells[key].Effects[0].SubType);
    }

    [Theory]
    [InlineData("spell.059", 0)] [InlineData("spell.015", 1)] [InlineData(CureParalysisKey, 2)]
    public void Real_cure_delivery_selects_all_matching_sources_and_preserves_other_conditions_and_targets(string key, int subtype)
    {
        using Fixture f = new(); var s = f.Session;
        Assert.Equal(DaggerfallDiseaseAdmission.Started, s.InflictDisease(new("brain.one", "monster", 2000, 1, [DaggerfallClassicDisease.BrainFever])));
        Assert.Equal(DaggerfallDiseaseAdmission.Started, s.InflictDisease(new("brain.two", "quest", 2001, 1, [DaggerfallClassicDisease.BrainFever])));
        Assert.True(s.State.Poisons.Afflict(s.State.Actors.Player.Actor, (int)DaggerfallPoisonVariant.Arsenic));
        Assert.True(s.State.Poisons.Afflict(s.State.Actors.Get(2000).Actor, (int)DaggerfallPoisonVariant.NuxVomica));
        Paralyze(s, "paralysis.one", 2000, 1); Paralyze(s, "paralysis.two", 2001, 1); Paralyze(s, "paralysis.other", 1, 2000);
        s.AdvanceElapsedTime(60);
        var selected = s.State.Effects.Active.Where(e => e.Context.Target.Value == 1 && Matches(e, subtype))
            .Select(e => e.Context.Instance.Value).ToArray();
        var retained = s.State.Effects.Active.Where(e => e.Context.Target.Value != 1 || !Matches(e, subtype))
            .Select(e => e.Context.Instance.Value).ToArray();
        Assert.NotEmpty(selected);
        List<DaggerfallEffectOutcome> outcomes = []; s.State.Effects.Completed += outcomes.Add;
        var bundle = Cast(s, key);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(bundle.Results).Outcome);
        Assert.Equal(retained.Order(), s.State.Effects.Active.Select(e => e.Context.Instance.Value).Order());
        Assert.Equal(selected.Order(), outcomes.Where(o => o.Kind == DaggerfallEffectOutcomeKind.Cured).Select(o => o.Instance).Order());
        Assert.Equal(subtype != 2, s.State.Effects.ControlsFor(1).Movement);
        Assert.True(s.State.Effects.ControlsFor(2000).Movement);
        Assert.Equal(subtype != 1, s.State.Poisons.IsAfflicted(s.State.Actors.Player.Actor));
        Assert.True(s.State.Poisons.IsAfflicted(s.State.Actors.Get(2000).Actor));
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(retained.Order(), restored.State.Effects.Active.Select(e => e.Context.Instance.Value).Order());
        restored.AdvanceElapsedTime(120);
        Assert.DoesNotContain(restored.State.Effects.Active, e => e.Context.Target.Value == 1 && Matches(e, subtype));
        Assert.Equal(subtype != 2, restored.State.Effects.ControlsFor(1).Movement);
        Assert.Equal(DaggerfallCastOutcome.NoMatch, Assert.Single(Cast(restored, key).Results).Outcome);
    }

    [Fact]
    public void Cure_paralyzation_releases_real_movement_and_physical_attack_consumers_before_expiry()
    {
        using Fixture f = new(); var s = f.Session;
        s.State.Kit.Rules.RegisterAction(s.DefinitionsByActor[1].ActionId!, new CertainStrike());
        Paralyze(s, "blocked.one", 2000, 1); Paralyze(s, "blocked.two", 2001, 1);
        s.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        Assert.Equal(Vector2.Zero, f.Spatial.StepRequests.Last().Command.PlanarIntent);
        double before = Health(s, 2000).Current;
        s.ResolveExplicitMelee(new(1, 2000, 1, 1000, .125)); Assert.Equal(before, Health(s, 2000).Current);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(Cast(s, CureParalysisKey).Results).Outcome);
        Assert.Equal(default, s.State.Effects.ControlsFor(1));
        s.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.NotEqual(Vector2.Zero, f.Spatial.StepRequests.Last().Command.PlanarIntent);
        s.ResolveExplicitMelee(new(1, 2000, 1, 1000, .125)); Assert.True(Health(s, 2000).Current < before);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(default, restored.State.Effects.ControlsFor(1));
    }

    [Fact]
    public void Cure_disease_removes_live_attribute_sources_without_healing_prior_health_loss()
    {
        using Fixture f = new(); var s = f.Session;
        var willpower = s.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Willpower.Value));
        double baseline = willpower.Value;
        Assert.Equal(DaggerfallDiseaseAdmission.Started, s.InflictDisease(new("brain", "monster", 2000, 1, [DaggerfallClassicDisease.BrainFever])));
        s.AdvanceElapsedTime(86400);
        Assert.True(willpower.Value < baseline); double health = Health(s, 1).Current;
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(Cast(s, "spell.059").Results).Outcome);
        Assert.Equal(baseline, willpower.Value); Assert.Equal(health, Health(s, 1).Current);
        Assert.Empty(willpower.Sources);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(baseline, restored.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Willpower.Value)).Value);
        restored.AdvanceElapsedTime(86400); Assert.Equal(baseline, restored.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Willpower.Value)).Value);
    }

    [Fact]
    public void Cure_poison_removes_residue_and_current_drug_modifiers_without_healing_spent_vitals()
    {
        using Fixture f = new(); var s = f.Session;
        var endurance = s.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value));
        var luck = s.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Luck.Value));
        double baseEndurance = endurance.Value, baseLuck = luck.Value;
        Assert.True(s.State.Poisons.Afflict(s.State.Actors.Player.Actor, (int)DaggerfallPoisonVariant.Arsenic));
        s.AdvanceElapsedTime(11 * 60);
        Assert.True(endurance.Value < baseEndurance);
        var poison = Assert.Single(s.State.Effects.Active);
        DaggerfallPoisonArms.CompleteCourse(poison, DaggerfallPoisonArms.State(poison), () => s.State.Character.Career);
        Assert.True(s.State.Poisons.HasPersistingDamage(s.State.Actors.Player.Actor));
        // A completed damaging source and a live drug must both be selected by the same cure.
        Assert.True(s.State.Poisons.Afflict(s.State.Actors.Player.Actor, (int)DaggerfallPoisonVariant.Indulcet));
        s.AdvanceElapsedTime(13 * 60); Assert.True(luck.Value > baseLuck);
        double health = Health(s, 1).Current, stamina = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current;
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(Cast(s, "spell.058").Results).Outcome);
        Assert.Equal(baseEndurance, endurance.Value); Assert.Equal(baseLuck, luck.Value); Assert.Equal(health, Health(s, 1).Current);
        Assert.Equal(stamina, s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current);
        Assert.False(s.State.Poisons.IsAfflicted(s.State.Actors.Player.Actor));
        using var restored = f.Restore(s.CaptureSave()); Assert.False(restored.State.Poisons.IsAfflicted(restored.State.Actors.Player.Actor));
        restored.AdvanceElapsedTime(600); Assert.Equal(baseLuck, restored.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Luck.Value)).Value);
    }

    [Fact]
    public void A_real_cure_delivered_during_elapsed_rounds_removes_all_restrictions_before_remaining_rounds()
    {
        DaggerfallSession? session = null;
        int rounds = 0;
        var inputs = ReadInputs(TestData.RepositoryRoot);
        var catalog = new DaggerfallEffectCatalog([
            DaggerfallParalysisEffects.Definition((_, _, _) => { }),
            .. DaggerfallCureEffects.Definitions(
                target => DaggerfallDiseasePolicy.CureAllDiseases(session!.State.Effects, target),
                actor => session!.State.Poisons.Cure(actor),
                target => DaggerfallParalysisEffects.Cure(session!.State.Effects, target)),
            // The test participant delivers a normal cast on the second admitted round.
            new("scheduled-caller", "scheduled-caller", DaggerfallEffectStacking.Stack, 1, 1,
                MagicRound: effect =>
                {
                    if (++rounds != 2) return;
                    Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(Cast(session!, CureParalysisKey).Results).Outcome);
                    effect.ExpireAfterCurrentRound = true;
                }),
        ]);
        var composition = new DaggerfallSessionComposition(CureDefinitions.Value, inputs, DaggerfallTuning.Defaults) { Effects = catalog };
        List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, inputs);
        var engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service,
            new AppearanceFake(releases), random: RandomMaximum.Create());
        using var s = session = DaggerfallSession.StartNew(engine.Context, composition);
        s.State.Progression.AdvanceTo(0, 100);
        Paralyze(s, "long.one", 2000, 1); Paralyze(s, "long.two", 2001, 1);
        using var json = System.Text.Json.JsonDocument.Parse("{}");
        s.State.Effects.Start(new("round-caller", "scheduled-caller", "test", 1, 1, "scheduled", "Magic", null, 1, 10, json.RootElement));
        Assert.True(s.State.Effects.ControlsFor(1).Movement);
        s.State.Effects.AdvanceElapsedRounds(10);
        Assert.Equal(2, rounds); Assert.Empty(s.State.Effects.Active); Assert.Equal(default, s.State.Effects.ControlsFor(1));
        using var restored = DaggerfallSession.Restore(engine.Context, composition, s.CaptureSave());
        Assert.Empty(restored.State.Effects.Active); restored.State.Effects.AdvanceElapsedRounds(10);
        Assert.Equal(default, restored.State.Effects.ControlsFor(1));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Cure_bindings_accept_every_target_shape_and_reject_nonmagic_elements_before_payment(int subtype)
    {
        using Fixture f = new(); var s = f.Session;
        for (int range = 0; range < 5; range++)
        {
            var original = TestPayload.Definitions.Magic.Spells["spell.059"];
            var spell = original with { RangeType = range, Effects = [new("cure", 3, subtype, 0, 0, 0, 100, 0, 1, 0, 0, 0, 0, 0)] };
            var casting = CastingFor(s, spell, 100);
            Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
            var bundle = casting.Release(1, true).Bundle!; casting.Deliver(bundle, [range == 0 ? 1 : 2000]);
            Assert.Equal(DaggerfallCastOutcome.NoMatch, Assert.Single(bundle.Results).Outcome);
            for (int element = 0; element < 4; element++)
            {
                // Element gates apply to constructed spells; the donor reads classic records unfiltered.
                casting = CastingFor(s, spell with { Element = element, IsCustom = true }, 100);
                double before = Magicka(s).Current;
                Assert.Equal(DaggerfallCastOutcome.UnsupportedEffect, casting.Ready(1, spell.Key).Outcome);
                Assert.Equal(before, Magicka(s).Current);
            }
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Failed_cure_chance_preserves_the_existing_condition(int subtype)
    {
        using Fixture f = new(); var s = f.Session;
        Paralyze(s, "retained", 2000, 1);
        var original = TestPayload.Definitions.Magic.Spells["spell.059"];
        var spell = original with { Effects = [new("cure", 3, subtype, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0)] };
        var casting = CastingFor(s, spell, 100); Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
        var bundle = casting.Release(1, true).Bundle!; casting.Deliver(bundle, [1]);
        Assert.Equal(DaggerfallCastOutcome.ChanceFailed, Assert.Single(bundle.Results).Outcome);
        Assert.Equal("retained", Assert.Single(s.State.Effects.Active).Context.Instance.Value);
    }

    private static bool Matches(DaggerfallActiveEffect effect, int subtype) => subtype switch
    {
        0 => effect.Context.Source.Key is "monster" or "quest",
        1 => effect.Context.Source.Key == DaggerfallPoisonRuntime.SourceKey,
        2 => effect.Definition.Key == "paralyze",
        _ => false,
    };
    private static void Paralyze(DaggerfallSession s, string instance, long caster, long target)
    {
        var setting = TestPayload.Definitions.Magic.Spells["spell.047"].Effects[0];
        var state = System.Text.Json.JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(setting, 1, 0, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        s.State.Effects.Start(new(instance, "paralyze", "spell.paralyze", caster, target, setting.Key, "Magic", null, 1, 10000, state));
    }
    private static DaggerfallLiveSpell Cast(DaggerfallSession s, string key)
    {
        Magicka(s).Maximum.BaseValue = 10000; Magicka(s).SetCurrent(10000);
        s.State.Character.LearnSpell(key); Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell(key).Outcome);
        var bundle = s.Casting.Release(1, true).Bundle!; s.Casting.Deliver(bundle, [1]); return bundle;
    }
    private static Track Magicka(DaggerfallSession s) => s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
    private static Track Health(DaggerfallSession s, long id) => (id == 1 ? s.State.Actors.Player.Stats : s.State.Actors.Get(id).Stats).GetTrack(TrackId.Parse("health"));
    private static DaggerfallCasting CastingFor(DaggerfallSession s, DaggerfallSpellDefinition spell, int draw)
    {
        var catalog = TestPayload.Definitions.Magic with { Spells = new Dictionary<string, DaggerfallSpellDefinition> { [spell.Key] = spell } };
        return new(catalog, s.State.Effects, id => id == 1 ? s.State.Actors.Player.Actor : s.State.Actors.TryGet(id, out var actor) ? actor.Actor : null,
            s.MagicProfile, _ => true, _ => { }, _ => { }, DaggerfallCastingTests.SaveDice.Create(draw), 1, playerKnowsSpell: _ => true);
    }
    private sealed class CertainStrike : ICombatContribution
    { public void Hit(TryHitEvent interaction) => interaction.Hit = true; }

    /// <summary>
    /// No published spell cures paralysis: SPELLS.STD's only Cure Paralyzation slot is Free Action's,
    /// which the donor patches to Free Action. A constructed copy of Cure Poison carries the subtype.
    /// </summary>
    private const string CureParalysisKey = "spell.cure-paralysis";
    private static readonly SharedFixture<DaggerfallDefinitions> CureDefinitions = new(() =>
    {
        System.Text.Json.Nodes.JsonObject root = TestPayload.Sections("magic");
        System.Text.Json.Nodes.JsonArray spells = Spells(root) ?? throw new InvalidOperationException("The payload publishes no spells.");
        System.Text.Json.Nodes.JsonNode cure = spells.Single(spell => spell!["key"]!.GetValue<string>() == "spell.015")!.DeepClone();
        cure["key"] = CureParalysisKey; cure["identity"] = 900; cure["name"] = "Cure Paralyzation";
        cure["effects"]![0]!["key"] = CureParalysisKey + ".effect.1"; cure["effects"]![0]!["subType"] = 2;
        spells.Add(cure);
        return DaggerfallBaseContent.Read(TestPayload.Splice(root.AsObject()));

        static System.Text.Json.Nodes.JsonArray? Spells(System.Text.Json.Nodes.JsonNode? node) => node switch
        {
            System.Text.Json.Nodes.JsonObject value => value.TryGetPropertyValue("spells", out var found) && found is System.Text.Json.Nodes.JsonArray { Count: > 0 } array
                && array[0] is System.Text.Json.Nodes.JsonObject first && first.ContainsKey("identity")
                ? array : value.Select(property => Spells(property.Value)).FirstOrDefault(result => result is not null),
            System.Text.Json.Nodes.JsonArray value => value.Select(Spells).FirstOrDefault(result => result is not null),
            _ => null,
        };
    });

    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSessionComposition _composition = new(CureDefinitions.Value, ReadInputs(TestData.RepositoryRoot), DaggerfallTuning.Defaults);
        internal SpatialFake Spatial = null!;
        internal DaggerfallSession Session { get; }
        internal Fixture()
        {
            Session = DaggerfallSession.StartNew(Engine().Context, _composition);
            Session.State.Progression.AdvanceTo(0, 100);
            foreach (string name in new[] { "health", "magicka", "stamina" })
            { var track = Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(name)); track.Maximum.BaseValue = 10000; track.SetCurrent(10000); }
        }
        private EngineContextFake Engine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, _composition.StartSite);
            Spatial = SpatialFake.Create(_composition.StartSite.SpatialArtifact.Sha256, releases); Spatial.KeepPosition = true;
            return EngineContextFake.Create(content, Spatial.Service, new AppearanceFake(releases), random: RandomMaximum.Create());
        }
        internal DaggerfallSession Restore(RulesetSavePayload payload) => DaggerfallSession.Restore(Engine().Context, _composition, payload);
        public void Dispose() => Session.Dispose();
    }
}
