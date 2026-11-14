namespace WorldRpg.Rulesets.Daggerfall;

[Flags]
internal enum DaggerfallConcealment
{
    None = 0,
    InvisibleNormal = 1, InvisibleTrue = 2,
    BlendingNormal = 4, BlendingTrue = 8,
    ShadeNormal = 16, ShadeTrue = 32,
}

/// <summary>
/// Typed perception meaning supplied by a compiled effect family. Perception reads this
/// projection instead of inferring concealment or language bonuses from effect keys or payloads.
/// </summary>
internal readonly record struct DaggerfallPerceptionEffectState(
    bool Invisible = false,
    bool Blending = false,
    bool Shade = false,
    int ComprehendLanguagesBonus = 0,
    DaggerfallConcealment Concealment = DaggerfallConcealment.None)
{
    internal DaggerfallPerceptionEffectState Validate()
    {
        if (ComprehendLanguagesBonus < 0)
            throw new ArgumentOutOfRangeException(nameof(ComprehendLanguagesBonus));
        return this;
    }

    internal DaggerfallPerceptionEffectState Combine(DaggerfallPerceptionEffectState other) => new DaggerfallPerceptionEffectState(
        Invisible || other.Invisible,
        Blending || other.Blending,
        Shade || other.Shade,
        checked(ComprehendLanguagesBonus + other.ComprehendLanguagesBonus),
        Concealment | other.Concealment).Validate();
}
