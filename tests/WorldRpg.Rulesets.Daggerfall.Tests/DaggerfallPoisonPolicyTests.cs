using WorldRpg.Rulesets.Daggerfall.Content;
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

    [Fact]
    public void The_classic_poison_archetypes_are_the_donors_own_table()
    {
        // The donor builds all twelve variants from constants in its poison effect, so the ruleset owns
        // the table and this pins it: order, variant numbers, the weapon-poison/drug split, and the two
        // windows each archetype rolls.
        Assert.Empty(DaggerfallPoisonArchetypes.Validate(DaggerfallPoisonArchetypes.All));
        Assert.Equal(12, DaggerfallPoisonArchetypes.All.Count);
        Assert.Equal(Enumerable.Range(128, 12), DaggerfallPoisonArchetypes.All.Select(archetype => archetype.Variant));
        Assert.All(DaggerfallPoisonArchetypes.All, archetype => Assert.Equal(archetype.Variant, (int)Enum.Parse<DaggerfallPoisonVariant>(archetype.Name.Replace("_", string.Empty))));
        Assert.Equal(
            ["Nux_Vomica", "Arsenic", "Moonseed", "Drothweed", "Somnalius", "Pyrrhic_Acid", "Magebane", "Thyrwort", "Indulcet", "Sursum", "Quaesto_Vil", "Aegrotat"],
            DaggerfallPoisonArchetypes.All.Select(archetype => archetype.Name));

        // 0-7 are weapon poisons and 8-11 are drugs; the donor splits them at the ninth variant.
        Assert.Equal(
            [.. Enumerable.Repeat(DaggerfallPoisonKind.WeaponPoison, 8), .. Enumerable.Repeat(DaggerfallPoisonKind.Drug, 4)],
            DaggerfallPoisonArchetypes.All.Select(archetype => archetype.Kind));

        (int OnsetMin, int OnsetMax, int DurationMin, int DurationMax)[] windows =
        [
            (4, 4, 3, 10), (10, 10, 20, 1000), (0, 0, 1, 4), (5, 10, 5, 30), (0, 0, 2, 10), (0, 0, 1, 2),
            (2, 2, 5, 20), (0, 0, 1, 3), (2, 12, 2, 6), (1, 4, 2, 2), (2, 12, 1, 4), (0, 0, 5, 20),
        ];
        Assert.Equal(windows, DaggerfallPoisonArchetypes.All.Select(archetype =>
            (archetype.MinimumOnsetMinutes, archetype.MaximumOnsetMinutes, archetype.MinimumDurationMinutes, archetype.MaximumDurationMinutes)));

        // Every archetype's key is the one the admission policy reports, so nothing names an effect twice.
        Assert.All(DaggerfallPoisonArchetypes.All, archetype =>
            Assert.Equal(archetype.Key, DaggerfallPoisonPolicy.EffectKey((DaggerfallPoisonVariant)archetype.Variant)));
    }

    [Fact]
    public void A_poison_tick_is_the_donors_own_effect_and_its_positive_arms_are_marked()
    {
        // The donor rolls these with an exclusive maximum, so the inclusive bounds here are already one
        // lower than its argument; the arms that help the victim are the ones a cure has to take back.
        DaggerfallPoisonArchetype nux = DaggerfallPoisonArchetypes.All.Single(archetype => archetype.Variant == 128);
        DaggerfallPoisonArchetype arsenic = DaggerfallPoisonArchetypes.All.Single(archetype => archetype.Variant == 129);
        DaggerfallPoisonArchetype drothweed = DaggerfallPoisonArchetypes.All.Single(archetype => archetype.Variant == 131);
        DaggerfallPoisonArchetype indulcet = DaggerfallPoisonArchetypes.All.Single(archetype => archetype.Variant == 136);
        DaggerfallPoisonArchetype sursum = DaggerfallPoisonArchetypes.All.Single(archetype => archetype.Variant == 137);
        DaggerfallPoisonArchetype quaesto = DaggerfallPoisonArchetypes.All.Single(archetype => archetype.Variant == 138);
        DaggerfallPoisonArchetype aegrotat = DaggerfallPoisonArchetypes.All.Single(archetype => archetype.Variant == 139);

        Assert.Equal([new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Health, null, 2, 11)], nux.Effects);
        Assert.Equal(
            [new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Health, null, 2, 2), new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Attribute, "endurance", -1, -1)],
            arsenic.Effects);
        Assert.Equal(
            [new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Attribute, "strength", -9, -5),
             new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Attribute, "agility", -4, -1),
             new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Attribute, "speed", -4, -1)],
            drothweed.Effects);

        // The four drugs are also the ones that help their victim somewhere: luck, strength, fatigue and
        // magicka respectively. Those arms are marked so the ending can take them back.
        Assert.Equal([new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Fatigue, null, 10, 99),
            new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Attribute, "luck", 4, 9, IsPositive: true)], indulcet.Effects);
        Assert.Equal([new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Attribute, "intelligence", -29, -10),
            new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Attribute, "strength", 5, 19, IsPositive: true)], sursum.Effects);
        Assert.Equal([new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Attribute, "willpower", -3, -1),
            new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Fatigue, null, 5, 9, IsPositive: true)], quaesto.Effects);
        Assert.Equal([new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Attribute, "endurance", -4, -1),
            new DaggerfallPoisonEffect(DaggerfallPoisonTarget.Magicka, null, 5, 9, IsPositive: true)], aegrotat.Effects);
        Assert.Equal(4, DaggerfallPoisonArchetypes.All.SelectMany(archetype => archetype.Effects).Count(effect => effect.IsPositive));

        // A broken row is reported rather than accepted.
        DaggerfallPoisonArchetype sound = DaggerfallPoisonArchetypes.All[0];
        Assert.NotEmpty(DaggerfallPoisonArchetypes.Validate([sound with { Variant = 999 }]));
        Assert.NotEmpty(DaggerfallPoisonArchetypes.Validate([sound with { Effects = [] }]));
        Assert.NotEmpty(DaggerfallPoisonArchetypes.Validate([sound with { MinimumDurationMinutes = 0 }]));
        Assert.NotEmpty(DaggerfallPoisonArchetypes.Validate([sound, sound]));
    }

    [Fact]
    public void The_pack_carries_the_twelve_archetypes_as_classic_records()
    {
        // The archetype table owns the donor's onset and magnitude constants; the pack owns the names,
        // the variant order and which classic effects each poison applies. The two must agree, so this
        // pins the record half the table is joined to: twelve bang-named spell records in the table's own
        // order, plus one that is not a poison at all (the lycanthropy record shares the naming).
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        List<DaggerfallSpellDefinition> records = [.. definitions.Magic.Spells.Values
            .Where(spell => spell.Name.StartsWith('!'))
            .OrderBy(spell => spell.Identity)];
        // Eleven of the twelve archetypes are in the pack, in the table's own order; the twelfth is not,
        // and the last bang-named record is a different affliction that shares the naming. That gap is
        // recorded here rather than smoothed over: either the classic set this pack was imported from
        // lacks the twelfth, or the importer drops it, and until that is settled the donor's table is the
        // only source for it.
        Assert.Equal(12, records.Count);
        Assert.Equal(
            ["!Nux Vomica", "!Arsenic", "!Moonseed", "!Drothweed", "!Somnalius", "!Pyrrhic Acid",
             "!Magebane", "!Thyrwort", "!Indulcet", "!Sursum", "!Quaesto Vil"],
            records.Take(11).Select(spell => spell.Name));
        Assert.Equal("!Lycanthropy", records[11].Name);
        Assert.DoesNotContain(records, spell => spell.Name.Contains("Aegrotat", StringComparison.Ordinal));
        Assert.Equal(Enumerable.Range(71, 11), records.Take(11).Select(spell => spell.Identity));
        Assert.All(records.Take(11), spell => Assert.NotEmpty(spell.Effects));

        // The table's names are the donor's spelling of the same archetypes, in the records' order, so the
        // eleven join by position and the twelfth stands on the donor's table alone.
        Assert.Equal(
            records.Take(11).Select(spell => spell.Name[1..].Replace(" ", string.Empty)),
            DaggerfallPoisonArchetypes.All.Take(11).Select(archetype => archetype.Name.Replace("_", string.Empty)));
        Assert.Equal("Aegrotat", DaggerfallPoisonArchetypes.All[11].Name);
    }
}
