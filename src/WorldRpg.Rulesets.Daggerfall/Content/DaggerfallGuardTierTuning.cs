using System.Globalization;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The three settlement watch tiers keyed from settlement size/type (<see cref="DaggerfallSiteRecord"/>),
/// settled in Den #11145 / <c>wiki/rusty-dagger/guards.md</c>.
/// </summary>
internal enum DaggerfallSettlementWatchTier
{
    /// <summary>Small villages, hamlets, and unwalled settlements.</summary>
    Hamlet = 0,

    /// <summary>Medium towns and regional hubs.</summary>
    Town = 1,

    /// <summary>Regional capitals and major walled cities.</summary>
    Capital = 2,
}

/// <summary>
/// Post role of a town watchman, officer, palace/office guard, or occupation garrison patrol (Den #11167).
/// Rank-and-file street watch keep the region's local watch livery; special posts and occupation garrisons wear
/// the current liege's livery (falling back to the region's own arms when independent).
/// </summary>
internal enum DaggerfallGuardPostKind
{
    /// <summary>Rank-and-file street patrol or crackdown reserve retaining local regional watch identity.</summary>
    StreetWatch = 0,

    /// <summary>Watch officer or knight posted at a gate of a proper walled city (wears liege livery).</summary>
    WalledCityGateOfficer = 1,

    /// <summary>Guard or knight posted at a palace or keep (wears liege livery).</summary>
    PalaceGuard = 2,

    /// <summary>Guard posted at a local crown or governor office (wears liege livery).</summary>
    CrownOfficeGuard = 3,

    /// <summary>Conquest-occupation garrison patrol layered on top of local watch (wears occupying liege livery).</summary>
    OccupationGarrison = 4,
}

/// <summary>
/// Appearance &amp; faction livery resolved for a guard placement or live guard actor (Den #11167).
/// Rank-and-file <see cref="DaggerfallGuardPostKind.StreetWatch"/> retain their local regional identity and
/// neutral tint; special posts and occupation garrisons receive the sovereign's faction and heraldic livery tint.
/// </summary>
internal sealed record DaggerfallGuardAppearanceOverride(
    int FactionId,
    string LiveryKey,
    Color? Tint,
    DaggerfallGuardPostKind PostKind,
    bool UsesLiegeLivery);

/// <summary>
/// Deterministic heraldic livery colors for sovereign crown seats, the Empire, and independent regional arms
/// consumed by guard appearance and inspection until the full 44-region heraldry pack (#11144/#11165) replaces them.
/// </summary>
internal static class DaggerfallGuardLiveryColors
{
    private static readonly Color DaggerfallRoyalBlue = new(0.54f, 0.70f, 1.00f, 1f);
    private static readonly Color WayrestCrimson = new(1.00f, 0.54f, 0.56f, 1f);
    private static readonly Color SentinelGold = new(1.00f, 0.86f, 0.48f, 1f);
    private static readonly Color OrsiniumEmerald = new(0.56f, 0.90f, 0.62f, 1f);
    private static readonly Color EmpirePurple = new(0.86f, 0.60f, 1.00f, 1f);

    internal static (string LiveryKey, Color? Tint) Resolve(
        DaggerfallGuardPostKind postKind,
        string liverySlug,
        int liveryRegion,
        bool isIndependentFallback)
    {
        if (postKind == DaggerfallGuardPostKind.StreetWatch)
            return ($"watch:region:{liveryRegion}", null);

        if (!isIndependentFallback)
        {
            return liverySlug switch
            {
                "daggerfall" => ("crown:daggerfall", DaggerfallRoyalBlue),
                "wayrest" => ("crown:wayrest", WayrestCrimson),
                "sentinel" => ("crown:sentinel", SentinelGold),
                "orsinium" or "orsinium-area" => ("crown:orsinium", OrsiniumEmerald),
                "empire" or "imperial-legion" or "the-empire" => ("crown:empire", EmpirePurple),
                _ => ($"crown:{liverySlug}", RegionalTint(liveryRegion + 101)),
            };
        }

        return ($"region:{liveryRegion}", RegionalTint(liveryRegion));
    }

    internal static string FormatHex(Color? tint)
    {
        Color c = tint ?? new Color(1f, 1f, 1f, 1f);
        int r = Math.Clamp((int)MathF.Round(c.R * 255f), 0, 255);
        int g = Math.Clamp((int)MathF.Round(c.G * 255f), 0, 255);
        int b = Math.Clamp((int)MathF.Round(c.B * 255f), 0, 255);
        return string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");
    }

    private static Color RegionalTint(int seed)
    {
        uint h = unchecked((uint)(seed * 2654435761L) ^ 0x9E3779B9U);
        float r = 0.62f + ((h & 0xFFU) / 255f) * 0.34f;
        float g = 0.62f + (((h >> 8) & 0xFFU) / 255f) * 0.34f;
        float b = 0.62f + (((h >> 16) & 0xFFU) / 255f) * 0.34f;
        return new Color(r, g, b, 1f);
    }
}

/// <summary>
/// Typed patrol density, reserve cap, crime-response strength, stat/level band, and equipment loadout for one
/// settlement watch tier (Den #11166).
/// </summary>
internal sealed record DaggerfallGuardTierProfile(
    string Label,
    int BaselinePatrolGuards,
    int MaxCrackdownReserves,
    int MinimumResponseGuards,
    int MaximumResponseGuards,
    int MinimumLevelOffset,
    int MaximumLevelOffset,
    double HealthScale,
    int EquipmentMaterialLevel,
    int ArmorPieceChancePercent,
    int ShieldChancePercent,
    string[] ArmorMaterials,
    int[] WeaponTemplates,
    string[] WeaponMaterials,
    int[] ShieldTemplates)
{
    internal DaggerfallGuardTierProfile Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Label);
        if (BaselinePatrolGuards < 1 || BaselinePatrolGuards > 24)
            throw new ArgumentOutOfRangeException(nameof(BaselinePatrolGuards), "Watch tier baseline patrol guards must be in 1..24.");
        if (MaxCrackdownReserves < 0 || MaxCrackdownReserves > DaggerfallExteriorPopulation.CrackdownGuardSlots)
            throw new ArgumentOutOfRangeException(nameof(MaxCrackdownReserves), "Watch tier max crackdown reserves must stay within ExteriorPopulation.CrackdownGuardSlots.");
        if (MinimumResponseGuards < 1 || MaximumResponseGuards < MinimumResponseGuards || MaximumResponseGuards > 64)
            throw new ArgumentOutOfRangeException(nameof(MinimumResponseGuards), "Watch tier crime-response guard bounds must be ordered in 1..64.");
        if (MinimumLevelOffset < 0 || MaximumLevelOffset < MinimumLevelOffset || MaximumLevelOffset > 20)
            throw new ArgumentOutOfRangeException(nameof(MinimumLevelOffset), "Watch tier level offsets must be ordered in 0..20.");
        if (!double.IsFinite(HealthScale) || HealthScale <= 0d || HealthScale > 5d)
            throw new ArgumentOutOfRangeException(nameof(HealthScale), "Watch tier health scale must be finite and in (0, 5].");
        if (EquipmentMaterialLevel < 1 || EquipmentMaterialLevel > 30)
            throw new ArgumentOutOfRangeException(nameof(EquipmentMaterialLevel), "Watch tier equipment material level must be in 1..30.");
        if (ArmorPieceChancePercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(ArmorPieceChancePercent), "Watch tier armor piece chance must be in 0..100.");
        if (ShieldChancePercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(ShieldChancePercent), "Watch tier shield chance must be in 0..100.");
        if (ArmorMaterials is null || ArmorMaterials.Length == 0 || ArmorMaterials.Any(m => !DaggerfallItemMaterials.IsArmorMaterial(m)))
            throw new ArgumentException("Watch tier requires at least one valid armor material.", nameof(ArmorMaterials));
        if (WeaponTemplates is null || WeaponTemplates.Length == 0 || WeaponTemplates.Any(t => t <= 0))
            throw new ArgumentException("Watch tier requires at least one positive weapon template index.", nameof(WeaponTemplates));
        if (WeaponMaterials is null || WeaponMaterials.Length == 0 || WeaponMaterials.Any(m => !DaggerfallItemMaterials.IsWeaponMaterial(m)))
            throw new ArgumentException("Watch tier requires at least one valid weapon material.", nameof(WeaponMaterials));
        if (ShieldTemplates is null || ShieldTemplates.Length == 0 || ShieldTemplates.Any(t => t is < 109 or > 112))
            throw new ArgumentException("Watch tier requires valid classic shield templates (109..112).", nameof(ShieldTemplates));
        return this;
    }

    internal static DaggerfallGuardTierProfile Read(JsonElement section, DaggerfallGuardTierProfile fallback)
    {
        if (section.ValueKind != JsonValueKind.Object)
            return fallback;
        return new DaggerfallGuardTierProfile(
            Label: section.TryGetProperty("label", out JsonElement label) ? label.GetString() ?? fallback.Label : fallback.Label,
            BaselinePatrolGuards: section.TryGetProperty("baselinePatrolGuards", out JsonElement patrols) ? patrols.GetInt32() : fallback.BaselinePatrolGuards,
            MaxCrackdownReserves: section.TryGetProperty("maxCrackdownReserves", out JsonElement reserves) ? reserves.GetInt32() : fallback.MaxCrackdownReserves,
            MinimumResponseGuards: section.TryGetProperty("minimumResponseGuards", out JsonElement minResp) ? minResp.GetInt32() : fallback.MinimumResponseGuards,
            MaximumResponseGuards: section.TryGetProperty("maximumResponseGuards", out JsonElement maxResp) ? maxResp.GetInt32() : fallback.MaximumResponseGuards,
            MinimumLevelOffset: section.TryGetProperty("minimumLevelOffset", out JsonElement minLvl) ? minLvl.GetInt32() : fallback.MinimumLevelOffset,
            MaximumLevelOffset: section.TryGetProperty("maximumLevelOffset", out JsonElement maxLvl) ? maxLvl.GetInt32() : fallback.MaximumLevelOffset,
            HealthScale: section.TryGetProperty("healthScale", out JsonElement hpScale) ? hpScale.GetDouble() : fallback.HealthScale,
            EquipmentMaterialLevel: section.TryGetProperty("equipmentMaterialLevel", out JsonElement matLvl) ? matLvl.GetInt32() : fallback.EquipmentMaterialLevel,
            ArmorPieceChancePercent: section.TryGetProperty("armorPieceChancePercent", out JsonElement armorChance) ? armorChance.GetInt32() : fallback.ArmorPieceChancePercent,
            ShieldChancePercent: section.TryGetProperty("shieldChancePercent", out JsonElement shieldChance) ? shieldChance.GetInt32() : fallback.ShieldChancePercent,
            ArmorMaterials: section.TryGetProperty("armorMaterials", out JsonElement armorMats) ? ReadStringArray(armorMats) : fallback.ArmorMaterials,
            WeaponTemplates: section.TryGetProperty("weaponTemplates", out JsonElement weaponTpls) ? ReadIntArray(weaponTpls) : fallback.WeaponTemplates,
            WeaponMaterials: section.TryGetProperty("weaponMaterials", out JsonElement weaponMats) ? ReadStringArray(weaponMats) : fallback.WeaponMaterials,
            ShieldTemplates: section.TryGetProperty("shieldTemplates", out JsonElement shieldTpls) ? ReadIntArray(shieldTpls) : fallback.ShieldTemplates)
            .Validate();
    }

    private static string[] ReadStringArray(JsonElement array) =>
        [.. array.EnumerateArray().Select(e => e.GetString() ?? string.Empty)];

    private static int[] ReadIntArray(JsonElement array) =>
        [.. array.EnumerateArray().Select(e => e.GetInt32())];
}

/// <summary>
/// Typed 3-tier settlement watch tuning (Den #11166, settled in <c>wiki/rusty-dagger/guards.md</c>):
/// resolves Hamlet/Village, Town, and Capital/Major City watch tiers from site size/type and provides
/// patrol density, crackdown reserve caps, crime-response counts, stat/level bands, and gear loadouts.
/// </summary>
internal sealed record DaggerfallGuardTierTuning(
    int TownMinimumBlocks,
    int CapitalMinimumBlocks,
    DaggerfallGuardTierProfile Hamlet,
    DaggerfallGuardTierProfile Town,
    DaggerfallGuardTierProfile Capital)
{
    private const int PalaceBuildingType = 16;

    internal static DaggerfallGuardTierTuning Default { get; } = new DaggerfallGuardTierTuning(
        TownMinimumBlocks: 9,
        CapitalMinimumBlocks: 25,
        Hamlet: new DaggerfallGuardTierProfile(
            Label: "Hamlet Watch",
            BaselinePatrolGuards: 1,
            MaxCrackdownReserves: 2,
            MinimumResponseGuards: 1,
            MaximumResponseGuards: 3,
            MinimumLevelOffset: 1,
            MaximumLevelOffset: 2,
            HealthScale: 0.75d,
            EquipmentMaterialLevel: 1,
            ArmorPieceChancePercent: 55,
            ShieldChancePercent: 30,
            ArmorMaterials: ["leather"],
            WeaponTemplates: [115, 288, 116, 113], // staff/spear, club, shortsword, dagger
            WeaponMaterials: ["iron"],
            ShieldTemplates: [109]),               // buckler
        Town: new DaggerfallGuardTierProfile(
            Label: "Town Watch",
            BaselinePatrolGuards: 3,
            MaxCrackdownReserves: 4,
            MinimumResponseGuards: 2,
            MaximumResponseGuards: 4,
            MinimumLevelOffset: 3,
            MaximumLevelOffset: 5,
            HealthScale: 1.0d,
            EquipmentMaterialLevel: 2,
            ArmorPieceChancePercent: 75,
            ShieldChancePercent: 75,
            ArmorMaterials: ["chain"],
            WeaponTemplates: [118, 119, 120, 124], // broadsword, saber, longsword, mace
            WeaponMaterials: ["iron", "steel"],
            ShieldTemplates: [110, 111]),          // round shield, kite shield
        Capital: new DaggerfallGuardTierProfile(
            Label: "Capital Watch",
            BaselinePatrolGuards: 5,
            MaxCrackdownReserves: 6,
            MinimumResponseGuards: 3,
            MaximumResponseGuards: 6,
            MinimumLevelOffset: 5,
            MaximumLevelOffset: 7,
            HealthScale: 1.25d,
            EquipmentMaterialLevel: 5,
            ArmorPieceChancePercent: 95,
            ShieldChancePercent: 85,
            ArmorMaterials: ["steel", "iron", "chain"], // plate / mail armour
            WeaponTemplates: [127, 120, 122, 126],      // battle-axe/halberd, longsword, claymore, warhammer
            WeaponMaterials: ["steel", "silver"],
            ShieldTemplates: [111, 112]))               // kite shield, tower shield
        .Validate();

    internal DaggerfallGuardTierTuning Validate()
    {
        if (TownMinimumBlocks <= 0 || CapitalMinimumBlocks < TownMinimumBlocks)
            throw new ArgumentOutOfRangeException(nameof(TownMinimumBlocks), "Guard tier block thresholds must satisfy 0 < TownMinimumBlocks <= CapitalMinimumBlocks.");
        ArgumentNullException.ThrowIfNull(Hamlet);
        ArgumentNullException.ThrowIfNull(Town);
        ArgumentNullException.ThrowIfNull(Capital);
        Hamlet.Validate();
        Town.Validate();
        Capital.Validate();
        return this;
    }

    internal DaggerfallGuardTierProfile ProfileFor(DaggerfallSettlementWatchTier tier) => tier switch
    {
        DaggerfallSettlementWatchTier.Hamlet => Hamlet,
        DaggerfallSettlementWatchTier.Capital => Capital,
        _ => Town,
    };

    /// <summary>
    /// Resolves the watch tier for <paramref name="site"/> from its settlement class, exterior block footprint,
    /// palace presence, or explicit regional capital marker.
    /// </summary>
    internal DaggerfallSettlementWatchTier ResolveTier(DaggerfallSiteRecord? site, bool isRegionalCapital = false)
    {
        if (site is null)
            return DaggerfallSettlementWatchTier.Town;

        int blocks = site.Exterior?.Blocks.Count ?? 0;
        bool hasPalace = site.Exterior?.BuildingTypes.Contains(PalaceBuildingType) == true;
        if (isRegionalCapital
            || hasPalace
            || blocks >= CapitalMinimumBlocks)
        {
            return DaggerfallSettlementWatchTier.Capital;
        }

        if (site.Kind == DaggerfallSiteKind.TownCity
            || blocks >= TownMinimumBlocks)
        {
            return DaggerfallSettlementWatchTier.Town;
        }

        return DaggerfallSettlementWatchTier.Hamlet;
    }

    internal DaggerfallGuardTierProfile ResolveProfile(DaggerfallSiteRecord? site, bool isRegionalCapital = false) =>
        ProfileFor(ResolveTier(site, isRegionalCapital));

    internal static DaggerfallGuardTierTuning Read(JsonElement section)
    {
        if (section.ValueKind != JsonValueKind.Object)
            return Default;
        return new DaggerfallGuardTierTuning(
            TownMinimumBlocks: section.TryGetProperty("townMinimumBlocks", out JsonElement townBlocks) ? townBlocks.GetInt32() : Default.TownMinimumBlocks,
            CapitalMinimumBlocks: section.TryGetProperty("capitalMinimumBlocks", out JsonElement capBlocks) ? capBlocks.GetInt32() : Default.CapitalMinimumBlocks,
            Hamlet: section.TryGetProperty("hamlet", out JsonElement hamlet) ? DaggerfallGuardTierProfile.Read(hamlet, Default.Hamlet) : Default.Hamlet,
            Town: section.TryGetProperty("town", out JsonElement town) ? DaggerfallGuardTierProfile.Read(town, Default.Town) : Default.Town,
            Capital: section.TryGetProperty("capital", out JsonElement capital) ? DaggerfallGuardTierProfile.Read(capital, Default.Capital) : Default.Capital)
            .Validate();
    }
}
