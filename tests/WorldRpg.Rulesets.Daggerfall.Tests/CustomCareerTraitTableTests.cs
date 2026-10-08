using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class CustomCareerTraitTableTests
{
    [Theory]
    [InlineData("acute-hearing", 1)]
    [InlineData("adrenaline-rush", 4)]
    [InlineData("athleticism", 4)]
    [InlineData("bonus-to-hit:animals", 6)]
    [InlineData("bonus-to-hit:daedra", 3)]
    [InlineData("bonus-to-hit:humanoid", 6)]
    [InlineData("bonus-to-hit:undead", 6)]
    [InlineData("rapid-healing:general", 4)]
    [InlineData("rapid-healing:light", 2)]
    [InlineData("rapid-healing:darkness", 3)]
    [InlineData("regenerate-health:general", 14)]
    [InlineData("regenerate-health:light", 6)]
    [InlineData("regenerate-health:darkness", 10)]
    [InlineData("regenerate-health:immersed", 2)]
    [InlineData("spell-absorption:general", 14)]
    [InlineData("spell-absorption:light", 8)]
    [InlineData("spell-absorption:darkness", 12)]
    [InlineData("damage:sunlight", -10)]
    [InlineData("damage:holy-places", -6)]
    [InlineData("darkness-powered-magery:reduced", -7)]
    [InlineData("darkness-powered-magery:unable", -10)]
    [InlineData("light-powered-magery:reduced", -10)]
    [InlineData("light-powered-magery:unable", -14)]
    [InlineData("inability-to-regen", -14)]
    public void Advertised_special_variants_have_donor_difficulty_and_valid_current_schema(string key, int points)
    {
        var trait = Parse(key); var choices = Choices();
        bool advantage = DaggerfallCustomCareerPolicy.SupportedAdvantages.Contains(trait.Id);
        choices = choices with { Advantages = advantage ? [trait] : [], Disadvantages = advantage ? [] : [trait] };
        Assert.Equal(4 + points, DaggerfallCustomCareerPolicy.Difficulty(choices));
        Assert.Empty(DaggerfallCustomCareerPolicy.Validate(TestPayload.Definitions, choices));
        Assert.Contains(key, DaggerfallCustomCareerPolicy.Options(advantage ? DaggerfallCustomCareerPolicy.SupportedAdvantages : DaggerfallCustomCareerPolicy.SupportedDisadvantages));
    }

    [Theory]
    [InlineData("paralysis", 1)]
    [InlineData("magic", 2)]
    [InlineData("poison", 4)]
    [InlineData("fire", 8)]
    [InlineData("frost", 16)]
    [InlineData("shock", 32)]
    [InlineData("disease", 64)]
    public void Every_tolerance_target_reaches_its_exact_live_career_flag_and_rejects_conflicts(string target, int flag)
    {
        var definitions = TestPayload.Definitions;
        var fallback = definitions.Catalogs.Careers.First();
        foreach (var (id, expected, points) in new[] {
            ("immunity", DaggerfallDiseaseCareerTolerance.Immune, 10),
            ("resistance", DaggerfallDiseaseCareerTolerance.Resistant, 5),
            ("low-tolerance", DaggerfallDiseaseCareerTolerance.LowTolerance, -5),
            ("critical-weakness", DaggerfallDiseaseCareerTolerance.CriticalWeakness, -14) })
        {
            bool advantage = points > 0;
            var choices = Choices() with { Advantages = advantage ? [new(id, target)] : [], Disadvantages = advantage ? [] : [new(id, target)] };
            var career = DaggerfallCustomCareerPolicy.Compile(definitions, choices, fallback).Career;
            Assert.Equal(expected, DaggerfallCareerTolerances.Tolerance(career, flag));
            Assert.Equal(4 + points, DaggerfallCustomCareerPolicy.Difficulty(choices));
            foreach (int other in new[] { 1, 2, 4, 8, 16, 32, 64 }.Where(other => other != flag))
                Assert.Equal(DaggerfallDiseaseCareerTolerance.Normal, DaggerfallCareerTolerances.Tolerance(career, other));
        }
        var conflict = Choices() with { Advantages = [new("immunity", target)], Disadvantages = [new("low-tolerance", target)] };
        Assert.Contains(DaggerfallCustomCareerPolicy.Validate(definitions, conflict), error => error.Contains("cannot both target"));
    }

    [Theory]
    [InlineData("acute-hearing", 1, 0, 0, 0, 0, 0)]
    [InlineData("athleticism", 2, 0, 0, 0, 0, 0)]
    [InlineData("adrenaline-rush", 4, 0, 0, 0, 0, 0)]
    [InlineData("inability-to-regen", 8, 0, 0, 0, 0, 0)]
    [InlineData("damage:sunlight", 16, 0, 0, 0, 0, 0)]
    [InlineData("damage:holy-places", 32, 0, 0, 0, 0, 0)]
    [InlineData("darkness-powered-magery:unable", 0, 1, 0, 0, 0, 0)]
    [InlineData("darkness-powered-magery:reduced", 0, 2, 0, 0, 0, 0)]
    [InlineData("light-powered-magery:unable", 0, 0, 1, 0, 0, 0)]
    [InlineData("light-powered-magery:reduced", 0, 0, 2, 0, 0, 0)]
    [InlineData("rapid-healing:light", 0, 0, 0, 1, 0, 0)]
    [InlineData("rapid-healing:darkness", 0, 0, 0, 2, 0, 0)]
    [InlineData("rapid-healing:general", 0, 0, 0, 4, 0, 0)]
    [InlineData("regenerate-health:light", 0, 0, 0, 0, 1, 0)]
    [InlineData("regenerate-health:darkness", 0, 0, 0, 0, 2, 0)]
    [InlineData("regenerate-health:immersed", 0, 0, 0, 0, 4, 0)]
    [InlineData("regenerate-health:general", 0, 0, 0, 0, 8, 0)]
    [InlineData("spell-absorption:light", 0, 0, 0, 0, 0, 1)]
    [InlineData("spell-absorption:darkness", 0, 0, 0, 0, 0, 2)]
    [InlineData("spell-absorption:general", 0, 0, 0, 0, 0, 4)]
    public void Special_traits_compile_into_the_classic_career_bytes_a_preset_career_carries(string key,
        int abilities, int darkMagery, int lightMagery, int rapidHealing, int regeneration, int absorption)
    {
        var trait = Parse(key); var choices = Choices();
        bool advantage = DaggerfallCustomCareerPolicy.SupportedAdvantages.Contains(trait.Id);
        choices = choices with { Advantages = advantage ? [trait] : [], Disadvantages = advantage ? [] : [trait] };
        var career = DaggerfallCustomCareerPolicy.Compile(TestPayload.Definitions, choices, TestPayload.Definitions.Catalogs.Careers.First()).Career;
        Assert.Equal(new DaggerfallCareerSpecials(abilities, darkMagery, lightMagery, rapidHealing, regeneration, absorption), career.Specials);
        Assert.True(career.Specials.IsClassic);
    }

    private static DaggerfallCustomCareerTrait Parse(string key)
    { var split = key.Split(':'); return new(split[0], split.Length == 1 ? null : split[1]); }
    private static DaggerfallCustomCareerChoices Choices() => DaggerfallCustomCareerChoices.Default(TestPayload.Definitions,
        TestPayload.Definitions.Catalogs.Careers.First()) with { HitPointsPerLevel = 12 };
}
