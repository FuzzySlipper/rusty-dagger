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

/// <summary>A bow's shot: how long the draw takes in seconds, and the release, whose windup ends as the arrow leaves.</summary>
internal sealed record DaggerfallShotTuning(double Draw, DaggerfallAttackTiming Release);

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
/// <param name="WindedStamina">At or below this much stamina a character is winded: no power attack, kick or guard, until it refills past it.</param>
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
    internal static DaggerfallCombatantTuning Default { get; } = new(.004d, .15d, .004d, .5d, .05d, [35, 70], 16d, 1.4d, .5d, 1.5d, 3d, .85d, 1.25d, .75d, 1.5d);

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

/// <summary>
/// The player's combat Stamina: how big the pool is, how it refills, and how a rising Fatigue squeezes both. Fatigue
/// here is the share of the classic pool used up, from 0 rested to 1 at collapse.
/// </summary>
/// <param name="Base">The pool before attributes.</param>
/// <param name="PerAttributePoint">What each point of Strength and of Endurance adds to the pool.</param>
/// <param name="RefillPerSecond">How fast Stamina refills.</param>
/// <param name="QuietSeconds">How long after the last spend the refill starts.</param>
/// <param name="FatiguePerSwing">The classic pool's cost of each attack, in its stored units.</param>
/// <param name="ShrinkFromFatigue">The Fatigue past which the pool shrinks.</param>
/// <param name="ShrunkScale">The share of the pool left at collapse.</param>
/// <param name="SlowFromFatigue">The Fatigue past which the refill slows.</param>
/// <param name="SlowedScale">The share of the refill rate left at collapse.</param>
internal sealed record DaggerfallStaminaTuning(double Base, double PerAttributePoint, double RefillPerSecond, double QuietSeconds, double FatiguePerSwing,
    double ShrinkFromFatigue, double ShrunkScale, double SlowFromFatigue, double SlowedScale)
{
    internal static DaggerfallStaminaTuning Default { get; } = new(20d, .75d, 8d, 1d, 11d, .5d, .4d, .75d, .5d);

    /// <summary>The pool a character of these attributes has at this Fatigue.</summary>
    internal int Maximum(int strength, int endurance, double fatigue) =>
        (int)((Base + PerAttributePoint * (strength + endurance)) * Squeeze(fatigue, ShrinkFromFatigue, ShrunkScale));

    /// <summary>The share of the refill rate left at this Fatigue.</summary>
    internal double RefillScale(double fatigue) => Squeeze(fatigue, SlowFromFatigue, SlowedScale);

    private static double Squeeze(double fatigue, double from, double scale) =>
        1d - (1d - scale) * Math.Clamp((fatigue - from) / (1d - from), 0d, 1d);

    internal static DaggerfallStaminaTuning Read(JsonElement root) => new(
        NonNegative(root, "base"), NonNegative(root, "perAttributePoint"), Positive(root, "refillPerSecond"), NonNegative(root, "quietSeconds"),
        NonNegative(root, "fatiguePerSwing"), Share(root, "shrinkFromFatigue"), Share(root, "shrunkScale"), Share(root, "slowFromFatigue"),
        Positive(root, "slowedScale"));

    private static double NonNegative(JsonElement parent, string name) => parent.GetProperty(name).GetDouble() is >= 0d and var value && double.IsFinite(value)
        ? value : throw new InvalidOperationException($"Stamina tuning '{name}' must not be negative.");

    private static double Positive(JsonElement parent, string name) => parent.GetProperty(name).GetDouble() is > 0d and var value && double.IsFinite(value)
        ? value : throw new InvalidOperationException($"Stamina tuning '{name}' must be positive.");

    private static double Share(JsonElement parent, string name) => parent.GetProperty(name).GetDouble() is >= 0d and < 1d and var value
        ? value : throw new InvalidOperationException($"Stamina tuning '{name}' must be a share below one.");
}

/// <summary>
/// What a blow does to the body it lands on: its force against the target's weight sets how it reacts, and heavy blows
/// have weight on screen. Force and weight are in the classic's weight units, in which a person weighs 240.
/// </summary>
/// <param name="FastForce">A fast attack's force, and an arrow's.</param>
/// <param name="PowerForce">A power attack's force.</param>
/// <param name="KickForce">A kick's force.</param>
/// <param name="StrengthPerPointAbove50">The share of its force each point of Strength above average adds, or takes off below it.</param>
/// <param name="DefaultWeight">The weight of a person, for anyone the content gives none.</param>
/// <param name="MinimumWeight">The least a body counts as weighing, so a fast attack doesn't fling a rat across the room.</param>
/// <param name="StaggerImpact">The force over weight that staggers a target, stopping what it was doing.</param>
/// <param name="KnockdownImpact">The force over weight that knocks a target down, which only a kick or a power attack can.</param>
/// <param name="StaggerSeconds">How long a stagger holds a target.</param>
/// <param name="KnockdownSeconds">How long a target stays down.</param>
/// <param name="PushPerImpact">How fast each point of force over weight throws a target, in metres per second.</param>
/// <param name="MaximumPush">The fastest a blow throws anything.</param>
/// <param name="PushDeceleration">How fast a thrown target slows, in metres per second per second.</param>
/// <param name="HitStopRate">The rate world time runs at through a hit-stop.</param>
/// <param name="HitStopSteps">How many admitted steps a blow's hit-stop lasts.</param>
/// <param name="HeavyHitStopSteps">How many a kick's or a power attack's lasts.</param>
/// <param name="PunchDegrees">How far a landed blow knocks the view.</param>
/// <param name="HeavyPunchDegrees">How far a kick or a power attack knocks it.</param>
/// <param name="PunchSettleRate">How fast the view settles back, per second.</param>
internal sealed record DaggerfallStrikeTuning(double FastForce, double PowerForce, double KickForce, double StrengthPerPointAbove50, int DefaultWeight, int MinimumWeight,
    double StaggerImpact, double KnockdownImpact, double StaggerSeconds, double KnockdownSeconds, double PushPerImpact, double MaximumPush, double PushDeceleration,
    double HitStopRate, int HitStopSteps, int HeavyHitStopSteps, double PunchDegrees, double HeavyPunchDegrees, double PunchSettleRate)
{
    internal static DaggerfallStrikeTuning Default { get; } = new(400d, 900d, 1600d, .01d, 240, 160, 2.5d, 5d, .5d, 1.6d, 1d, 7d, 9d, .25d, 1, 2, .5d, 1.5d, 14d);

    /// <summary>Whether a player's attack is one of the heavy ones.</summary>
    internal static bool Heavy(string attack) => attack is DaggerfallAttackIds.Kick or DaggerfallAttackIds.PowerOut;

    /// <summary>What a player's attack does to a target of <paramref name="weight"/>; a weightless target, such as a ghost, is untouched.</summary>
    internal DaggerfallStrikeReaction React(string attack, int strength, int? weight)
    {
        int mass = weight ?? DefaultWeight;
        if (mass <= 0) return default;
        bool kick = attack == DaggerfallAttackIds.Kick;
        double force = kick ? KickForce : attack == DaggerfallAttackIds.PowerOut ? PowerForce : FastForce;
        double impact = force * (1d + (strength - 50) * StrengthPerPointAbove50) / Math.Max(mass, MinimumWeight);
        DaggerfallStrikeLevel level = impact >= KnockdownImpact && Heavy(attack) ? DaggerfallStrikeLevel.Knockdown
            : impact >= StaggerImpact ? DaggerfallStrikeLevel.Stagger : DaggerfallStrikeLevel.Flinch;
        double push = level != DaggerfallStrikeLevel.Flinch || kick ? Math.Min(MaximumPush, impact * PushPerImpact) : 0d;
        double hold = level switch { DaggerfallStrikeLevel.Knockdown => KnockdownSeconds, DaggerfallStrikeLevel.Stagger => StaggerSeconds, _ => 0d };
        return new(level, push, hold);
    }

    internal static DaggerfallStrikeTuning Read(JsonElement root) => new(
        Positive(root, "fastForce"), Positive(root, "powerForce"), Positive(root, "kickForce"), Positive(root, "strengthPerPointAbove50"),
        (int)Positive(root, "defaultWeight"), (int)Positive(root, "minimumWeight"), Positive(root, "staggerImpact"), Positive(root, "knockdownImpact"), Positive(root, "staggerSeconds"), Positive(root, "knockdownSeconds"),
        Positive(root, "pushPerImpact"), Positive(root, "maximumPush"), Positive(root, "pushDeceleration"), Positive(root, "hitStopRate"),
        (int)Positive(root, "hitStopSteps"), (int)Positive(root, "heavyHitStopSteps"), Positive(root, "punchDegrees"), Positive(root, "heavyPunchDegrees"), Positive(root, "punchSettleRate"));

    private static double Positive(JsonElement parent, string name) => parent.GetProperty(name).GetDouble() is > 0d and var value && double.IsFinite(value)
        ? value : throw new InvalidOperationException($"Strike tuning '{name}' must be positive.");
}

/// <summary>How hard a blow rocked its target.</summary>
internal enum DaggerfallStrikeLevel { Flinch, Stagger, Knockdown }

/// <summary>What a blow does to its target: how hard it rocks it, how fast it throws it, and how long it holds it.</summary>
internal readonly record struct DaggerfallStrikeReaction(DaggerfallStrikeLevel Level, double Push, double HoldSeconds);

/// <summary>The player's attack set: its timing per weapon class, and what a power attack and a kick cost and do.</summary>
internal sealed record DaggerfallAttackTuning(
    double PowerThresholdSeconds, double PowerDamageMultiplier, double PowerStaminaMultiplier,
    DaggerfallAttackTiming Kick, int KickStaminaCost, int KickDamage, float KickReach,
    IReadOnlyDictionary<string, DaggerfallWeaponClassAttacks> Classes,
    IReadOnlyDictionary<string, DaggerfallAttackPath> Paths,
    IReadOnlyList<DaggerfallAttackPathGroup> PathGroups)
{
    internal DaggerfallCombatantTuning Combatant { get; init; } = DaggerfallCombatantTuning.Default;
    internal DaggerfallStaminaTuning Stamina { get; init; } = DaggerfallStaminaTuning.Default;
    internal DaggerfallStrikeTuning Strike { get; init; } = DaggerfallStrikeTuning.Default;

    /// <summary>How far along the view an attack's start looks for a civilian it is aimed at, in metres.</summary>
    internal double AimRange { get; init; } = 64d;

    /// <summary>The left hand's cast: its spell releases as the windup ends.</summary>
    internal DaggerfallAttackTiming Cast { get; init; } = new(.3d, .02d, .3d);

    /// <summary>A bow's shot.</summary>
    internal DaggerfallShotTuning Shot { get; init; } = new(.6d, new(.02d, .02d, .45d));

    private static readonly DaggerfallWeaponClassAttacks ShortBlade = new(new(.18d, .12d, .28d), .5d, new(.15d, .12d, .35d), 1.9f);

    internal static DaggerfallAttackTuning Default { get; } = new(.2d, 2d, 2d, new(.25d, .12d, .4d), 10, 1, 1.7f,
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
            DaggerfallAttackIds.ShotDraw => new(Shot.Draw, 0d, 0d),
            DaggerfallAttackIds.Shot => Shot.Release,
            DaggerfallAttackIds.PowerIn => new(weapon.PowerCharge, 0d, 0d),
            DaggerfallAttackIds.PowerOut => weapon.PowerRelease,
            DaggerfallAttackIds.Kick => Kick,
            DaggerfallAttackIds.Cast => Cast,
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
        { Shot = new(Positive(root.GetProperty("shot"), "draw"), ReadTiming(root.GetProperty("shot").GetProperty("release"))), Combatant = DaggerfallCombatantTuning.Read(root.GetProperty("combatant")), Stamina = DaggerfallStaminaTuning.Read(root.GetProperty("stamina")), Strike = DaggerfallStrikeTuning.Read(root.GetProperty("strike")), Cast = ReadTiming(root.GetProperty("cast")), AimRange = root.GetProperty("aimRange").GetDouble() };
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
