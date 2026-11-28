using System.Globalization;
using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>
/// How the party goes: cautious (the default), reckless (faster and more tiring), halted by the way (waiting, neither
/// tiring nor recovering) or in camp (recovering).
/// </summary>
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

    /// <summary>
    /// Something besides the map holds the world (the game menu, a question waiting for an answer, another screen, a
    /// host pause): the map's time stands still behind it, as the world's does.
    /// </summary>
    bool HeldElsewhere { get; }

    /// <summary>Passes one stretch through the one calendar with the activity's per-minute rules; the seconds it applied.</summary>
    long Pass(DaggerfallExpeditionStretch stretch);

    /// <summary>One hour in camp: the rest hour's recovery, whole in lodging or aboard, the rough-ground share in the wild.</summary>
    void RecoverCampHour(bool sheltered);

    /// <summary>The party drops from exhaustion: in the water it drowns, elsewhere it sleeps an hour where it fell.</summary>
    void Collapse(bool inWater);

    /// <summary>Rolls one hour's unseen encounter at the party's pixel; true when a foe was selected, waiting to be met.</summary>
    bool EncounterArrives(DaggerfallTravelMapPixel pixel, bool atLocation, long minute);

    /// <summary>A keyed draw for the map's bands, from <c>minimum</c> to <c>maximum</c> inclusive.</summary>
    int Roll(string key, int minimum, int maximum);

    /// <summary>The player's level, which a band's table entry is drawn against.</summary>
    int PlayerLevel { get; }

    /// <summary>The party's sight's named input, a later scouting skill's share from 0 to 1.</summary>
    double Scouting { get; }

    /// <summary>The input against a band's sight: the player's stealth as a share from 0 to 1.</summary>
    double Concealment { get; }

    /// <summary>A band is met at the party's pixel: its members are kept for the profile Enter puts the player in, staged so.</summary>
    void MeetBand(DaggerfallExpeditionBand band, DaggerfallTravelMapPixel pixel, DaggerfallEncounterStaging staging);

    /// <summary>A persistent <see cref="ActorLocationKind.OnOverlandBand"/> travelling party is met at the party's pixel.</summary>
    void MeetPersistentBand(DaggerfallPersistentOverlandBand band, DaggerfallTravelMapPixel pixel, DaggerfallEncounterStaging staging) { }

    /// <summary>A persistent <see cref="ActorLocationKind.OnOverlandBand"/> travelling party reached its destination pixel.</summary>
    void PersistentBandArrived(DaggerfallPersistentOverlandBand band, long second) { }

    /// <summary>A persistent <see cref="ActorLocationKind.OnOverlandBand"/> party was retired off-screen (e.g. off-screen clash or raid resolution).</summary>
    void PersistentBandRetiredOffScreen(string bandId, long second, DaggerfallWorldConditionToken? raidedUnder = null) { }

    /// <summary>Active world-condition tokens affecting <paramref name="region"/> (stubbed/authored or B2 board-driven).</summary>
    IReadOnlyList<DaggerfallWorldConditionToken> WorldConditions(int region) => [];

    /// <summary>The world-condition tokens active in <paramref name="region"/> on calendar day <paramref name="day"/>.</summary>
    IReadOnlyList<DaggerfallWorldConditionToken> WorldConditionsOn(int region, int day) => [];

    /// <summary>A band on the road was delayed where it stood for <paramref name="seconds"/>.</summary>
    void PersistentBandDelayed(DaggerfallPersistentOverlandBand band, long seconds) { }
}

/// <summary>Where Enter puts the player: the party's pixel, the location standing there if any, and the pixel the party came from.</summary>
internal sealed record DaggerfallExpeditionEntry(DaggerfallTravelMapPixel Pixel, DaggerfallSiteRecord? Location, DaggerfallTravelMapPixel CameFrom);

/// <summary>A band the map shows: where it is, what it looks like, whether it has seen the party, and its inspection details.</summary>
internal sealed record DaggerfallExpeditionBandView(int Id, double X, double Y, double Heading, DaggerfallBandKind Kind, string Name,
    bool Camp, bool Spotted, double Distance)
{
    /// <summary>The mobile its foe is, which the travel map draws it as.</summary>
    internal int? Mobile { get; init; }

    /// <summary>Non-null when this token represents a persistent <see cref="ActorLocationKind.OnOverlandBand"/> travelling party.</summary>
    internal string? PersistentBandId { get; init; }

    /// <summary>A persistent party out on the road beyond the party's reach, not on the map (#11178).</summary>
    internal bool OnRoad { get; init; }

    /// <summary>The colour of the arms the band flies, as <c>#rrggbb</c>, if it flies any (#11180).</summary>
    internal string? Banner { get; init; }

    /// <summary>Non-null when this band was staged in response to a <see cref="DaggerfallWorldConditionToken"/>.</summary>
    internal string? ConditionTokenId { get; init; }

    /// <summary>Player-facing band category badge (e.g. "Caravan", "Sealed Envoy", "Patrol", "Faction Clash").</summary>
    internal string Category { get; init; } = string.Empty;

    /// <summary>Player-facing faction, court, guild, or seat label when known or revealed.</summary>
    internal string? Faction { get; init; }

    /// <summary>Opposing sides when this band is an active two-sided clash.</summary>
    internal string? ClashSides { get; init; }

    /// <summary>Discovered origin place name when known to the player.</summary>
    internal string? OriginName { get; init; }

    /// <summary>Discovered destination place name when known to the player.</summary>
    internal string? DestinationName { get; init; }

    /// <summary>Concise player-facing inspection summary for tooltip/detail panel.</summary>
    internal string? Detail { get; init; }

    /// <summary>Whether this band is an active two-sided faction clash.</summary>
    internal bool IsClash { get; init; }

    /// <summary>Whether this band is actively hostile toward the player.</summary>
    internal bool Hostile { get; init; }
}

/// <summary>
/// An active band-vs-band clash or caravan raid held on a persistent overland band while within the player's
/// sight/approach so entering stages both opposing sides facing each other. Null when not clashing.
/// </summary>
internal sealed record DaggerfallPersistentBandClashSave(
    [property: JsonRequired] string RivalName,
    [property: JsonRequired] long EndsAtSecond,
    string? RivalBandId = null,
    int? RivalMobileId = null,
    int RivalSize = 0)
{
    internal DaggerfallPersistentBandClashSave Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RivalName);
        if (EndsAtSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(EndsAtSecond), "Persistent band clash end time cannot be negative.");
        if (RivalSize < 0)
            throw new ArgumentOutOfRangeException(nameof(RivalSize), "Persistent band clash rival size cannot be negative.");
        if (string.IsNullOrWhiteSpace(RivalBandId) && (RivalMobileId is null || RivalSize <= 0))
            throw new ArgumentException("Persistent band clash requires either a rival persistent band id or a rival mobile id and positive size.");
        return this;
    }
}

/// <summary>
/// One persistent travelling party on the overland map linked to <see cref="DaggerfallActorLedger"/> entries whose
/// location override is <see cref="ActorLocationKind.OnOverlandBand"/> with the same <paramref name="BandId"/>.
/// Survives map close/reopen and save/load alongside transient generated chasers.
/// </summary>
internal sealed record DaggerfallPersistentBandSave(
    [property: JsonRequired] string BandId,
    [property: JsonRequired] string Name,
    [property: JsonRequired] DaggerfallBandKind Kind,
    [property: JsonRequired] double X,
    [property: JsonRequired] double Y,
    [property: JsonRequired] double Heading,
    [property: JsonRequired] double SpeedPixelsPerHour,
    DaggerfallExpeditionMark? Destination = null,
    DaggerfallWorldProfileKeySave? DestinationProfile = null,
    bool Camp = false,
    bool Spotted = false,
    bool Hostile = false,
    int? Mobile = null,
    DaggerfallTravelMapPixel? RouteOrigin = null,
    int RouteStep = 0,
    string? Faction = null,
    DaggerfallSiteIdSave? OriginSite = null,
    DaggerfallPersistentBandClashSave? ActiveClash = null,
    bool OnRoad = false,
    int RoadSegment = 0,
    long? HeldUntilSecond = null,
    string? Banner = null,
    int[]? AvoidRegions = null)
{
    internal DaggerfallPersistentBandSave Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(BandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (!Enum.IsDefined(Kind)) throw new ArgumentException($"Persistent band '{BandId}' names an unknown kind.");
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Heading)
            || !double.IsFinite(SpeedPixelsPerHour) || SpeedPixelsPerHour < 0)
            throw new ArgumentOutOfRangeException(nameof(X), $"Persistent band '{BandId}' has invalid coordinates or speed.");
        new DaggerfallTravelMapPixel((int)Math.Floor(X), (int)Math.Floor(Y)).RequireInBounds(nameof(X));
        Destination?.Pixel.RequireInBounds(nameof(Destination));
        DestinationProfile?.Validate();
        RouteOrigin?.RequireInBounds(nameof(RouteOrigin));
        if (RouteStep < 0)
            throw new ArgumentOutOfRangeException(nameof(RouteStep), $"Persistent band '{BandId}' has negative route step {RouteStep}.");
        if (RoadSegment < 0 || HeldUntilSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(RoadSegment), $"Persistent band '{BandId}' has a negative road segment or hold.");
        if (OnRoad && (Hostile || Destination is null || ActiveClash is not null))
            throw new ArgumentException($"Persistent band '{BandId}' is on the road although it is hostile, clashing or bound nowhere.");
        _ = OriginSite?.Require();
        ActiveClash?.Validate();
        return this;
    }
}

/// <summary>A point on the map the party may head for or the player may pin: a pixel, and the place there when one was chosen.</summary>
internal sealed record DaggerfallExpeditionMark([property: JsonRequired] DaggerfallTravelMapPixel Pixel, DaggerfallSiteId? Place);

/// <summary>The leg the party walks: the donor's route path from where it started to where it heads, and how far along it the party is.</summary>
internal sealed record DaggerfallExpeditionLeg([property: JsonRequired] DaggerfallTravelMapPixel Origin,
    [property: JsonRequired] DaggerfallExpeditionMark Destination, [property: JsonRequired] int Step, [property: JsonRequired] double Progress);

/// <summary>Lodging paid at a place, good until an absolute second.</summary>
internal sealed record DaggerfallExpeditionLodging([property: JsonRequired] DaggerfallSiteId Place, [property: JsonRequired] long PaidUntil);

/// <summary>
/// Persisted unplaced transient rival members of an entered overland clash whose initial scene placement was
/// obstructed, along with the durable actor IDs needed to orient both sides once clear placement becomes available.
/// </summary>
internal sealed record DaggerfallPendingTransientClashSave(
    [property: JsonRequired] string BandId,
    [property: JsonRequired] DaggerfallWorldProfileKeySave ProfileKey,
    [property: JsonRequired] long[] PrimaryActorIds,
    [property: JsonRequired] int RivalMobileId,
    [property: JsonRequired] int RemainingCount,
    long[]? SpawnedRivalIds = null,
    int AttemptOrdinal = 0)
{
    internal DaggerfallPendingTransientClashSave Validate()
    {
        if (string.IsNullOrWhiteSpace(BandId))
            throw new ArgumentException("Pending transient clash save requires a non-empty BandId.");
        ArgumentNullException.ThrowIfNull(ProfileKey);
        _ = ProfileKey.Require();
        ArgumentNullException.ThrowIfNull(PrimaryActorIds);
        if (RivalMobileId < 0)
            throw new ArgumentOutOfRangeException(nameof(RivalMobileId));
        if (RemainingCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(RemainingCount), "Pending transient clash must retain at least one remaining member.");
        if (AttemptOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(AttemptOrdinal));
        return this;
    }
}

/// <summary>
/// The expedition saved where the map was left: whether it was open, where the player's body stands and the party is,
/// the leg, mode, pin, ship and lodging, the low-fatigue warning, an encounter waiting to be met and the hour in camp.
/// </summary>
/// <param name="EncounterFoe">The band that caught the party, by name; null for an unseen hour's encounter.</param>
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
    [property: JsonRequired] long CampSeconds,
    string? EncounterFoe = null,
    DaggerfallPersistentBandSave[]? PersistentBands = null,
    string? PendingPersistentBandId = null,
    DaggerfallPendingTransientClashSave[]? PendingTransientClashes = null)
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
        if (EncounterFoe is not null && !EncounterWaiting) throw new ArgumentException("Expedition save names a foe with no encounter waiting.");
        HashSet<string> bandIds = new(StringComparer.Ordinal);
        if (PersistentBands is not null)
        {
            foreach (DaggerfallPersistentBandSave band in PersistentBands)
            {
                ArgumentNullException.ThrowIfNull(band);
                band.Validate();
                if (!bandIds.Add(band.BandId))
                    throw new ArgumentException($"Expedition save repeats persistent band '{band.BandId}'.");
            }
        }
        if (PendingPersistentBandId is not null)
        {
            if (!EncounterWaiting)
                throw new ArgumentException("Expedition save names a pending persistent band with no encounter waiting.");
            if (!bandIds.Contains(PendingPersistentBandId))
                throw new ArgumentException($"Expedition save names missing pending persistent band '{PendingPersistentBandId}'.");
        }
        if (PendingTransientClashes is not null)
        {
            foreach (DaggerfallPendingTransientClashSave pending in PendingTransientClashes)
            {
                ArgumentNullException.ThrowIfNull(pending);
                pending.Validate();
            }
        }
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
    private string? _encounterFoe;
    private long _campSeconds;
    private int _speed;
    private double _owed;
    private string? _message;
    private (long NpcId, Func<bool> Available, DaggerfallSiteId? Place)? _teleport;
    private string? _held;
    private readonly DaggerfallExpeditionBands _bands;
    private readonly List<DaggerfallPersistentOverlandBand> _persistentBands = [];
    private int _nextPersistentDisplayId = -1;
    private DaggerfallExpeditionBand? _bandAhead;
    private DaggerfallPersistentOverlandBand? _persistentBandAhead;
    private string? _pendingPersistentBandId;
    private DaggerfallTravelDestination[] _found = [];

    /// <param name="tables">The classic encounter tables bands are staged from; none stages no band.</param>
    /// <param name="mobiles">The foes the tables name.</param>
    internal DaggerfallExpedition(DaggerfallTravelPolicy policy, DaggerfallSiteContext sites, DaggerfallExpeditionTuning tuning,
        IDaggerfallExpeditionSurroundings world, DaggerfallExpeditionSave? saved = null, DaggerfallEncounterSet? tables = null,
        DaggerfallMobileCatalogSet? mobiles = null, DaggerfallCreatureFactionSet? creatureFactions = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _sites = sites ?? throw new ArgumentNullException(nameof(sites));
        _tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _world = world ?? throw new ArgumentNullException(nameof(world));
        foreach (DaggerfallSiteRecord site in sites.Records)
            if (site.Exterior is not null) _locations.TryAdd(new(site.MapPixelX, site.MapPixelY), site);
        _bands = new DaggerfallExpeditionBands(policy, tables ?? new DaggerfallEncounterSet([]),
            mobiles ?? new DaggerfallMobileCatalogSet(new Dictionary<int, DaggerfallMobileDefinition>(), new Dictionary<string, DaggerfallMobileDefinition>()),
            _tuning.Bands, _locations.ContainsKey, creatureFactions);
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

    /// <summary>The band that caught the party and waits to be fought, by name; null for an unseen hour's encounter.</summary>
    internal string? EncounterFoe => _encounterWaiting ? _encounterFoe : null;

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
        _encounterFoe = null;
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
            return _message = _encounterWaiting ? EncounterLine() : "The party has moved on. Enter to go on foot from here.";
        Close();
        return null;
    }

    /// <summary>The map closes: transient chasers clear, while persistent <see cref="ActorLocationKind.OnOverlandBand"/> parties remain tracked.</summary>
    internal void Close()
    {
        _bands.Clear();
        _bandAhead = null;
        _persistentBandAhead = null;
        _pendingPersistentBandId = null;
        _open = false;
        _teleport = null;
        _held = null;
        _leg = null;
        _route = [];
        _ship = DaggerfallExpeditionShip.None;
        _sailed = false;
        _encounterWaiting = false;
        _encounterFoe = null;
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
        SteerAway();
        Reroute(new(pixel, place));
        _mode = _pace;
        _message = null;
        if (_speed == 0) _speed = 1;
    }

    /// <summary>Stops the party where it stands, its leg forgotten.</summary>
    internal void Stop()
    {
        if (Refused() is { } refusal) { _message = refusal; return; }
        SteerAway();
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
        if (_world.HeldElsewhere) { _owed = 0; return 0; }
        // Going on by any means (the same way again, a halt or a camp) passes by a band come upon unseen.
        SteerAway();
        // A long frame does not leap: the map gives it at most a quarter second's time.
        _owed += Math.Min(realSeconds, LongestFrame) * _tuning.TimeScales[_speed - 1] * DaggerfallCalendar.SecondsPerMinute
            * (_mode == DaggerfallExpeditionMode.Camp ? _tuning.CampTime : 1);
        long passed = 0;
        while (_owed >= 1 && Running)
        {
            // A stretch can raise a question (a quest's message, an offer): the rest of the frame's time waits for it.
            if (_world.HeldElsewhere) { _owed = 0; break; }
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
                // The water drowns the party the stretch was swimming in, though its pixel is still the shore it left.
                _world.Collapse(activity == DaggerfallExpeditionActivity.Swimming);
                _leg = null; _route = [];
                if (!_world.Defeated) _mode = DaggerfallExpeditionMode.Halt;
                Pause(_world.Defeated ? null : "Exhausted, you drop where you stand.");
                break;
            }
            if (BandsStop(applied, after)) break;
            if (Arrived()) break;
            if (after.MinuteNumber / DaggerfallCalendar.MinutesPerHour != before.MinuteNumber / DaggerfallCalendar.MinutesPerHour
                && _ship == DaggerfallExpeditionShip.None && !_policy.IsOcean(_party)
                && _world.EncounterArrives(_party, LocationHere is not null, after.MinuteNumber))
            {
                _encounterWaiting = true;
                _encounterFoe = null;
                Pause(EncounterLine());
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
        if (_bandAhead is { } band && _bands.Within(band, Position))
        {
            // The band the party came upon unseen is met as it stood: camped, or on its way.
            _world.MeetBand(band, _party, band.Camp ? DaggerfallEncounterStaging.Camp : DaggerfallEncounterStaging.Travelling);
            _bands.Remove(band);
            _bandAhead = null;
        }
        if (_pendingPersistentBandId is { } caughtBandId)
        {
            DaggerfallPersistentOverlandBand? caught = _persistentBands.FirstOrDefault(b =>
                StringComparer.Ordinal.Equals(b.BandId, caughtBandId));
            _pendingPersistentBandId = null;
            if (caught is not null)
            {
                _world.MeetPersistentBand(caught, _party, DaggerfallEncounterStaging.Caught);
                _persistentBands.Remove(caught);
                if (_persistentBandAhead == caught) _persistentBandAhead = null;
            }
        }
        else if (_persistentBandAhead is { } persistent && _persistentBands.Contains(persistent)
            && persistent.Distance(Position.X, Position.Y) <= _bands.Tuning.MeetDistance)
        {
            _world.MeetPersistentBand(persistent, _party, persistent.Camp ? DaggerfallEncounterStaging.Camp : DaggerfallEncounterStaging.Travelling);
            _persistentBands.Remove(persistent);
            _persistentBandAhead = null;
        }
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

    internal DaggerfallExpeditionSave Capture()
    {
        DaggerfallPersistentBandSave[]? persistent = _persistentBands.Count > 0
            ? [.. _persistentBands.OrderBy(b => b.BandId, StringComparer.Ordinal).Select(b => b.ToSave())]
            : null;
        return !_open || _teleport is not null || _held is not null
            ? DaggerfallExpeditionSave.Closed with { Mode = _pace, Pin = _pin, Lodging = _lodging, PersistentBands = persistent }
            : new(true, _body, _party, _cameFrom, _leg, _mode, _pin, _ship, _sailed, _lodging, _lowFatigueWarned, _encounterWaiting, _campSeconds, EncounterFoe, persistent, _encounterWaiting ? _pendingPersistentBandId : null);
    }

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
        _encounterFoe = saved.EncounterFoe;
        _campSeconds = saved.CampSeconds;
        _message = _encounterWaiting ? EncounterLine() : null;
        RestorePersistentBands(saved.PersistentBands);
        _pendingPersistentBandId = saved.PendingPersistentBandId;
    }

    /// <summary>A closed map's remembered pace, pin, lodging and persistent overland bands, from a save that left it closed.</summary>
    private void RestoreClosed(DaggerfallExpeditionSave saved)
    {
        if (saved.Open) return;
        _pace = saved.Mode is DaggerfallExpeditionMode.Reckless ? saved.Mode : DaggerfallExpeditionMode.Cautious;
        _mode = _pace;
        _pin = saved.Pin;
        _lodging = saved.Lodging;
        RestorePersistentBands(saved.PersistentBands);
    }

    private void RestorePersistentBands(DaggerfallPersistentBandSave[]? savedBands)
    {
        _persistentBands.Clear();
        _persistentBandAhead = null;
        if (savedBands is null) return;
        foreach (DaggerfallPersistentBandSave band in savedBands)
            _persistentBands.Add(DaggerfallPersistentOverlandBand.FromSave(band.Validate(), _nextPersistentDisplayId--));
    }

    /// <summary>All active persistent <see cref="ActorLocationKind.OnOverlandBand"/> travelling parties.</summary>
    internal IReadOnlyList<DaggerfallPersistentOverlandBand> PersistentBands => _persistentBands;

    /// <summary>Registers or replaces a persistent travelling party on the overland map.</summary>
    internal DaggerfallPersistentOverlandBand RegisterPersistentBand(DaggerfallPersistentBandSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        save.Validate();
        _persistentBands.RemoveAll(existing => StringComparer.Ordinal.Equals(existing.BandId, save.BandId));
        DaggerfallPersistentOverlandBand created = DaggerfallPersistentOverlandBand.FromSave(save, _nextPersistentDisplayId--);
        _persistentBands.Add(created);
        return created;
    }

    /// <summary>Removes a persistent travelling party from the overland map.</summary>
    internal bool RemovePersistentBand(string bandId)
    {
        if (_persistentBandAhead is { } ahead && StringComparer.Ordinal.Equals(ahead.BandId, bandId))
            _persistentBandAhead = null;
        return _persistentBands.RemoveAll(b => StringComparer.Ordinal.Equals(b.BandId, bandId)) > 0;
    }

    /// <summary>How far the party sees now, in map pixels.</summary>
    internal double PartySight => _bands.PartySight(SightConditions());

    /// <summary>How far a band sees the party now, in map pixels.</summary>
    internal double BandSight => _bands.BandSight(SightConditions());

    /// <summary>The bands within the party's sight (generated chasers and persistent parties alike), nearest first.</summary>
    internal IReadOnlyList<DaggerfallExpeditionBandView> SeenBands => _open && _teleport is null
        ? [.. _bands.Seen(Position, PartySight).Select(View)
            .Concat(_persistentBands.Where(b => !b.OnRoad && b.Distance(Position.X, Position.Y) <= PartySight).Select(ViewPersistent))
            .OrderBy(view => view.Distance)]
        : [];

    /// <summary>Every band staged around the party (generated chasers and persistent parties alike), for a developer.</summary>
    internal IReadOnlyList<DaggerfallExpeditionBandView> AllBands =>
        [.. _bands.All.Select(View).Concat(_persistentBands.Select(ViewPersistent)).OrderBy(view => view.Distance)];

    /// <summary>The band the party has come upon unseen and stands beside, if any.</summary>
    internal DaggerfallExpeditionBandView? BandAhead =>
        _bandAhead is { } band ? View(band)
        : _persistentBandAhead is { } persistent ? ViewPersistent(persistent)
        : null;

    /// <summary>
    /// Stages one band for a developer, a distance (pixels) and bearing (degrees clockwise from north) from the party,
    /// camped or wandering. Null when one was staged, else why not.
    /// </summary>
    internal string? StageBand(double distance, double bearing, bool camp)
    {
        if (!_open || _teleport is not null || _held is not null) return "Open the travel map outdoors first.";
        return _bands.StageAt(_world.Calendar.ToAbsoluteSeconds(), Position, _world.PlayerLevel, _world.Roll, distance, bearing, camp,
            _world.WorldConditions(HostAt(_party).Id.Region)).Refusal;
    }

    private DaggerfallExpeditionBandView View(DaggerfallExpeditionBand band)
    {
        (double x, double y) = Position;
        bool isClash = band.IsFactionClash;
        DaggerfallBandKind kind = isClash ? DaggerfallBandKind.Clash : band.Kind;
        string? clashSides = isClash
            ? $"{band.PrimarySideName ?? band.Name} vs {band.RivalName}"
            : null;
        string category = isClash
            ? "Faction Clash"
            : kind switch
            {
                DaggerfallBandKind.Patrol => "Patrol",
                DaggerfallBandKind.Bandits => "Bandits",
                DaggerfallBandKind.Beasts => "Beasts",
                DaggerfallBandKind.Undead => "Undead",
                DaggerfallBandKind.Monsters => "Monsters",
                DaggerfallBandKind.Hostile => "Hostile Band",
                _ => "Travellers",
            };
        string detail = isClash
            ? $"Active clash between {band.PrimarySideName ?? band.Name} and {band.RivalName}. Enter to intervene or pass by."
            : band.Spotted
                ? $"{category} spotted you and gives chase."
                : band.Camp
                    ? $"{category} camped by the road."
                    : kind == DaggerfallBandKind.Patrol
                        ? "Armed patrol watching the regional roads."
                        : $"{category} roaming the wilderness.";
        return new(band.Id, band.X, band.Y, band.Heading, kind, band.Name, band.Camp, band.Spotted, band.Distance(x, y))
        {
            Mobile = band.Choice.MobileId,
            ConditionTokenId = band.ConditionTokenId,
            Category = category,
            ClashSides = clashSides,
            Detail = detail,
            IsClash = isClash,
            Hostile = !isClash && kind is DaggerfallBandKind.Bandits or DaggerfallBandKind.Beasts or DaggerfallBandKind.Undead or DaggerfallBandKind.Monsters or DaggerfallBandKind.Hostile,
        };
    }

    private DaggerfallExpeditionBandView ViewPersistent(DaggerfallPersistentOverlandBand band)
    {
        (double x, double y) = Position;
        if (band.ActiveClash is { } clash)
        {
            string clashSides = $"{band.Name} vs {clash.RivalName}";
            bool isCaravanRaid = IsRaidableTraveller(band);
            string clashCategory = isCaravanRaid ? "Caravan Raid" : "Faction Clash";
            return new(band.DisplayId, band.X, band.Y, band.Heading, DaggerfallBandKind.Clash, clashSides, Camp: true, band.Spotted, band.Distance(x, y))
            {
                Mobile = band.Mobile ?? clash.RivalMobileId,
                PersistentBandId = band.BandId,
                Category = clashCategory,
                Faction = band.Faction,
                ClashSides = clashSides,
                OriginName = KnownSiteName(band.OriginSite?.Require()),
                DestinationName = KnownSiteName(band.Destination?.Place),
                Detail = $"Active {(isCaravanRaid ? "caravan raid" : "clash")}: {clashSides}. Enter to intervene or pass by.",
                IsClash = true,
                Hostile = false,
            };
        }

        bool isDispatch = band.BandId.StartsWith("dispatch:", StringComparison.Ordinal);
        bool isTraffic = band.BandId.StartsWith(DaggerfallOverlandTraffic.BandIdPrefix, StringComparison.Ordinal);
        bool isHostileProducer = band.BandId.StartsWith(DaggerfallHostileOverlandBands.BandIdPrefix, StringComparison.Ordinal);
        string category = isDispatch
            ? "Sealed Envoy"
            : isHostileProducer
                ? band.BandId.Contains(":bounty:", StringComparison.Ordinal)
                    ? "Bounty Hunters"
                    : band.BandId.Contains(":patrol-", StringComparison.Ordinal)
                        ? "Hostile Patrol"
                        : "Hit Squad"
                : band.Kind switch
                {
                    DaggerfallBandKind.Caravan => "Caravan",
                    DaggerfallBandKind.Courier => "Courier",
                    DaggerfallBandKind.Convoy => "Grain Convoy",
                    DaggerfallBandKind.Patrol => band.Hostile ? "Hostile Patrol" : "Patrol",
                    DaggerfallBandKind.Hostile => "Hostile Squad",
                    DaggerfallBandKind.Clash => "Faction Clash",
                    DaggerfallBandKind.Bandits => "Bandits",
                    DaggerfallBandKind.Beasts => "Beasts",
                    DaggerfallBandKind.Undead => "Undead",
                    DaggerfallBandKind.Monsters => "Monsters",
                    _ => "Travellers",
                };
        string? originName = KnownSiteName(band.OriginSite?.Require());
        string? destName = KnownSiteName(band.Destination?.Place);
        string detail;
        if (isDispatch)
        {
            detail = destName is not null
                ? $"Carrying a sealed political dispatch bound for {destName}."
                : "Carrying a sealed political dispatch.";
        }
        else if (band.Hostile)
        {
            detail = band.Spotted
                ? $"{category} spotted you and gives chase."
                : $"{category} hunting on the regional roads.";
        }
        else if (isTraffic && band.Kind == DaggerfallBandKind.Caravan)
        {
            detail = destName is not null
                ? $"Merchant caravan bound for {destName}."
                : "Merchant caravan travelling the regional roads.";
        }
        else if (isTraffic && band.Kind == DaggerfallBandKind.Courier)
        {
            detail = destName is not null
                ? $"Regional courier bound for {destName}."
                : "Regional courier riding between settlements.";
        }
        else if (band.Kind == DaggerfallBandKind.Convoy)
        {
            detail = destName is not null
                ? $"Wagons of {band.Faction ?? "a court"}'s grain bound for {destName}, under guard."
                : $"Wagons of {band.Faction ?? "a court"}'s grain on the road, under guard.";
        }
        else if (band.Camp)
        {
            detail = $"{category} camped by the road.";
        }
        else
        {
            detail = destName is not null
                ? $"{category} travelling toward {destName}."
                : $"{category} travelling on the road.";
        }

        return new(band.DisplayId, band.X, band.Y, band.Heading, band.Kind, band.Name, band.Camp, band.Spotted, band.Distance(x, y))
        {
            Mobile = band.Mobile,
            PersistentBandId = band.BandId,
            OnRoad = band.OnRoad,
            Banner = band.Banner,
            Category = category,
            Faction = band.Faction,
            OriginName = originName,
            DestinationName = destName,
            Detail = detail,
            IsClash = band.Kind == DaggerfallBandKind.Clash,
            Hostile = band.Hostile,
        };
    }

    private string? KnownSiteName(DaggerfallSiteId? id) =>
        id is { } siteId && Known(siteId) && _sites.TryFind(siteId, out DaggerfallSiteRecord? site) ? site.Name : null;

    private DaggerfallBandSightConditions SightConditions()
    {
        int climate = _policy.Climate(_party);
        return new(_mode, !_world.Calendar.IsDay, _world.Weather(climate), climate, _world.Scouting, _world.Concealment);
    }

    private const long ClashHoldSeconds = 4L * SecondsPerHour;

    /// <summary>
    /// The bands' part of a stretch: advance persistent and transient band movement first, resolve all band-vs-band
    /// collisions before player spotting or encounter stops can suppress them, and then arbitrate player meet/spotting
    /// events and persistent arrivals. At sea no band intercepts the party.
    /// </summary>
    private bool BandsStop(long seconds, DaggerfallCalendar after)
    {
        long now = after.ToAbsoluteSeconds();
        ExpireActiveClashes(now);
        TakeToRoadOrMap(Position);
        StepRoad(now - seconds, now, Position);

        Dictionary<string, (double X, double Y)> beforePositions = new(_persistentBands.Count, StringComparer.Ordinal);
        foreach (DaggerfallPersistentOverlandBand persistent in _persistentBands)
            beforePositions[persistent.BandId] = (persistent.X, persistent.Y);

        HashSet<string> arrivedBandIds = MovePersistentBands(seconds, now);

        bool atSea = _policy.IsOcean(_party);
        if (!atSea)
        {
            IReadOnlyList<DaggerfallWorldConditionToken> activeConditions = _world.WorldConditions(HostAt(_party).Id.Region);
            _bands.AdvanceMovement(seconds, now, Position, _world.PlayerLevel, _world.Roll, activeConditions);
        }

        _ = ResolveBandCollisions(now, Position, mapOpen: true, beforePositions);

        if (EvaluatePersistentPlayerEvents(arrivedBandIds)) return true;
        if (atSea) return false;

        double partySight = PartySight;
        DaggerfallBandEvent? found = _bands.EvaluatePlayerEvents(Position, SightConditions());
        if (found is null || !_bands.All.Contains(found.Band))
            return false;
        DaggerfallExpeditionBand band = found.Band;
        bool many = band.Size > 1;
        switch (found.Kind)
        {
            case DaggerfallBandEventKind.Spotted when !band.Camp && band.Distance(Position.X, Position.Y) <= partySight:
                Pause($"{band.Name} {(many ? "have" : "has")} seen you and {(many ? "give" : "gives")} chase!");
                return true;
            case DaggerfallBandEventKind.Caught:
                _world.MeetBand(band, _party, DaggerfallEncounterStaging.Caught);
                _bands.Remove(band);
                _bandAhead = null;
                _encounterWaiting = true;
                _encounterFoe = band.Name;
                Pause(EncounterLine());
                return true;
            case DaggerfallBandEventKind.CameUpon:
                _bandAhead = band;
                Pause(band.IsFactionClash
                    ? $"You come upon {band.Name}. Enter to intervene, or go on to pass it by."
                    : $"You come upon {band.Name}{(band.Camp ? ", camped by the way" : "")}. Enter to fight, or go on to pass it by.");
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Advances persistent overland bands while the expedition map is closed (inn sleep, dungeon rest, walking on foot,
    /// quest time) without triggering player-intercept pauses. The bands on the road go the whole interval at once, a
    /// segment at a time (<see cref="StepRoad"/>); the live ones near the party step in bounded hourly slices so outbound
    /// caravans that arrive mid-interval can dispatch and progress on their return legs, and off-screen clashes resolve
    /// cleanly. The interval runs from absolute second <paramref name="from"/> to <paramref name="to"/>, which a caller
    /// cutting a longer one into pieces gives for each piece, whatever the calendar already reads.
    /// </summary>
    internal int AdvancePersistentBandsOffMap(long from, long to, (double X, double Y) partyPosition)
    {
        if (to <= from || _persistentBands.Count == 0) return 0;

        long sliceCursor = from;
        TakeToRoadOrMap(partyPosition);
        int arrivals = StepRoad(from, to, partyPosition);
        const int maxSlices = 48;
        long remaining = to - from;
        int slices = 0;
        while (remaining > 0 && _persistentBands.Any(band => !band.OnRoad))
        {
            long stepSeconds = slices + 1 < maxSlices
                ? Math.Min(remaining, SecondsPerHour)
                : remaining;
            remaining -= stepSeconds;
            long sliceStart = sliceCursor;
            sliceCursor += stepSeconds;
            slices++;

            ExpireActiveClashes(sliceCursor);
            Dictionary<string, (double X, double Y)> beforePositions = new(_persistentBands.Count, StringComparer.Ordinal);
            foreach (DaggerfallPersistentOverlandBand band in _persistentBands)
                beforePositions[band.BandId] = (band.X, band.Y);

            double hours = stepSeconds / (double)SecondsPerHour;
            foreach (DaggerfallPersistentOverlandBand band in _persistentBands.ToArray())
            {
                if (band.OnRoad || HeldAt(band, sliceStart)) continue;
                bool arrived = band.StepMove(partyPosition, hours, allowPursuit: false, policy: _policy);
                if (!arrived) continue;
                _persistentBands.Remove(band);
                if (_persistentBandAhead == band) _persistentBandAhead = null;
                _world.PersistentBandArrived(band, sliceCursor);
                arrivals++;
            }
            ResolveBandCollisions(sliceCursor, partyPosition, mapOpen: false, beforePositions);
        }
        return arrivals;
    }

    /// <summary>
    /// Evaluates band-vs-band collisions across transient and persistent bands at <paramref name="now"/>:
    /// collisions within player approach/sight coalesce into a live joinable clash, while off-screen collisions
    /// resolve cleanly with zero orphaned <c>OnOverlandBand</c> ledger entries.
    /// </summary>
    internal int ResolveBandCollisions(
        long now,
        (double X, double Y) partyPosition,
        bool mapOpen,
        IReadOnlyDictionary<string, (double X, double Y)>? persistentBeforePositions = null)
    {
        int collisions = 0;
        double collisionDistance = _bands.Tuning.MeetDistance * 1.5;
        double holdRadius = mapOpen ? PartySight * 1.5 : 0d;

        // 1. Transient raider/monster/patrol vs Persistent caravan/patrol/hostile collisions (on-map only).
        if (mapOpen && _bands.All.Count > 0 && _persistentBands.Count > 0)
        {
            foreach (DaggerfallExpeditionBand transient in _bands.All.ToArray())
            {
                if (transient.IsFactionClash || transient.Spotted) continue;
                foreach (DaggerfallPersistentOverlandBand persistent in _persistentBands.ToArray())
                {
                    if (persistent.OnRoad || persistent.ActiveClash is not null || persistent.Spotted) continue;
                    if (transient.Distance(persistent.X, persistent.Y) > collisionDistance) continue;
                    if (!AreOpposingTransientAndPersistent(transient, persistent)) continue;

                    _bands.Remove(transient);
                    if (_bandAhead == transient) _bandAhead = null;
                    collisions++;

                    if (persistent.Distance(partyPosition.X, partyPosition.Y) <= holdRadius)
                    {
                        persistent.X = (persistent.X + transient.X) * 0.5;
                        persistent.Y = (persistent.Y + transient.Y) * 0.5;
                        persistent.ActiveClash = new DaggerfallPersistentBandClashSave(
                            RivalName: transient.Name,
                            EndsAtSecond: now + ClashHoldSeconds,
                            RivalBandId: null,
                            RivalMobileId: transient.Choice.MobileId,
                            RivalSize: transient.Size);
                    }
                    else
                    {
                        ResolveOffScreenTransientRaid(persistent, now);
                    }
                    break;
                }
            }
        }

        // 2. Persistent vs Persistent collisions (on-map and off-map), using segment closest-approach when prior positions
        // are available so fast opposing bands do not tunnel past each other across an hourly off-map slice.
        if (_persistentBands.Count >= 2)
        {
            DaggerfallPersistentOverlandBand[] snapshot = [.. _persistentBands.Where(band => !band.OnRoad)];
            HashSet<string> consumed = new(StringComparer.Ordinal);
            for (int i = 0; i < snapshot.Length; i++)
            {
                DaggerfallPersistentOverlandBand a = snapshot[i];
                if (consumed.Contains(a.BandId) || a.ActiveClash is not null || a.Spotted) continue;

                for (int j = i + 1; j < snapshot.Length; j++)
                {
                    DaggerfallPersistentOverlandBand b = snapshot[j];
                    if (consumed.Contains(b.BandId) || b.ActiveClash is not null || b.Spotted) continue;
                    (double a0x, double a0y) = persistentBeforePositions is not null && persistentBeforePositions.TryGetValue(a.BandId, out var pa) ? pa : (a.X, a.Y);
                    (double b0x, double b0y) = persistentBeforePositions is not null && persistentBeforePositions.TryGetValue(b.BandId, out var pb) ? pb : (b.X, b.Y);
                    if (SegmentClosestApproachDistance(a0x, a0y, a.X, a.Y, b0x, b0y, b.X, b.Y) > collisionDistance) continue;
                    if (!AreOpposingPersistentBands(a, b)) continue;

                    // Keep the non-hostile caravan/patrol as the primary carrier if one side is non-hostile.
                    (DaggerfallPersistentOverlandBand primary, DaggerfallPersistentOverlandBand rival) =
                        !b.Hostile && a.Hostile ? (b, a) : (a, b);
                    consumed.Add(primary.BandId);
                    consumed.Add(rival.BandId);
                    _persistentBands.Remove(rival);
                    if (_persistentBandAhead == rival) _persistentBandAhead = null;
                    collisions++;

                    if (mapOpen && primary.Distance(partyPosition.X, partyPosition.Y) <= holdRadius)
                    {
                        primary.X = (primary.X + rival.X) * 0.5;
                        primary.Y = (primary.Y + rival.Y) * 0.5;
                        primary.ActiveClash = new DaggerfallPersistentBandClashSave(
                            RivalName: rival.Name,
                            EndsAtSecond: now + ClashHoldSeconds,
                            RivalBandId: rival.BandId,
                            RivalMobileId: rival.Mobile,
                            RivalSize: 0);
                    }
                    else
                    {
                        ResolveOffScreenPersistentClash(primary, rival.BandId, now);
                    }
                    break;
                }
            }
        }

        return collisions;
    }

    private void ExpireActiveClashes(long now)
    {
        if (_persistentBands.Count == 0) return;
        foreach (DaggerfallPersistentOverlandBand band in _persistentBands.ToArray())
        {
            if (band.ActiveClash is not { } clash || now < clash.EndsAtSecond) continue;
            if (StringComparer.Ordinal.Equals(_pendingPersistentBandId, band.BandId)) continue;
            band.ActiveClash = null;
            if (clash.RivalBandId is { Length: > 0 } rivalBandId)
            {
                ResolveOffScreenPersistentClash(band, rivalBandId, now);
            }
            else
            {
                ResolveOffScreenTransientRaid(band, now);
            }
        }
    }

    private void ResolveOffScreenTransientRaid(DaggerfallPersistentOverlandBand band, long now)
    {
        // Deterministic off-screen outcome: 50% the caravan/band escapes and resumes its route; 50% it is scattered/retired cleanly.
        int roll = _world.Roll($"clash:raid:{band.BandId}:{now}", 1, 100);
        if (roll <= 50 && IsRaidableTraveller(band))
        {
            _persistentBands.Remove(band);
            if (_persistentBandAhead == band) _persistentBandAhead = null;
            _world.PersistentBandRetiredOffScreen(band.BandId, now);
        }
    }

    private void ResolveOffScreenPersistentClash(DaggerfallPersistentOverlandBand primary, string rivalBandId, long now)
    {
        // Always retire the absorbed rival cleanly so zero OnOverlandBand ledger entries are orphaned.
        _world.PersistentBandRetiredOffScreen(rivalBandId, now);
        int roll = _world.Roll($"clash:persistent:{primary.BandId}:{rivalBandId}:{now}", 1, 100);
        if (roll <= 50 && (IsRaidableTraveller(primary) || primary.Hostile))
        {
            _persistentBands.Remove(primary);
            if (_persistentBandAhead == primary) _persistentBandAhead = null;
            _world.PersistentBandRetiredOffScreen(primary.BandId, now);
        }
    }

    private bool AreOpposingTransientAndPersistent(DaggerfallExpeditionBand transient, DaggerfallPersistentOverlandBand persistent)
    {
        bool transientRaider = transient.Kind is DaggerfallBandKind.Bandits or DaggerfallBandKind.Monsters or DaggerfallBandKind.Undead or DaggerfallBandKind.Beasts or DaggerfallBandKind.Hostile;
        if (transientRaider && IsRaidableTraveller(persistent))
            return true;
        if (transient.Kind == DaggerfallBandKind.Patrol && persistent.Hostile)
            return true;
        if (persistent.Kind == DaggerfallBandKind.Patrol && transientRaider)
            return true;
        if (transient.Choice.MobileId is int tm && persistent.Mobile is int pm && _bands.AreOpposingMobiles(tm, pm))
            return true;
        return false;
    }

    private bool AreOpposingPersistentBands(DaggerfallPersistentOverlandBand a, DaggerfallPersistentOverlandBand b)
    {
        if ((a.Hostile && IsRaidableTraveller(b)) || (b.Hostile && IsRaidableTraveller(a)))
            return true;
        if ((a.Hostile && !b.Hostile && b.Kind == DaggerfallBandKind.Patrol)
            || (b.Hostile && !a.Hostile && a.Kind == DaggerfallBandKind.Patrol))
            return true;
        if (a.Mobile is int ma && b.Mobile is int mb && _bands.AreOpposingMobiles(ma, mb))
            return true;
        return false;
    }

    private static bool IsRaidableTraveller(DaggerfallPersistentOverlandBand band) =>
        !band.Hostile && band.Kind is DaggerfallBandKind.Caravan or DaggerfallBandKind.Travellers or DaggerfallBandKind.Courier or DaggerfallBandKind.Convoy;

    /// <summary>
    /// Puts each band where it belongs for the party at <paramref name="party"/>: a band on the road comes onto the map,
    /// where it stands, within the road tuning's live distance; a live band free to go goes back on the road beyond its road
    /// distance. A hostile band stays live, as does one bound nowhere, clashing, camped, spotting the party, or met.
    /// </summary>
    private void TakeToRoadOrMap((double X, double Y) party)
    {
        DaggerfallRoadTuning road = _tuning.Road;
        foreach (DaggerfallPersistentOverlandBand band in _persistentBands)
        {
            double distance = band.Distance(party.X, party.Y);
            if (band.OnRoad)
            {
                if (distance <= road.LiveDistance) band.OnRoad = false;
            }
            else if (distance > road.RoadDistance && !band.Hostile && band.Destination is not null && band.SpeedPixelsPerHour > 0
                && band.ActiveClash is null && !band.Spotted && !band.Camp && band != _persistentBandAhead
                && !StringComparer.Ordinal.Equals(band.BandId, _pendingPersistentBandId))
            {
                // The road judges the segments it sets out along from here; the ones it went along live were met live.
                band.RoadSegment = Math.Max(band.RoadSegment, (band.RouteStep + road.SegmentPixels - 1) / road.SegmentPixels);
                band.OnRoad = true;
            }
        }
    }

    /// <summary>
    /// Moves every band on the road from absolute second <paramref name="from"/> to <paramref name="to"/> (#11178), judging
    /// each segment it sets out along by <see cref="JudgeRoad"/>: one that arrives is delivered as a live band's arrival is,
    /// and one raided is retired as an off-screen raid retires a band. A band an arrival sends out (a caravan's way home)
    /// sets out from the second of that arrival, on the road if it is beyond the party at <paramref name="party"/>.
    /// Returns the arrivals.
    /// </summary>
    private int StepRoad(long from, long to, (double X, double Y) party)
    {
        if (to <= from) return 0;
        DaggerfallRoadTuning road = _tuning.Road;
        long delaySeconds = (long)Math.Round(road.DelayHours * SecondsPerHour);
        int arrivals = 0;
        Queue<(DaggerfallPersistentOverlandBand Band, long From)> going = new(_persistentBands.Where(band => band.OnRoad).Select(band => (band, from)));
        while (going.TryDequeue(out (DaggerfallPersistentOverlandBand Band, long From) next))
        {
            DaggerfallPersistentOverlandBand band = next.Band;
            DaggerfallWorldConditionToken? raidedUnder = null;
            (DaggerfallRoadStep step, long at) = band.StepOnRoad(next.From, to, _policy, road.SegmentPixels, delaySeconds, (segment, second) =>
            {
                (DaggerfallRoadMeeting meeting, DaggerfallWorldConditionToken? under) = JudgeRoad(band, segment, second);
                if (meeting == DaggerfallRoadMeeting.Delayed) _world.PersistentBandDelayed(band, delaySeconds);
                raidedUnder = under;
                return meeting;
            });
            if (step == DaggerfallRoadStep.Travelling) continue;
            _persistentBands.Remove(band);
            if (step == DaggerfallRoadStep.Raided)
            {
                _world.PersistentBandRetiredOffScreen(band.BandId, at, raidedUnder);
                continue;
            }
            HashSet<DaggerfallPersistentOverlandBand> before = [.. _persistentBands];
            _world.PersistentBandArrived(band, at);
            arrivals++;
            TakeToRoadOrMap(party);
            foreach (DaggerfallPersistentOverlandBand sent in _persistentBands.Where(sent => sent.OnRoad && !before.Contains(sent)))
                going.Enqueue((sent, at));
        }
        return arrivals;
    }

    /// <summary>
    /// What meets <paramref name="band"/> setting out along road segment <paramref name="segment"/> at absolute second
    /// <paramref name="second"/>: the road's own chances, each hazard held then in the region it stands in by its severity,
    /// and a hostile band on the map within reach; one keyed roll by the band, its route and the segment decides. A band
    /// that cannot be raided (a patrol) is only delayed. A raid names the hazard's token it fell in, read in that order
    /// (none for the road's own or a hostile band's), so a board convoy can tell whose soldiers took it (#11180).
    /// </summary>
    private (DaggerfallRoadMeeting Meeting, DaggerfallWorldConditionToken? Under) JudgeRoad(DaggerfallPersistentOverlandBand band, int segment, long second)
    {
        DaggerfallRoadTuning road = _tuning.Road;
        List<(DaggerfallRoadChances Chances, int Severity, DaggerfallWorldConditionToken? Token)> held = [(road.Wilds, 1, null)];
        int region = _policy.RegionAt(new((int)Math.Floor(band.X), (int)Math.Floor(band.Y)));
        if (region >= 0)
        {
            int day = checked((int)(second / DaggerfallCalendar.SecondsPerDay));
            foreach (IGrouping<DaggerfallRoadHazard, DaggerfallWorldConditionToken> hazard in _world.WorldConditionsOn(region, day)
                .Where(token => DaggerfallRoadTuning.HazardOf(token) is not null)
                .GroupBy(token => DaggerfallRoadTuning.HazardOf(token)!.Value)
                .OrderBy(group => group.Key))
            {
                DaggerfallWorldConditionToken strongest = hazard.OrderByDescending(token => token.Severity).ThenBy(token => token.TokenId, StringComparer.Ordinal).First();
                held.Add((road.Hazards[hazard.Key], strongest.Severity, strongest));
            }
        }
        if (_persistentBands.Any(other => other.Hostile && !other.OnRoad && other.Distance(band.X, band.Y) <= road.HostileReach))
            held.Add((road.HostileBand, 1, null));
        int raid = held.Sum(entry => entry.Chances.Raid * entry.Severity), delay = held.Sum(entry => entry.Chances.Delay * entry.Severity);
        bool raidable = IsRaidableTraveller(band);
        if (!raidable) (raid, delay) = (0, raid + delay);
        int roll = _world.Roll(string.Create(CultureInfo.InvariantCulture,
            $"road:{band.BandId}:{band.RouteOrigin?.X}:{band.RouteOrigin?.Y}:{segment}"), 1, 100);
        if (roll > raid) return (roll <= raid + delay ? DaggerfallRoadMeeting.Delayed : DaggerfallRoadMeeting.Clear, null);
        int reach = 0;
        foreach ((DaggerfallRoadChances chances, int severity, DaggerfallWorldConditionToken? token) in held)
            if (roll <= (reach += chances.Raid * severity)) return (DaggerfallRoadMeeting.Raided, token);
        return (DaggerfallRoadMeeting.Raided, null);
    }

    /// <summary>Whether a delay on the road still holds <paramref name="band"/> at absolute second <paramref name="now"/>; one that has run out is cleared.</summary>
    private static bool HeldAt(DaggerfallPersistentOverlandBand band, long now)
    {
        if (band.HeldUntilSecond is not { } held) return false;
        if (held > now) return true;
        band.HeldUntilSecond = null;
        return false;
    }

    /// <summary>Closest Euclidean distance between two linearly moving points over <c>t in [0, 1]</c>.</summary>
    private static double SegmentClosestApproachDistance(
        double a0x, double a0y, double a1x, double a1y,
        double b0x, double b0y, double b1x, double b1y)
    {
        double dx0 = a0x - b0x;
        double dy0 = a0y - b0y;
        double dvx = (a1x - a0x) - (b1x - b0x);
        double dvy = (a1y - a0y) - (b1y - b0y);
        double denom = dvx * dvx + dvy * dvy;
        double t = denom > 1e-12 ? Math.Clamp(-(dx0 * dvx + dy0 * dvy) / denom, 0d, 1d) : 0d;
        double cx = dx0 + dvx * t;
        double cy = dy0 + dvy * t;
        return Math.Sqrt(cx * cx + cy * cy);
    }

    private HashSet<string> MovePersistentBands(long seconds, long now)
    {
        HashSet<string> arrivedBandIds = new(StringComparer.Ordinal);
        if (_persistentBands.Count == 0) return arrivedBandIds;
        double hours = seconds / (double)SecondsPerHour;
        (double px, double py) = Position;
        foreach (DaggerfallPersistentOverlandBand band in _persistentBands.ToArray())
        {
            if (band.OnRoad || HeldAt(band, now)) continue;
            if (band.StepMove((px, py), hours, allowPursuit: true, policy: _policy))
                arrivedBandIds.Add(band.BandId);
        }
        return arrivedBandIds;
    }

    private bool EvaluatePersistentPlayerEvents(HashSet<string> arrivedBandIds)
    {
        if (_persistentBands.Count == 0) return false;
        (double px, double py) = Position;
        double partySight = PartySight;
        double sees = _bands.BandSight(SightConditions());
        double meetDistance = _bands.Tuning.MeetDistance;
        bool atSea = _policy.IsOcean(_party);

        DaggerfallPersistentOverlandBand? spottedStop = null;
        foreach (DaggerfallPersistentOverlandBand band in _persistentBands.ToArray())
        {
            if (band.OnRoad) continue;
            double distance = band.Distance(px, py);
            if (band.Passed && distance > meetDistance * 2) band.Passed = false;

            if (!atSea && distance <= meetDistance && !band.Passed)
            {
                if (band.ActiveClash is { } clash)
                {
                    _persistentBandAhead = band;
                    Pause($"You come upon {band.Name} vs {clash.RivalName}. Enter to intervene, or go on to pass it by.");
                    return true;
                }
                if (band.Hostile && (band.Spotted || sees >= meetDistance))
                {
                    // Keep the caught persistent band in _persistentBands until Entry() actually enters the 3D scene
                    // so saving while EncounterWaiting keeps the band and its ActorLedger members consistent and reloadable.
                    _pendingPersistentBandId = band.BandId;
                    if (_persistentBandAhead == band) _persistentBandAhead = null;
                    _encounterWaiting = true;
                    _encounterFoe = band.Name;
                    Pause(EncounterLine());
                    return true;
                }
                _persistentBandAhead = band;
                Pause($"You come upon {band.Name}{(band.Camp ? ", camped by the way" : "")}. Enter to meet, or go on to pass it by.");
                return true;
            }

            if (band.ActiveClash is null && arrivedBandIds.Contains(band.BandId))
            {
                _persistentBands.Remove(band);
                if (_persistentBandAhead == band) _persistentBandAhead = null;
                _world.PersistentBandArrived(band, _world.Calendar.ToAbsoluteSeconds());
                continue;
            }

            if (!atSea && band.ActiveClash is null && band.Hostile && !band.Spotted && !band.Passed && distance <= sees)
            {
                band.Spotted = true;
                band.Camp = false;
                if (distance <= partySight) spottedStop ??= band;
            }
        }

        if (spottedStop is not null)
        {
            Pause($"{spottedStop.Name} spotted you and gives chase!");
            return true;
        }
        return false;
    }

    /// <summary>Going on past a band come upon unseen, by another way or the same, leaves it be until the party has drawn off.</summary>
    private void SteerAway()
    {
        if (_bandAhead is { } band)
        {
            band.Passed = true;
            _bandAhead = null;
        }
        if (_persistentBandAhead is { } persistent)
        {
            persistent.Passed = true;
            _persistentBandAhead = null;
        }
    }

    private string? Refused() => _held ?? (_teleport is not null ? "Choose where the guild should teleport you."
        : _encounterWaiting ? EncounterLine() : null);

    /// <summary>The map's line while an encounter waits: the band that caught the party, or an unseen one.</summary>
    private string EncounterLine() => _encounterFoe is { } foe ? $"{foe} caught up with you! Enter to fight." : "Something approaches! Enter to meet it.";

    /// <summary>
    /// Heads a new way from where the party stands. Midway between two pixels it goes on along the new way from that
    /// spot when the way passes either of them; otherwise it starts from the nearer one.
    /// </summary>
    private void Reroute(DaggerfallExpeditionMark destination)
    {
        if (_leg is not { Progress: > 0 } leg || Next() is not { } ahead)
        {
            SetLeg(new(_party, destination, 0, 0));
            return;
        }
        if (DaggerfallTravelPolicy.Path(_party, destination.Pixel) is [var first, ..] && first == ahead)
        {
            SetLeg(new(_party, destination, 0, leg.Progress));
            return;
        }
        DaggerfallTravelMapPixel behind = _party;
        if (DaggerfallTravelPolicy.Path(ahead, destination.Pixel) is [var back, ..] && back == behind)
        {
            // Turning back: the party stands between the same two pixels, now counted from the one ahead.
            _cameFrom = behind;
            _party = ahead;
            SetLeg(new(ahead, destination, 0, 1 - leg.Progress));
            return;
        }
        if (leg.Progress >= .5) { _cameFrom = behind; _party = ahead; }
        SetLeg(new(_party, destination, 0, 0));
    }

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
