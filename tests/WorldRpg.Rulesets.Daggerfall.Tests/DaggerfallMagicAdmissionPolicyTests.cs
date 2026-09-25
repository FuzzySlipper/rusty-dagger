using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallMagicAdmissionPolicyTests
{
    [Fact]
    public void Caster_level_defaults_only_for_a_missing_caster()
    {
        Assert.Equal(1, DaggerfallMagicAdmissionPolicy.CalculateCasterLevel(null));
        Assert.Equal(0, DaggerfallMagicAdmissionPolicy.CalculateCasterLevel(0));
        Assert.Equal(17, DaggerfallMagicAdmissionPolicy.CalculateCasterLevel(17));
        Assert.Equal(-1, DaggerfallMagicAdmissionPolicy.CalculateCasterLevel(-1));
    }

    [Fact]
    public void Donor_effect_mappings_keep_flags_element_precedence_and_resistance_priority()
    {
        Assert.Equal(DaggerfallMagicToleranceFlags.Normal, DaggerfallMagicAdmissionPolicy.GetToleranceFlag(DaggerfallMagicTolerance.Normal));
        Assert.Equal(DaggerfallMagicToleranceFlags.Immune, DaggerfallMagicAdmissionPolicy.GetToleranceFlag(DaggerfallMagicTolerance.Immune));
        Assert.Equal(DaggerfallMagicToleranceFlags.Resistant, DaggerfallMagicAdmissionPolicy.GetToleranceFlag(DaggerfallMagicTolerance.Resistant));
        Assert.Equal(DaggerfallMagicToleranceFlags.LowTolerance, DaggerfallMagicAdmissionPolicy.GetToleranceFlag(DaggerfallMagicTolerance.LowTolerance));
        Assert.Equal(DaggerfallMagicToleranceFlags.CriticalWeakness, DaggerfallMagicAdmissionPolicy.GetToleranceFlag(DaggerfallMagicTolerance.CriticalWeakness));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallMagicAdmissionPolicy.GetToleranceFlag((DaggerfallMagicTolerance)99));

        DaggerfallMagicEffectSource fireParalysis = Effect(DaggerfallMagicAllowedElements.Fire,
            DaggerfallMagicBundleElement.Fire, paralysis: true);
        Assert.Equal(DaggerfallMagicEffectFlags.Paralysis | DaggerfallMagicEffectFlags.Fire,
            DaggerfallMagicAdmissionPolicy.GetEffectFlags(fireParalysis));
        Assert.Equal(DaggerfallMagicResistanceElement.Fire, DaggerfallMagicAdmissionPolicy.GetElementType(fireParalysis));
        Assert.Equal(DaggerfallMagicResistanceElement.Magic,
            DaggerfallMagicAdmissionPolicy.GetElementType(Effect(DaggerfallMagicAllowedElements.Magic, DaggerfallMagicBundleElement.Fire)));
        Assert.Equal(DaggerfallMagicResistanceElement.Magic,
            DaggerfallMagicAdmissionPolicy.GetElementType(Effect(DaggerfallMagicAllowedElements.Magic,
                DaggerfallMagicBundleElement.None) with { HasParentBundle = false }));
        Assert.Equal(DaggerfallMagicResistanceElement.DiseaseOrPoison,
            DaggerfallMagicAdmissionPolicy.GetElementType(Effect(DaggerfallMagicAllowedElements.Poison, DaggerfallMagicBundleElement.Poison)));
        Assert.Equal(DaggerfallMagicResistanceElement.None,
            DaggerfallMagicAdmissionPolicy.GetElementType(Effect(DaggerfallMagicAllowedElements.Fire, DaggerfallMagicBundleElement.None)));

        DaggerfallMagicResistanceModifiers modifiers = new(11, 22, 33, 44, 55);
        Assert.Equal(11, DaggerfallMagicAdmissionPolicy.GetResistanceModifier(
            DaggerfallMagicEffectFlags.Disease | DaggerfallMagicEffectFlags.Fire, modifiers));
        Assert.Equal(22, DaggerfallMagicAdmissionPolicy.GetResistanceModifier(DaggerfallMagicEffectFlags.Fire, modifiers));
        Assert.Equal(33, DaggerfallMagicAdmissionPolicy.GetResistanceModifier(DaggerfallMagicEffectFlags.Frost, modifiers));
        Assert.Equal(44, DaggerfallMagicAdmissionPolicy.GetResistanceModifier(DaggerfallMagicEffectFlags.Shock, modifiers));
        Assert.Equal(55, DaggerfallMagicAdmissionPolicy.GetResistanceModifier(DaggerfallMagicEffectFlags.Magic, modifiers));
        Assert.Equal(0, DaggerfallMagicAdmissionPolicy.GetResistanceModifier(DaggerfallMagicEffectFlags.Paralysis, modifiers));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(30, 0)]
    [InlineData(31, 5)]
    [InlineData(49, 95)]
    [InlineData(50, 100)]
    [InlineData(51, 100)]
    public void Saving_throw_uses_inclusive_percentile_and_twenty_point_proration(int roll, int expected)
    {
        Assert.Equal(expected, DaggerfallMagicAdmissionPolicy.SavingThrow(
            DaggerfallMagicResistanceElement.Fire,
            DaggerfallMagicEffectFlags.Fire,
            Target(),
            modifier: 0,
            () => roll));
    }

    [Fact]
    public void Saving_throw_adds_live_willpower_and_clamps_the_final_chance()
    {
        Assert.Equal(0, DaggerfallMagicAdmissionPolicy.SavingThrow(
            DaggerfallMagicResistanceElement.Magic, DaggerfallMagicEffectFlags.Magic,
            Target(willpower: 50), 0, () => 35));
        Assert.Equal(5, DaggerfallMagicAdmissionPolicy.SavingThrow(
            DaggerfallMagicResistanceElement.Magic, DaggerfallMagicEffectFlags.Magic,
            Target(willpower: 50), 0, () => 36));

        DaggerfallMagicTargetProfile veryLow = Target(willpower: 0) with
        {
            CareerTolerances = Career(magic: DaggerfallMagicTolerance.CriticalWeakness),
        };
        Assert.Equal(100, DaggerfallMagicAdmissionPolicy.SavingThrow(
            DaggerfallMagicResistanceElement.Magic, DaggerfallMagicEffectFlags.Magic, veryLow, 0, () => 1));
        Assert.Equal(95, DaggerfallMagicAdmissionPolicy.SavingThrow(
            DaggerfallMagicResistanceElement.Magic, DaggerfallMagicEffectFlags.Magic,
            Target(willpower: 100_000), 0, () => 94));
    }

    [Fact]
    public void Active_resistance_roll_precedes_saving_throw_and_immunity_short_circuits()
    {
        int draws = 0;
        DaggerfallMagicTargetProfile resisted = Target() with
        {
            ActiveResistances = [new(DaggerfallMagicResistanceElement.Fire, 100)],
        };
        Assert.Equal(0, DaggerfallMagicAdmissionPolicy.SavingThrow(
            DaggerfallMagicResistanceElement.Fire, DaggerfallMagicEffectFlags.Fire, resisted, 0,
            () => { draws++; return 1; }));
        Assert.Equal(1, draws);

        draws = 0;
        DaggerfallMagicTargetProfile careerImmune = Target() with
        {
            CareerTolerances = Career(fire: DaggerfallMagicTolerance.Immune),
        };
        Assert.Equal(0, DaggerfallMagicAdmissionPolicy.SavingThrow(
            DaggerfallMagicResistanceElement.Fire, DaggerfallMagicEffectFlags.Fire, careerImmune, 0,
            () => { draws++; return 1; }));
        Assert.Equal(0, draws);

        draws = 0;
        DaggerfallMagicTargetProfile activeMiss = Target() with
        {
            ActiveResistances = [new(DaggerfallMagicResistanceElement.Fire, 30)],
        };
        Assert.Equal(100, DaggerfallMagicAdmissionPolicy.SavingThrow(
            DaggerfallMagicResistanceElement.Fire, DaggerfallMagicEffectFlags.Fire, activeMiss, 0,
            () => ++draws == 1 ? 31 : 51));
        Assert.Equal(2, draws);
    }

    [Fact]
    public void Racial_and_career_tolerance_flags_compose_as_the_donor_does()
    {
        DaggerfallMagicTargetProfile racial = Target() with
        {
            PlayerRaceTolerances = new(
                Resistance: DaggerfallMagicEffectFlags.None,
                Immunity: DaggerfallMagicEffectFlags.Paralysis,
                LowTolerance: DaggerfallMagicEffectFlags.None,
                CriticalWeakness: DaggerfallMagicEffectFlags.None),
        };
        Assert.Equal(0, DaggerfallMagicAdmissionPolicy.SavingThrow(
            DaggerfallMagicResistanceElement.Fire,
            DaggerfallMagicEffectFlags.Fire | DaggerfallMagicEffectFlags.Paralysis,
            racial, 0, () => throw new Xunit.Sdk.XunitException("racial paralysis immunity must not roll")));

        // The donor identifies additive mixed career modifiers as a DFUnity deviation;
        // the classic rule grants immunity ahead of critical weakness.
        DaggerfallMagicTargetProfile mixed = Target() with
        {
            CareerTolerances = Career(magic: DaggerfallMagicTolerance.Immune,
                fire: DaggerfallMagicTolerance.CriticalWeakness),
        };
        Assert.Equal(0, DaggerfallMagicAdmissionPolicy.SavingThrow(
            DaggerfallMagicResistanceElement.Fire,
            DaggerfallMagicEffectFlags.Magic | DaggerfallMagicEffectFlags.Fire,
            mixed, 0, () => throw new Xunit.Sdk.XunitException("classic immunity must not roll")));
    }

    [Fact]
    public void Effect_overloads_apply_best_live_resistance_and_preserve_unbundled_fallbacks()
    {
        DaggerfallMagicEffectSource source = Effect(DaggerfallMagicAllowedElements.Fire, DaggerfallMagicBundleElement.Fire);
        DaggerfallMagicTargetProfile target = Target() with
        {
            ResistanceModifiers = new(DiseaseOrPoison: 100, Fire: 25, Frost: 0, Shock: 0, Magic: 0),
        };
        Assert.Equal(0, DaggerfallMagicAdmissionPolicy.SavingThrow(source, target, () => 55));
        Assert.Equal(-1, DaggerfallMagicAdmissionPolicy.ModifyEffectAmount(source, target, -39, () => 56));

        DaggerfallMagicEffectSource unbundled = Effect(DaggerfallMagicAllowedElements.Fire,
            DaggerfallMagicBundleElement.Fire) with { HasParentBundle = false };
        Assert.Equal(100, DaggerfallMagicAdmissionPolicy.SavingThrow(unbundled, target,
            () => throw new Xunit.Sdk.XunitException("an unbundled effect must not roll")));
        Assert.Equal(-39, DaggerfallMagicAdmissionPolicy.ModifyEffectAmount(unbundled, target, -39,
            () => throw new Xunit.Sdk.XunitException("an unbundled effect must not roll")));
        Assert.Equal(23, DaggerfallMagicAdmissionPolicy.ModifyEffectAmount(null, target, 23,
            () => throw new Xunit.Sdk.XunitException("a missing effect must not roll")));
    }

    [Fact]
    public void Casting_quote_wrapper_uses_the_donor_default_and_existing_cost_owner()
    {
        DaggerfallDefinitions definitions = Load();
        DaggerfallSpellDefinition spell = definitions.Magic.Spells["spell.001"];
        IReadOnlyDictionary<string, int> emptySkills = new Dictionary<string, int>();
        Assert.Equal(DaggerfallMagicCostPolicy.QuoteCasting(definitions.Magic, spell, emptySkills, enchantingItem: true).SpellPoints,
            DaggerfallMagicAdmissionPolicy.CalculateCastingCost(definitions.Magic, spell));

        Dictionary<string, int> skills = spell.Effects
            .Select(effect => definitions.Magic.RequireEffectCost(effect).School)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(school => school, _ => 33, StringComparer.Ordinal);
        Assert.Equal(DaggerfallMagicCostPolicy.QuoteCasting(definitions.Magic, spell, skills, enchantingItem: false).SpellPoints,
            DaggerfallMagicAdmissionPolicy.CalculateCastingCost(definitions.Magic, spell, skills, enchantingItem: false));
    }

    private static DaggerfallMagicTargetProfile Target(int willpower = 0) => new(
        willpower,
        Career(),
        PlayerRaceTolerances: null,
        BiographyMagicResistance: 0,
        BiographyPoisonResistance: 0,
        BiographyDiseaseResistance: 0,
        ResistanceModifiers: new(0, 0, 0, 0, 0),
        ActiveResistances: []);

    private static DaggerfallMagicCareerTolerances Career(
        DaggerfallMagicTolerance paralysis = DaggerfallMagicTolerance.Normal,
        DaggerfallMagicTolerance magic = DaggerfallMagicTolerance.Normal,
        DaggerfallMagicTolerance poison = DaggerfallMagicTolerance.Normal,
        DaggerfallMagicTolerance fire = DaggerfallMagicTolerance.Normal,
        DaggerfallMagicTolerance frost = DaggerfallMagicTolerance.Normal,
        DaggerfallMagicTolerance shock = DaggerfallMagicTolerance.Normal,
        DaggerfallMagicTolerance disease = DaggerfallMagicTolerance.Normal) =>
        new(paralysis, magic, poison, fire, frost, shock, disease);

    private static DaggerfallMagicEffectSource Effect(
        DaggerfallMagicAllowedElements allowedElements,
        DaggerfallMagicBundleElement bundleElement,
        bool paralysis = false,
        bool disease = false) =>
        new(true, paralysis, disease, allowedElements, bundleElement);

    private static DaggerfallDefinitions Load()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "content/worldrpg/payloads/daggerfall.base.json")))
            directory = directory.Parent;
        return TestPayload.Definitions;
    }
}
