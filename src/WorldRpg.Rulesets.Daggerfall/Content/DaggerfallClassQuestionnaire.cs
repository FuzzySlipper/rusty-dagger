namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallClassAnswer(string Text, int Archetype);
internal sealed record DaggerfallClassQuestion(int Number, string Text, IReadOnlyList<DaggerfallClassAnswer> Answers);
internal sealed record DaggerfallClassRecommendation(int Warrior, int Rogue, int Mage, string CareerId);
internal sealed record DaggerfallClassQuestionnaire(IReadOnlyList<DaggerfallClassQuestion> Questions,
    IReadOnlyList<DaggerfallClassRecommendation> Recommendations);
