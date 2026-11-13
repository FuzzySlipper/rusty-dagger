using WorldRpg.Kit.Combat;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The upright body every site actor presents to spell and harm contact, and where its eye sits.</summary>
internal static class DaggerfallContactBody
{
    internal static readonly ActorBodyEnvelope Envelope = new(.3f, 1.8f);

    /// <summary>A site actor's eye sits near the top of its body envelope.</summary>
    internal const float EyeFraction = .9f;
}
