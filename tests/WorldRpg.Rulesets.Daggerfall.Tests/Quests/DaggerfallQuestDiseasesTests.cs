using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.DaggerfallQuestEnvironmentTests;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>A quest's diseases built without the session, over the fixture's disease owner and active effects.</summary>
public sealed class DaggerfallQuestDiseasesTests
{
    [Fact]
    public void A_quest_disease_bypasses_the_saving_throw_each_time_it_is_given_and_the_cure_ends_it()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallState state = fixture.Session.State;
        state.Progression.AdvanceTo(0, 2);
        DaggerfallQuestDiseases diseases = new(fixture.Definitions, fixture.Session.Diseases, state.Effects, state.Actors.Player.DurableId);
        string pox = fixture.Definitions.QuestSources.Tables.Diseases.Lookup.First(pair => pair.Value == 0).Key;
        DaggerfallQuestRuntimeInstance instance = Instance();

        Assert.True(diseases.Knows(pox));
        Assert.False(diseases.Knows("MissingDisease"));
        diseases.Apply(instance, Operation(DaggerfallQuestTaskOperationKind.MakePcDiseased, pox));
        Assert.Single(state.Effects.Active, effect => effect.Definition.Key == "disease-witches-pox"
            && effect.Context.Instance.Value == "quest:quest:1:disease:1:0");

        diseases.Apply(instance, Operation(DaggerfallQuestTaskOperationKind.CurePcDisease, pox));
        Assert.DoesNotContain(state.Effects.Active, effect => effect.Definition.Key == "disease-witches-pox");
        Assert.Throws<NotSupportedException>(() => diseases.Apply(instance, Operation(DaggerfallQuestTaskOperationKind.MakePcDiseased, "MissingDisease")));
    }
}
