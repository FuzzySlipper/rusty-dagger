namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Provisional donor damage modulation and observed vampiric range (DEC-11).</summary>
internal sealed record DaggerfallStrikeEnchantmentTuning(int DamageAdjustment, double VampiricRange)
{
    internal DaggerfallStrikeEnchantmentTuning Validate()
    {
        if (DamageAdjustment < 0 || !double.IsFinite(VampiricRange) || VampiricRange <= 0)
            throw new ArgumentOutOfRangeException(nameof(DamageAdjustment), "Enchantment damage adjustment and range must be finite and nonnegative/positive.");
        return this;
    }
}
