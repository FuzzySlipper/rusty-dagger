using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The donor's lines when an effect from someone else takes hold of the player.</summary>
public sealed class EffectStartMessageTests
{
    private const long Player = DaggerfallActorIdentity.PlayerEntityId;

    [Theory]
    [InlineData("drain-strength", "You feel drained.")]
    [InlineData("paralyze", "You are paralyzed.")]
    [InlineData("silence", "You are silenced.")]
    [InlineData("slowfall", "Slow fall active.")]
    [InlineData("regenerate", "You are regenerating.")]
    [InlineData("invisibility-true", "You are invisible.")]
    [InlineData("chameleon-normal", "You are blending.")]
    [InlineData("shadow-true", "You are a shade.")]
    public void An_effect_another_source_starts_on_the_player_is_announced(string effectKey, string line)
    {
        PresentationState presentation = new(string.Empty, DaggerfallOutcomePresentation.MessageLifetimeSeconds);
        DaggerfallOutcomePresentation outcomes = new(presentation, new Dictionary<long, DaggerfallActorDefinition>());

        outcomes.React(new MagicEffectFact(Outcome(DaggerfallEffectOutcomeKind.Started, effectKey, Player, caster: 2)));

        Assert.Equal(line, presentation.LastOutcome);
    }

    [Fact]
    public void Only_a_drain_speaks_again_when_a_like_effect_deepens_it()
    {
        PresentationState presentation = new(string.Empty, DaggerfallOutcomePresentation.MessageLifetimeSeconds);
        DaggerfallOutcomePresentation outcomes = new(presentation, new Dictionary<long, DaggerfallActorDefinition>());

        outcomes.React(new MagicEffectFact(Outcome(DaggerfallEffectOutcomeKind.Refreshed, "silence", Player, caster: 2)));
        Assert.Equal(string.Empty, presentation.LastOutcome);
        outcomes.React(new MagicEffectFact(Outcome(DaggerfallEffectOutcomeKind.Refreshed, "drain-agility", Player, caster: 2)));
        Assert.Equal("You feel drained.", presentation.LastOutcome);
    }

    [Fact]
    public void The_players_own_casts_and_effects_on_others_are_not_announced()
    {
        PresentationState presentation = new(string.Empty, DaggerfallOutcomePresentation.MessageLifetimeSeconds);
        DaggerfallOutcomePresentation outcomes = new(presentation, new Dictionary<long, DaggerfallActorDefinition>());

        outcomes.React(new MagicEffectFact(Outcome(DaggerfallEffectOutcomeKind.Started, "regenerate", Player, caster: Player)));
        outcomes.React(new MagicEffectFact(Outcome(DaggerfallEffectOutcomeKind.Started, "paralyze", 2, caster: Player)));
        outcomes.React(new MagicEffectFact(Outcome(DaggerfallEffectOutcomeKind.Started, "heal-health", Player, caster: 2)));

        Assert.Equal(string.Empty, presentation.LastOutcome);
    }

    private static DaggerfallEffectOutcome Outcome(DaggerfallEffectOutcomeKind kind, string key, long target, long caster) =>
        new(kind, "instance", key, target) { CasterId = caster };
}
