using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The player's diseases, built without the session: who an exposure lands on, the cures and the symptoms line.</summary>
public sealed class DaggerfallDiseasesTests
{
    private sealed class Fixture : IDisposable
    {
        internal Fixture()
        {
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
            StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player,
                DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
            Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health", DaggerActorFactory.PlayerCapabilities);
            Actors.CreateActor(2, new EntityTypeId("nymph"), DaggerCombatFixture.Stats(100d, 600d),
                new ActorPose(new WorldPoint(1f, 0f, 0f), 0f), "health", DaggerActorFactory.NonPlayerCapabilities);
            DaggerfallCharacterState character = new(definitions, stats, player);
            IRandomService random = RandomMaximum.Create();
            DaggerfallWorldTime time = new(DaggerfallCalendar.FromAbsoluteSeconds(100L * DaggerfallCalendar.SecondsPerDay), 0d, 12d);
            Effects = new(Actors, new DaggerfallEffectCatalog(DaggerfallDiseasePolicy.Definitions(random,
                () => time.Calendar.DayNumber, () => character.Career)));
            Diseases = new DaggerfallDiseases(Effects, Actors, random, time, character, new CombatResolution(), Presentation, definitions);
        }

        internal ActorsState Actors { get; } = new();
        internal DaggerfallEffectLifecycle Effects { get; }
        internal PresentationState Presentation { get; } = new("Ready", 5d);
        internal DaggerfallDiseases Diseases { get; }

        public void Dispose() => Actors.Dispose();
    }

    private static DaggerfallDiseaseExposure Exposure(string instance, long target, params DaggerfallClassicDisease[] candidates) =>
        new(instance, "test-exposure", null, target, candidates);

    [Fact]
    public void An_exposure_lands_only_on_a_player_past_the_first_level()
    {
        using Fixture fixture = new();
        Assert.Equal(DaggerfallDiseaseAdmission.TargetIsNotPlayer,
            fixture.Diseases.Inflict(Exposure("monster", 2, DaggerfallClassicDisease.BloodRot)));
        Assert.Equal(DaggerfallDiseaseAdmission.LevelOneImmune,
            fixture.Diseases.Inflict(Exposure("first-level", DaggerfallActorIdentity.PlayerEntityId, DaggerfallClassicDisease.BloodRot)));
        Assert.Empty(fixture.Effects.Active);

        fixture.Actors.Player.Progression.AdvanceTo(500, 2);
        Assert.Equal(DaggerfallDiseaseAdmission.Started,
            fixture.Diseases.Inflict(Exposure("levelled", DaggerfallActorIdentity.PlayerEntityId, DaggerfallClassicDisease.BloodRot)));
        Assert.Single(fixture.Effects.Active);
    }

    [Fact]
    public void A_cure_ends_one_disease_or_all_of_them_on_the_player()
    {
        using Fixture fixture = new();
        fixture.Actors.Player.Progression.AdvanceTo(500, 2);
        long player = DaggerfallActorIdentity.PlayerEntityId;
        fixture.Diseases.Inflict(Exposure("rot", player, DaggerfallClassicDisease.BloodRot));
        fixture.Diseases.Inflict(Exposure("fever", player, DaggerfallClassicDisease.BrainFever));
        fixture.Diseases.Inflict(Exposure("plague", player, DaggerfallClassicDisease.Plague));

        Assert.Equal(1, fixture.Diseases.Cure(DaggerfallClassicDisease.BloodRot));
        Assert.Equal(0, fixture.Diseases.Cure(DaggerfallClassicDisease.BloodRot));
        Assert.Equal(2, fixture.Diseases.CureAll());
        Assert.Empty(fixture.Effects.Active);
    }

    [Fact]
    public void Symptoms_that_act_tell_the_player_and_nobody_else()
    {
        using Fixture fixture = new();
        fixture.Diseases.SymptomsActed(2);
        Assert.Equal("Ready", fixture.Presentation.LastOutcome);

        fixture.Diseases.SymptomsActed(DaggerfallActorIdentity.PlayerEntityId);
        Assert.Equal(string.Concat(TestPayload.Definitions.Text.Require(new(DaggerfallTextKind.Internal, "youFeelSomewhatBad")).TextRuns),
            fixture.Presentation.LastOutcome);
        Assert.NotEqual("Ready", fixture.Presentation.LastOutcome);
    }
}
