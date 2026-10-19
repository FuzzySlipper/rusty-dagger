using WorldRpg.Kit.Combat;
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

    [Fact]
    public void Contact_outcomes_each_have_their_own_line_from_the_players_side()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        PresentationState state = new(string.Empty, DaggerfallOutcomePresentation.MessageLifetimeSeconds);
        DaggerfallOutcomePresentation outcome = new(state, new Dictionary<long, DaggerfallActorDefinition> { [2000] = definitions.RequireActor(new("rat")) },
            definitions.Mobiles, definitions.Text);
        const long player = DaggerfallActorIdentity.PlayerEntityId;

        // A swing at empty air is its own feedback and writes no line; one aimed at someone out of reach says so.
        outcome.React(new AttackWhiffedFact(player, null, EnemyAttack: false, 1, 1));
        Assert.Equal(string.Empty, state.LastOutcome);
        outcome.React(new AttackWhiffedFact(player, 2000, EnemyAttack: false, 1, 1));
        Assert.Equal("Your blow meets nothing; Rat is out of reach", state.LastOutcome);
        outcome.React(new AttackWhiffedFact(2000, player, EnemyAttack: true, 1, 2));
        Assert.Equal("Rat's blow misses you", state.LastOutcome);
        outcome.React(new AttackNegatedFact(player, 2000, HarmNegation.Deflected, EnemyAttack: false, 1, 3));
        Assert.Equal("Rat deflects your blow", state.LastOutcome);
        outcome.React(new AttackNegatedFact(2000, player, HarmNegation.Deflected, EnemyAttack: true, 1, 4));
        Assert.Equal("You deflect Rat's blow", state.LastOutcome);
        outcome.React(new AttackHitFact(player, 2000, 6, 0, 0, EnemyAttack: false, 1, 5) { Mitigated = new(0, 6) });
        Assert.Equal("Rat's armor absorbs your blow", state.LastOutcome);
        outcome.React(new AttackHitFact(2000, player, 9, 4, 0, EnemyAttack: true, 1, 6) { Mitigated = new(2, 3) });
        Assert.Equal("Rat hit you for 4 damage; 2 deflected, 3 absorbed", state.LastOutcome);
        // A classic blow carries no mitigation and reads as it always has.
        outcome.React(new AttackHitFact(player, 2000, 7, 7, 0, EnemyAttack: false, 1, 7));
        Assert.Equal("Hit Rat for 7 damage", state.LastOutcome);
    }
}
