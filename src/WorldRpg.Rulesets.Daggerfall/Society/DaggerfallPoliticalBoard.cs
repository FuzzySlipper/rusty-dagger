using System.Text.Json;
using System.Text.Json.Serialization;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>The political board as saved: the day of its last Sunday and the board itself (<see cref="BoardSave"/>).</summary>
internal sealed record DaggerfallPoliticalBoardSave(
    [property: JsonRequired] long LastTurnDay,
    [property: JsonRequired] JsonElement Board)
{
    internal DaggerfallPoliticalBoardSave Validate()
    {
        if (LastTurnDay % DaggerfallPoliticalBoard.DaysPerWeek != 0)
            throw new ArgumentOutOfRangeException(nameof(LastTurnDay), LastTurnDay, "The political board's last turn must fall on a Sunday.");
        if (Board.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The political board save holds no board.", nameof(Board));
        return this;
    }
}

/// <summary>
/// The political board running in the world (B2). It plays one board turn at the start of every Sunday the calendar
/// reaches, however the time passes (play, rest, travel, prison), each in order, so the Bay's politics run on while
/// the player sleeps. Every playthrough starts from the canon table and seed (<see cref="DaggerfallPoliticalCanon"/>).
/// <para>
/// What the world reads of it is A6's condition tokens: the board is <see cref="DaggerfallWorldConditions"/>' external
/// source, one token for each condition on a region, rebuilt after every turn. What the board hears of the world is
/// queued as <see cref="WorldNudge"/>s and applied at the start of the next turn; the counterplay that eased a board
/// token is drained into <see cref="ConditionEased"/> then.
/// </para>
/// <para>
/// A slow move rides with a sealed courier (<see cref="DaggerfallCourierDispatches"/>, the dispatch named by the move's
/// id) from the largest town of the region it set out from to that of the region or seat it aims at. A courier the
/// player seizes or kills intercepts the move; one the player carries to its door brings the move in at the next
/// Sunday; one still on the road, or in the player's pack, on the Sunday the move is due delays it a week. Courier
/// friction is the world's. Once its move has settled the board forgets the dispatch.
/// </para>
/// <para>
/// The board hears the world's office changes and quests (<see cref="ObserveOfficeChanged"/>, <see cref="ObserveQuestSettled"/>):
/// a seat holder dying or replaced gives the seat its new holder, a local office holder killed in a region's chief town
/// takes the seat's piece off that office, and a quest finished for a seat's faction gives the seat a little ground.
/// </para>
/// </summary>
internal sealed class DaggerfallPoliticalBoard
{
    internal const int DaysPerWeek = 7;
    private const long MinutesPerDay = 24 * 60;
    internal const string TokenPrefix = "board:";

    private readonly DaggerfallPoliticalCanon _canon;
    private readonly DaggerfallBoardSeatSet _seats;
    private readonly DaggerfallFactionsSet _factions;
    private readonly Func<string, DaggerfallSeatLeaderPersona> _seatPersona;
    private readonly DaggerfallWorldConditions _conditions;
    private readonly Board _board;
    private readonly DaggerfallPoliticalNews _news;
    private readonly DaggerfallCourierDispatches? _couriers;
    private readonly IReadOnlyList<DaggerfallSiteRecord> _sites;
    private readonly Dictionary<string, DaggerfallSiteRecord?> _towns = new(StringComparer.Ordinal);
    private readonly Dictionary<int, List<DaggerfallWorldConditionToken>> _tokens = [];
    private readonly Dictionary<string, (string Region, ConditionKind Kind, string? Detail)> _tokenConditions = new(StringComparer.Ordinal);
    private long _lastTurnDay;

    /// <summary>
    /// The board for a new game starting on <paramref name="day"/> (<paramref name="saved"/> null), or the board a save
    /// carried. A save made before the board existed starts the canon board on its own day. Without
    /// <paramref name="couriers"/> slow moves travel unseen. <paramref name="seatPersona"/> is who holds a seat in the world
    /// now (<see cref="DaggerfallOfficeLedger.ResolveBoardSeatPersona"/>).
    /// </summary>
    internal DaggerfallPoliticalBoard(DaggerfallPoliticalCanon canon, DaggerfallBoardSeatSet seats, DaggerfallFactionsSet factions,
        Func<string, DaggerfallSeatLeaderPersona> seatPersona, DaggerfallWorldConditions conditions, IReadOnlyList<DaggerfallSiteRecord> sites,
        DaggerfallCourierDispatches? couriers, long day, DaggerfallPoliticalBoardSave? saved)
    {
        _canon = canon;
        _seats = seats;
        _factions = factions;
        _seatPersona = seatPersona;
        _conditions = conditions;
        _sites = sites;
        _couriers = couriers;
        if (saved is null)
        {
            _board = Board.Create(canon.Setup, canon.Cards);
            _lastTurnDay = SundayOnOrBefore(day);
        }
        else
        {
            saved.Validate();
            _board = Board.Load(canon.Setup, canon.Cards, BoardSave.FromJson(saved.Board.GetRawText(), "the political board save"));
            _lastTurnDay = saved.LastTurnDay;
        }
        _news = new DaggerfallPoliticalNews(_board, canon.Cards, Town, Faction);
        Rebuild();
        conditions.BindExternalSource(TokensFor);
        couriers?.BindBoardMoveCallback(CourierSettled);
    }

    internal Board Board => _board;

    /// <summary>The day the board last played a turn on (or, before its first, the Sunday before the game began).</summary>
    internal long LastTurnDay => _lastTurnDay;

    /// <summary>The day the next turn plays on.</summary>
    internal long NextTurnDay => _lastTurnDay + DaysPerWeek;

    /// <summary>What the board published after its latest turn this session; null before the first.</summary>
    internal BoardOutputs? Latest { get; private set; }

    /// <summary>The trace of the latest turn this session (the nudges it applied among it); null before the first.</summary>
    internal TurnTrace? LatestTrace { get; private set; }

    /// <summary>
    /// Plays every turn due by <paramref name="day"/>: one for each Sunday from the last played to it, and returns the
    /// news they made (<see cref="DaggerfallPoliticalNews"/>). The counterplay <paramref name="drainCounterplay"/> hands
    /// over (<see cref="DaggerfallConditionCounterplay.ConsumeBoardEvents"/>) is applied at the start of each, where it
    /// eased a board token.
    /// </summary>
    internal IReadOnlyList<DaggerfallPoliticalNewsItem> AdvanceToDay(long day, Func<IReadOnlyList<DaggerfallConditionCounterplayRecord>> drainCounterplay)
    {
        List<DaggerfallPoliticalNewsItem> news = [];
        while (NextTurnDay <= day)
        {
            HashSet<string> warsBefore = [.. _board.Wars.Select(war => war.Id)];
            _conditions.NotifyBeforeExternalMutation();
            foreach (DaggerfallConditionCounterplayRecord record in drainCounterplay())
                if (Eased(record) is { } eased) _board.Queue(eased);
            DelayCouriersOnTheRoad();
            TurnTrace trace = BoardTurn.Play(_board);
            _lastTurnDay += DaysPerWeek;
            SendCouriers(trace);
            LatestTrace = trace;
            Latest = BoardOutputs.Of(_board, _canon.Cards, trace);
            news.AddRange(_news.Of(trace, warsBefore, _lastTurnDay * MinutesPerDay));
            ForgetSettledCouriers();
            Rebuild();
        }
        return news;
    }

    internal DaggerfallPoliticalBoardSave Capture()
    {
        using JsonDocument board = JsonDocument.Parse(_board.Save().ToJson());
        return new DaggerfallPoliticalBoardSave(_lastTurnDay, board.RootElement.Clone());
    }

    /// <summary>
    /// The board's tokens on classic region <paramref name="region"/>. A seat's succession is left out while the world
    /// shows its own crisis for the seat's office (<see cref="DaggerfallWorldConditions.RaiseSuccessionCrisis"/>), so a
    /// throne left vacant is told once.
    /// </summary>
    internal IReadOnlyList<DaggerfallWorldConditionToken> TokensFor(int region)
    {
        if (!_tokens.TryGetValue(region, out List<DaggerfallWorldConditionToken>? tokens)) return [];
        return tokens.Any(token => token.Kind == DaggerfallWorldConditionKind.SuccessionCrisis && ShownByTheWorld(token))
            ? [.. tokens.Where(token => token.Kind != DaggerfallWorldConditionKind.SuccessionCrisis || !ShownByTheWorld(token))]
            : tokens;
    }

    private bool ShownByTheWorld(DaggerfallWorldConditionToken succession) =>
        succession.SourceFactionId is int faction
        && _conditions.Has(DaggerfallWorldConditions.OfficeSuccessionCrisisTokenId(DaggerfallOfficeLedger.FactionOfficeKey(faction)));

    /// <summary>
    /// The largest town in board region <paramref name="region"/>'s classic region, where its couriers and news set out:
    /// a city before a hamlet before a village (Betony has no city). Null where it has none.
    /// </summary>
    internal DaggerfallSiteRecord? Town(string region)
    {
        if (_towns.TryGetValue(region, out DaggerfallSiteRecord? town)) return town;
        int catalogRegion = _board.Region(region).CatalogRegion;
        town = _sites
            .Where(site => site.Id.Region == catalogRegion && site.Exterior is not null
                && site.Kind is DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet or DaggerfallSiteKind.TownVillage)
            .OrderBy(site => site.Kind)
            .ThenByDescending(site => site.Exterior!.Width * site.Exterior.Height)
            .ThenBy(site => site.Id.Index)
            .FirstOrDefault();
        _towns[region] = town;
        return town;
    }

    /// <summary>
    /// An office changed hands in the world (<see cref="DaggerfallOfficeLedger.OfficeChanged"/>), heard at the next
    /// Sunday. A seat's office changing gives the seat its new holder; one with no persona of their own, or no one while
    /// the office stands vacant, plays on as the predecessor did, as a regency, and a succession opens (Patch's Q2). A
    /// local office in a region's chief town that a seat holds on the board goes back to local people.
    /// </summary>
    internal void ObserveOfficeChanged(DaggerfallOfficeChangedNotification change)
    {
        if (change.Kind is DaggerfallOfficeKind.Ruler or DaggerfallOfficeKind.MajorSeat)
        {
            if (change.OfficeFactionId is not int faction || !_seats.TryGetByFactionId(faction, out DaggerfallBoardSeatBinding? binding)
                || !_board.HasSeat(binding!.SeatId)) return;
            string holder = HolderOf(_seatPersona(binding.SeatId));
            if (holder != _board.Seat(binding.SeatId).Holder) _board.Queue(new HolderChanged(binding.SeatId, holder));
        }
        else if (change.OfficeSiteId is { } site && BoardOffice(change.Kind) is { } office)
        {
            foreach (Region region in _board.Regions.Where(region => region.Offices.ContainsKey(office) && Town(region.Id)?.Id == site))
                _board.Queue(new OfficeHolderRemoved(region.Id, office));
        }
    }

    /// <summary>
    /// A quest for <paramref name="factionId"/> settled. One finished for a seat's faction (or a faction under it) gives the
    /// seat +1 influence in <paramref name="region"/>, or +1 favour where the board has no region there or the seat can
    /// place no more influence (Patch's Q3). A failed quest moves nothing.
    /// </summary>
    internal void ObserveQuestSettled(int factionId, bool succeeded, int? region)
    {
        if (!succeeded || SeatOfFaction(factionId) is not { } seat) return;
        string? boardRegion = region is int catalogRegion ? _board.Regions.FirstOrDefault(held => held.CatalogRegion == catalogRegion)?.Id : null;
        QuestOutcome influence = new(seat, boardRegion is null ? [] : [boardRegion], [new CardEffect(CardEffectKind.Influence, 1)]);
        _board.Queue(boardRegion is not null && Nudges.Problem(_board, influence) is null && _board.PiecesLeft(_board.Seat(seat)) > 0
            ? influence
            : new QuestOutcome(seat, [], [new CardEffect(CardEffectKind.Gain, 1, Resource.Favour)]));
    }

    /// <summary>The day turn <paramref name="turn"/> was played on.</summary>
    internal long TurnDay(int turn) => _lastTurnDay - (long)(_board.Turn - turn) * DaysPerWeek;

    private static long SundayOnOrBefore(long day) => day - ((day % DaysPerWeek) + DaysPerWeek) % DaysPerWeek;

    /// <summary>Stages a courier for each slow move this turn played that travels between two towns.</summary>
    private void SendCouriers(TurnTrace trace)
    {
        if (_couriers is null) return;
        long minute = _lastTurnDay * MinutesPerDay;
        foreach (PendingMove move in _board.Pending.Where(move => move.Played == trace.Turn))
        {
            Seat seat = _board.Seat(move.Seat);
            string? targetSeat = move.Targets.Count > 0 && _board.HasSeat(move.Targets[0]) ? move.Targets[0] : null;
            string? from = move.Origin ?? seat.Home;
            string? to = move.Targets.Count > 0 && _board.HasRegion(move.Targets[0]) ? move.Targets[0]
                : targetSeat is not null ? _board.Seat(targetSeat).Home : null;
            if (from is null || to is null || Town(from) is not { } origin || Town(to) is not { } destination || origin.Id == destination.Id) continue;
            string aim = targetSeat is not null ? _board.Seat(targetSeat).Name : _board.Region(to).Name;
            _couriers.StageDispatch(move.Id, seat.Id, move.Card.Id, $"{seat.Name}: {move.Card.Name}, for {aim}", origin, destination,
                minute, checked((int)_lastTurnDay), Faction(seat.Id), Faction(targetSeat));
        }
    }

    /// <summary>Delays a week each move due this coming Sunday whose courier has not reached its door.</summary>
    private void DelayCouriersOnTheRoad()
    {
        if (_couriers is null) return;
        foreach (PendingMove move in _board.Pending.Where(move => move.Arrives == _board.Turn + 1).ToArray())
            if (_couriers.TryGet(move.Id, out DaggerfallCourierDispatchSave? dispatch)
                && dispatch!.Status is DaggerfallDispatchStatus.InTransit or DaggerfallDispatchStatus.BoughtAndRevealed or DaggerfallDispatchStatus.CarriedByPlayer)
                _board.Queue(new MoveDelayed(move.Id, 1));
    }

    /// <summary>What the player did with a board move's courier, queued for the next Sunday; a delivery on schedule tells the board nothing.</summary>
    private void CourierSettled(DaggerfallDispatchResolution resolution)
    {
        if (!_board.Pending.Any(move => move.Id == resolution.DispatchId)) return;
        if (resolution.Outcome == DaggerfallDispatchStatus.Intercepted) _board.Queue(new MoveIntercepted(resolution.DispatchId));
        else if (resolution.Outcome == DaggerfallDispatchStatus.Delivered && resolution.DeliveredByPlayer) _board.Queue(new MoveDelivered(resolution.DispatchId));
    }

    /// <summary>Forgets the dispatches whose moves are no longer under way.</summary>
    private void ForgetSettledCouriers()
    {
        if (_couriers is null) return;
        HashSet<string> pending = [.. _board.Pending.Select(move => move.Id)];
        foreach (DaggerfallCourierDispatchSave dispatch in _couriers.All.ToArray())
            if (IsMoveId(dispatch.DispatchId) && !pending.Contains(dispatch.DispatchId)
                && dispatch.Status is DaggerfallDispatchStatus.Delivered or DaggerfallDispatchStatus.Intercepted)
                _couriers.Forget(dispatch.DispatchId);
    }

    /// <summary>Whether <paramref name="id"/> names a board move (<c>turn-seat</c>), not a dispatch staged by hand.</summary>
    private bool IsMoveId(string id) =>
        id.IndexOf('-', StringComparison.Ordinal) is > 0 and var dash && int.TryParse(id.AsSpan(0, dash), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)
        && _board.HasSeat(id[(dash + 1)..]);

    /// <summary>The board condition a counterplay record eased, by the amount A6 already showed; null for a token not the board's.</summary>
    private ConditionEased? Eased(DaggerfallConditionCounterplayRecord record) =>
        _tokenConditions.TryGetValue(record.TokenId, out (string Region, ConditionKind Kind, string? Detail) condition)
            ? new ConditionEased(condition.Region, condition.Kind, record.PreviousSeverity - record.NewSeverity, condition.Detail)
            : null;

    private void Rebuild()
    {
        _tokens.Clear();
        _tokenConditions.Clear();
        foreach (Region region in _board.Regions)
        {
            foreach (RegionCondition condition in region.Conditions)
            {
                if (Token(region, condition) is not { } token) continue;
                Add(region.CatalogRegion, token);
                _tokenConditions[token.TokenId] = (region.Id, condition.Kind, condition.Detail);
            }
        }
        foreach (OpenArc arc in _board.Arcs.Where(arc => arc.Kind == ArcKind.Succession))
        {
            Seat seat = _board.Seat(arc.Subjects[0]);
            if (seat.Home is not { } home) continue;
            int catalogRegion = _board.Region(home).CatalogRegion;
            Add(catalogRegion, new DaggerfallWorldConditionToken(
                $"{TokenPrefix}{home}:succession:{seat.Id}",
                DaggerfallWorldConditionKind.SuccessionCrisis,
                Region: catalogRegion,
                SourceFactionId: Faction(seat.Id),
                SubjectTag: seat.Id,
                Severity: 2,
                StartedDay: Day(arc.Started)).Validate());
        }
    }

    private void Add(int region, DaggerfallWorldConditionToken token)
    {
        if (!_tokens.TryGetValue(region, out List<DaggerfallWorldConditionToken>? tokens)) _tokens[region] = tokens = [];
        tokens.Add(token);
    }

    /// <summary>The A6 token a board condition shows as, or null for a condition the world has no token for.</summary>
    private DaggerfallWorldConditionToken? Token(Region region, RegionCondition condition)
    {
        (string? first, string? second) = Pair(condition.Detail);
        (DaggerfallWorldConditionKind kind, int severity, int? source, int? target, string? tag)? mapped = condition.Kind switch
        {
            ConditionKind.Famine => (DaggerfallWorldConditionKind.GrainShortage, condition.Magnitude, Faction(condition.Source), null, null),
            ConditionKind.PriceSurge when condition.Detail == "grain" => (DaggerfallWorldConditionKind.GrainShortage, condition.Magnitude, Faction(condition.Source), null, "grain"),
            ConditionKind.Raiders => (DaggerfallWorldConditionKind.GrainShortage, condition.Magnitude, Faction(condition.Source), null, "raiders"),
            ConditionKind.Plague => (DaggerfallWorldConditionKind.PlagueScare, condition.Magnitude, Faction(condition.Source), null, null),
            ConditionKind.MonsterSurge => (DaggerfallWorldConditionKind.MonsterSurge, condition.Magnitude, Faction(condition.Source), null, condition.Detail),
            ConditionKind.WarFront => (DaggerfallWorldConditionKind.BorderTension, condition.Magnitude, Faction(first), Faction(second), null),
            ConditionKind.Skirmish => (DaggerfallWorldConditionKind.BorderTension, 1, Faction(first), Faction(second), null),
            ConditionKind.Unrest => (DaggerfallWorldConditionKind.BorderTension, 1, Faction(condition.Source), null, "unrest"),
            ConditionKind.Crackdown or ConditionKind.CrimeWave or ConditionKind.WitchHunt =>
                (DaggerfallWorldConditionKind.CrimeCrackdown, condition.Magnitude, Faction(condition.Source), null, Slug(condition.Kind)),
            ConditionKind.Persecution => (DaggerfallWorldConditionKind.GuildEmbargo, condition.Magnitude, Faction(condition.Source), Faction(condition.Detail), condition.Detail),
            ConditionKind.Recruitment or ConditionKind.Prosperity or ConditionKind.Festival =>
                (DaggerfallWorldConditionKind.Decree, condition.Magnitude, Faction(condition.Source), null, Slug(condition.Kind)),
            _ => null,
        };
        if (mapped is not { } token) return null;
        string id = condition.Detail is null
            ? $"{TokenPrefix}{region.Id}:{Slug(condition.Kind)}"
            : $"{TokenPrefix}{region.Id}:{Slug(condition.Kind)}:{condition.Detail}";
        return new DaggerfallWorldConditionToken(id, token.kind,
            Region: region.CatalogRegion,
            SourceFactionId: token.source,
            TargetFactionId: token.target,
            SubjectTag: token.tag,
            Severity: Math.Clamp(token.severity, DaggerfallWorldConditionToken.MinSeverity, DaggerfallWorldConditionToken.MaxSeverity),
            StartedDay: Day(condition.Start)).Validate();
    }

    /// <summary>The two seats a war front's or skirmish's detail names (<c>a,b</c>).</summary>
    private static (string? First, string? Second) Pair(string? detail) =>
        detail?.Split(',') is [var first, var second] ? (first, second) : (null, null);

    /// <summary>
    /// The board's name for whoever holds a seat in the world: the cast individual, else the actor, else the seat's
    /// vacancy. Only cast individuals the canon names have a persona; anyone else plays as a regency.
    /// </summary>
    private static string HolderOf(DaggerfallSeatLeaderPersona persona) =>
        persona.IsVacant ? $"vacant:{persona.SeatId}"
            : persona.HolderCastId ?? (persona.HolderActorId is long actor ? $"actor:{actor}" : $"untitled:{persona.SeatId}");

    /// <summary>The board office a local office kind is.</summary>
    private static Office? BoardOffice(DaggerfallOfficeKind kind) => kind switch
    {
        DaggerfallOfficeKind.WatchCaptain => Office.Watch,
        DaggerfallOfficeKind.CourtFactor => Office.Factor,
        DaggerfallOfficeKind.TempleHead => Office.Temple,
        DaggerfallOfficeKind.GuildHallMaster => Office.Hall,
        _ => null,
    };

    /// <summary>The seat whose office faction is <paramref name="factionId"/> or one of its parents.</summary>
    private string? SeatOfFaction(int factionId)
    {
        HashSet<int> seen = [];
        for (int faction = factionId; faction > 0 && seen.Add(faction);
             faction = _factions.Factions.TryGetValue(faction, out DaggerfallFactionDefinition? definition) ? definition.Parent : 0)
            if (_seats.TryGetByFactionId(faction, out DaggerfallBoardSeatBinding? binding) && _board.HasSeat(binding!.SeatId))
                return binding.SeatId;
        return null;
    }

    /// <summary>The office faction of seat <paramref name="seat"/>, or null for none (the world laid it).</summary>
    private int? Faction(string? seat) =>
        seat is not null && _seats.TryGetBySeatId(seat, out DaggerfallBoardSeatBinding? binding) ? binding!.OfficeFactionId : null;

    private int Day(int turn) => checked((int)Math.Max(0, TurnDay(turn)));

    /// <summary>A board condition kind in token ids and tags: <c>MonsterSurge</c> is <c>monster-surge</c>.</summary>
    private static string Slug(ConditionKind kind) =>
        string.Concat(kind.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? $"-{char.ToLowerInvariant(c)}" : char.ToLowerInvariant(c).ToString()));
}
