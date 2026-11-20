using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>How the party goes: cautious (the default), reckless (faster and more tiring), or stopped by the road or in camp.</summary>
internal enum DaggerfallExpeditionMode { Cautious, Reckless, Halt, Camp }

/// <summary>What the party does with a stretch of time, which Condition charges or recovers as it would anywhere.</summary>
internal enum DaggerfallExpeditionActivity { Marching, Swimming, Sailing, Halted, Camping }

/// <summary>The ship the party sails on: none, a hired passage, or the player's own.</summary>
internal enum DaggerfallExpeditionShip { None, Hired, Owned }

/// <summary>One stretch of time the party passes through the session's calendar, doing one thing.</summary>
/// <param name="FatigueFactor">How many times the activity's per-minute fatigue the stretch spends.</param>
internal readonly record struct DaggerfallExpeditionStretch(long Seconds, DaggerfallExpeditionActivity Activity, double FatigueFactor);

/// <summary>
/// What the expedition asks of the world it crosses, each answered by the domain that knows it. The session answers;
/// the expedition never reads a later domain's state itself.
/// </summary>
internal interface IDaggerfallExpeditionSurroundings
{
    DaggerfallCalendar Calendar { get; }

    /// <summary>The weather now over a source climate.</summary>
    DaggerfallWeatherKind Weather(int climate);

    /// <summary>The land transport the player owns: a horse, else a cart, else their feet.</summary>
    DaggerfallTransportMode Transport { get; }

    bool OwnsShip { get; }

    /// <summary>A guild grants the player free passage on a hired ship.</summary>
    bool FreeShipPassage { get; }

    /// <summary>Pays from carried gold; false, with nothing paid, when it is not carried.</summary>
    bool TrySpendGold(int amount);

    /// <summary>The player's fatigue as a share of its pool.</summary>
    double FatigueShare { get; }

    bool Exhausted { get; }

    bool Defeated { get; }

    /// <summary>Passes one stretch through the one calendar with the activity's per-minute rules; the seconds it applied.</summary>
    long Pass(DaggerfallExpeditionStretch stretch);

    /// <summary>One hour in camp: the rest hour's recovery, whole in lodging or aboard, the rough-ground share in the wild.</summary>
    void RecoverCampHour(bool sheltered);

    /// <summary>The party drops from exhaustion: in the water it drowns, elsewhere it sleeps an hour where it fell.</summary>
    void Collapse(bool inWater);

    /// <summary>Rolls one hour's unseen encounter at the party's pixel; true when a foe was selected, waiting to be met.</summary>
    bool EncounterArrives(DaggerfallTravelMapPixel pixel, bool atLocation, long minute);
}

/// <summary>Where Enter puts the player: the party's pixel, the location standing there if any, and the pixel the party came from.</summary>
internal sealed record DaggerfallExpeditionEntry(DaggerfallTravelMapPixel Pixel, DaggerfallSiteRecord? Location, DaggerfallTravelMapPixel CameFrom);

/// <summary>A point on the map the party may head for or the player may pin: a pixel, and the place there when one was chosen.</summary>
internal sealed record DaggerfallExpeditionMark([property: JsonRequired] DaggerfallTravelMapPixel Pixel, DaggerfallSiteId? Place);

/// <summary>The leg the party walks: the donor's route path from where it started to where it heads, and how far along it the party is.</summary>
internal sealed record DaggerfallExpeditionLeg([property: JsonRequired] DaggerfallTravelMapPixel Origin,
    [property: JsonRequired] DaggerfallExpeditionMark Destination, [property: JsonRequired] int Step, [property: JsonRequired] double Progress);

/// <summary>Lodging paid at a place, good until an absolute second.</summary>
internal sealed record DaggerfallExpeditionLodging([property: JsonRequired] DaggerfallSiteId Place, [property: JsonRequired] long PaidUntil);

/// <summary>
/// The expedition saved where the map was left: whether it was open, where the player's body stands and the party is,
/// the leg, mode, pin, ship and lodging, the low-fatigue warning, an encounter waiting to be met and the hour in camp.
/// </summary>
internal sealed record DaggerfallExpeditionSave(
    [property: JsonRequired] bool Open,
    [property: JsonRequired] DaggerfallTravelMapPixel Body,
    [property: JsonRequired] DaggerfallTravelMapPixel Party,
    [property: JsonRequired] DaggerfallTravelMapPixel CameFrom,
    DaggerfallExpeditionLeg? Leg,
    [property: JsonRequired] DaggerfallExpeditionMode Mode,
    DaggerfallExpeditionMark? Pin,
    [property: JsonRequired] DaggerfallExpeditionShip Ship,
    [property: JsonRequired] bool Sailed,
    DaggerfallExpeditionLodging? Lodging,
    [property: JsonRequired] bool LowFatigueWarned,
    [property: JsonRequired] bool EncounterWaiting,
    [property: JsonRequired] long CampSeconds)
{
    internal static DaggerfallExpeditionSave Closed { get; } = new(false, default, default, default, null, DaggerfallExpeditionMode.Cautious,
        null, DaggerfallExpeditionShip.None, false, null, false, false, 0);

    internal DaggerfallExpeditionSave Validate()
    {
        Body.RequireInBounds(nameof(Body)); Party.RequireInBounds(nameof(Party)); CameFrom.RequireInBounds(nameof(CameFrom));
        Pin?.Pixel.RequireInBounds(nameof(Pin));
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(Ship)) throw new ArgumentException("Expedition save names an unknown mode or ship.");
        if (CampSeconds is < 0 or >= DaggerfallCalendar.SecondsPerMinute * DaggerfallCalendar.MinutesPerHour)
            throw new ArgumentOutOfRangeException(nameof(CampSeconds));
        if (Leg is { } leg)
        {
            leg.Origin.RequireInBounds(nameof(Leg)); leg.Destination.Pixel.RequireInBounds(nameof(Leg));
            IReadOnlyList<DaggerfallTravelMapPixel> path = DaggerfallTravelPolicy.Path(leg.Origin, leg.Destination.Pixel);
            if (leg.Step < 0 || leg.Step >= path.Count || !double.IsFinite(leg.Progress) || leg.Progress is < 0 or >= 1
                || (leg.Step == 0 ? leg.Origin : path[leg.Step - 1]) != Party)
                throw new ArgumentException("Expedition save's leg does not pass through the party's pixel.");
        }
        if (!Open && (Leg is not null || Ship != DaggerfallExpeditionShip.None || EncounterWaiting))
            throw new ArgumentException("A closed expedition keeps no leg, ship or waiting encounter.");
        return this;
    }
}

/// <summary>
/// The expedition map, which replaces fast travel: the party crosses the atlas on the donor's route walk while time runs
/// at a chosen speed, one stretch at a time through the session's one calendar. It holds where the party is and heads,
/// how it goes (cautious, reckless, halted or camped), its pin and Find, a ship from a port, lodging paid in a settlement,
/// the low-fatigue warning and an unseen encounter waiting to be met; and it says where Enter puts the player. Passing
/// time, what each activity costs the player, the encounter's selection and the relocation are the session's, asked
/// through <see cref="IDaggerfallExpeditionSurroundings"/>. A guild's free teleport chooses its place on the same map.
/// </summary>
/// <remarks>
/// A stretch ends at the next pixel, the next hour or ten minutes, whichever is first, so a crossing, an hour's encounter
/// roll, a camp hour and the fatigue checks each fall between stretches. That boundary is also where map bands (#10936)
/// can be seen and met.
/// </remarks>
internal sealed class DaggerfallExpedition
{
    private const long SecondsPerHour = DaggerfallCalendar.SecondsPerMinute * DaggerfallCalendar.MinutesPerHour;
    private const long LongestStretch = 10 * DaggerfallCalendar.SecondsPerMinute;
    private const double LongestFrame = .25;

    private static readonly IReadOnlySet<DaggerfallSiteKind> Settlements = new HashSet<DaggerfallSiteKind>
        { DaggerfallSiteKind.TownCity, DaggerfallSiteKind.TownHamlet, DaggerfallSiteKind.TownVillage, DaggerfallSiteKind.Tavern };

    private readonly DaggerfallTravelPolicy _policy;
    private readonly DaggerfallSiteContext _sites;
    private readonly DaggerfallExpeditionTuning _tuning;
    private readonly IDaggerfallExpeditionSurroundings _world;
    private readonly Dictionary<DaggerfallTravelMapPixel, DaggerfallSiteRecord> _locations = [];

    private bool _open;
    private DaggerfallTravelMapPixel _body;
    private DaggerfallTravelMapPixel _party;
    private DaggerfallTravelMapPixel _cameFrom;
    private DaggerfallExpeditionLeg? _leg;
    private IReadOnlyList<DaggerfallTravelMapPixel> _route = [];
    private DaggerfallExpeditionMode _mode;
    private DaggerfallExpeditionMode _pace = DaggerfallExpeditionMode.Cautious;
    private DaggerfallExpeditionMark? _pin;
    private DaggerfallExpeditionShip _ship;
    private bool _sailed;
    private DaggerfallExpeditionLodging? _lodging;
    private bool _lowFatigueWarned;
    private bool _encounterWaiting;
    private long _campSeconds;
    private int _speed;
    private double _owed;
    private string? _message;
    private (long NpcId, Func<bool> Available, DaggerfallSiteId? Place)? _teleport;
    private string? _held;
    private DaggerfallTravelDestination[] _found = [];

    internal DaggerfallExpedition(DaggerfallTravelPolicy policy, DaggerfallSiteContext sites, DaggerfallExpeditionTuning tuning,
        IDaggerfallExpeditionSurroundings world, DaggerfallExpeditionSave? saved = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _sites = sites ?? throw new ArgumentNullException(nameof(sites));
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _world = world ?? throw new ArgumentNullException(nameof(world));
        foreach (DaggerfallSiteRecord site in sites.Records)
            if (site.Exterior is not null) _locations.TryAdd(new(site.MapPixelX, site.MapPixelY), site);
        if (saved is { Open: true }) Restore(saved.Validate());
        else if (saved is not null) RestoreClosed(saved);
    }

    /// <summary>Whether the map is up, with the party on it.</summary>
    internal bool Open => _open;

    /// <summary>Whether a guild's free teleport, not a journey, is being chosen.</summary>
    internal bool Teleporting => _teleport is not null;

    /// <summary>Whether time runs on the map now.</summary>
    internal bool Running => _open && _teleport is null && _held is null && _speed > 0 && !_encounterWaiting;

    /// <summary>Why the party cannot set out from where the player stands (indoors, enemies near), or null when it can.</summary>
    internal string? Held => _held;

    /// <summary>An encounter was rolled and waits for the player to enter and meet it.</summary>
    internal bool EncounterWaiting => _encounterWaiting;

    internal DaggerfallTravelMapPixel Party => _party;
    internal DaggerfallExpeditionMode Mode => _mode;
    internal int Speed => _speed;
    internal DaggerfallExpeditionMark? Destination => _leg?.Destination;
    internal DaggerfallExpeditionMark? Pin => _pin;
    internal DaggerfallExpeditionShip Ship => _ship;
    internal string? Message => _message;
    internal DaggerfallTravelPolicy Policy => _policy;
    internal DaggerfallExpeditionTuning Tuning => _tuning;

    /// <summary>The party's place between pixels: its pixel's centre, moved along the leg as far as it has gone.</summary>
    internal (double X, double Y) Position
    {
        get
        {
            if (_leg is not { } leg || Next() is not { } next) return (_party.X + .5, _party.Y + .5);
            return (_party.X + .5 + (next.X - _party.X) * leg.Progress, _party.Y + .5 + (next.Y - _party.Y) * leg.Progress);
        }
    }

    /// <summary>The way the party faces, in degrees clockwise from north: along its leg, or null standing.</summary>
    internal double? Heading => Next() is { } next
        ? (Math.Atan2(next.X - _party.X, _party.Y - next.Y) * 180 / Math.PI + 360) % 360 : null;

    /// <summary>
    /// Puts the party on the map at the player's pixel, aboard their own ship when they stand on its deck. A map left open
    /// by a save is already on it and stays where the party was. With <paramref name="held"/>, the player cannot set out
    /// from where they stand (indoors, with enemies near): the map opens to be looked at, saying why, and only closes.
    /// </summary>
    internal void Begin(DaggerfallTravelMapPixel body, bool aboardOwnShip, string? held = null)
    {
        body.RequireInBounds(nameof(body));
        _speed = 0;
        _owed = 0;
        if (_open && _held is null) { _message = null; return; }
        _held = held;
        _message = held;
        _open = true;
        _body = _party = _cameFrom = body;
        _leg = null;
        _route = [];
        _mode = _pace;
        _ship = aboardOwnShip ? DaggerfallExpeditionShip.Owned : DaggerfallExpeditionShip.None;
        _sailed = aboardOwnShip;
        _encounterWaiting = false;
        _campSeconds = 0;
    }

    /// <summary>
    /// Offers a guild's free teleport from the provider with this NPC: the map opens on the player to choose a place.
    /// <paramref name="available"/> answers whether the guild still offers it when the player accepts.
    /// </summary>
    internal string OfferGuildTeleport(long npcId, Func<bool> available, DaggerfallTravelMapPixel body)
    {
        Begin(body, aboardOwnShip: false);
        _teleport = (npcId, available, null);
        return _message = "Choose a place for the guild to teleport you to.";
    }

    /// <summary>
    /// Leaves the map without entering: only while the party still stands where the player does, off any ship and with no
    /// encounter waiting, since otherwise the player is no longer where their body was left. Null when it closed, else why not.
    /// </summary>
    internal string? Leave()
    {
        if (!_open) return null;
        if (_teleport is null && (_party != _body || _ship != DaggerfallExpeditionShip.None || _encounterWaiting))
            return _message = _encounterWaiting ? "Something approaches. Enter to meet it." : "The party has moved on. Enter to go on foot from here.";
        Close();
        return null;
    }

    /// <summary>The map closes: the player left it, entered the world, or the game left play.</summary>
    internal void Close()
    {
        _open = false;
        _teleport = null;
        _held = null;
        _leg = null;
        _route = [];
        _ship = DaggerfallExpeditionShip.None;
        _sailed = false;
        _encounterWaiting = false;
        _campSeconds = 0;
        _speed = 0;
        _owed = 0;
        _message = null;
        _mode = _pace;
    }

    /// <summary>Sets time running at one of the speeds, or paused at zero.</summary>
    internal void SetSpeed(int speed)
    {
        if (speed < 0 || speed > _tuning.Speeds) throw new ArgumentOutOfRangeException(nameof(speed));
        if (Refused() is { } refusal) { _message = refusal; return; }
        _speed = speed;
        _owed = 0;
        _message = null;
    }

    /// <summary>
    /// Sets how the party goes. A pace (cautious or reckless) is remembered for the next leg; a camp needs dry land, and in
    /// a settlement it takes lodging, paid as the camp's time runs. A paused map starts at the slowest speed.
    /// </summary>
    internal void SetMode(DaggerfallExpeditionMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (Refused() is { } refusal) { _message = refusal; return; }
        if (mode == DaggerfallExpeditionMode.Camp && Swimming)
        {
            _message = "You cannot camp in the water.";
            return;
        }
        if (mode is DaggerfallExpeditionMode.Cautious or DaggerfallExpeditionMode.Reckless) _pace = mode;
        _mode = mode;
        _message = null;
        if (_speed == 0) _speed = 1;
    }

    /// <summary>Heads the party for a pixel, or the place there. The pace it last kept resumes, and a paused map starts.</summary>
    internal void SetDestination(DaggerfallTravelMapPixel pixel, DaggerfallSiteId? place)
    {
        if (!pixel.IsInBounds) { _message = "That lies beyond the edge of the map."; return; }
        if (place is { } id && !_sites.IsDiscovered(id)) { _message = "You do not know that place."; return; }
        if (_teleport is { } offer)
        {
            if (place is null || !_locations.ContainsKey(pixel)) { _message = "The guild teleports only to a place you know."; return; }
            _teleport = offer with { Place = place };
            _message = null;
            return;
        }
        if (Refused() is { } refusal) { _message = refusal; return; }
        if (pixel == _party)
        {
            _leg = null;
            _route = [];
            _message = "The party is already there.";
            return;
        }
        SetLeg(new(_party, new(pixel, place), 0, 0));
        _mode = _pace;
        _message = null;
        if (_speed == 0) _speed = 1;
    }

    /// <summary>Stops the party where it stands, its leg forgotten.</summary>
    internal void Stop()
    {
        if (Refused() is { } refusal) { _message = refusal; return; }
        _leg = null;
        _route = [];
        _message = null;
    }

    /// <summary>Pins a pixel, or the place there, to come back to.</summary>
    internal void SetPin(DaggerfallTravelMapPixel pixel, DaggerfallSiteId? place)
    {
        if (!pixel.IsInBounds) return;
        _pin = new(pixel, place is { } id && _sites.IsDiscovered(id) ? id : null);
        _message = null;
    }

    internal void ClearPin() => _pin = null;

    /// <summary>
    /// Finds a known place by name, as the classic map's Find: a place named by the term comes before the places of a
    /// region it names. The best match is pinned.
    /// </summary>
    internal void Find(string? text, IReadOnlyList<string> regionNames)
    {
        string term = text?.Trim() ?? string.Empty;
        if (term.Length == 0) { _found = []; _message = null; return; }
        _found = [.. _policy.SupportedDestinations()
            .Select(destination => (Destination: destination, Rank: PlaceRank(destination.Name, destination.Id.Region, term, regionNames)))
            .Where(match => match.Rank is not null)
            .OrderBy(match => match.Rank)
            .Select(match => match.Destination)
            .Take(40)];
        if (_found.Length == 0) { _message = "No place you know matches that name or region."; return; }
        _pin = new(_found[0].MapPixel, _found[0].Id);
        _message = null;
    }

    /// <summary>The places the last Find matched, best first.</summary>
    internal IReadOnlyList<DaggerfallTravelDestination> Found => _found;

    /// <summary>
    /// How well a place answers a search: its exact name, then a name starting with the term, then a name containing it,
    /// then a region that names it; null when nothing does. The order within a rank is the catalog's.
    /// </summary>
    internal static int? PlaceRank(string name, int region, string term, IReadOnlyList<string> regionNames)
    {
        if (name.Equals(term, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains(term, StringComparison.OrdinalIgnoreCase)) return 2;
        return DaggerfallRegionNames.Name(regionNames, region).Contains(term, StringComparison.OrdinalIgnoreCase) ? 3 : null;
    }

    /// <summary>
    /// Boards a ship at a port within reach of the sea, hired for gold or the player's own; or, aboard on land, goes ashore.
    /// Null when it was done, else why not.
    /// </summary>
    internal string? ToggleShip()
    {
        if (Refused() is { } refusal) return _message = refusal;
        if (_ship != DaggerfallExpeditionShip.None)
        {
            if (_policy.IsOcean(_party)) return _message = "You cannot leave the ship at sea.";
            _ship = DaggerfallExpeditionShip.None;
            _sailed = false;
            _message = "You go ashore.";
            return null;
        }
        if (Swimming) return _message = "No ship takes on a swimmer.";
        if (!NearPort()) return _message = "Ships sail from a port, and none is near enough.";
        int fare = _world.FreeShipPassage ? 0 : _tuning.ShipHire;
        if (_world.OwnsShip) _ship = DaggerfallExpeditionShip.Owned;
        else if (_world.TrySpendGold(fare)) _ship = DaggerfallExpeditionShip.Hired;
        else return _message = $"A passage costs {fare} gold, which you do not carry.";
        _sailed = false;
        if (_mode == DaggerfallExpeditionMode.Camp) _mode = DaggerfallExpeditionMode.Halt;
        _message = _ship == DaggerfallExpeditionShip.Owned ? "You board your ship."
            : fare == 0 ? "The guild grants you passage." : $"You hire a passage for {fare} gold.";
        return null;
    }

    /// <summary>Whether a ship can be boarded here: a port within reach, and the sea within reach of the party.</summary>
    internal bool NearPort()
    {
        int reach = _tuning.PortReach;
        bool port = false, sea = false;
        for (int y = _party.Y - reach; y <= _party.Y + reach; y++)
            for (int x = _party.X - reach; x <= _party.X + reach; x++)
            {
                DaggerfallTravelMapPixel pixel = new(x, y);
                if (!pixel.IsInBounds) continue;
                port |= _locations.TryGetValue(pixel, out DaggerfallSiteRecord? site) && site.Exterior is { PortTownAndUnknown: > 0 };
                sea |= _policy.IsOcean(pixel);
            }
        return port && sea;
    }

    /// <summary>Whether play has revealed a place, so the map may name it.</summary>
    internal bool Known(DaggerfallSiteId place) => _sites.IsDiscovered(place);

    /// <summary>Whether the map can close with no entry: the party still stands with the body, ashore, with nothing waiting.</summary>
    internal bool CanLeave => _open && (_teleport is not null || _held is not null
        || (_party == _body && _ship == DaggerfallExpeditionShip.None && !_encounterWaiting));

    /// <summary>The party is out under the sky: on the map, and not lodged indoors in camp.</summary>
    internal bool UnderSky => _open && _teleport is null && _held is null && !(_mode == DaggerfallExpeditionMode.Camp && LodgingHere is not null);

    /// <summary>The party is in the water without a ship.</summary>
    internal bool Swimming => _ship == DaggerfallExpeditionShip.None && _policy.IsOcean(_party);

    /// <summary>The location standing at the party's pixel, discovered or not.</summary>
    internal DaggerfallSiteRecord? LocationHere => _locations.GetValueOrDefault(_party);

    /// <summary>
    /// The location whose exterior a player entering at a pixel stands in: the one at that pixel, else the nearest, whose
    /// streamed wilderness reaches it. Ties go to the first in the catalog.
    /// </summary>
    internal DaggerfallSiteRecord HostAt(DaggerfallTravelMapPixel pixel)
    {
        if (_locations.TryGetValue(pixel, out DaggerfallSiteRecord? here)) return here;
        DaggerfallSiteRecord? nearest = null;
        long best = long.MaxValue;
        foreach ((DaggerfallTravelMapPixel at, DaggerfallSiteRecord site) in _locations)
        {
            long dx = at.X - pixel.X, dy = at.Y - pixel.Y, distance = dx * dx + dy * dy;
            if (distance < best) { best = distance; nearest = site; }
        }
        return nearest ?? throw new InvalidOperationException("The world has no location to enter from.");
    }

    /// <summary>The location standing at a pixel, discovered or not.</summary>
    internal DaggerfallSiteRecord? LocationAt(DaggerfallTravelMapPixel pixel) => _locations.GetValueOrDefault(pixel);

    /// <summary>What a night's lodging costs where the party stands, or null where camping is free.</summary>
    internal int? LodgingHere => _ship == DaggerfallExpeditionShip.None && LocationHere is { } site && Settlements.Contains(site.Kind)
        && _tuning.Lodging.TryGetValue(site.Kind, out int gold) ? gold : null;

    /// <summary>
    /// The minutes the party's next pixel takes at its pace: the donor's pixel minutes, then the reckless share, the night
    /// and the weather over that pixel. Null while it is not moving.
    /// </summary>
    internal double? MinutesToNext() =>
        Next() is { } next && _mode is not (DaggerfallExpeditionMode.Halt or DaggerfallExpeditionMode.Camp) ? PixelTime(next) : null;

    /// <summary>
    /// Roughly how long the rest of the leg takes at the present pace, hour and weather, in seconds; null standing still.
    /// </summary>
    internal long? RemainingSeconds()
    {
        if (_leg is not { } leg || MinutesToNext() is not double first) return null;
        double minutes = first * (1 - leg.Progress);
        for (int step = leg.Step + 1; step < _route.Count; step++) minutes += PixelTime(_route[step]);
        return (long)Math.Ceiling(minutes * DaggerfallCalendar.SecondsPerMinute);
    }

    /// <summary>The minutes moving into a pixel takes at the party's pace now, on its ship or its own transport.</summary>
    private double PixelTime(DaggerfallTravelMapPixel pixel)
    {
        bool aboard = _ship != DaggerfallExpeditionShip.None;
        double minutes = _policy.PixelMinutes(pixel, aboard ? DaggerfallTransportMode.Foot : _world.Transport, aboard);
        if (_mode == DaggerfallExpeditionMode.Reckless && !(aboard && _policy.IsOcean(pixel))) minutes *= _tuning.RecklessTime;
        if (!_world.Calendar.IsDay) minutes *= _tuning.NightTime;
        return minutes * _tuning.WeatherTime[_world.Weather(_policy.Climate(pixel))];
    }

    /// <summary>
    /// Runs the map for one admitted update's real seconds at the chosen speed: game time passes in stretches through the
    /// session's calendar while the party moves, halts or camps, until the time is spent or something stops it (arrival,
    /// an encounter, the low-fatigue warning, exhaustion, defeat, or a stretch the calendar would not take). Returns the
    /// game seconds that passed.
    /// </summary>
    internal long Advance(double realSeconds)
    {
        if (!Running || !double.IsFinite(realSeconds) || realSeconds <= 0) return 0;
        // A long frame does not leap: the map gives it at most a quarter second's time.
        _owed += Math.Min(realSeconds, LongestFrame) * _tuning.TimeScales[_speed - 1] * DaggerfallCalendar.SecondsPerMinute;
        long passed = 0;
        while (_owed >= 1 && Running)
        {
            if (_world.Defeated) { Pause(null); break; }
            if (_mode == DaggerfallExpeditionMode.Camp && !PayLodging()) break;
            DaggerfallCalendar before = _world.Calendar;
            (DaggerfallExpeditionActivity activity, double fatigue, double? pixelMinutes) = Plan();
            long untilHour = SecondsPerHour - before.ToAbsoluteSeconds() % SecondsPerHour;
            double cap = Math.Min(Math.Min(_owed, untilHour), LongestStretch);
            if (pixelMinutes is double perPixel)
                cap = Math.Min(cap, Math.Max(1, Math.Round(perPixel * DaggerfallCalendar.SecondsPerMinute * (1 - _leg!.Progress))));
            long seconds = Math.Max(1, (long)Math.Floor(cap));
            long applied = _world.Pass(new(seconds, activity, fatigue));
            passed += applied;
            _owed -= applied;
            if (applied <= 0) { Pause("Time would not pass here."); break; }
            if (pixelMinutes is double crossing) Walk(applied, crossing);
            if (activity == DaggerfallExpeditionActivity.Camping) Camp(applied);
            else _campSeconds = 0;
            DaggerfallCalendar after = _world.Calendar;
            if (_world.Defeated) { Pause(null); break; }
            if (_world.Exhausted)
            {
                _world.Collapse(Swimming);
                _leg = null; _route = [];
                if (!_world.Defeated) _mode = DaggerfallExpeditionMode.Halt;
                Pause(_world.Defeated ? null : "Exhausted, you drop where you stand.");
                break;
            }
            if (Arrived()) break;
            if (after.MinuteNumber / DaggerfallCalendar.MinutesPerHour != before.MinuteNumber / DaggerfallCalendar.MinutesPerHour
                && _ship == DaggerfallExpeditionShip.None && !_policy.IsOcean(_party)
                && _world.EncounterArrives(_party, LocationHere is not null, after.MinuteNumber))
            {
                _encounterWaiting = true;
                Pause("Something approaches! Enter to meet it.");
                break;
            }
            if (WarnLowFatigue(activity)) break;
        }
        if (!Running) _owed = 0;
        return passed;
    }

    /// <summary>
    /// Where Enter puts the player, or null with the map's line saying why not: never in the open sea, and never with a
    /// guild teleport being chosen, which teleports instead.
    /// </summary>
    internal DaggerfallExpeditionEntry? Entry()
    {
        if (!_open || _teleport is not null) return null;
        if (_held is not null) { _message = _held; return null; }
        if (_policy.IsOcean(_party)) { _message = "There is no land here. Make for the shore first."; return null; }
        return new(_party, LocationHere, _cameFrom);
    }

    /// <summary>The place the guild's teleport was asked for, while the guild still grants it; else null with the reason.</summary>
    internal (long NpcId, DaggerfallTravelDestination Place)? TeleportChoice()
    {
        if (_teleport is not { } offer) return null;
        if (!offer.Available()) { _message = "The guild teleportation service is no longer available."; return null; }
        DaggerfallTravelDestination[] chosen = offer.Place is { } id ? [.. _policy.SupportedDestinations().Where(place => place.Id == id)] : [];
        if (chosen.Length == 0) { _message = "Choose a place you know first."; return null; }
        return (offer.NpcId, chosen[0]);
    }

    /// <summary>The place the guild's teleport has been asked for, if any.</summary>
    internal DaggerfallSiteId? TeleportPlace => _teleport?.Place;

    internal DaggerfallExpeditionSave Capture() => !_open || _teleport is not null || _held is not null ? DaggerfallExpeditionSave.Closed with { Mode = _pace, Pin = _pin, Lodging = _lodging }
        : new(true, _body, _party, _cameFrom, _leg, _mode, _pin, _ship, _sailed, _lodging, _lowFatigueWarned, _encounterWaiting, _campSeconds);

    private void Restore(DaggerfallExpeditionSave saved)
    {
        _open = true;
        _body = saved.Body;
        _party = saved.Party;
        _cameFrom = saved.CameFrom;
        if (saved.Leg is { } leg) SetLeg(leg);
        _mode = saved.Mode;
        _pace = saved.Mode is DaggerfallExpeditionMode.Reckless ? saved.Mode : DaggerfallExpeditionMode.Cautious;
        _pin = saved.Pin;
        _ship = saved.Ship;
        _sailed = saved.Sailed;
        _lodging = saved.Lodging;
        _lowFatigueWarned = saved.LowFatigueWarned;
        _encounterWaiting = saved.EncounterWaiting;
        _campSeconds = saved.CampSeconds;
    }

    /// <summary>A closed map's remembered pace, pin and lodging, from a save that left it closed.</summary>
    private void RestoreClosed(DaggerfallExpeditionSave saved)
    {
        if (saved.Open) return;
        _pace = saved.Mode is DaggerfallExpeditionMode.Reckless ? saved.Mode : DaggerfallExpeditionMode.Cautious;
        _mode = _pace;
        _pin = saved.Pin;
        _lodging = saved.Lodging;
    }

    private string? Refused() => _held ?? (_teleport is not null ? "Choose where the guild should teleport you."
        : _encounterWaiting ? "Something approaches. Enter to meet it." : null);

    private void SetLeg(DaggerfallExpeditionLeg leg)
    {
        _route = DaggerfallTravelPolicy.Path(leg.Origin, leg.Destination.Pixel);
        _leg = leg;
    }

    private DaggerfallTravelMapPixel? Next() => _leg is { } leg && leg.Step < _route.Count ? _route[leg.Step] : null;

    /// <summary>
    /// What the next stretch is spent doing, the fatigue it costs, and, moving, the minutes its pixel takes: a ship sails
    /// the sea, a swimmer swims to or from it, and the party marches over land at its pace and the weather's toll.
    /// </summary>
    private (DaggerfallExpeditionActivity Activity, double Fatigue, double? PixelMinutes) Plan()
    {
        bool aboard = _ship != DaggerfallExpeditionShip.None;
        if (_mode == DaggerfallExpeditionMode.Camp) return (DaggerfallExpeditionActivity.Camping, 1, null);
        DaggerfallExpeditionActivity standing = aboard ? DaggerfallExpeditionActivity.Sailing
            : Swimming ? DaggerfallExpeditionActivity.Swimming : DaggerfallExpeditionActivity.Halted;
        if (MinutesToNext() is not double minutes || Next() is not { } next) return (standing, 1, null);
        if (aboard && _policy.IsOcean(next)) return (DaggerfallExpeditionActivity.Sailing, 1, minutes);
        if (!aboard && (_policy.IsOcean(next) || Swimming)) return (DaggerfallExpeditionActivity.Swimming, 1, minutes);
        double fatigue = (_mode == DaggerfallExpeditionMode.Reckless ? _tuning.RecklessFatigue : 1)
            * _tuning.WeatherFatigue[_world.Weather(_policy.Climate(next))];
        return (DaggerfallExpeditionActivity.Marching, fatigue, minutes);
    }

    /// <summary>Moves the party along its leg by the seconds a crossing took, stepping into the next pixel once it is crossed.</summary>
    private void Walk(long seconds, double minutes)
    {
        if (_leg is not { } leg) return;
        double crossing = minutes * DaggerfallCalendar.SecondsPerMinute;
        double progress = leg.Progress + seconds / crossing;
        // The calendar passes whole seconds, so a pixel is crossed once less than half a second of it is left.
        if ((1 - progress) * crossing >= .5) { _leg = leg with { Progress = progress }; return; }
        _cameFrom = _party;
        _party = _route[leg.Step];
        _leg = leg with { Step = leg.Step + 1, Progress = 0 };
        if (_ship != DaggerfallExpeditionShip.None)
        {
            if (_policy.IsOcean(_party)) _sailed = true;
            else if (_sailed)
            {
                _ship = DaggerfallExpeditionShip.None;
                _sailed = false;
                _message = "The ship puts you ashore.";
            }
        }
    }

    private bool Arrived()
    {
        if (_leg is not { } leg || leg.Step < _route.Count) return false;
        string where = leg.Destination.Place is { } place && _sites.TryFind(place, out DaggerfallSiteRecord? site) ? site.Name : "your destination";
        _leg = null;
        _route = [];
        Pause($"The party reaches {where}.");
        return true;
    }

    /// <summary>A camp hour's recovery once a whole hour has been spent in camp.</summary>
    private void Camp(long seconds)
    {
        _campSeconds += seconds;
        while (_campSeconds >= SecondsPerHour)
        {
            _campSeconds -= SecondsPerHour;
            _world.RecoverCampHour(sheltered: _ship != DaggerfallExpeditionShip.None || LodgingHere is not null);
        }
    }

    /// <summary>
    /// Lodging in a settlement is paid as its camp begins and again each day it lasts; a party that cannot pay halts.
    /// True when the camp may go on.
    /// </summary>
    private bool PayLodging()
    {
        if (LodgingHere is not int gold || LocationHere is not { } site) return true;
        long now = _world.Calendar.ToAbsoluteSeconds();
        if (_lodging is { } paid && paid.Place == site.Id && now < paid.PaidUntil) return true;
        if (!_world.TrySpendGold(gold))
        {
            _mode = DaggerfallExpeditionMode.Halt;
            Pause($"A night's lodging in {site.Name} costs {gold} gold, which you do not carry.");
            return false;
        }
        _lodging = new(site.Id, now + DaggerfallCalendar.SecondsPerDay);
        _message = $"You take lodging in {site.Name} for {gold} gold.";
        return true;
    }

    /// <summary>The low-fatigue warning pauses the map once, until fatigue rises past the line again.</summary>
    private bool WarnLowFatigue(DaggerfallExpeditionActivity activity)
    {
        if (_world.FatigueShare >= _tuning.LowFatigue) { _lowFatigueWarned = false; return false; }
        if (_lowFatigueWarned || activity is not (DaggerfallExpeditionActivity.Marching or DaggerfallExpeditionActivity.Swimming)) return false;
        _lowFatigueWarned = true;
        Pause("You are growing weary. Halt or make camp before you drop.");
        return true;
    }

    private void Pause(string? message)
    {
        _speed = 0;
        _owed = 0;
        if (message is not null) _message = message;
    }
}
