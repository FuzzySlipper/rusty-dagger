using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>What a new character's background does to standing, applied to the social state with no session.</summary>
public sealed class DaggerfallSocialBackgroundTests
{
    [Fact]
    public void A_background_grants_its_faction_and_social_reputations()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player,
            DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
        DaggerfallCharacterState character = new(definitions, stats, player);
        character.BeginFreshChoices(RandomMinimum.Create());
        DaggerfallCharacterBackgroundSave rolled = character.Pending!.Background!;
        DaggerfallBiographyDefinition biography = definitions.Biographies.Biographies.Single(value => value.ClassIndex == rolled.BiographyClassIndex);
        DaggerfallBiographyAnswerSave[] answers = [.. biography.Questions.Select(question => new DaggerfallBiographyAnswerSave(question.Number, question.Answers[0].Letter))];
        foreach (DaggerfallBiographyEffectKind kind in new[] { DaggerfallBiographyEffectKind.FactionReputation, DaggerfallBiographyEffectKind.SocialReputation })
        {
            var (question, answer) = biography.Questions.SelectMany(question => question.Answers.Select(answer => (question, answer)))
                .First(value => value.answer.Effects.Any(effect => effect.Kind == kind));
            answers[Array.FindIndex(answers, value => value.Question == question.Number)] = new(question.Number, answer.Letter);
        }
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer("class00");
        DaggerfallCharacterBackgroundSave background = DaggerfallCharacterBackgroundPolicy.Update(definitions, career, character.Pending.ToIdentity(), rolled, answers,
            [new DaggerfallCreationAllocationSave(career.Attributes[0], rolled.AttributeBonusPool)],
            [new DaggerfallCreationAllocationSave(career.PrimarySkills[0], 6), new DaggerfallCreationAllocationSave(career.MajorSkills[0], 6), new DaggerfallCreationAllocationSave(career.MinorSkills[0], 6)]);
        var factions = DaggerfallCharacterBackgroundPolicy.FactionReputations(definitions, career, background).ToArray();
        var groups = DaggerfallCharacterBackgroundPolicy.SocialReputations(definitions, career, background).ToArray();
        Assert.NotEmpty(factions);
        Assert.NotEmpty(groups);
        DaggerfallSocialState social = new(definitions.Factions);
        int faction = factions[0].Faction;

        social.ApplyBackground(definitions, career, background);

        Assert.NotEqual(0, social.FactionReputation(faction));
        foreach (var group in groups.GroupBy(value => value.Group))
            Assert.Equal(group.Sum(value => value.Amount), social.PersonalReputation(group.Key));
    }
}
