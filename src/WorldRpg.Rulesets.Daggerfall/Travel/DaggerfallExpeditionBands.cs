using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>What a band on the map roughly is, as its token shows: read from its foe's team and nature.</summary>
internal enum DaggerfallBandKind { Bandits, Travellers, Beasts, Undead, Monsters }

/// <summary>
/// One band on the expedition map: a staged token, not a simulated party. Its foe is the one its table gave when it was
/// staged; it camps or wanders, may spot the party and pursue it, and leaves after a while. It is never saved: a band
/// that is met becomes queued encounters, and the rest are staged afresh.
/// </summary>
internal sealed class DaggerfallExpeditionBand
{
    internal DaggerfallExpeditionBand(int id, DaggerfallEncounterRequest request, DaggerfallEncounterChoice choice, string name,
        DaggerfallBandKind kind, int size, bool camp, double x, double y, long leaves)
    {
        Id = id; Request = request; Choice = choice; Name = name; Kind = kind; Size = size; Camp = camp; X = TargetX = x; Y = TargetY = y; Leaves = leaves;
    }

    internal int Id { get; }
    internal DaggerfallEncounterRequest Request { get; }
    internal DaggerfallEncounterChoice Choice { get; }

    /// <summary>The band's name as the map writes it: its foe, and how many.</summary>
    internal string Name { get; }
    internal DaggerfallBandKind Kind { get; }
    internal int Size { get; }

    /// <summary>A camp stays put; any other band wanders, and pursues the party once it has spotted it.</summary>
    internal bool Camp { get; set; }

    /// <summary>Where it stands, in fractional world-map pixels.</summary>
    internal double X { get; set; }
    internal double Y { get; set; }

    /// <summary>The way it faces, in degrees clockwise from north.</summary>
    internal double Heading { get; set; }
    internal double TargetX { get; set; }
    internal double TargetY { get; set; }

    /// <summary>The absolute second it leaves the map.</summary>
    internal long Leaves { get; }

    /// <summary>It has seen the party: a wandering band now pursues it, and meeting it is a fight.</summary>
    internal bool Spotted { get; set; }

    /// <summary>The party came upon it unseen and steered away: it neither meets nor spots the party until the party has drawn off.</summary>
    internal bool Passed { get; set; }

    internal double Distance(double x, double y) => Math.Sqrt((X - x) * (X - x) + (Y - y) * (Y - y));
}

/// <summary>What a band did in a stretch that the map stops for.</summary>
internal enum DaggerfallBandEventKind { Spotted, Caught, CameUpon }

internal sealed record DaggerfallBandEvent(DaggerfallBandEventKind Kind, DaggerfallExpeditionBand Band);

/// <summary>
/// One persistent travelling party on the expedition map linked to <see cref="DaggerfallActorLedger"/> entries whose
/// location override is <see cref="ActorLocationKind.OnOverlandBand"/> with the same <see cref="BandId"/>.
/// Unlike transient <see cref="DaggerfallExpeditionBand"/> chasers, a persistent band survives map close/reopen and
/// save/load, marches toward its destination across map stretches, and materializes its exact ledger actors into
/// <c>EntityStore</c> on intercept or destination arrival.
/// </summary>
internal sealed class DaggerfallPersistentOverlandBand
{
    internal DaggerfallPersistentOverlandBand(
        string bandId,
        int displayId,
        string name,
        DaggerfallBandKind kind,
        double x,
        double y,
        double heading,
        double speedPixelsPerHour,
        DaggerfallExpeditionMark? destination = null,
        DaggerfallWorldProfileKeySave? destinationProfile = null,
        bool camp = false,
        bool spotted = false,
        bool hostile = false,
        int? mobile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        BandId = bandId;
        DisplayId = displayId;
        Name = name;
        Kind = kind;
        X = x;
        Y = y;
        Heading = heading;
        SpeedPixelsPerHour = speedPixelsPerHour;
        Destination = destination;
        DestinationProfile = destinationProfile;
        Camp = camp;
        Spotted = spotted;
        Hostile = hostile;
        Mobile = mobile;
    }

    internal string BandId { get; }
    internal int DisplayId { get; }
    internal string Name { get; }
    internal DaggerfallBandKind Kind { get; }
    internal double X { get; set; }
    internal double Y { get; set; }
    internal double Heading { get; set; }
    internal double SpeedPixelsPerHour { get; }
    internal DaggerfallExpeditionMark? Destination { get; set; }
    internal DaggerfallWorldProfileKeySave? DestinationProfile { get; set; }
    internal bool Camp { get; set; }
    internal bool Spotted { get; set; }
    internal bool Passed { get; set; }
    internal bool Hostile { get; }
    internal int? Mobile { get; }

    internal double Distance(double x, double y) => Math.Sqrt((X - x) * (X - x) + (Y - y) * (Y - y));

    /// <summary>
    /// Advances this persistent band by <paramref name="hours"/> toward its destination (or pursuing the party if
    /// <see cref="Hostile"/> and <see cref="Spotted"/>). Returns true when the band reaches its destination pixel.
    /// </summary>
    internal bool StepMove((double X, double Y) party, double hours)
    {
        if (Camp || SpeedPixelsPerHour <= 0 || hours <= 0) return false;
        double targetX, targetY;
        bool pursuing = Hostile && Spotted;
        if (pursuing)
        {
            targetX = party.X;
            targetY = party.Y;
        }
        else if (Destination is { } dest)
        {
            targetX = dest.Pixel.X + .5;
            targetY = dest.Pixel.Y + .5;
        }
        else
        {
            return false;
        }

        double dx = targetX - X;
        double dy = targetY - Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double step = SpeedPixelsPerHour * hours;
        if (length <= 1e-9) return !pursuing && Destination is not null;

        Heading = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        double moved = Math.Min(step, length);
        X += dx / length * moved;
        Y += dy / length * moved;
        return !pursuing && Destination is not null && length <= step + 1e-9;
    }

    internal DaggerfallPersistentBandSave ToSave() => new(
        BandId,
        Name,
        Kind,
        X,
        Y,
        Heading,
        SpeedPixelsPerHour,
        Destination,
        DestinationProfile,
        Camp,
        Spotted,
        Hostile,
        Mobile);

    internal static DaggerfallPersistentOverlandBand FromSave(DaggerfallPersistentBandSave save, int displayId) => new(
        save.BandId,
        displayId,
        save.Name,
        save.Kind,
        save.X,
        save.Y,
        save.Heading,
        save.SpeedPixelsPerHour,
        save.Destination,
        save.DestinationProfile,
        save.Camp,
        save.Spotted,
        save.Hostile,
        save.Mobile);
}

/// <summary>
/// The light, time and weather a band and the party see by, and the party's way of going, at the moment sight is judged.
/// </summary>
/// <param name="Scouting">The named input a later scouting skill feeds the party's sight: a share from 0 to 1.</param>
/// <param name="Concealment">The named input the player's stealth feeds against a band's sight: a share from 0 to 1.</param>
internal readonly record struct DaggerfallBandSightConditions(DaggerfallExpeditionMode Mode, bool Night, DaggerfallWeatherKind Weather,
    int Climate, double Scouting, double Concealment);

/// <summary>
/// The bands staged around the party on the expedition map, as fakery: every few hours a keyed roll may put one in the
/// wild a few pixels off, its foe drawn from the same classic tables as an unseen encounter for that pixel and hour.
/// Each stretch moves them (a camp stays, a band wanders or, once it has spotted the party, pursues it), judges sight
/// both ways, and reports the first thing the map stops for: a band spotting the party, catching it, or the party coming
/// upon one unseen. The map shows only the bands within the party's sight.
/// </summary>
internal sealed class DaggerfallExpeditionBands(DaggerfallTravelPolicy policy, DaggerfallEncounterSet tables,
    DaggerfallMobileCatalogSet mobiles, DaggerfallBandTuning tuning, Func<DaggerfallTravelMapPixel, bool> located)
{
    private const long SecondsPerHour = DaggerfallCalendar.SecondsPerMinute * DaggerfallCalendar.MinutesPerHour;
    private readonly List<DaggerfallExpeditionBand> _bands = [];
    private int _nextId = 1;
    private long _nextStaging = -1;

    internal IReadOnlyList<DaggerfallExpeditionBand> All => _bands;
    internal DaggerfallBandTuning Tuning => tuning;

    /// <summary>The map closed: its bands are gone, and staging starts over when it opens.</summary>
    internal void Clear()
    {
        _bands.Clear();
        _nextStaging = -1;
    }

    internal void Remove(DaggerfallExpeditionBand band) => _bands.Remove(band);

    /// <summary>Whether a band is still staged and near enough the party to be met.</summary>
    internal bool Within(DaggerfallExpeditionBand band, (double X, double Y) party) =>
        _bands.Contains(band) && band.Distance(party.X, party.Y) <= tuning.MeetDistance;

    /// <summary>How far the party sees, in map pixels.</summary>
    internal double PartySight(DaggerfallBandSightConditions conditions)
    {
        DaggerfallSightTuning sight = tuning.PartySight;
        return sight.Base * sight.Mode[conditions.Mode] * (conditions.Night ? sight.Night : 1) * sight.Weather[conditions.Weather]
            * sight.Climate[DaggerfallWeatherState.ClimateGroup(conditions.Climate)] * (1 + conditions.Scouting * sight.InputWeight);
    }

    /// <summary>How far a band sees the party, in map pixels: farther by a reckless march or a camp's fire, less by stealth.</summary>
    internal double BandSight(DaggerfallBandSightConditions conditions)
    {
        DaggerfallSightTuning sight = tuning.BandSight;
        return sight.Base * sight.Mode[conditions.Mode] * (conditions.Night ? sight.Night : 1) * sight.Weather[conditions.Weather]
            * sight.Climate[DaggerfallWeatherState.ClimateGroup(conditions.Climate)] * Math.Max(0, 1 - conditions.Concealment * sight.InputWeight);
    }

    /// <summary>The bands within the party's sight, nearest first.</summary>
    internal IEnumerable<DaggerfallExpeditionBand> Seen((double X, double Y) party, double sight) =>
        _bands.Where(band => band.Distance(party.X, party.Y) <= sight).OrderBy(band => band.Distance(party.X, party.Y));

    /// <summary>
    /// One stretch of the map's time for the bands: staging when an interval has passed, then each band moves and looks,
    /// and the first one the map must stop for is returned.
    /// </summary>
    /// <param name="now">The absolute second the stretch ended.</param>
    /// <param name="draw">A keyed draw from <c>minimum</c> to <c>maximum</c> inclusive.</param>
    internal DaggerfallBandEvent? Step(long seconds, long now, (double X, double Y) party, DaggerfallBandSightConditions conditions,
        int playerLevel, Func<string, int, int, int> draw)
    {
        if (_nextStaging < 0) _nextStaging = now - seconds + (long)(tuning.StageEveryHours * SecondsPerHour);
        while (now >= _nextStaging)
        {
            TryStage(_nextStaging, party, playerLevel, draw, null, null);
            _nextStaging += (long)(tuning.StageEveryHours * SecondsPerHour);
        }
        _bands.RemoveAll(band => now >= band.Leaves || band.Distance(party.X, party.Y) > tuning.StageDistance.Maximum * 2);
        double hours = seconds / (double)SecondsPerHour;
        double sees = BandSight(conditions);
        DaggerfallBandEvent? found = null;
        foreach (DaggerfallExpeditionBand band in _bands)
        {
            if (!band.Camp) Move(band, party, hours, draw);
            double distance = band.Distance(party.X, party.Y);
            if (band.Passed && distance > tuning.MeetDistance * 2) band.Passed = false;
            // A meeting settles first: one that sees as far as the meeting is never come upon unseen, however far the
            // stretch carried the party.
            if (distance <= tuning.MeetDistance && !band.Passed)
                return new(band.Spotted || sees >= tuning.MeetDistance ? DaggerfallBandEventKind.Caught : DaggerfallBandEventKind.CameUpon, band);
            if (!band.Spotted && !band.Passed && distance <= sees)
            {
                // A camp that sees the party breaks camp to give chase.
                band.Spotted = true;
                band.Camp = false;
                found ??= new(DaggerfallBandEventKind.Spotted, band);
            }
        }
        return found;
    }

    /// <summary>
    /// Stages one band now, for a developer: at a distance (pixels) and bearing (degrees clockwise from north) from the
    /// party, camped or wandering. Null with the reason when nothing fits there.
    /// </summary>
    internal (DaggerfallExpeditionBand? Band, string? Refusal) StageAt(long now, (double X, double Y) party, int playerLevel,
        Func<string, int, int, int> draw, double distance, double bearing, bool camp)
    {
        DaggerfallExpeditionBand? band = TryStage(now, party, playerLevel, draw, (distance, bearing), camp);
        return band is null ? (null, "No band can be staged there: open sea, a location, or a foe the tables do not publish.") : (band, null);
    }

    private DaggerfallExpeditionBand? TryStage(long when, (double X, double Y) party, int playerLevel, Func<string, int, int, int> draw,
        (double Distance, double Bearing)? at, bool? camp)
    {
        if (tables.Tables.Count == 0) return null;
        int id = _nextId;
        string key = $"band:{id}:at:{when}";
        if (at is null)
        {
            if (_bands.Count >= tuning.MaximumBands || draw($"{key}:chance", 1, 100) > tuning.StageChance) return null;
        }
        (double distance, double bearing) = at ?? (draw($"{key}:distance", (int)(tuning.StageDistance.Minimum * 100), (int)(tuning.StageDistance.Maximum * 100)) / 100d,
            draw($"{key}:bearing", 0, 359));
        double radians = bearing * Math.PI / 180;
        double x = party.X + Math.Sin(radians) * distance, y = party.Y - Math.Cos(radians) * distance;
        DaggerfallTravelMapPixel pixel = new((int)Math.Floor(x), (int)Math.Floor(y));
        if (!pixel.IsInBounds || policy.IsOcean(pixel) || located(pixel)) return null;
        int climate = policy.Climate(pixel);
        long timeOfDay = when % DaggerfallCalendar.SecondsPerDay / DaggerfallCalendar.SecondsPerMinute;
        bool day = timeOfDay is >= 360 and <= 1080;
        DaggerfallEncounterRequest request = new(day ? DaggerfallEncounterContext.WildernessDay : DaggerfallEncounterContext.WildernessNight,
            Math.Max(1, playerLevel), Climate: climate);
        DaggerfallEncounterChoice choice = DaggerfallEncounterPolicy.Pick(tables, request, (roll, minimum, maximum) => draw($"{key}:{roll}", minimum, maximum));
        if (choice.MobileId is not int mobileId || !mobiles.Mobiles.TryGetValue(mobileId, out DaggerfallMobileDefinition? mobile) || !mobile.IsPublished)
            return null;
        int size = draw($"{key}:size", tuning.Size.Minimum, tuning.Size.Maximum);
        bool camped = camp ?? draw($"{key}:camp", 1, 100) <= tuning.CampShare;
        DaggerfallExpeditionBand band = new(id, request, choice, size == 1 ? mobile.DonorName : $"{size} {Plural(mobile.DonorName)}",
            KindOf(mobile), size, camped, x, y, when + (long)(tuning.LifetimeHours * SecondsPerHour));
        _nextId++;
        _bands.Add(band);
        return band;
    }

    private void Move(DaggerfallExpeditionBand band, (double X, double Y) party, double hours, Func<string, int, int, int> draw)
    {
        (double toX, double toY, double speed) = band.Spotted ? (party.X, party.Y, tuning.PursueSpeed) : (band.TargetX, band.TargetY, tuning.WanderSpeed);
        double dx = toX - band.X, dy = toY - band.Y, length = Math.Sqrt(dx * dx + dy * dy);
        double step = speed * hours;
        if (length > 1e-9)
        {
            band.Heading = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
            double moved = Math.Min(step, length);
            band.X += dx / length * moved;
            band.Y += dy / length * moved;
        }
        if (!band.Spotted && length <= step)
        {
            // A wanderer picks a new spot a pixel or two off once it reaches the last.
            int turn = draw($"band:{band.Id}:wander:{band.X:F2},{band.Y:F2}", 0, 359);
            double radians = turn * Math.PI / 180;
            band.TargetX = band.X + Math.Sin(radians) * 1.5;
            band.TargetY = band.Y - Math.Cos(radians) * 1.5;
        }
    }

    /// <summary>What a foe's band looks like on the map, from its team and nature.</summary>
    internal static DaggerfallBandKind KindOf(DaggerfallMobileDefinition mobile) => (mobile.Team, mobile.Affinity) switch
    {
        ("Criminals", _) => DaggerfallBandKind.Bandits,
        (_, "Human") => DaggerfallBandKind.Travellers,
        (_, "Animal") => DaggerfallBandKind.Beasts,
        (_, "Undead") or ("Undead", _) => DaggerfallBandKind.Undead,
        _ => DaggerfallBandKind.Monsters,
    };

    private static string Plural(string name) => name.EndsWith('s') ? name : name.EndsWith("Thief", StringComparison.Ordinal) ? name[..^1] + "ves"
        : name.EndsWith('y') ? name[..^1] + "ies" : name + "s";
}

/// <summary>One sight radius's rule values: a base in map pixels and its factors, and how much its named input weighs.</summary>
internal sealed record DaggerfallSightTuning(double Base, IReadOnlyDictionary<DaggerfallExpeditionMode, double> Mode, double Night,
    IReadOnlyDictionary<DaggerfallWeatherKind, double> Weather, IReadOnlyList<double> Climate, double InputWeight)
{
    internal DaggerfallSightTuning Validate(string name)
    {
        if (!double.IsFinite(Base) || Base <= 0) throw new InvalidOperationException($"Band tuning '{name}.base' must be positive.");
        if (Enum.GetValues<DaggerfallExpeditionMode>().Any(mode => !Mode.TryGetValue(mode, out double value) || !double.IsFinite(value) || value < 0)
            || Enum.GetValues<DaggerfallWeatherKind>().Any(weather => !Weather.TryGetValue(weather, out double value) || !double.IsFinite(value) || value < 0)
            || Climate.Count != DaggerfallWeatherState.ClimateCount || Climate.Any(value => !double.IsFinite(value) || value < 0)
            || !double.IsFinite(Night) || Night < 0 || !double.IsFinite(InputWeight) || InputWeight < 0)
            throw new InvalidOperationException($"Band tuning '{name}' needs a factor for every mode, weather and climate group, none negative.");
        return this;
    }

    internal static DaggerfallSightTuning Read(JsonElement section, string name) => new DaggerfallSightTuning(
        section.GetProperty("base").GetDouble(),
        Enum.GetValues<DaggerfallExpeditionMode>().ToDictionary(mode => mode, mode => section.GetProperty("mode").GetProperty(Key(mode)).GetDouble()),
        section.GetProperty("night").GetDouble(),
        Enum.GetValues<DaggerfallWeatherKind>().ToDictionary(weather => weather, weather => section.GetProperty("weather").GetProperty(Key(weather)).GetDouble()),
        [.. section.GetProperty("climate").EnumerateArray().Select(value => value.GetDouble())],
        section.GetProperty("inputWeight").GetDouble()).Validate(name);

    private static string Key<T>(T value) where T : Enum => value.ToString().ToLowerInvariant();
}

/// <summary>
/// The bands' rule values, the <c>bands</c> part of the expedition tuning: how often and how far off they are staged and
/// how many, how long they stay, how big and how many camp, how fast they wander and pursue, how near is a meeting, and
/// the two sight radii (the party's fed by scouting, a band's eased by concealment).
/// </summary>
internal sealed record DaggerfallBandTuning(double StageEveryHours, int StageChance, int MaximumBands, double LifetimeHours,
    (double Minimum, double Maximum) StageDistance, (int Minimum, int Maximum) Size, int CampShare, double WanderSpeed, double PursueSpeed,
    double MeetDistance, DaggerfallSightTuning PartySight, DaggerfallSightTuning BandSight)
{
    private static IReadOnlyDictionary<DaggerfallWeatherKind, double> Weather(double fog, double rain, double thunder, double snow) =>
        new Dictionary<DaggerfallWeatherKind, double>
        {
            [DaggerfallWeatherKind.Sunny] = 1, [DaggerfallWeatherKind.Cloudy] = 1, [DaggerfallWeatherKind.Overcast] = .9,
            [DaggerfallWeatherKind.Fog] = fog, [DaggerfallWeatherKind.Rain] = rain, [DaggerfallWeatherKind.Thunder] = thunder,
            [DaggerfallWeatherKind.Snow] = snow,
        };

    private static IReadOnlyDictionary<DaggerfallExpeditionMode, double> Modes(double cautious, double reckless, double halt, double camp) =>
        new Dictionary<DaggerfallExpeditionMode, double>
        {
            [DaggerfallExpeditionMode.Cautious] = cautious, [DaggerfallExpeditionMode.Reckless] = reckless,
            [DaggerfallExpeditionMode.Halt] = halt, [DaggerfallExpeditionMode.Camp] = camp,
        };

    internal static DaggerfallBandTuning Default { get; } = new(2, 40, 4, 24, (2.5, 5), (1, 3), 40, .25, .8, .35,
        new(3, Modes(1, .75, 1, .8), .5, Weather(.4, .75, .6, .7), [1.3, 1.2, .7, .8, 1, .85], 1),
        new(1.6, Modes(.8, 1.4, .7, 1.3), .6, Weather(.5, .8, .7, .75), [1.2, 1.1, .7, .8, 1, .85], .5));

    internal DaggerfallBandTuning Validate()
    {
        if (!double.IsFinite(StageEveryHours) || StageEveryHours <= 0 || StageChance is < 0 or > 100 || MaximumBands < 0
            || !double.IsFinite(LifetimeHours) || LifetimeHours <= 0 || StageDistance.Minimum <= 0 || StageDistance.Maximum < StageDistance.Minimum
            || Size.Minimum < 1 || Size.Maximum < Size.Minimum || CampShare is < 0 or > 100 || WanderSpeed < 0 || PursueSpeed < 0
            || !double.IsFinite(MeetDistance) || MeetDistance <= 0)
            throw new InvalidOperationException("Band tuning needs positive intervals, distances and sizes, and shares from 0 to 100.");
        PartySight.Validate("partySight");
        BandSight.Validate("bandSight");
        return this;
    }

    internal static DaggerfallBandTuning Read(JsonElement section) => new DaggerfallBandTuning(
        section.GetProperty("stageEveryHours").GetDouble(),
        section.GetProperty("stageChance").GetInt32(),
        section.GetProperty("maximumBands").GetInt32(),
        section.GetProperty("lifetimeHours").GetDouble(),
        (section.GetProperty("stageDistance")[0].GetDouble(), section.GetProperty("stageDistance")[1].GetDouble()),
        (section.GetProperty("size")[0].GetInt32(), section.GetProperty("size")[1].GetInt32()),
        section.GetProperty("campShare").GetInt32(),
        section.GetProperty("wanderSpeed").GetDouble(),
        section.GetProperty("pursueSpeed").GetDouble(),
        section.GetProperty("meetDistance").GetDouble(),
        DaggerfallSightTuning.Read(section.GetProperty("partySight"), "partySight"),
        DaggerfallSightTuning.Read(section.GetProperty("bandSight"), "bandSight")).Validate();
}
