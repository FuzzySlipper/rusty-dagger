using System.Numerics;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One touch spell at its cast frame: who casts it, from where and which way, and whom an AI caster's behavior already admitted in reach.</summary>
/// <param name="CasterId">The casting actor.</param>
/// <param name="Eye">Where the touch starts.</param>
/// <param name="Forward">The unit direction the caster reaches along.</param>
/// <param name="Admitted">The target an enemy's behavior already admitted in touch reach, or none for an aimed player cast.</param>
internal readonly record struct DaggerfallTouchCast(long CasterId, Vector3 Eye, Vector3 Forward, long? Admitted);

/// <summary>What a touch reaches: whether the spell is released at all, and every actor it reaches.</summary>
internal readonly record struct DaggerfallTouchReach(bool Releases, IReadOnlyList<long> Targets)
{
    internal static DaggerfallTouchReach Refused { get; } = new(false, []);
}
