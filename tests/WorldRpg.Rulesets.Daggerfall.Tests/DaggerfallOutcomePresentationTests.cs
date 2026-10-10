using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallOutcomePresentationTests
{
    [Fact]
    public void Defeat_names_the_actor_by_its_display_name_without_formatting_an_identity_record()
    {
        DaggerfallActorDefinition rat = TestPayload.Definitions.RequireActor(new("rat"));
        PresentationState state = new(string.Empty, DaggerfallOutcomePresentation.MessageLifetimeSeconds);
        DaggerfallOutcomePresentation outcome = new(state, new Dictionary<long, DaggerfallActorDefinition> { [2000] = rat }, TestPayload.Definitions.Mobiles);

        outcome.React(new ActorDiedFact(2000, 1, DaggerfallDamageCause.PhysicalAttack, 10, 2, 1, 1));

        Assert.Equal($"Defeated Rat for 2 damage; gained {rat.Rewards.ExperienceReward} XP", state.LastOutcome);
    }

    [Fact]
    public void Combat_lines_name_an_encounter_by_its_display_name()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition nightblade = definitions.RequireActor(new("encounter-nightblade"));
        PresentationState state = new(string.Empty, DaggerfallOutcomePresentation.MessageLifetimeSeconds);
        DaggerfallOutcomePresentation outcome = new(state, new Dictionary<long, DaggerfallActorDefinition> { [2000] = nightblade },
            definitions.Mobiles, definitions.Text);

        outcome.React(new AttackHitFact(2000, DaggerfallActorIdentity.PlayerEntityId, 7, 7, 0, EnemyAttack: true, 1, 1));
        Assert.Equal("Nightblade hit you for 7 damage", state.LastOutcome);
        outcome.React(new AttackMissedFact(DaggerfallActorIdentity.PlayerEntityId, 2000, 1, 50, EnemyAttack: false, 1, 2));
        Assert.Equal("Missed Nightblade", state.LastOutcome);
        outcome.React(new ActorDiedFact(2000, 1, DaggerfallDamageCause.PhysicalAttack, 10, 2, 1, 3));
        Assert.StartsWith("Defeated Nightblade for 2 damage", state.LastOutcome);
    }
}
