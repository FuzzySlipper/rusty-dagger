namespace WorldRpg.Rulesets.Daggerfall.Policies;

internal sealed record DaggerfallWeaponPoisonSource(ulong ItemId, int Variant);
internal enum DaggerfallMonsterHitConsequence { None, RatDisease, UndeadDisease, Plague, Werewolf, Wereboar, Vampire, Paralysis, Fatigue }
internal sealed record DaggerfallMonsterHit(int Slot, int Damage, DaggerfallMonsterHitConsequence Consequence);
internal sealed record DaggerfallMonsterHitExposure(long Attacker, long Target, ulong Generation, ulong Step, DaggerfallMonsterHit Hit)
{
    internal string Instance => $"monster-hit:{Generation}:{Step}:{Attacker}:{Target}:{Hit.Slot}";
}

/// <summary>FORM-06 natural-hit vectors keyed by normalized classic mobile identity.</summary>
internal static class DaggerfallMonsterHitPolicy
{
    // Donor's inclusive float 0..100 window, represented at one ten-thousandth percent.
    private const int PercentWindow = 1_000_000;
    internal static DaggerfallMonsterHitConsequence Select(int? mobile, bool playerTarget, Func<int, int, int> draw)
    {
        switch (mobile)
        {
            case 0: return draw(1, 100) <= 5 ? DaggerfallMonsterHitConsequence.RatDisease : DaggerfallMonsterHitConsequence.None;
            case 3: return draw(1, 100) <= 2 ? DaggerfallMonsterHitConsequence.RatDisease : DaggerfallMonsterHitConsequence.None;
            case 17: return draw(1, 100) <= 2 ? DaggerfallMonsterHitConsequence.UndeadDisease : DaggerfallMonsterHitConsequence.None;
            case 19: return draw(1, 100) <= 5 ? DaggerfallMonsterHitConsequence.UndeadDisease : DaggerfallMonsterHitConsequence.None;
            case 6 or 20: return DaggerfallMonsterHitConsequence.Paralysis;
            case 10 or 42: return DaggerfallMonsterHitConsequence.Fatigue;
            case 9 or 14:
                return draw(0, PercentWindow) <= 6_000 && playerTarget
                    ? mobile == 9 ? DaggerfallMonsterHitConsequence.Werewolf : DaggerfallMonsterHitConsequence.Wereboar
                    : DaggerfallMonsterHitConsequence.None;
            case 28 or 30:
                int roll = draw(0, PercentWindow);
                return roll <= 6_000 && playerTarget ? DaggerfallMonsterHitConsequence.Vampire
                    : roll <= 20_000 ? DaggerfallMonsterHitConsequence.Plague : DaggerfallMonsterHitConsequence.None;
            default: return DaggerfallMonsterHitConsequence.None;
        }
    }

    internal static IReadOnlyList<DaggerfallClassicDisease> Diseases(DaggerfallMonsterHitConsequence consequence) => consequence switch
    {
        DaggerfallMonsterHitConsequence.RatDisease => [DaggerfallClassicDisease.Plague, DaggerfallClassicDisease.StomachRot, DaggerfallClassicDisease.BrainFever],
        DaggerfallMonsterHitConsequence.UndeadDisease => [DaggerfallClassicDisease.Plague, DaggerfallClassicDisease.YellowFever, DaggerfallClassicDisease.StomachRot,
            DaggerfallClassicDisease.Consumption, DaggerfallClassicDisease.BrainFever, DaggerfallClassicDisease.SwampRot, DaggerfallClassicDisease.Cholera,
            DaggerfallClassicDisease.Leprosy, DaggerfallClassicDisease.RedDeath, DaggerfallClassicDisease.TyphoidFever, DaggerfallClassicDisease.Dementia],
        DaggerfallMonsterHitConsequence.Plague => [DaggerfallClassicDisease.Plague],
        _ => [],
    };
}
