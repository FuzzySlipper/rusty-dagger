using System.Text.Json;
using WorldRpg.Kit.Combat;

namespace WorldRpg.Rulesets.Daggerfall.Variants;

/// <summary>Whether deflection on a channel turns the whole blow aside or only part of it.</summary>
internal enum DaggerfallDeflectionMode { Full, Partial }

internal sealed record DaggerfallChannelDeflection(DaggerfallDeflectionMode Mode, double Fraction);

/// <summary>The contact + mitigation variant's own tuning profile.</summary>
internal sealed record DaggerfallContactTuning(
    double SwingWidth, double SwingHeight, double SwingDrop, double DefaultReach,
    double ProjectileSpeed, double DefaultRange,
    double CriticalChancePerSkillPoint, double CriticalChancePerAttackChancePoint, double CriticalDamageMultiplier,
    double DeflectionBaseChance, double DeflectionPerDodgingPoint, double DeflectionPerAgilityAbove50, double DeflectionMaximumChance,
    IReadOnlyDictionary<HarmChannelId, DaggerfallChannelDeflection> Deflection,
    double AbsorptionPerArmorPoint, double AbsorptionPerShieldPoint, double AbsorptionPerArmorValueShift, double MonsterArmorBaseline, double MonsterAbsorptionPerPoint,
    IReadOnlyDictionary<HarmChannelId, double> AbsorptionMultipliers,
    double GuardStaminaPerSecond, double GuardDeflectionBonus, double GuardShieldDeflectionBonus, double GuardMaximumChance,
    double TouchReach, double TouchWidth, double TouchHeight, double TouchDrop)
{
    internal DaggerfallChannelDeflection? DeflectionFor(HarmChannelId channel) => Deflection.GetValueOrDefault(channel);
    internal double AbsorptionMultiplier(HarmChannelId channel) => AbsorptionMultipliers.GetValueOrDefault(channel);

    internal static DaggerfallContactTuning Read(ReadOnlySpan<byte> payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload.ToArray());
        JsonElement root = document.RootElement;
        JsonElement melee = root.GetProperty("melee"), projectile = root.GetProperty("projectile"), critical = root.GetProperty("critical"),
            deflection = root.GetProperty("deflection"), absorption = root.GetProperty("absorption"), guard = root.GetProperty("guard"),
            touch = root.GetProperty("touch");
        Dictionary<HarmChannelId, DaggerfallChannelDeflection> channels = [];
        foreach (JsonProperty channel in deflection.GetProperty("channels").EnumerateObject())
        {
            DaggerfallDeflectionMode mode = channel.Value.GetProperty("mode").GetString() switch
            {
                "full" => DaggerfallDeflectionMode.Full,
                "partial" => DaggerfallDeflectionMode.Partial,
                string other => throw new InvalidOperationException($"Contact tuning deflection mode '{other}' for channel '{channel.Name}' is neither 'full' nor 'partial'."),
                null => throw new InvalidOperationException($"Contact tuning deflection for channel '{channel.Name}' names no mode."),
            };
            double fraction = mode == DaggerfallDeflectionMode.Partial ? Fraction(channel.Value, "fraction") : 1d;
            channels.Add(new HarmChannelId(channel.Name), new(mode, fraction));
        }
        Dictionary<HarmChannelId, double> multipliers = [];
        foreach (JsonProperty channel in absorption.GetProperty("channelMultipliers").EnumerateObject())
            multipliers.Add(new HarmChannelId(channel.Name), Multiplier(channel.Value, channel.Name));
        return new(
            Positive(melee, "swingWidth"), Positive(melee, "swingHeight"), NonNegative(melee, "swingDrop"), Positive(melee, "defaultReach"),
            Positive(projectile, "speedMetersPerSecond"), Positive(projectile, "defaultRange"),
            NonNegative(critical, "chancePerSkillPoint"), NonNegative(critical, "chancePerAttackChancePoint"), Positive(critical, "damageMultiplier"),
            NonNegative(deflection, "baseChance"), NonNegative(deflection, "dodgingPerPoint"), NonNegative(deflection, "agilityPerPointAbove50"),
            Percent(deflection, "maximumChance"), channels,
            NonNegative(absorption, "perArmorPoint"), NonNegative(absorption, "perShieldPoint"), NonNegative(absorption, "perArmorValueShift"),
            NonNegative(absorption, "monsterArmorBaseline"), NonNegative(absorption, "monsterAbsorbPerPoint"), multipliers,
            NonNegative(guard, "staminaPerSecond"), NonNegative(guard, "deflectionBonus"), NonNegative(guard, "shieldDeflectionBonus"),
            Percent(guard, "maximumChance"),
            Positive(touch, "reach"), Positive(touch, "width"), Positive(touch, "height"), NonNegative(touch, "drop"));
    }

    private static double Value(JsonElement parent, string name) => parent.GetProperty(name).GetDouble() is double value && double.IsFinite(value)
        ? value : throw new InvalidOperationException($"Contact tuning '{name}' must be finite.");
    private static double Positive(JsonElement parent, string name) => Value(parent, name) is > 0d and var value
        ? value : throw new InvalidOperationException($"Contact tuning '{name}' must be positive.");
    private static double NonNegative(JsonElement parent, string name) => Value(parent, name) is >= 0d and var value
        ? value : throw new InvalidOperationException($"Contact tuning '{name}' must not be negative.");
    private static double Multiplier(JsonElement value, string name) => value.GetDouble() is var number && double.IsFinite(number) && number >= 0d
        ? number : throw new InvalidOperationException($"Contact tuning multiplier for '{name}' must be a finite non-negative number.");
    private static double Fraction(JsonElement parent, string name) => Value(parent, name) is >= 0d and <= 1d and var value
        ? value : throw new InvalidOperationException($"Contact tuning '{name}' must lie between 0 and 1.");
    private static double Percent(JsonElement parent, string name) => Value(parent, name) is >= 0d and <= 100d and var value
        ? value : throw new InvalidOperationException($"Contact tuning '{name}' must lie between 0 and 100.");
}
