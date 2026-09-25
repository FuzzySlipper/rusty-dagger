using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Which natural talents the worn items improve, as the donor's own params name them.</summary>
internal readonly record struct DaggerfallHeldTalents(bool AcuteHearing, bool Athleticism, bool AdrenalineRush);

/// <summary>
/// A living creature that can satisfy a held enchantment's "near" condition, already grouped the way
/// the donor groups it: the ruleset's own enemy-group policy answers that, so this owner never
/// re-derives a grouping from a mobile id.
/// </summary>
internal readonly record struct DaggerfallNearbyCreature(DaggerfallEnemyGroup Group, WorldPoint Position);

/// <summary>
/// The enchantments the player's worn items hold. Daggerfall's item enchantments are "held" effect
/// payloads: they contribute while their item is equipped and stop the moment it is not. This owner
/// recomputes them from one signature — the equipment revision, the calendar that conditions some of
/// them, and the creature groups standing near the player — and applies each through a per-source stat
/// source, so taking a contribution back is exact and a restored save rebinds the same source rather
/// than stacking a second one.
/// </summary>
/// <remarks>
/// Donor values read from the effect classes: <c>EnhancesSkill.modAmount</c> 15 per skill,
/// <c>ExtraSpellPts.maxIncrease</c> 75 gated by season, lunar phase or nearby creature group within
/// <c>nearbyRadius</c> 18m, and <c>IncreasedWeightAllowance</c> ×1.25/×1.5. The enchantment's classic
/// param carries its meaning: a skill index for EnhancesSkill, and the donor's own param order for the
/// rest.
/// </remarks>
internal sealed class DaggerfallHeldEnchantments
{
    // Classic EnchantmentTypes (API/ItemsFile.cs): ExtraSpellPts = 3, IncreasedWeightAllowance = 7,
    // EnhancesSkill = 10, ImprovesTalents = 13. Potent-vs, regeneration and the cast-when-* payloads
    // belong to their own owners rather than to the worn-state contributions.
    private const int ExtraSpellPointsType = 3;
    private const int IncreasedWeightAllowanceType = 7;
    private const int RegeneratesHealthType = 5;
    private const int EnhancesSkillType = 10;
    private const int StrengthensArmorType = 12;
    private const int ImprovesTalentsType = 13;

    internal const int EnhancedSkillPoints = 15;

    /// <summary>
    /// The donor's StrengthensArmor shifts the wearer's armor value down by five, and a lower armor
    /// value is the stronger rating. It sets one modifier rather than adding per item, so two worn
    /// sources leave the same rating rather than doubling it.
    /// </summary>
    internal const int StrengthenedArmorValue = -5;
    internal const int ExtraSpellPoints = 75;
    internal const double NearbyCreatureMeters = 18d;

    /// <summary>
    /// The donor's health regeneration: one point every fourth magic round, per worn source, which is
    /// fifteen points an hour apiece.
    /// </summary>
    internal const int RegeneratedHealthPerTick = 1;
    internal const int RoundsPerRegeneration = 4;
    private const int AlwaysRegenerates = 0;
    private const int SunlightRegenerates = 1;
    private const int DarknessRegenerates = 2;
    private const int NeverRegenerates = 3;

    /// <summary>
    /// The classic skill order the enchantment param indexes, mirroring the donor's
    /// <c>DFCareer.Skills</c> enumeration rather than the pack's alphabetical vocabulary.
    /// </summary>
    private static readonly string[] ClassicSkillIds =
    [
        "medical", "etiquette", "streetwise", "jumping", "orcish", "harpy", "giantish", "dragonish",
        "nymph", "daedric", "spriggan", "centaurian", "impish", "lockpicking", "mercantile",
        "pickpocket", "stealth", "swimming", "climbing", "backstabbing", "dodging", "running",
        "destruction", "restoration", "illusion", "alteration", "thaumaturgy", "mysticism",
        "short-blade", "long-blade", "hand-to-hand", "axe", "blunt-weapon", "archery", "critical-strike",
    ];

    private readonly MechanicsEquipmentCoordinator _equipment;
    private readonly DaggerfallItemInstances _instances;
    private readonly IReadOnlyDictionary<string, DaggerfallMagicItemDefinition> _magicItems;
    private readonly StatsComponent _stats;
    private readonly EntityDirectory _entities;
    private readonly EntityId _actor;
    private readonly Func<DaggerfallCalendar> _calendar;
    private readonly Func<WorldPoint?> _playerPosition;
    private readonly Func<IReadOnlyList<DaggerfallNearbyCreature>> _nearby;
    private readonly Func<bool> _playerInSunlight;
    private readonly List<AppliedContribution> _applied = [];
    // Indexed by the donor's RegensHealth params: always, in sunlight, in darkness, and one slot for a
    // param the donor never names, which counts for cleanup but never contributes a tick.
    private readonly int[] _regeneration = new int[4];
    private HeldSignature? _signature;
    // The donor counts magic rounds since startup or load and resets that count on either, so the
    // every-fourth-round beat is anchored to the session rather than to the calendar's absolute minute.
    private long _roundsSinceStart;
    private int _regenerationTotal => _regeneration[AlwaysRegenerates] + _regeneration[SunlightRegenerates]
        + _regeneration[DarknessRegenerates];

    internal DaggerfallHeldEnchantments(MechanicsEquipmentCoordinator equipment, DaggerfallItemInstances instances,
        IReadOnlyDictionary<string, DaggerfallMagicItemDefinition> magicItems, StatsComponent playerStats, EntityDirectory entities, EntityId actor,
        Func<DaggerfallCalendar> calendar, Func<WorldPoint?> playerPosition, Func<IReadOnlyList<DaggerfallNearbyCreature>> nearby,
        Func<bool>? playerInSunlight = null)
    {
        _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _magicItems = magicItems ?? throw new ArgumentNullException(nameof(magicItems));
        _stats = playerStats ?? throw new ArgumentNullException(nameof(playerStats));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _actor = actor;
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _playerPosition = playerPosition ?? throw new ArgumentNullException(nameof(playerPosition));
        _nearby = nearby ?? throw new ArgumentNullException(nameof(nearby));
        _playerInSunlight = playerInSunlight ?? (() => false);
    }

    /// <summary>The talents the worn items improve right now.</summary>
    internal DaggerfallHeldTalents Talents { get; private set; }

    /// <summary>What the worn items add to the player's carry allowance, ×1 when nothing does.</summary>
    internal double CarryMultiplier { get; private set; } = 1d;

    /// <summary>The armor-value shift the worn items give, zero when none strengthens armor.</summary>
    internal int ArmorValueModifier { get; private set; }

    /// <summary>How many worn sources regenerate health, one tick each.</summary>
    internal int RegeneratingSources => _regenerationTotal;

    /// <summary>
    /// Recomputes every held contribution when what they depend on changed: the equipment revision
    /// covers equip, unequip, transfer, break and a restored save; the calendar covers season and lunar
    /// phase; the nearby groups cover a creature arriving or leaving. Nothing re-applies while all three
    /// read the same.
    /// </summary>
    internal void Refresh()
    {
        EquipmentRead read = _equipment.Read();
        DaggerfallCalendar calendar = _calendar();
        IReadOnlyList<DaggerfallNearbyCreature> nearby = _nearby();
        HeldSignature signature = new(read.Revision, calendar.Season, MoonRatio(calendar), NearbySignature(nearby));
        if (_signature == signature) return;
        _signature = signature;

        // Take back everything this owner gave before recomputing: an item that left the body for any
        // reason takes its enchantment with it, and an item that stayed is re-applied under the same
        // identity so nothing stacks twice.
        foreach (AppliedContribution applied in _applied) Stat(applied.StatId).RemoveSource(applied.Identity);
        _applied.Clear();
        Talents = default;
        double carry = 1d;
        int armor = 0;
        Array.Clear(_regeneration);

        foreach (WorldRpg.Kit.Inventory.EquipmentAssignment assignment in read.Assignments)
        {
            if (!TryEnchantments(assignment, out IReadOnlyList<DaggerfallMagicEnchantmentDefinition> enchantments)) continue;
            foreach (DaggerfallMagicEnchantmentDefinition enchantment in enchantments)
            {
                switch (enchantment.Type)
                {
                    case EnhancesSkillType when SkillFor(enchantment.Param) is { } skill:
                        Apply(assignment, enchantment, new DaggerfallStatId(skill), EnhancedSkillPoints);
                        break;
                    case ExtraSpellPointsType when Holds(enchantment.Param, calendar, nearby):
                        Apply(assignment, enchantment, DaggerfallMechanicsIds.MagickaMaximum, ExtraSpellPoints);
                        break;
                    case IncreasedWeightAllowanceType:
                        carry = Math.Max(carry, WeightMultiplier(enchantment.Param));
                        break;
                    case StrengthensArmorType:
                        armor = StrengthenedArmorValue;
                        break;
                    case RegeneratesHealthType:
                        _regeneration[RegenerationCondition(enchantment.Param)]++;
                        break;
                    case ImprovesTalentsType:
                        Talents = Talent(enchantment.Param) switch
                        {
                            DaggerfallHeldTalentKind.AcuteHearing => Talents with { AcuteHearing = true },
                            DaggerfallHeldTalentKind.Athleticism => Talents with { Athleticism = true },
                            DaggerfallHeldTalentKind.AdrenalineRush => Talents with { AdrenalineRush = true },
                            _ => Talents,
                        };
                        break;
                }
            }
        }

        CarryMultiplier = carry;
        ArmorValueModifier = armor;
    }

    /// <summary>
    /// Applies what the worn items do on the rounds that just elapsed. One magic round is one game
    /// minute, and the beat is the donor's: it counts rounds since the session started or a save was
    /// restored, and reads the count before the round it is serving, so the first round after either is
    /// itself a beat. A catch-up interval therefore regenerates for every fourth round it covered
    /// rather than once.
    /// </summary>
    /// <param name="minutes">How many magic rounds that interval covered.</param>
    internal void AdvanceRounds(int minutes)
    {
        if (minutes <= 0) return;
        // The donor bounds its own catch-up well below a year of minutes; the effect lifecycle's cap is
        // that same bound, reused here so a held payload cannot out-heal the effects beside it.
        int rounds = Math.Min(minutes, checked((int)DaggerfallEffectLifecycle.MaximumElapsedCatchupRounds));
        int ticks = 0;
        for (int round = 1; round <= rounds; round++)
            // The donor increments its counter after raising the round, so the round being served reads
            // the count that preceded it: the session's first round is a beat, not its fourth.
            if ((_roundsSinceStart + round - 1) % RoundsPerRegeneration == 0) ticks++;
        _roundsSinceStart += rounds;
        if (ticks == 0 || _regenerationTotal == 0) return;

        bool sunlight = _playerInSunlight();
        int sources = _regeneration[AlwaysRegenerates]
            + _regeneration[sunlight ? SunlightRegenerates : DarknessRegenerates];
        if (sources == 0) return;

        Track health = _stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        long maximum = checked((long)health.Maximum.Value);
        long current = checked((long)health.Current);
        long restored = Math.Min(maximum, checked(current + checked((long)ticks * sources * RegeneratedHealthPerTick)));
        // A wearer already at full health is not a change, and the donor's IncreaseHealth clamps there.
        if (restored == current) return;
        health.SetCurrent(restored, clamp: true);
    }

    private void Apply(WorldRpg.Kit.Inventory.EquipmentAssignment assignment, DaggerfallMagicEnchantmentDefinition enchantment, DaggerfallStatId statId, int amount)
    {
        EffectSourceIdentity identity = IdentityFor(assignment, enchantment);
        Stat stat = Stat(statId);
        List<StatSource> sources = [.. stat.Sources.Where(source => source.Identity != identity)];
        sources.Add(new StatSource(
            identity,
            SourceDefinitionId.Parse($"daggerfall.held.{enchantment.Key}"),
            priority: 0,
            [new StatContributionDefinition(
                StatId.Parse(statId.Value),
                StackingGroupId.Parse($"daggerfall.held.{enchantment.Key}"),
                MechanicsStackingPolicy.Sum,
                new StatContribution.Add(amount))]));
        stat.SetSources(StatId.Parse(statId.Value), sources);
        _applied.Add(new AppliedContribution(statId, identity));
    }

    /// <summary>
    /// What a worn item's enchantment key applies: a published magic item's own payloads, or one of the
    /// item maker's settings, which no published item carries. Both answer the same effect shape, so the
    /// applied contributions have one path.
    /// </summary>
    private bool TryEnchantments(WorldRpg.Kit.Inventory.EquipmentAssignment assignment,
        out IReadOnlyList<DaggerfallMagicEnchantmentDefinition> enchantments)
    {
        enchantments = [];
        if (_entities.IdentityOf(new EntityId(assignment.Item.EntityId)) is not { Kind: DurableIdentityKind.Item } identity) return false;
        if (!_instances.ContainsUnique(identity.Value)) return false;
        if (_instances.RequireUnique(identity.Value).Enchantment is not { } key) return false;
        if (_magicItems.TryGetValue(key, out DaggerfallMagicItemDefinition? published))
        {
            enchantments = published.Enchantments;
            return true;
        }
        if (!DaggerfallEnchantmentSettings.TryResolve(key, out DaggerfallEnchantmentSetting setting)) return false;
        enchantments = [DaggerfallEnchantmentSettings.ToEffect(setting)];
        return true;
    }

    private EffectSourceIdentity IdentityFor(WorldRpg.Kit.Inventory.EquipmentAssignment assignment, DaggerfallMagicEnchantmentDefinition enchantment)
    {
        DurableIdentityReference identity = _entities.IdentityOf(new EntityId(assignment.Item.EntityId));
        return new EffectSourceIdentity(_actor, EffectInstanceId.Parse($"held.{identity.Value}"), 1,
            SourceDefinitionId.Parse($"daggerfall.held.{enchantment.Key}"));
    }

    private Stat Stat(DaggerfallStatId id) => _stats.GetStat(StatId.Parse(id.Value));

    /// <summary>The pack skill a classic skill index names, or null for an index the pack does not carry.</summary>
    private string? SkillFor(int classicIndex) => classicIndex >= 0 && classicIndex < ClassicSkillIds.Length
        ? ClassicSkillIds[classicIndex]
        : null;

    /// <summary>
    /// Whether an ExtraSpellPts param's condition holds now. Params 0-3 are the seasons, 4-6 the lunar
    /// phases, 7-10 the creature groups the donor's own group function recognizes.
    /// </summary>
    internal bool ConditionHolds(int param) => Holds(param, _calendar(), _nearby());

    private bool Holds(int param, DaggerfallCalendar calendar, IReadOnlyList<DaggerfallNearbyCreature> nearby) => param switch
    {
        0 => calendar.Season == DaggerfallSeason.Winter,
        1 => calendar.Season == DaggerfallSeason.Spring,
        2 => calendar.Season == DaggerfallSeason.Summer,
        3 => calendar.Season == DaggerfallSeason.Autumn,
        4 => IsMoon(calendar, MoonPhase.Full),
        5 => IsMoon(calendar, MoonPhase.HalfWax) || IsMoon(calendar, MoonPhase.HalfWane),
        6 => IsMoon(calendar, MoonPhase.New),
        7 => IsNear(nearby, DaggerfallEnemyGroup.Undead),
        8 => IsNear(nearby, DaggerfallEnemyGroup.Daedra),
        9 => IsNear(nearby, DaggerfallEnemyGroup.Humanoid),
        10 => IsNear(nearby, DaggerfallEnemyGroup.Animals),
        _ => false,
    };

    private bool IsNear(IReadOnlyList<DaggerfallNearbyCreature> nearby, DaggerfallEnemyGroup group)
    {
        if (_playerPosition() is not WorldPoint player) return false;
        foreach (DaggerfallNearbyCreature creature in nearby)
        {
            if (creature.Group != group) continue;
            // The donor's lookup keeps every object strictly inside the radius.
            if (Distance(player, creature.Position) < NearbyCreatureMeters) return true;
        }
        return false;
    }

    private static double Distance(WorldPoint left, WorldPoint right)
    {
        double x = left.X - right.X, y = left.Y - right.Y, z = left.Z - right.Z;
        return Math.Sqrt((x * x) + (y * y) + (z * z));
    }

    private bool IsMoon(DaggerfallCalendar calendar, MoonPhase phase) =>
        MoonPhaseAt(calendar, masser: true) == phase || MoonPhaseAt(calendar, masser: false) == phase;

    /// <summary>
    /// The donor's lunar phase: a 32-day cycle over the day of the year and the year's own day count,
    /// offset three days for Masser and one back for Secunda so the full moon lands where classic put it.
    /// </summary>
    private static MoonPhase MoonPhaseAt(DaggerfallCalendar calendar, bool masser)
    {
        int offset = masser ? 3 : -1;
        int ratio = ((calendar.DayOfYear + (calendar.Year * 12 * 30) + offset) % 32 + 32) % 32;
        return ratio switch
        {
            0 => MoonPhase.Full,
            16 => MoonPhase.New,
            <= 5 => MoonPhase.ThreeWane,
            <= 10 => MoonPhase.HalfWane,
            <= 15 => MoonPhase.OneWane,
            <= 22 => MoonPhase.OneWax,
            <= 28 => MoonPhase.HalfWax,
            _ => MoonPhase.ThreeWax,
        };
    }

    private static int MoonRatio(DaggerfallCalendar calendar) => ((calendar.DayOfYear + (calendar.Year * 12 * 30)) % 32 + 32) % 32;

    /// <summary>The four groups whose presence can matter, as a change signature.</summary>
    private int NearbySignature(IReadOnlyList<DaggerfallNearbyCreature> nearby)
    {
        int signature = 0;
        if (IsNear(nearby, DaggerfallEnemyGroup.Undead)) signature |= 1;
        if (IsNear(nearby, DaggerfallEnemyGroup.Daedra)) signature |= 2;
        if (IsNear(nearby, DaggerfallEnemyGroup.Humanoid)) signature |= 4;
        if (IsNear(nearby, DaggerfallEnemyGroup.Animals)) signature |= 8;
        return signature;
    }

    /// <summary>
    /// The donor's RegensHealth params: all the time, in sunlight, in darkness. The donor leaves its
    /// own switch without a default, so a param it does not name never regenerates.
    /// </summary>
    private static int RegenerationCondition(int param) => param switch
    {
        0 => AlwaysRegenerates,
        1 => SunlightRegenerates,
        2 => DarknessRegenerates,
        _ => NeverRegenerates,
    };

    /// <summary>
    /// Whether the player stands in sunlight, exactly as the donor reads it: daytime, not inside any
    /// structure — a building counts as much as a dungeon — and not in prison.
    /// </summary>
    internal static bool InSunlight(bool isDay, bool insideStructure, bool inPrison) => isDay && !insideStructure && !inPrison;

    internal static double WeightMultiplier(int param) => param switch
    {
        0 => 1.25d,
        1 => 1.5d,
        _ => 1d,
    };

    internal static DaggerfallHeldTalentKind Talent(int param) => param switch
    {
        0 => DaggerfallHeldTalentKind.AcuteHearing,
        1 => DaggerfallHeldTalentKind.Athleticism,
        2 => DaggerfallHeldTalentKind.AdrenalineRush,
        _ => DaggerfallHeldTalentKind.None,
    };

    internal enum DaggerfallHeldTalentKind { None, AcuteHearing, Athleticism, AdrenalineRush }

    private enum MoonPhase { New, OneWax, HalfWax, ThreeWax, Full, ThreeWane, HalfWane, OneWane }

    private readonly record struct AppliedContribution(DaggerfallStatId StatId, EffectSourceIdentity Identity);

    private readonly record struct HeldSignature(ulong EquipmentRevision, DaggerfallSeason Season, int MoonRatio, int NearbyGroups);
}
