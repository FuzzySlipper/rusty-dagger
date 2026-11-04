using System.Text.Json;
using WorldRpg.Kit.Combat;
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

/// <summary>A weapon class's attacks: the fast attack, the power attack's charge in seconds, its release, and how far they reach.</summary>
internal sealed record DaggerfallWeaponClassAttacks(DaggerfallAttackTiming Fast, double PowerCharge, DaggerfallAttackTiming PowerRelease, float Reach);

/// <summary>An attack's path in view space, as <see cref="SwingArc"/> takes it; its range comes from the weapon class.</summary>
internal readonly record struct DaggerfallAttackPath(float Start, float End, float Tilt);

/// <summary>The player's attack set: its timing per weapon class, and what a power attack and a kick cost and do.</summary>
internal sealed record DaggerfallAttackTuning(
    double PowerThresholdSeconds, double PowerDamageMultiplier, double PowerStaminaMultiplier,
    DaggerfallAttackTiming Kick, int KickStaminaCost, int KickDamage, float KickReach,
    IReadOnlyDictionary<string, DaggerfallWeaponClassAttacks> Classes,
    IReadOnlyDictionary<string, DaggerfallAttackPath> Paths)
{
    private static readonly DaggerfallWeaponClassAttacks ShortBlade = new(new(.18d, .12d, .28d), .5d, new(.15d, .12d, .35d), 1.9f);

    internal static DaggerfallAttackTuning Default { get; } = new(.2d, 2d, 2d, new(.25d, .12d, .4d), 6, 1, 1.7f,
        new Dictionary<string, DaggerfallWeaponClassAttacks>(StringComparer.Ordinal)
        {
            [DaggerfallSkills.ShortBlade] = ShortBlade,
            [DaggerfallSkills.LongBlade] = new(new(.25d, .15d, .33d), .7d, new(.2d, .15d, .4d), 2.3f),
            [DaggerfallSkills.Axe] = new(new(.3d, .15d, .35d), .8d, new(.22d, .15d, .45d), 2.2f),
            [DaggerfallSkills.BluntWeapon] = new(new(.32d, .15d, .38d), .85d, new(.25d, .15d, .45d), 2.2f),
            [DaggerfallSkills.HandToHand] = new(new(.15d, .1d, .2d), .4d, new(.12d, .1d, .3d), 1.6f),
        },
        new Dictionary<string, DaggerfallAttackPath>(StringComparer.Ordinal)
        {
            ["fastA"] = new(37f, -61f, 10f),
            ["fastB"] = new(-50f, 45f, -10f),
            ["fastC"] = new(-35f, 30f, 85f),
            ["fastD"] = new(-12f, 12f, 0f),
            [DaggerfallAttackIds.PowerOut] = new(-45f, 35f, 80f),
            [DaggerfallAttackIds.Kick] = new(-12f, 12f, 0f),
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

    /// <summary>The arc a melee attack sweeps with a weapon of <paramref name="skill"/>; refuses an attack with no path, a content error.</summary>
    internal SwingArc Arc(string attack, string skill)
    {
        if (!Paths.TryGetValue(attack, out DaggerfallAttackPath path)) throw new InvalidOperationException($"Attack tuning has no path for '{attack}'.");
        return new(path.Start, path.End, path.Tilt, attack == DaggerfallAttackIds.Kick ? KickReach : For(skill).Reach);
    }

    internal static DaggerfallAttackTuning Read(JsonElement root)
    {
        Dictionary<string, DaggerfallWeaponClassAttacks> classes = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in root.GetProperty("classes").EnumerateObject())
            classes.Add(entry.Name, new(ReadTiming(entry.Value.GetProperty("fast")), Positive(entry.Value, "powerChargeSeconds"), ReadTiming(entry.Value.GetProperty("powerRelease")), (float)Positive(entry.Value, "reach")));
        Dictionary<string, DaggerfallAttackPath> paths = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in root.GetProperty("paths").EnumerateObject())
            paths.Add(entry.Name, new(entry.Value.GetProperty("start").GetSingle(), entry.Value.GetProperty("end").GetSingle(), entry.Value.GetProperty("tilt").GetSingle()));
        return new(Positive(root, "powerThresholdSeconds"), Positive(root, "powerDamageMultiplier"), Positive(root, "powerStaminaMultiplier"),
            ReadTiming(root.GetProperty("kick")), root.GetProperty("kickStaminaCost").GetInt32(), root.GetProperty("kickDamage").GetInt32(), (float)Positive(root, "kickReach"), classes, paths);
    }

    private static DaggerfallAttackTiming ReadTiming(JsonElement value) =>
        new(Positive(value, "windup"), Positive(value, "commit"), Positive(value, "recovery"));

    private static double Positive(JsonElement parent, string name) => parent.GetProperty(name).GetDouble() is > 0d and var value && double.IsFinite(value)
        ? value : throw new InvalidOperationException($"Attack tuning '{name}' must be positive and finite.");
}
