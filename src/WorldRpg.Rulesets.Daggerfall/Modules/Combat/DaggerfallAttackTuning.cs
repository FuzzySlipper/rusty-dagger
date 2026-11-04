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

/// <summary>
/// The paths of weapons that swing differently from the default, by item template: what the rules hold the arc to, so a
/// weapon's cut roughly follows the way its clips carry it. The arc stays authoritative; the clip only sells it.
/// </summary>
internal sealed record DaggerfallAttackPathGroup(IReadOnlySet<int> Templates, IReadOnlyDictionary<string, DaggerfallAttackPath> Paths);

/// <summary>
/// How a character shapes the attack: what Speed, weapon skill and Strength do to its timing, what skill does to its
/// damage and chain, how a winded character fights, and an Adrenaline Rush at low health.
/// </summary>
/// <param name="SpeedPerPointAbove50">The share of an attack's time each point of Speed above average takes off, or adds below it.</param>
/// <param name="SkillAt100">The share of an attack's time a weapon skill of 100 takes off.</param>
/// <param name="StrengthPerPointAbove50">The share of a power attack's charge each point of Strength above average takes off.</param>
/// <param name="MinimumTimeScale">The shortest an attack's time can be scaled to.</param>
/// <param name="SkillDamagePerPoint">The damage each point of weapon skill adds, rounded down; unarmed damage scales with skill by its own formula.</param>
/// <param name="ChainSkills">The skill each chain step past the second needs.</param>
/// <param name="WindedStamina">At or below this much stamina a character is winded: no power attack, kick or guard.</param>
/// <param name="WindedTimeScale">How much longer a winded character's attacks take.</param>
/// <param name="WindedDamageScale">How much of its damage a winded character's blow does.</param>
/// <param name="BackstabMinimum">The multiplier on an unaware target at no Backstabbing.</param>
/// <param name="BackstabMaximum">The multiplier on an unaware target at Backstabbing 100.</param>
/// <param name="RushTimeScale">An Adrenaline Rush's time scale.</param>
/// <param name="RushDamageScale">An Adrenaline Rush's damage scale.</param>
/// <param name="ImprovedRushTimeScale">An improved rush's time scale.</param>
/// <param name="ImprovedRushDamageScale">An improved rush's damage scale.</param>
internal sealed record DaggerfallCombatantTuning(double SpeedPerPointAbove50, double SkillAt100, double StrengthPerPointAbove50, double MinimumTimeScale,
    double SkillDamagePerPoint, IReadOnlyList<int> ChainSkills, double WindedStamina, double WindedTimeScale, double WindedDamageScale,
    double BackstabMinimum, double BackstabMaximum, double RushTimeScale, double RushDamageScale, double ImprovedRushTimeScale, double ImprovedRushDamageScale)
{
    internal static DaggerfallCombatantTuning Default { get; } = new(.004d, .15d, .004d, .5d, .05d, [35, 70], 128d, 1.4d, .5d, 1.5d, 3d, .85d, 1.25d, .75d, 1.5d);

    /// <summary>How many fast attacks a weapon skill chains: two, and one more for each skill step reached.</summary>
    internal int ChainLength(int skill) => Math.Min(DaggerfallAttackIds.Fast.Count, 2 + ChainSkills.Count(step => skill >= step));

    /// <summary>The share of a blow's damage an unaware target takes on top of it, by Backstabbing.</summary>
    internal double BackstabMultiplier(int skill) => BackstabMinimum + (BackstabMaximum - BackstabMinimum) * Math.Clamp(skill, 0, 100) / 100d;

    internal static DaggerfallCombatantTuning Read(JsonElement root) => new(
        NonNegative(root, "speedPerPointAbove50"), NonNegative(root, "skillAt100"), NonNegative(root, "strengthPerPointAbove50"), NonNegative(root, "minimumTimeScale"),
        NonNegative(root, "skillDamagePerPoint"), [.. root.GetProperty("chainSkills").EnumerateArray().Select(step => step.GetInt32())],
        NonNegative(root, "windedStamina"), NonNegative(root, "windedTimeScale"), NonNegative(root, "windedDamageScale"),
        NonNegative(root, "backstabMinimum"), NonNegative(root, "backstabMaximum"), NonNegative(root, "rushTimeScale"), NonNegative(root, "rushDamageScale"),
        NonNegative(root, "improvedRushTimeScale"), NonNegative(root, "improvedRushDamageScale"));

    private static double NonNegative(JsonElement parent, string name) => parent.GetProperty(name).GetDouble() is >= 0d and var value && double.IsFinite(value)
        ? value : throw new InvalidOperationException($"Attack tuning '{name}' must not be negative.");
}

/// <summary>The player's attack set: its timing per weapon class, and what a power attack and a kick cost and do.</summary>
internal sealed record DaggerfallAttackTuning(
    double PowerThresholdSeconds, double PowerDamageMultiplier, double PowerStaminaMultiplier,
    DaggerfallAttackTiming Kick, int KickStaminaCost, int KickDamage, float KickReach,
    IReadOnlyDictionary<string, DaggerfallWeaponClassAttacks> Classes,
    IReadOnlyDictionary<string, DaggerfallAttackPath> Paths,
    IReadOnlyList<DaggerfallAttackPathGroup> PathGroups)
{
    internal DaggerfallCombatantTuning Combatant { get; init; } = DaggerfallCombatantTuning.Default;

    private static readonly DaggerfallWeaponClassAttacks ShortBlade = new(new(.18d, .12d, .28d), .5d, new(.15d, .12d, .35d), 1.9f);

    internal static DaggerfallAttackTuning Default { get; } = new(.2d, 2d, 2d, new(.25d, .12d, .4d), 64, 1, 1.7f,
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
        },
        [
            new(new HashSet<int> { 116, 117, 118, 119, 120, 121, 124, 127, 288 },
                new Dictionary<string, DaggerfallAttackPath>(StringComparer.Ordinal) { ["fastC"] = new(30f, -55f, 35f) }),
            new(new HashSet<int> { 115 }, new Dictionary<string, DaggerfallAttackPath>(StringComparer.Ordinal)
            {
                ["fastB"] = new(-50f, 45f, 15f),
                [DaggerfallAttackIds.PowerOut] = new(45f, -50f, 5f),
            }),
        ]);

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

    /// <summary>
    /// The arc a melee attack sweeps with a weapon of <paramref name="skill"/> and item <paramref name="template"/>, a path
    /// group's where one covers it; refuses an attack with no path, a content error.
    /// </summary>
    internal SwingArc Arc(string attack, string skill, int? template)
    {
        DaggerfallAttackPath? chosen = PathGroups.FirstOrDefault(group => template is int item && group.Templates.Contains(item) && group.Paths.ContainsKey(attack))?.Paths[attack];
        if ((chosen ?? (Paths.TryGetValue(attack, out DaggerfallAttackPath common) ? common : null)) is not { } path)
            throw new InvalidOperationException($"Attack tuning has no path for '{attack}'.");
        return new(path.Start, path.End, path.Tilt, attack == DaggerfallAttackIds.Kick ? KickReach : For(skill).Reach);
    }

    internal static DaggerfallAttackTuning Read(JsonElement root)
    {
        Dictionary<string, DaggerfallWeaponClassAttacks> classes = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in root.GetProperty("classes").EnumerateObject())
            classes.Add(entry.Name, new(ReadTiming(entry.Value.GetProperty("fast")), Positive(entry.Value, "powerChargeSeconds"), ReadTiming(entry.Value.GetProperty("powerRelease")), (float)Positive(entry.Value, "reach")));
        List<DaggerfallAttackPathGroup> groups = [];
        foreach (JsonElement group in root.GetProperty("pathGroups").EnumerateArray())
            groups.Add(new(group.GetProperty("templates").EnumerateArray().Select(template => template.GetInt32()).ToHashSet(), ReadPaths(group.GetProperty("paths"))));
        return new(Positive(root, "powerThresholdSeconds"), Positive(root, "powerDamageMultiplier"), Positive(root, "powerStaminaMultiplier"),
            ReadTiming(root.GetProperty("kick")), root.GetProperty("kickStaminaCost").GetInt32(), root.GetProperty("kickDamage").GetInt32(), (float)Positive(root, "kickReach"), classes, ReadPaths(root.GetProperty("paths")), groups)
        { Combatant = DaggerfallCombatantTuning.Read(root.GetProperty("combatant")) };
    }

    private static Dictionary<string, DaggerfallAttackPath> ReadPaths(JsonElement value)
    {
        Dictionary<string, DaggerfallAttackPath> paths = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in value.EnumerateObject())
            paths.Add(entry.Name, new(entry.Value.GetProperty("start").GetSingle(), entry.Value.GetProperty("end").GetSingle(), entry.Value.GetProperty("tilt").GetSingle()));
        return paths;
    }

    private static DaggerfallAttackTiming ReadTiming(JsonElement value) =>
        new(Positive(value, "windup"), Positive(value, "commit"), Positive(value, "recovery"));

    private static double Positive(JsonElement parent, string name) => parent.GetProperty(name).GetDouble() is > 0d and var value && double.IsFinite(value)
        ? value : throw new InvalidOperationException($"Attack tuning '{name}' must be positive and finite.");
}
