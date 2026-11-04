using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// One motion's three phases, in seconds: the windup up to the hit, the commit (the hit window), and the
/// recovery, in which the next fast attack can start.
/// </summary>
internal readonly record struct DaggerfallAttackTiming(double Windup, double Commit, double Recovery)
{
    internal double Duration => Windup + Commit + Recovery;
}

/// <summary>A weapon class's attacks: the fast attack, the power attack's charge in seconds, and its release.</summary>
internal sealed record DaggerfallWeaponClassAttacks(DaggerfallAttackTiming Fast, double PowerCharge, DaggerfallAttackTiming PowerRelease);

/// <summary>The player's attack set: its timing per weapon class, and what a power attack and a kick cost and do.</summary>
internal sealed record DaggerfallAttackTuning(
    double PowerThresholdSeconds, double PowerDamageMultiplier, double PowerStaminaMultiplier,
    DaggerfallAttackTiming Kick, int KickStaminaCost, int KickDamage,
    IReadOnlyDictionary<string, DaggerfallWeaponClassAttacks> Classes)
{
    private static readonly DaggerfallWeaponClassAttacks ShortBlade = new(new(.18d, .12d, .28d), .5d, new(.15d, .12d, .35d));

    internal static DaggerfallAttackTuning Default { get; } = new(.2d, 2d, 2d, new(.25d, .12d, .4d), 6, 1,
        new Dictionary<string, DaggerfallWeaponClassAttacks>(StringComparer.Ordinal)
        {
            [DaggerfallSkills.ShortBlade] = ShortBlade,
            [DaggerfallSkills.LongBlade] = new(new(.25d, .15d, .33d), .7d, new(.2d, .15d, .4d)),
            [DaggerfallSkills.Axe] = new(new(.3d, .15d, .35d), .8d, new(.22d, .15d, .45d)),
            [DaggerfallSkills.BluntWeapon] = new(new(.32d, .15d, .38d), .85d, new(.25d, .15d, .45d)),
            [DaggerfallSkills.HandToHand] = new(new(.15d, .1d, .2d), .4d, new(.12d, .1d, .3d)),
        });

    /// <summary>The attacks of the weapon class a skill names; refuses a skill with none, which is a content error.</summary>
    internal DaggerfallWeaponClassAttacks For(string skill) => Classes.TryGetValue(skill, out DaggerfallWeaponClassAttacks? attacks)
        ? attacks : throw new InvalidOperationException($"Attack tuning has no weapon class '{skill}'.");

    /// <summary>The timing of an attack motion with a weapon of <paramref name="skill"/>; null for a motion with none of its own.</summary>
    internal DaggerfallAttackTiming? Timing(string attack, string skill)
    {
        DaggerfallWeaponClassAttacks weapon = For(skill);
        return attack switch
        {
            DaggerfallAttackIds.PowerIn => new(weapon.PowerCharge, 0d, 0d),
            DaggerfallAttackIds.PowerOut => weapon.PowerRelease,
            DaggerfallAttackIds.Kick => Kick,
            _ => DaggerfallAttackIds.Fast.Contains(attack) ? weapon.Fast : null,
        };
    }

    internal static DaggerfallAttackTuning Read(JsonElement root)
    {
        Dictionary<string, DaggerfallWeaponClassAttacks> classes = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in root.GetProperty("classes").EnumerateObject())
            classes.Add(entry.Name, new(ReadTiming(entry.Value.GetProperty("fast")), Positive(entry.Value, "powerChargeSeconds"), ReadTiming(entry.Value.GetProperty("powerRelease"))));
        return new(Positive(root, "powerThresholdSeconds"), Positive(root, "powerDamageMultiplier"), Positive(root, "powerStaminaMultiplier"),
            ReadTiming(root.GetProperty("kick")), root.GetProperty("kickStaminaCost").GetInt32(), root.GetProperty("kickDamage").GetInt32(), classes);
    }

    private static DaggerfallAttackTiming ReadTiming(JsonElement value) =>
        new(Positive(value, "windup"), Positive(value, "commit"), Positive(value, "recovery"));

    private static double Positive(JsonElement parent, string name) => parent.GetProperty(name).GetDouble() is > 0d and var value && double.IsFinite(value)
        ? value : throw new InvalidOperationException($"Attack tuning '{name}' must be positive and finite.");
}
