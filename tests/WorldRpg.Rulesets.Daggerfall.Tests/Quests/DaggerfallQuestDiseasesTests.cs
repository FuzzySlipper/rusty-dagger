using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;

using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.DaggerfallQuestEnvironmentTests;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>A quest's diseases built without the session, over a disease owner and effect lifecycle of their own.</summary>
public sealed class DaggerfallQuestDiseasesTests
{
    /// <summary>The player and the disease owner over its effects, as Afflictions' disease tests build them.</summary>
    internal sealed class Stage : IDisposable
    {
        internal Stage()
        {
            DaggerfallActorDefinition player = Definitions.RequireActor(new DaggerfallActorId("player"));
            StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player,
                DaggerfallPlayerVitals.Initial(player.Stats, Definitions.Catalogs.RequireCareer("class00")));
            Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health", DaggerActorFactory.PlayerCapabilities);
            DaggerfallCharacterState character = new(Definitions, stats, player);
            IRandomService random = RandomMaximum.Create();
            DaggerfallWorldTime time = new(DaggerfallCalendar.FromAbsoluteSeconds(100L * DaggerfallCalendar.SecondsPerDay), 0d, 12d);
            Effects = new(Actors, new DaggerfallEffectCatalog(DaggerfallDiseasePolicy.Definitions(random,
                () => time.Calendar.DayNumber, () => character.Career)));
            Diseases = new(Definitions, new DaggerfallDiseases(Effects, Actors, random, time, character, new CombatResolution(),
                new PresentationState("Ready", 5d), Definitions), Effects, DaggerfallActorIdentity.PlayerEntityId);
        }

        internal DaggerfallDefinitions Definitions { get; } = TestPayload.Definitions;
        internal ActorsState Actors { get; } = new();
        internal DaggerfallEffectLifecycle Effects { get; }
        internal DaggerfallQuestDiseases Diseases { get; }
        internal string Pox => Definitions.QuestSources.Tables.Diseases.Lookup.First(pair => pair.Value == 0).Key;

        public void Dispose() => Actors.Dispose();
    }

    [Fact]
    public void A_quest_disease_bypasses_the_saving_throw_each_time_it_is_given_and_the_cure_ends_it()
    {
        using Stage stage = new();
        stage.Actors.Player.Progression.AdvanceTo(0, 2);
        DaggerfallQuestRuntimeInstance instance = Instance();

        Assert.True(stage.Diseases.Knows(stage.Pox));
        Assert.False(stage.Diseases.Knows("MissingDisease"));
        stage.Diseases.Apply(instance, Operation(DaggerfallQuestTaskOperationKind.MakePcDiseased, stage.Pox));
        Assert.Single(stage.Effects.Active, effect => effect.Definition.Key == "disease-witches-pox"
            && effect.Context.Instance.Value == "quest:quest:1:disease:1:0");

        stage.Diseases.Apply(instance, Operation(DaggerfallQuestTaskOperationKind.CurePcDisease, stage.Pox));
        Assert.DoesNotContain(stage.Effects.Active, effect => effect.Definition.Key == "disease-witches-pox");
        Assert.Throws<NotSupportedException>(() => stage.Diseases.Apply(instance, Operation(DaggerfallQuestTaskOperationKind.MakePcDiseased, "MissingDisease")));
    }
}
