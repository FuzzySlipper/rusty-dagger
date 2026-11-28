using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>
/// The road's rule values (#11178), the expedition tuning's <c>road</c> section: how near the party a travelling band is
/// live on the map, and what a band on the road beyond meets along the way. Each segment of a band's route is judged once
/// as it sets out along it, from the hazards held then in the region it starts in: its chance in a hundred of a raid and
/// of a delay is the sum, over each hazard, of that hazard's chance times its severity.
/// </summary>
/// <param name="LiveDistance">Within this many map pixels of the party a band on the road becomes live on the map.</param>
/// <param name="RoadDistance">Beyond this many pixels a live band that is free to go goes back on the road; more than <paramref name="LiveDistance"/>, so a band at the edge does not flicker between them.</param>
/// <param name="SegmentPixels">How many route pixels one segment of the road covers.</param>
/// <param name="DelayHours">How long a delay holds a band where it was delayed.</param>
/// <param name="Wilds">The road's own chances, with no hazard held: severity one.</param>
/// <param name="Hazards">Each hazard's chances per point of severity, by the world condition that holds it.</param>
/// <param name="HostileReach">Within this many pixels of a hostile band on the map, the segment carries <paramref name="HostileBand"/> too.</param>
/// <param name="HostileBand">A hostile band's chances, near enough.</param>
internal sealed record DaggerfallRoadTuning(double LiveDistance, double RoadDistance, int SegmentPixels, double DelayHours,
    DaggerfallRoadChances Wilds, IReadOnlyDictionary<DaggerfallRoadHazard, DaggerfallRoadChances> Hazards, double HostileReach,
    DaggerfallRoadChances HostileBand)
{
    internal static DaggerfallRoadTuning Default { get; } = new(8, 12, 4, 6, new(0, 1),
        new Dictionary<DaggerfallRoadHazard, DaggerfallRoadChances>
        {
            [DaggerfallRoadHazard.War] = new(3, 6), [DaggerfallRoadHazard.Raiders] = new(5, 4),
            [DaggerfallRoadHazard.Monsters] = new(3, 5), [DaggerfallRoadHazard.Plague] = new(0, 6),
        }, 3, new(25, 10));

    internal DaggerfallRoadTuning Validate()
    {
        if (!double.IsFinite(LiveDistance) || LiveDistance <= 0 || !double.IsFinite(RoadDistance) || RoadDistance <= LiveDistance)
            throw new InvalidOperationException("Road tuning needs a positive 'liveDistance' and a greater 'roadDistance'.");
        if (SegmentPixels < 1) throw new InvalidOperationException("Road tuning 'segmentPixels' must be at least one.");
        if (!double.IsFinite(DelayHours) || DelayHours <= 0) throw new InvalidOperationException("Road tuning 'delayHours' must be positive.");
        if (!double.IsFinite(HostileReach) || HostileReach < 0) throw new InvalidOperationException("Road tuning 'hostileReach' cannot be negative.");
        Wilds.Validate("wilds");
        HostileBand.Validate("hostileBand");
        foreach (DaggerfallRoadHazard hazard in Enum.GetValues<DaggerfallRoadHazard>())
            (Hazards.TryGetValue(hazard, out DaggerfallRoadChances? chances) ? chances
                : throw new InvalidOperationException($"Road tuning 'hazards' lacks '{Key(hazard)}'.")).Validate($"hazards.{Key(hazard)}");
        return this;
    }

    internal static DaggerfallRoadTuning Read(JsonElement section) => new DaggerfallRoadTuning(
        section.GetProperty("liveDistance").GetDouble(),
        section.GetProperty("roadDistance").GetDouble(),
        section.GetProperty("segmentPixels").GetInt32(),
        section.GetProperty("delayHours").GetDouble(),
        DaggerfallRoadChances.Read(section.GetProperty("wilds")),
        Enum.GetValues<DaggerfallRoadHazard>().ToDictionary(hazard => hazard,
            hazard => DaggerfallRoadChances.Read(section.GetProperty("hazards").GetProperty(Key(hazard)))),
        section.GetProperty("hostileReach").GetDouble(),
        DaggerfallRoadChances.Read(section.GetProperty("hostileBand"))).Validate();

    private static string Key(DaggerfallRoadHazard hazard) => hazard switch
    {
        DaggerfallRoadHazard.War => "war",
        DaggerfallRoadHazard.Raiders => "raiders",
        DaggerfallRoadHazard.Monsters => "monsters",
        DaggerfallRoadHazard.Plague => "plague",
        _ => throw new ArgumentOutOfRangeException(nameof(hazard), hazard, null),
    };

    /// <summary>
    /// The hazard a world condition holds on the roads of its region, if any: a war's or a skirmish's tension (not a
    /// town's unrest), the board's raiders, a monster surge, or a plague's quarantine.
    /// </summary>
    internal static DaggerfallRoadHazard? HazardOf(DaggerfallWorldConditionToken token) => token.Kind switch
    {
        DaggerfallWorldConditionKind.BorderTension when token.SubjectTag is null => DaggerfallRoadHazard.War,
        DaggerfallWorldConditionKind.GrainShortage when token.SubjectTag == RaidersTag => DaggerfallRoadHazard.Raiders,
        DaggerfallWorldConditionKind.MonsterSurge => DaggerfallRoadHazard.Monsters,
        DaggerfallWorldConditionKind.PlagueScare => DaggerfallRoadHazard.Plague,
        _ => null,
    };

    /// <summary>The subject tag of the board's raiders, which show as a grain shortage on the roads they prey on.</summary>
    internal const string RaidersTag = "raiders";
}

/// <summary>What holds a region's roads dangerous.</summary>
internal enum DaggerfallRoadHazard { War, Raiders, Monsters, Plague }

/// <summary>A hazard's chances in a hundred, per segment, that a band on the road is raided and that it is delayed.</summary>
internal sealed record DaggerfallRoadChances(int Raid, int Delay)
{
    internal void Validate(string name)
    {
        if (Raid is < 0 or > 100 || Delay is < 0 or > 100)
            throw new InvalidOperationException($"Road tuning '{name}' chances must be from 0 to 100.");
    }

    internal static DaggerfallRoadChances Read(JsonElement section) =>
        new(section.GetProperty("raid").GetInt32(), section.GetProperty("delay").GetInt32());
}
