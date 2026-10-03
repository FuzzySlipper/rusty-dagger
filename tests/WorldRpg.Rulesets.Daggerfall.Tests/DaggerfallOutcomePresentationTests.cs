using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallOutcomePresentationTests
{
    [Fact]
    public void Defeat_names_the_actor_without_formatting_an_identity_record()
    {
        DaggerfallActorDefinition rat = TestPayload.Definitions.RequireActor(new("rat"));
        PresentationState state = new(string.Empty);
        DaggerfallOutcomePresentation outcome = new(state, new Dictionary<long, DaggerfallActorDefinition> { [2000] = rat });

        outcome.React(new ActorDiedFact(2000, 1, DaggerfallDamageCause.PhysicalAttack, 10, 2, 1, 1));

        Assert.Equal($"Defeated rat for 2 damage; gained {rat.Rewards.ExperienceReward} XP", state.LastOutcome);
    }
}
