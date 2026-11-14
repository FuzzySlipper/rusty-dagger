namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestOfferTuning(int SocialProviderChancePercent, int CastleProviderChancePercent)
{
    internal DaggerfallQuestOfferTuning Validate() => SocialProviderChancePercent is >= 0 and <= 100 && CastleProviderChancePercent is >= 0 and <= 100
        ? this : throw new ArgumentException("Quest provider chances must be percentages.");
}
