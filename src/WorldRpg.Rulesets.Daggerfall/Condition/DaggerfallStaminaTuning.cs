using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// The player's combat Stamina: how big the pool is, how it refills, and how a rising Fatigue squeezes both. Fatigue
/// here is the share of the classic pool used up, from 0 rested to 1 at collapse.
/// </summary>
/// <param name="Base">The pool before attributes.</param>
/// <param name="PerAttributePoint">What each point of Strength and of Endurance adds to the pool.</param>
/// <param name="RefillPerSecond">How fast Stamina refills.</param>
/// <param name="QuietSeconds">How long after the last spend the refill starts.</param>
/// <param name="FatiguePerSwing">The classic pool's cost of each attack, in its stored units.</param>
/// <param name="ShrinkFromFatigue">The Fatigue past which the pool shrinks.</param>
/// <param name="ShrunkScale">The share of the pool left at collapse.</param>
/// <param name="SlowFromFatigue">The Fatigue past which the refill slows.</param>
/// <param name="SlowedScale">The share of the refill rate left at collapse.</param>
internal sealed record DaggerfallStaminaTuning(double Base, double PerAttributePoint, double RefillPerSecond, double QuietSeconds, double FatiguePerSwing,
    double ShrinkFromFatigue, double ShrunkScale, double SlowFromFatigue, double SlowedScale)
{
    internal static DaggerfallStaminaTuning Default { get; } = new(20d, .75d, 8d, 1d, 11d, .5d, .4d, .75d, .5d);

    /// <summary>The pool a character of these attributes has at this Fatigue.</summary>
    internal int Maximum(int strength, int endurance, double fatigue) =>
        (int)((Base + PerAttributePoint * (strength + endurance)) * Squeeze(fatigue, ShrinkFromFatigue, ShrunkScale));

    /// <summary>The share of the refill rate left at this Fatigue.</summary>
    internal double RefillScale(double fatigue) => Squeeze(fatigue, SlowFromFatigue, SlowedScale);

    private static double Squeeze(double fatigue, double from, double scale) =>
        1d - (1d - scale) * Math.Clamp((fatigue - from) / (1d - from), 0d, 1d);

    internal static DaggerfallStaminaTuning Read(JsonElement root) => new(
        NonNegative(root, "base"), NonNegative(root, "perAttributePoint"), Positive(root, "refillPerSecond"), NonNegative(root, "quietSeconds"),
        NonNegative(root, "fatiguePerSwing"), Share(root, "shrinkFromFatigue"), Share(root, "shrunkScale"), Share(root, "slowFromFatigue"),
        Positive(root, "slowedScale"));

    private static double NonNegative(JsonElement parent, string name) => parent.GetProperty(name).GetDouble() is >= 0d and var value && double.IsFinite(value)
        ? value : throw new InvalidOperationException($"Stamina tuning '{name}' must not be negative.");

    private static double Positive(JsonElement parent, string name) => parent.GetProperty(name).GetDouble() is > 0d and var value && double.IsFinite(value)
        ? value : throw new InvalidOperationException($"Stamina tuning '{name}' must be positive.");

    private static double Share(JsonElement parent, string name) => parent.GetProperty(name).GetDouble() is >= 0d and < 1d and var value
        ? value : throw new InvalidOperationException($"Stamina tuning '{name}' must be a share below one.");
}
