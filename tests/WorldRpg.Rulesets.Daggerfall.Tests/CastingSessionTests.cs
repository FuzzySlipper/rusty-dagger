using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class CastingSessionTests
{
    [Fact]
    public void Known_published_spell_uses_real_composition_feedback_skill_save_and_reconstructed_effect_sources()
    {
        using Fixture f = new("spell.075");
        var s = f.Session;
        Assert.Equal(DaggerfallCastOutcome.UnknownSpell, s.ReadyPlayerSpell(f.Spell.Key).Outcome);
        s.State.Character.LearnSpell(f.Spell.Key);
        Track magicka = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        magicka.Maximum.BaseValue = 1000; magicka.SetCurrent(1000);
        double strength = s.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).Value;
        Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell(f.Spell.Key).Outcome);
        var result = s.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, result.Outcome);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(result.Bundle!.Results).Outcome);
        Assert.True(s.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).Value > strength);
        string school = TestPayload.Definitions.Magic.RequireEffectCost(f.Spell.Effects[0]).School;
        Assert.Equal(1, s.State.Progression.SkillUses[school]);
        Assert.Equal(1, f.Rounds);
        s.ReadyPlayerSpell(f.Spell.Key);
        var save = s.CaptureSave();
        var payload = DaggerfallSavePayload.Read(save);
        Assert.Equal(2, payload.NextCastSequence);
        Assert.Equal(f.Spell.Key,s.Casting.ReadyFor(1)!.SpellKey);
        using var restored = f.Restore(save);
        Assert.Equal(f.Spell.Key,restored.Casting.ReadyFor(1)!.SpellKey);
        Assert.Equal(2, restored.Casting.NextSequence);
        Assert.Equal(1, f.Rounds); // Restoring the compiled contribution does not re-run MagicRound.
        Assert.Equal(s.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).Value,
            restored.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).Value);
        Assert.Equal(1, restored.State.Progression.SkillUses[school]);
        Assert.Same(restored.State.Character.Race, restored.MagicProfile(1).PlayerRace);
        restored.State.Effects.CancelActorReferences(1);
        Assert.Equal(strength, restored.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).Value);
    }

    [Theory]
    [InlineData("high-elf", true)]
    [InlineData("khajiit", false)]
    public void Incoming_compiled_paralysis_reads_selected_normalized_race_and_live_npc_level(string race, bool immune)
    {
        using Fixture f = new("spell.047", paralysis: true);
        var s = f.Session;
        s.State.Character.BeginChoices();
        var choices = s.State.Character.Pending!;
        s.State.Character.ReplacePending(choices with { RaceId = race, FaceIndex = 0 });
        s.State.Character.CommitChoices();
        long enemy = s.State.Actors.All.First(actor => !actor.IsDefeated).DurableId;
        var magicka = s.State.Actors.Get(enemy).Stats.GetTrack(TrackId.Parse("magicka"));
        magicka.Maximum.BaseValue = 1000; magicka.SetCurrent(1000);
        Assert.Null(s.MagicProfile(enemy).PlayerRace);
        Assert.Equal(DaggerfallCastOutcome.Ready, s.Casting.Ready(enemy, f.Spell.Key).Outcome);
        f.Spatial.CapsuleCastHit = _ => default(SpatialHit) with { Present = true, Kind = SpatialHitKind.Entity, Entity = s.State.Actors.Player.Actor.Entity.Value };
        var result = s.ReleaseReadySpell(enemy, Vector3.UnitZ);
        Assert.Equal(immune ? DaggerfallCastOutcome.Immune : DaggerfallCastOutcome.Applied, Assert.Single(result.Bundle!.Results).Outcome);
        Assert.Equal(Math.Max(1, s.DefinitionsByActor[enemy].Level ?? 1), result.Bundle.CasterLevel);
        var sweep = Assert.Single(f.Spatial.CapsuleCastRequests);
        Assert.Equal(.25, sweep.Radius); Assert.Equal(0, sweep.HalfHeight); Assert.Equal(3f, sweep.Translation.Length());
        Assert.DoesNotContain(sweep.Entities.ToArray(), collider => collider.Entity == s.State.Actors.Get(enemy).Actor.Entity.Value);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(race, restored.MagicProfile(1).PlayerRace!.Id);
    }

    [Fact]
    public void A_used_item_effect_outlives_its_item_breaking_and_ends_only_when_the_item_is_destroyed()
    {
        using Fixture f = new("spell.075");
        var s = f.Session;
        var equipped = s.State.Equipment.Read().Assignments.Select(value => value.Item.EntityId).ToHashSet();
        var item = s.State.Inventory.Read().UniqueItems.First(item => !equipped.Contains(item.Entity.Value)
            && s.State.ItemInstances.RequireUnique(s.State.Inventory.GetDurableItemId(item.Entity).Value).MaximumCondition > 0);
        ulong id = s.State.Inventory.GetDurableItemId(item.Entity).Value;
        var metadata = s.State.ItemInstances.RequireUnique(id);
        s.State.ItemInstances.ReplaceUnique(id, metadata with { CurrentCondition = 10, MaximumCondition = 10 });
        Assert.Equal(DaggerfallCastOutcome.Ready, s.Casting.Ready(1, f.Spell.Key, id).Outcome); // Item source doesn't require learned spell.
        var result = s.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(0, result.Bundle!.Cost);
        Assert.Single(s.State.Effects.Active);
        // The donor ties only held bundles to their item: a used item's effect survives its break
        // and a save carrying it restores, while the broken item cannot be used again.
        s.State.ItemInstances.ReplaceUnique(id, s.State.ItemInstances.RequireUnique(id) with { CurrentCondition = 0 });
        Assert.Single(s.State.Effects.Active);
        Assert.Equal(DaggerfallCastOutcome.SourceUnavailable, s.Casting.Ready(1, f.Spell.Key, id).Outcome);
        using (var restored = f.Restore(s.CaptureSave())) Assert.Single(restored.State.Effects.Active);
        // Destroying the item ends what names it, since no effect may name an item that no longer exists.
        s.State.ItemInstances.RemoveUnique(id);
        Assert.Empty(s.State.Effects.Active);
    }

    [Theory]
    [InlineData("MR")]
    [InlineData("RP")]
    [InlineData("RD")]
    public void Actual_cast_profiles_keep_committed_biography_modifiers_distinct_and_player_only_through_restore(string modifier)
    {
        using Fixture f = new("spell.047",paralysis:true,random:DaggerfallCastingTests.SaveDice.Create(70));
        var s = f.Session; var character = s.State.Character;
        character.BeginChoices(RandomMinimum.Create());
        var draft = character.Pending!; var rolled = draft.Background!;
        var career = character.Career;
        var biography = TestPayload.Definitions.Biographies.Biographies.Single(value => value.ClassIndex == rolled.BiographyClassIndex);
        var answers = biography.Questions.Select(question => new DaggerfallBiographyAnswerSave(question.Number,
            (question.Answers.FirstOrDefault(answer => answer.Effects.Any(effect => effect.Kind == DaggerfallBiographyEffectKind.BiographyModifier && effect.First == modifier))
                ?? question.Answers[0]).Letter)).ToArray();
        var allocated = DaggerfallCharacterBackgroundPolicy.Update(TestPayload.Definitions,career,draft.ToIdentity(),rolled,answers,
            [new(career.Attributes[0],rolled.AttributeBonusPool)],
            [new(career.PrimarySkills[0],6),new(career.MajorSkills[0],6),new(career.MinorSkills[0],6)]);
        character.ReplacePending(draft with { Background = allocated }); character.CommitChoices();
        var profile = s.MagicProfile(1);
        Assert.Equal(allocated.Modifiers.MagicResistance,profile.BiographyMagicResistance);
        Assert.Equal(allocated.Modifiers.PoisonResistance,profile.BiographyPoisonResistance);
        Assert.Equal(allocated.Modifiers.DiseaseResistance,profile.BiographyDiseaseResistance);
        Assert.NotEqual(0,modifier == "MR" ? profile.BiographyMagicResistance : modifier == "RP" ? profile.BiographyPoisonResistance : profile.BiographyDiseaseResistance);
        long enemy = s.State.Actors.All.First(actor => !actor.IsDefeated).DurableId;
        var enemyProfile = s.MagicProfile(enemy);
        Assert.Equal(0,enemyProfile.BiographyMagicResistance); Assert.Equal(0,enemyProfile.BiographyPoisonResistance); Assert.Equal(0,enemyProfile.BiographyDiseaseResistance);
        var magicka = s.State.Actors.Get(enemy).Stats.GetTrack(TrackId.Parse("magicka"));
        magicka.Maximum.BaseValue = 1000; magicka.SetCurrent(1000);
        s.Casting.Ready(enemy,f.Spell.Key);
        var cast = s.Casting.Release(enemy,true).Bundle!;
        s.Casting.Deliver(cast,[1]);
        int expected = DaggerfallMagicAdmissionPolicy.SavingThrow(new(true,true,false,
            DaggerfallMagicAllowedElements.Magic,DaggerfallMagicBundleElement.Magic),profile,() => 70);
        Assert.InRange(expected,1,99);
        Assert.Equal(expected,Assert.Single(cast.Results).SavePercent);
        using var restored = f.Restore(s.CaptureSave());
        var rebuilt = restored.MagicProfile(1);
        Assert.Equal(profile.BiographyMagicResistance,rebuilt.BiographyMagicResistance);
        Assert.Equal(profile.BiographyPoisonResistance,rebuilt.BiographyPoisonResistance);
        Assert.Equal(profile.BiographyDiseaseResistance,rebuilt.BiographyDiseaseResistance);
        Assert.DoesNotContain(character.ReadCreation().Background!.UnsupportedEffects, value => value.Contains("magic-resistance"));
    }

    [Fact]
    public void Ranged_bundle_waits_for_real_collision_and_source_removal_is_terminal_once()
    {
        using Fixture f = new("spell.027");
        var s = f.Session;
        s.State.Character.LearnSpell(f.Spell.Key);
        var magicka = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        magicka.Maximum.BaseValue = 1000; magicka.SetCurrent(1000);
        s.ReadyPlayerSpell(f.Spell.Key);
        var release = s.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.Released, release.Outcome);
        Assert.Empty(s.State.Effects.Active);
        Assert.Equal(DaggerfallCastOutcome.Released, s.DeliverSpellImpact(release.Bundle!, Vector3.Zero, Vector3.UnitZ).Outcome);
        long target = s.State.Actors.All.First(actor => !actor.IsDefeated).DurableId;
        f.Spatial.FloorHit = _ => default(SpatialHit) with { Present = true, Kind = SpatialHitKind.Entity, Entity = s.State.Actors.Get(target).Actor.Entity.Value };
        var applied = s.DeliverSpellImpact(release.Bundle!, Vector3.Zero, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, applied.Outcome);
        Assert.Single(applied.Bundle!.Results);
        Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered, s.DeliverSpellImpact(release.Bundle!, Vector3.Zero, Vector3.UnitZ).Outcome);
    }

    private sealed class Fixture : IDisposable
    {
        internal DaggerfallSpellDefinition Spell { get; }
        internal DaggerfallSession Session { get; }
        internal SpatialFake Spatial { get; private set; } = null!;
        internal DaggerfallSessionComposition Composition { get; }
        internal int Rounds;
        private readonly IRandomService _random;
        internal Fixture(string key, bool paralysis = false, IRandomService? random = null)
        {
            _random = random ?? RandomMaximum.Create();
            Spell = TestPayload.Definitions.Magic.Spells[key];
            // The fixture supplies compiled contributions, never a second gameplay implementation.
            // Actual spell family bodies remain owned by their explicit follow-up tasks.
            IEnumerable<IActiveEffectContribution> Apply(DaggerfallActiveEffect effect)
            {
                var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)!;
                Stat strength = effect.Target.Get<StatsComponent>().GetStat(StatId.Parse("strength"));
                var handle = strength.AddModifier(Math.Max(1,state.Amount));
                return [new DelegateActiveEffectContribution(() => strength.RemoveModifier(handle))];
            }
            IEnumerable<IActiveEffectContribution> Resume(DaggerfallActiveEffect effect)
            {
                Stat strength = effect.Target.Get<StatsComponent>().GetStat(StatId.Parse("strength"));
                var handles = effect.Target.Get<DaggerfallRestoredStats>().ModifierHandles["strength"];
                var handle = Assert.Single(handles);
                return [new DelegateActiveEffectContribution(() => strength.RemoveModifier(handle))];
            }
            var definitions = Spell.Effects.DistinctBy(effect => (effect.Type,effect.SubType)).Select(effect =>
                new DaggerfallEffectDefinition($"compiled-{effect.Type}-{effect.SubType}", $"compiled-{effect.Type}-{effect.SubType}",
                    DaggerfallEffectStacking.Stack, 20, 1, Apply, _ => Rounds++, Resume, Feedback:DaggerfallEffectFeedback.MagicSparkle,
                    Spell:new(effect.Type,effect.SubType,SupportsDuration:true,SupportsMagnitude:!paralysis,IsParalysis:paralysis))).ToArray();
            Composition = new(TestPayload.Definitions, ReadInputs(TestData.RepositoryRoot), DaggerfallTuning.Defaults) { Effects = new(definitions) };
            Session = DaggerfallSession.StartNew(Engine().Context, Composition);
        }
        private EngineContextFake Engine()
        {
            List<string> releases = [];
            var content = new ContentFake(releases); PopulateContent(content, Composition.StartSite);
            Spatial = SpatialFake.Create(Composition.StartSite.SpatialArtifact.Sha256, releases);
            return EngineContextFake.Create(content, Spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service, random:_random);
        }
        internal DaggerfallSession Restore(WorldRpg.Kit.RulesetSavePayload save) => DaggerfallSession.Restore(Engine().Context, Composition, save);
        public void Dispose() => Session.Dispose();
    }
}
