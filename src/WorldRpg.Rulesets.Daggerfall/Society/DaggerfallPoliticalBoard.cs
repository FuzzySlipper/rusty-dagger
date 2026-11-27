using System.Text.Json;
using System.Text.Json.Serialization;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The political board as saved: the day of its last Sunday, the board itself (<see cref="BoardSave"/>) and the traces of
/// its latest turns, oldest first (<see cref="DaggerfallPoliticalBoard.RecentTurns"/>), so the developer tools keep their
/// record across a load.
/// </summary>
internal sealed record DaggerfallPoliticalBoardSave(
    [property: JsonRequired] long LastTurnDay,
    [property: JsonRequired] JsonElement Board,
    [property: JsonRequired] JsonElement RecentTurns)
{
    internal DaggerfallPoliticalBoardSave Validate()
    {
        if (LastTurnDay % DaggerfallPoliticalBoard.DaysPerWeek != 0)
            throw new ArgumentOutOfRangeException(nameof(LastTurnDay), LastTurnDay, "The political board's last turn must fall on a Sunday.");
        if (Board.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The political board save holds no board.", nameof(Board));
        if (RecentTurns.ValueKind != JsonValueKind.Array || RecentTurns.GetArrayLength() > DaggerfallPoliticalBoard.RecentTurnsKept)
            throw new ArgumentException($"The political board save must keep a list of at most {DaggerfallPoliticalBoard.RecentTurnsKept} turns.", nameof(RecentTurns));
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
    /// <summary>The tag of a cut-off region's grain shortage.</summary>
    internal const string CutOffTag = "cut-off";
    internal const string ParanoidCourtTag = "paranoid-court";
    internal const string HostageTreatyTag = "hostage-treaty";
    /// <summary>How many of the latest turns the developer tools can look back over (<see cref="RecentTurns"/>).</summary>
    internal const int RecentTurnsKept = 12;

    private readonly DaggerfallPoliticalCanon _canon;
    private readonly DaggerfallBoardSeatSet _seats;
    private readonly DaggerfallRegionLiege _liege;
    private readonly Func<string, DaggerfallSeatLeaderPersona> _seatPersona;
    private readonly DaggerfallWorldConditions _conditions;
    private readonly Board _board;
    private readonly DaggerfallPoliticalNews _news;
    private readonly DaggerfallCourierDispatches? _couriers;
    private readonly IReadOnlyList<DaggerfallSiteRecord> _sites;
    private DaggerfallOfficeLedger? _officeLedger;
    private readonly Dictionary<string, DaggerfallSiteRecord?> _towns = new(StringComparer.Ordinal);
    private readonly Dictionary<int, List<DaggerfallWorldConditionToken>> _tokens = [];
    private readonly Dictionary<string, (string Region, ConditionKind Kind, string? Detail)> _tokenConditions = new(StringComparer.Ordinal);
    private readonly List<TurnTrace> _recentTurns = [];
    private long _lastTurnDay;

    /// <summary>
    /// The board for a new game starting on <paramref name="day"/> (<paramref name="saved"/> null), or the board a save
    /// carried. A save made before the board existed starts the canon board on its own day. Without
    /// <paramref name="couriers"/> slow moves travel unseen. <paramref name="seatPersona"/> is who holds a seat in the world
    /// now (<see cref="DaggerfallOfficeLedger.ResolveBoardSeatPersona"/>). The board binds <paramref name="liege"/>, so the
    /// world reads each region's liege from its allegiance.
    /// </summary>
    internal DaggerfallPoliticalBoard(DaggerfallPoliticalCanon canon, DaggerfallBoardSeatSet seats, DaggerfallRegionLiege liege,
        Func<string, DaggerfallSeatLeaderPersona> seatPersona, DaggerfallWorldConditions conditions, IReadOnlyList<DaggerfallSiteRecord> sites,
        DaggerfallCourierDispatches? couriers, long day, DaggerfallPoliticalBoardSave? saved,
        DaggerfallOfficeLedger? officeLedger = null)
    {
        _canon = canon;
        _seats = seats;
        _liege = liege;
        _seatPersona = seatPersona;
        _conditions = conditions;
        _sites = sites;
        _couriers = couriers;
        _officeLedger = officeLedger;
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
            foreach (JsonElement turn in saved.RecentTurns.EnumerateArray())
                _recentTurns.Add(TurnTrace.FromRecordJson(turn.GetRawText(), "the political board's saved turns"));
            for (int index = 0; index < _recentTurns.Count; index++)
                if (_recentTurns[index].Turn != _board.Turn - _recentTurns.Count + 1 + index)
                    throw new ArgumentException(
                        $"The political board's saved turns must be the {_recentTurns.Count} turns up to its turn {_board.Turn}; turn {_recentTurns[index].Turn} is out of place.",
                        nameof(saved));
        }
        _news = new DaggerfallPoliticalNews(_board, canon.Cards, Town, Faction);
        liege.Bind(_board);
        Rebuild();
        conditions.BindExternalSource(TokensFor);
        couriers?.BindBoardMoveCallback(CourierSettled);
    }

    /// <summary>Binds the office ledger so weekly board holder, regency, and conquest changes write through <see cref="DaggerfallOfficeLedger.OfficeChanged"/>.</summary>
    internal void BindOfficeLedger(DaggerfallOfficeLedger officeLedger) =>
        _officeLedger = officeLedger ?? throw new ArgumentNullException(nameof(officeLedger));

    internal Board Board => _board;

    /// <summary>The day the board last played a turn on (or, before its first, the Sunday before the game began).</summary>
    internal long LastTurnDay => _lastTurnDay;

    /// <summary>The day the next turn plays on.</summary>
    internal long NextTurnDay => _lastTurnDay + DaysPerWeek;

    /// <summary>The trace of the latest turn kept (the nudges it applied among it); null before the first.</summary>
    internal TurnTrace? LatestTrace => _recentTurns.Count > 0 ? _recentTurns[^1] : null;

    /// <summary>The latest turns played, up to <see cref="RecentTurnsKept"/>, oldest first, for the developer tools; saved with the board.</summary>
    internal IReadOnlyList<TurnTrace> RecentTurns => _recentTurns;

    /// <summary>The courier riding with board move <paramref name="moveId"/>, if one was sent and is still on the books.</summary>
    internal DaggerfallCourierDispatchSave? CourierFor(string moveId) =>
        _couriers is not null && _couriers.TryGet(moveId, out DaggerfallCourierDispatchSave? dispatch) ? dispatch : null;

    /// <summary>Who holds seat <paramref name="seat"/>'s office in the world now.</summary>
    internal DaggerfallSeatLeaderPersona WorldHolder(string seat) => _seatPersona(seat);

    /// <summary>The name of seat card or World card <paramref name="id"/>; a gather or a withdrawal, which are no cards, by its id.</summary>
    internal string CardName(string id) =>
        _canon.Cards.Cards.FirstOrDefault(card => card.Id == id)?.Name ?? _canon.Cards.World?.FirstOrDefault(card => card.Id == id)?.Name ?? id;

    /// <summary>
    /// Plays every turn due by <paramref name="day"/>: one for each Sunday from the last played to it, and returns the
    /// news they made (<see cref="DaggerfallPoliticalNews"/>). The counterplay <paramref name="drainCounterplay"/> hands
    /// over (<see cref="DaggerfallConditionCounterplay.ConsumeBoardEvents"/>) is applied at the start of each, where it
    /// eased a board token. The couriers must have travelled up to each Sunday before its turn (the session steps an
    /// interval Sunday by Sunday), or one that arrived in time would be taken as late.
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
            ApplyBoardOfficeChanges(trace, checked((int)_lastTurnDay), _lastTurnDay * MinutesPerDay);
            SendCouriers(trace);
            _recentTurns.Add(trace);
            if (_recentTurns.Count > RecentTurnsKept) _recentTurns.RemoveAt(0);
            news.AddRange(_news.Of(trace, warsBefore, _lastTurnDay * MinutesPerDay));
            ForgetSettledCouriers();
            Rebuild();
        }
        return news;
    }

    internal DaggerfallPoliticalBoardSave Capture()
    {
        using JsonDocument board = JsonDocument.Parse(_board.Save().ToJson());
        using JsonDocument turns = JsonDocument.Parse($"[{string.Join(',', _recentTurns.Select(trace => trace.ToRecordJson()))}]");
        return new DaggerfallPoliticalBoardSave(_lastTurnDay, board.RootElement.Clone(), turns.RootElement.Clone());
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
    /// local office in a region's chief town that a seat holds on the board goes back to local people. A change the
    /// board made itself (<see cref="DaggerfallOfficeChangeCause.CardMove"/>) is already on the board and is not heard back.
    /// </summary>
    internal void ObserveOfficeChanged(DaggerfallOfficeChangedNotification change)
    {
        if (change.Cause == DaggerfallOfficeChangeCause.CardMove) return;
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

    /// <summary>
    /// Propagates non-nudge board-driven seat holder changes, severe court crises (regency), and forceful regional
    /// conquests/cessions (deposing the regional ruler) into <see cref="DaggerfallOfficeLedger"/> with
    /// <see cref="DaggerfallOfficeChangeCause.CardMove"/> so <see cref="ObserveOfficeChanged"/> never echoes them back.
    /// Peaceful annexations keep the regional lord (suzerainty) and local office challenges belong to #11127.
    /// </summary>
    private void ApplyBoardOfficeChanges(TurnTrace trace, int day, long minute)
    {
        if (_officeLedger is null)
            return;

        ApplyBoardChangeSlice(trace.Upkeep, forcefulConquest: false, day, minute);
        if (trace.World is { } world)
            ApplyBoardChangeSlice(world.Changes, forcefulConquest: false, day, minute);
        foreach (ArrivalTrace arrival in trace.Arrivals)
        {
            if (arrival.Outcome != MoveOutcome.Resolved) continue;
            ApplyBoardChangeSlice(arrival.Changes, IsConquestCard(arrival.Card), day, minute);
        }
        foreach (MoveTrace move in trace.Moves)
            ApplyBoardChangeSlice(move.Changes, IsConquestCard(move.Card), day, minute);
        ApplyBoardChangeSlice(trace.Wars, forcefulConquest: true, day, minute);
    }

    private void ApplyBoardChangeSlice(IReadOnlyList<BoardChange> changes, bool forcefulConquest, int day, long minute)
    {
        foreach (BoardChange change in changes)
        {
            // 1. Direct seat holder changes initiated on the board (never from world nudges).
            if (string.Equals(change.Field, "holder", StringComparison.Ordinal)
                && _board.HasSeat(change.Subject)
                && _seats.TryGetBySeatId(change.Subject, out DaggerfallBoardSeatBinding? seatBinding)
                && seatBinding is not null)
            {
                if (change.To.StartsWith("vacant:", StringComparison.Ordinal))
                {
                    _officeLedger!.DeposeBoardSeat(
                        seatBinding.SeatId,
                        DaggerfallOfficeChangeCause.CardMove,
                        day,
                        "was unseated on the political board",
                        minute);
                }
                else if (change.To.StartsWith("regent:", StringComparison.Ordinal))
                {
                    _officeLedger!.DeclareRegencyForBoardSeat(
                        seatBinding.SeatId,
                        DaggerfallOfficeChangeCause.CardMove,
                        day,
                        "holds interim regency by court decree",
                        minute,
                        exileIncumbent: true);
                }
                else
                {
                    _officeLedger!.SetBoardSeatHolder(
                        seatBinding.SeatId,
                        holderActorId: null,
                        holderCastId: change.To,
                        holderFactionId: null,
                        holderDisplayName: null,
                        DaggerfallOfficeChangeCause.CardMove,
                        day,
                        "assumed leadership of the seat",
                        currentMinute: minute);
                }
                SyncBoardSeatHolder(seatBinding.SeatId);
                continue;
            }

            // 2. Severe court crisis on a crown seat (HostageTreaty from defeat/demand-hostages, OR compounded court
            //    paralysis from 2+ simultaneous court plots) places the seat under interim regency when it is not
            //    already vacant or in regency.
            if (change.Field.StartsWith("court:", StringComparison.Ordinal)
                && !string.Equals(change.To, "none", StringComparison.Ordinal)
                && _board.HasSeat(change.Subject)
                && _seats.TryGetBySeatId(change.Subject, out DaggerfallBoardSeatBinding? courtSeat)
                && courtSeat is not null)
            {
                string officeKey = DaggerfallOfficeLedger.FactionOfficeKey(courtSeat.OfficeFactionId);
                DaggerfallOfficeOverrideSave? currentOver = _officeLedger!.TryGetOverride(officeKey);
                if (currentOver is { Vacant: true } or { IsRegency: true })
                    continue;
                Seat seat = _board.Seat(change.Subject);
                CourtCondition? hostage = seat.Court.FirstOrDefault(c => c.Kind == CourtConditionKind.HostageTreaty);
                bool compoundedCrisis = seat.Court.Count >= 2;
                if (hostage is not null || compoundedCrisis)
                {
                    string reason = hostage is not null
                        ? $"holds interim regency under hostage treaty with {_board.Seat(hostage.Source).Name}"
                        : "holds interim regency during compounded court paralysis";
                    _officeLedger.DeclareRegencyForBoardSeat(
                        courtSeat.SeatId,
                        DaggerfallOfficeChangeCause.CardMove,
                        day,
                        reason,
                        minute,
                        exileIncumbent: false);
                    SyncBoardSeatHolder(courtSeat.SeatId);
                }
                continue;
            }

            // 3. Forceful regional takeover (Conquer siege card or Wars treaty cession) deposes the province ruler
            //    into exile through the shared DeposeRegionalRuler caller (reused by #11143). Peaceful Annex keeps the lord.
            if (forcefulConquest
                && string.Equals(change.Field, "allegiance", StringComparison.Ordinal)
                && !string.Equals(change.To, "independent", StringComparison.Ordinal)
                && _board.HasRegion(change.Subject))
            {
                Region conquered = _board.Region(change.Subject);
                string victorName = _board.HasSeat(change.To) ? _board.Seat(change.To).Name : change.To;
                DaggerfallOfficeOverrideSave? deposed = _officeLedger!.DeposeRegionalRuler(
                    conquered.CatalogRegion,
                    DaggerfallOfficeChangeCause.CardMove,
                    day,
                    $"was deposed in the conquest of {conquered.Name} by {victorName}",
                    minute);
                if (deposed?.OfficeFactionId is int conqueredFaction
                    && _seats.TryGetByFactionId(conqueredFaction, out DaggerfallBoardSeatBinding? conqueredSeat)
                    && conqueredSeat is not null)
                {
                    SyncBoardSeatHolder(conqueredSeat.SeatId);
                }
            }
        }
    }

    private void SyncBoardSeatHolder(string seatId)
    {
        if (!_board.HasSeat(seatId)) return;
        string holder = HolderOf(_seatPersona(seatId));
        _board.SyncHolder(_board.Seat(seatId), holder);
    }

    private bool IsConquestCard(string cardId) =>
        _canon.Cards.Cards.FirstOrDefault(c => string.Equals(c.Id, cardId, StringComparison.Ordinal))
            ?.Effects.Any(e => e.Kind == CardEffectKind.Conquer) == true;

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
        // A region cut off from food shows it from its first week, more sharply the longer it lasts.
        foreach (Region region in _board.Regions.Where(region => region.Cut))
        {
            (string headline, string body) = DaggerfallBoardConditionWords.CutOff(region, region.CutWeeks);
            Add(region.CatalogRegion, new DaggerfallWorldConditionToken(
                $"{TokenPrefix}{region.Id}:{CutOffTag}",
                DaggerfallWorldConditionKind.GrainShortage,
                Region: region.CatalogRegion,
                SubjectTag: CutOffTag,
                Severity: Math.Clamp(1 + region.CutWeeks / Supply.CutWeeks, DaggerfallWorldConditionToken.MinSeverity, DaggerfallWorldConditionToken.MaxSeverity),
                StartedDay: Day(_board.Turn - region.CutWeeks + 1),
                Headline: headline,
                Body: body).Validate());
        }
        // A crown's court conditions show in its capital, issued by the crown itself, so a plot never names its plotter.
        foreach (Seat seat in _board.Seats.Where(seat => seat.Home is not null))
        {
            Region capital = _board.Region(seat.Home!);
            foreach (CourtCondition condition in seat.Court)
            {
                (string headline, string body) = DaggerfallBoardConditionWords.Court(_board, seat, capital, condition);
                string tag = CourtTag(condition.Kind);
                Add(capital.CatalogRegion, new DaggerfallWorldConditionToken(
                    $"{TokenPrefix}{capital.Id}:{tag}:{condition.Source}",
                    DaggerfallWorldConditionKind.Decree,
                    Region: capital.CatalogRegion,
                    SourceFactionId: Faction(seat.Id),
                    TargetFactionId: condition.Kind == CourtConditionKind.HostageTreaty ? Faction(condition.Source) : null,
                    SubjectTag: tag,
                    Severity: Math.Clamp(condition.Magnitude, DaggerfallWorldConditionToken.MinSeverity, DaggerfallWorldConditionToken.MaxSeverity),
                    StartedDay: Day(condition.Start),
                    Headline: headline,
                    Body: body).Validate());
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
            ConditionKind.PriceSurge => (DaggerfallWorldConditionKind.Decree, condition.Magnitude, Faction(condition.Source), null, DaggerfallConditionPrices.PriceSurgeTag),
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
        (string Headline, string Body)? words = DaggerfallBoardConditionWords.Of(_board, region, condition);
        return new DaggerfallWorldConditionToken(id, token.kind,
            Region: region.CatalogRegion,
            SourceFactionId: token.source,
            TargetFactionId: token.target,
            SubjectTag: token.tag,
            Severity: Math.Clamp(token.severity, DaggerfallWorldConditionToken.MinSeverity, DaggerfallWorldConditionToken.MaxSeverity),
            StartedDay: Day(condition.Start),
            Headline: words?.Headline,
            Body: words?.Body).Validate();
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

    /// <summary>
    /// The seat whose office faction is <paramref name="factionId"/> or one above it, through each province's liege: a
    /// quest for an annexed region's court is the new liege's.
    /// </summary>
    private string? SeatOfFaction(int factionId)
    {
        foreach (DaggerfallFactionDefinition faction in _liege.Lineage(factionId))
            if (_seats.TryGetByFactionId(faction.Id, out DaggerfallBoardSeatBinding? binding) && _board.HasSeat(binding!.SeatId))
                return binding.SeatId;
        return null;
    }

    /// <summary>The office faction of seat <paramref name="seat"/>, or null for none (the world laid it).</summary>
    private int? Faction(string? seat) =>
        seat is not null && _seats.TryGetBySeatId(seat, out DaggerfallBoardSeatBinding? binding) ? binding!.OfficeFactionId : null;

    private int Day(int turn) => checked((int)Math.Max(0, TurnDay(turn)));

    /// <summary>The tag a court condition's token carries.</summary>
    private static string CourtTag(CourtConditionKind kind) => kind switch
    {
        CourtConditionKind.ParanoidCourt => ParanoidCourtTag,
        CourtConditionKind.HostageTreaty => HostageTreatyTag,
        _ => DaggerfallConditionPrices.CorruptAdvisorTag,
    };

    /// <summary>A board condition kind in token ids and tags: <c>MonsterSurge</c> is <c>monster-surge</c>.</summary>
    private static string Slug(ConditionKind kind) =>
        string.Concat(kind.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? $"-{char.ToLowerInvariant(c)}" : char.ToLowerInvariant(c).ToString()));
}
