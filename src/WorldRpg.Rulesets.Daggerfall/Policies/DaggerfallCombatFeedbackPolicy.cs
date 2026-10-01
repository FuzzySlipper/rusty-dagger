using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>The donor item sound meaning captured at the accepted strike boundary.</summary>
internal static class DaggerfallCombatFeedbackPolicy
{
    // DaggerfallUnityItem.GetSwingSound. Template values are the native weapon enum.
    internal static DaggerfallStrikeFeedback ForWeaponTemplate(int? template)
    {
        int? sound = template switch
        {
            121 or 122 or 123 or 125 or 126 or 127 => 105,
            115 or 117 or 118 or 119 or 120 or 124 or 128 => 347,
            113 or 114 or 116 => 106,
            129 or 130 => 3,
            _ => null,
        };
        return new(true, sound == 106 ? "swing" : sound is int ordinal ? $"sound.{ordinal}" : string.Empty);
    }
}
