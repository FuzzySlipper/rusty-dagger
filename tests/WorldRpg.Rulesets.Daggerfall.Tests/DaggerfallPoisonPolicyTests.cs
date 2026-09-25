using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The classic poison table and FORM-06's admission: which of the twelve a classic value names, and
/// what turns an attempt to poison someone into an effect, a resistance or an immunity.
/// </summary>
public sealed class DaggerfallPoisonPolicyTests
{
    [Fact]
    public void The_twelve_classic_poisons_are_the_donors_own_table()
    {
        // The donor numbers them from 128 (ItemsFile.cs Poisons) and builds each effect key as
        // Poison-{name}; four of the twelve are the drugs the donor treats apart.
        Assert.Equal(12, DaggerfallPoisonPolicy.Variants.Count);
        Assert.Equal(Enumerable.Range(128, 12), DaggerfallPoisonPolicy.Variants.Select(variant => (int)variant));
        Assert.Equal(DaggerfallPoisonVariant.NuxVomica, DaggerfallPoisonPolicy.Variants[0]);
        Assert.Equal(DaggerfallPoisonVariant.Thyrwort, DaggerfallPoisonPolicy.Variants[7]);
        Assert.Equal(DaggerfallPoisonVariant.Aegrotat, DaggerfallPoisonPolicy.Variants[11]);
        Assert.Equal([DaggerfallPoisonVariant.Indulcet, DaggerfallPoisonVariant.Sursum, DaggerfallPoisonVariant.QuaestoVil, DaggerfallPoisonVariant.Aegrotat],
            DaggerfallPoisonPolicy.Variants.Where(DaggerfallPoisonPolicy.IsDrug));
        // The donor's key is its enum identifier, so the two-word names keep their underscores.
        Assert.Equal("Poison-Nux_Vomica", DaggerfallPoisonPolicy.EffectKey(DaggerfallPoisonVariant.NuxVomica));
        Assert.Equal("Poison-Pyrrhic_Acid", DaggerfallPoisonPolicy.EffectKey(DaggerfallPoisonVariant.PyrrhicAcid));
        Assert.Equal("Poison-Quaesto_Vil", DaggerfallPoisonPolicy.EffectKey(DaggerfallPoisonVariant.QuaestoVil));
        Assert.Equal("Poison-Aegrotat", DaggerfallPoisonPolicy.EffectKey(DaggerfallPoisonVariant.Aegrotat));
        Assert.Equal(DaggerfallPoisonVariant.Moonseed, DaggerfallPoisonPolicy.VariantFor(130));
        Assert.Null(DaggerfallPoisonPolicy.VariantFor(127));
        Assert.Null(DaggerfallPoisonPolicy.VariantFor(0));
    }

    [Fact]
    public void A_target_that_cannot_be_poisoned_is_immune_before_any_throw()
    {
        // The pre-throw decision answers Admitted for an attempt the throw still has to decide, so it is
        // never mistaken for a completed admission.
        Assert.Equal(DaggerfallPoisonAdmission.Admitted, DaggerfallPoisonPolicy.AdmitBeforeThrow(Exposure()));
        Assert.Equal(DaggerfallPoisonAdmission.Resisted, DaggerfallPoisonPolicy.Admit(Exposure(), 1));
    }

    [Fact]
    public void A_target_that_cannot_be_poisoned_is_immune_however_the_attempt_is_made()
    {
        Assert.Equal(DaggerfallPoisonAdmission.Immune, DaggerfallPoisonPolicy.AdmitBeforeThrow(Exposure() with { CareerImmune = true }));
        Assert.Equal(DaggerfallPoisonAdmission.Immune, DaggerfallPoisonPolicy.AdmitBeforeThrow(Exposure() with { RaceImmune = true }));
        // The donor's level-1 rule keeps even a bypassed attempt from infecting a first-level target.
        Assert.Equal(DaggerfallPoisonAdmission.Immune, DaggerfallPoisonPolicy.AdmitBeforeThrow(Exposure() with { TargetLevel = 1, BypassResistance = true }));
        Assert.Equal(DaggerfallPoisonAdmission.Immune,
            DaggerfallPoisonPolicy.AdmitBeforeThrow(Exposure() with { Tolerance = DaggerfallDiseaseCareerTolerance.Immune }));
        Assert.Equal(DaggerfallPoisonAdmission.Admitted,
            DaggerfallPoisonPolicy.AdmitBeforeThrow(Exposure() with { TargetLevel = 2, BypassResistance = true }));
    }

    [Fact]
    public void The_poison_saving_throw_is_form_06_on_the_shared_element()
    {
        // 50 plus the career's own tolerance plus the background's poison modifier plus willpower/10,
        // inside the donor's ordinary 5-95 window, with immunity complete before the window.
        Assert.Equal(55, DaggerfallPoisonPolicy.SavingThrowChance(willpower: 50));
        Assert.Equal(80, DaggerfallPoisonPolicy.SavingThrowChance(willpower: 50, DaggerfallDiseaseCareerTolerance.Resistant));
        Assert.Equal(30, DaggerfallPoisonPolicy.SavingThrowChance(willpower: 50, DaggerfallDiseaseCareerTolerance.LowTolerance));
        Assert.Equal(65, DaggerfallPoisonPolicy.SavingThrowChance(willpower: 50, biographyModifier: 10));
        Assert.Equal(95, DaggerfallPoisonPolicy.SavingThrowChance(willpower: 1000));
        Assert.Equal(100, DaggerfallPoisonPolicy.SavingThrowChance(willpower: 50, DaggerfallDiseaseCareerTolerance.Immune));
    }

    [Fact]
    public void A_failed_save_poisons_fully_and_a_save_inside_twenty_only_lessens_it()
    {
        // The donor's save is roll-under: rolling above the chance fails it and the payload is full,
        // while a success prorates the payload and only reaches zero once the roll is twenty below.
        DaggerfallPoisonExposure exposure = Exposure();
        int chance = DaggerfallPoisonPolicy.SavingThrowChance(exposure.Willpower);

        Assert.Equal(DaggerfallPoisonAdmission.Admitted, DaggerfallPoisonPolicy.Admit(exposure, chance + 1));
        Assert.Equal(DaggerfallPoisonAdmission.Admitted, DaggerfallPoisonPolicy.Admit(exposure, 100));
        Assert.Equal(DaggerfallPoisonAdmission.Admitted, DaggerfallPoisonPolicy.Admit(exposure, chance));
        Assert.Equal(DaggerfallPoisonAdmission.Resisted, DaggerfallPoisonPolicy.Admit(exposure, chance - 20));
        Assert.Equal(DaggerfallPoisonAdmission.Resisted, DaggerfallPoisonPolicy.Admit(exposure, chance - 21));
        Assert.Equal(DaggerfallPoisonAdmission.Resisted, DaggerfallPoisonPolicy.Admit(exposure, 1));

        // A bypassed attempt never consults the throw, but still respects immunity.
        Assert.Equal(DaggerfallPoisonAdmission.Admitted, DaggerfallPoisonPolicy.Admit(exposure with { BypassResistance = true }, 1));
        Assert.Equal(DaggerfallPoisonAdmission.Immune,
            DaggerfallPoisonPolicy.Admit(exposure with { BypassResistance = true, CareerImmune = true }, 1));

        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallPoisonPolicy.Admit(exposure, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallPoisonPolicy.Admit(exposure, 101));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallPoisonPolicy.AdmitBeforeThrow(exposure with { TargetLevel = 0 }));
    }

    private static DaggerfallPoisonExposure Exposure() => new(TargetId: 2, TargetLevel: 5, CareerImmune: false, RaceImmune: false, Willpower: 50);
}
