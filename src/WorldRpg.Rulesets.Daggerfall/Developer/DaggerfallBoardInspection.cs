using System.Text.Json;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.World;
using BoardSeat = WorldRpg.Politics.Seat;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Live developer inspection of the political board the session runs (<c>board.state</c>, <c>board.seat</c>,
/// <c>board.turns</c>, <c>board.moves</c>), and <c>board.allegiance</c>, which hands a region to a seat at once for
/// checking what follows a liege. Reads the board owner on demand and owns no state of its own.
/// </summary>
internal sealed class DaggerfallBoardInspection(Func<DaggerfallPoliticalBoard?> politicalBoard, Func<DaggerfallCalendar> calendar)
    : IDeveloperBoardCommands
{
    public DebugCommandResult Allegiance(string region, string seat)
    {
        if (politicalBoard() is not { } owner) return DaggerfallDeveloperReplies.Refused("No political board runs in this session.");
        Board board = owner.Board;
        if (!board.HasRegion(region))
            return DaggerfallDeveloperReplies.Refused($"No board region '{region}'. Regions: {string.Join(", ", board.Regions.Select(held => held.Id))}.");
        if (seat != "none" && !board.HasSeat(seat))
            return DaggerfallDeveloperReplies.Refused($"No board seat '{seat}'. Seats: none, {string.Join(", ", board.Seats.Select(held => held.Id))}.");
        Region held = board.Region(region);
        if (held.Capital) return DaggerfallDeveloperReplies.Refused($"{held.Name} is a capital and keeps its crown.");
        board.SetAllegiance(held, seat == "none" ? null : board.Seat(seat));
        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            w.WriteString("region", held.Id);
            w.WriteString("allegiance", held.Allegiance);
            w.WriteEndObject();
        });
    }

    public DebugCommandResult SendConvoy()
    {
        if (politicalBoard() is not { } owner) return DaggerfallDeveloperReplies.Refused("No political board runs in this session.");
        Board board = owner.Board;
        if (!board.Agreements.Any(deal => deal.Gives.IsGrain || deal.Asks.IsGrain))
        {
            TradeTerm coin = TradeTerm.Parse("coin:1/4")!;
            (BoardSeat Giver, BoardSeat Receiver, TradeTerm Grain)? pair = board.Seats
                .SelectMany(giver => board.Seats.Where(receiver => receiver != giver).Select(receiver => (giver, receiver)))
                .Select(seats => Trade.Resolve(board, TradeTerm.Parse("grain")!, seats.giver, seats.receiver) is { } grain
                    && Trade.Fits(board, seats.receiver, seats.giver, coin, grain) ? (seats.giver, seats.receiver, grain) : ((BoardSeat, BoardSeat, TradeTerm)?)null)
                .FirstOrDefault(found => found is not null);
            if (pair is not { } struck) return DaggerfallDeveloperReplies.Refused("No two seats can trade grain now.");
            board.AddAgreement(Trade.Offer(board, struck.Receiver, struck.Giver, coin, struck.Grain));
        }
        IReadOnlyList<DaggerfallRationConvoySave> sent = owner.SendConvoys();
        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartArray();
            foreach (DaggerfallRationConvoySave convoy in sent)
            {
                w.WriteStartObject();
                w.WriteString("band", convoy.BandId);
                w.WriteString("giver", convoy.Giver);
                w.WriteString("receiver", convoy.Receiver);
                w.WriteString("origin", $"{convoy.Origin.Region}/{convoy.Origin.Index}");
                w.WriteString("destination", $"{convoy.Destination.Region}/{convoy.Destination.Index}");
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    public DebugCommandResult State()
    {
        if (politicalBoard() is not { } owner) return DaggerfallDeveloperReplies.Refused("No political board runs in this session.");
        Board board = owner.Board;
        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            WriteClock(w, owner);
            w.WriteString("leader", board.Leader);

            w.WriteStartArray("seats");
            foreach (BoardSeat seat in board.Seats)
            {
                w.WriteStartObject();
                w.WriteString("id", seat.Id);
                w.WriteString("name", seat.Name);
                w.WriteString("kind", seat.Kind.ToString());
                w.WriteString("holder", seat.Holder);
                w.WriteString("playsAs", seat.PlaysAs);
                w.WriteString("worldHolder", WorldHolderName(owner, seat.Id));
                w.WriteNumber("prestige", Prestige.Total(board, seat));
                w.WriteNumber("coin", seat.Coin);
                w.WriteNumber("favour", seat.Favour);
                w.WriteNumber("force", seat.Force);
                w.WriteString("plan", seat.Plan?.ToString());
                w.WriteString("home", seat.Home);
                w.WriteString("reserves", seat.Reserves);
                w.WriteNumber("contestedWeeks", seat.ContestedWeeks);
                w.WriteBoolean("reservesCut", Balance.ReservesCut(seat));
                w.WriteBoolean("arbiter", seat.Arbiter);
                w.WriteNumber("piecesLeft", board.PiecesLeft(seat));
                w.WriteNumber("grudges", seat.Grudges.Count);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("relations");
            for (int a = 0; a < board.Seats.Count; a++)
                for (int b = a + 1; b < board.Seats.Count; b++)
                    if (board.Relation(board.Seats[a].Id, board.Seats[b].Id) is var relation and not 0)
                    {
                        w.WriteStartObject();
                        w.WriteString("a", board.Seats[a].Id);
                        w.WriteString("b", board.Seats[b].Id);
                        w.WriteNumber("value", relation);
                        w.WriteEndObject();
                    }
            w.WriteEndArray();

            w.WriteStartArray("wars");
            foreach (War war in board.Wars)
            {
                w.WriteStartObject();
                w.WriteString("id", war.Id);
                w.WriteString("a", war.A);
                w.WriteString("b", war.B);
                w.WriteString("aim", war.Aim.ToString());
                Strings(w, "alliesOfA", war.AlliesOfA);
                Strings(w, "alliesOfB", war.AlliesOfB);
                w.WriteNumber("started", war.Started);
                w.WriteNumber("wearinessOfA", war.WearinessOfA);
                w.WriteNumber("wearinessOfB", war.WearinessOfB);
                if (war.FirstLoss is int loss) w.WriteNumber("firstLoss", loss); else w.WriteNull("firstLoss");
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("truces");
            foreach (Truce truce in board.Truces)
            {
                w.WriteStartObject();
                w.WriteString("a", truce.A);
                w.WriteString("b", truce.B);
                w.WriteNumber("until", truce.Until);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("feuds");
            foreach (Feud feud in board.Feuds)
            {
                w.WriteStartObject();
                w.WriteString("id", feud.Id);
                w.WriteString("a", feud.A);
                w.WriteString("b", feud.B);
                w.WriteNumber("started", feud.Started);
                w.WriteNumber("heat", feud.Heat);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("agreements");
            foreach (Agreement deal in board.Agreements)
            {
                w.WriteStartObject();
                w.WriteString("id", deal.Id);
                w.WriteString("proposer", deal.Proposer);
                w.WriteString("gives", deal.Gives.ToString());
                w.WriteString("partner", deal.Partner);
                w.WriteString("asks", deal.Asks.ToString());
                w.WriteNumber("start", deal.Start);
                w.WriteNumber("end", deal.End);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("regions");
            foreach (Region region in board.Regions)
            {
                w.WriteStartObject();
                w.WriteString("id", region.Id);
                w.WriteString("name", region.Name);
                w.WriteNumber("catalogRegion", region.CatalogRegion);
                w.WriteBoolean("capital", region.Capital);
                w.WriteString("allegiance", region.Allegiance);
                w.WriteNumber("control", region.Control);
                w.WriteNumber("prosperity", region.Prosperity);
                w.WriteNumber("order", region.Order);
                w.WriteString("supply", region.Supply.ToString());
                w.WriteNumber("cutWeeks", region.CutWeeks);
                w.WriteStartObject("influence");
                foreach ((string seat, int pieces) in region.Influence) w.WriteNumber(seat, pieces);
                w.WriteEndObject();
                w.WriteStartObject("offices");
                foreach ((Office office, string seat) in region.Offices) w.WriteString(office.ToString(), seat);
                w.WriteEndObject();
                w.WriteStartArray("conditions");
                foreach (RegionCondition condition in region.Conditions)
                {
                    w.WriteStartObject();
                    w.WriteString("kind", condition.Kind.ToString());
                    w.WriteString("detail", condition.Detail);
                    w.WriteNumber("magnitude", condition.Magnitude);
                    w.WriteString("source", condition.Source);
                    w.WriteNumber("end", condition.End);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteString("town", owner.Town(region.Id)?.Name);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("arbiters");
            foreach (BoardSeat seat in board.Seats.Where(seat => seat.Arbiter))
            {
                w.WriteStartObject();
                w.WriteString("seat", seat.Id);
                w.WriteNumber("arbitration", Balance.Arbitration(board, seat));
                w.WriteNumber("warsInForce", board.Wars.Count);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("pending");
            foreach (PendingMove move in board.Pending)
            {
                w.WriteStartObject();
                w.WriteString("id", move.Id);
                w.WriteString("seat", move.Seat);
                w.WriteString("card", move.Card.Id);
                Strings(w, "targets", move.Targets);
                w.WriteNumber("played", move.Played);
                w.WriteNumber("arrives", move.Arrives);
                WriteCourier(w, owner.CourierFor(move.Id));
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("queued");
            foreach (WorldNudge nudge in board.Queued) w.WriteStringValue(Describe(nudge));
            w.WriteEndArray();

            w.WriteStartArray("arcs");
            foreach (OpenArc arc in board.Arcs)
            {
                w.WriteStartObject();
                w.WriteString("kind", arc.Kind.ToString());
                w.WriteString("key", arc.Key);
                Strings(w, "subjects", arc.Subjects);
                w.WriteNumber("started", arc.Started);
                w.WriteBoolean("peaked", arc.Peaked);
                if (arc.Settles is int settles) w.WriteNumber("settles", settles); else w.WriteNull("settles");
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    public DebugCommandResult Seat(string seat)
    {
        if (politicalBoard() is not { } owner) return DaggerfallDeveloperReplies.Refused("No political board runs in this session.");
        Board board = owner.Board;
        if (string.IsNullOrWhiteSpace(seat) || !board.HasSeat(seat.Trim()))
            return DaggerfallDeveloperReplies.Refused($"No board seat '{seat}'. Seats: {string.Join(", ", board.Seats.Select(held => held.Id))}.");
        BoardSeat chosen = board.Seat(seat.Trim());
        Persona persona = board.Persona(chosen);
        SeatDeck deck = board.Deck(chosen);
        DaggerfallSeatLeaderPersona world = owner.WorldHolder(chosen.Id);
        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            WriteClock(w, owner);
            w.WriteString("id", chosen.Id);
            w.WriteString("name", chosen.Name);
            w.WriteString("kind", chosen.Kind.ToString());
            w.WriteString("holder", chosen.Holder);
            w.WriteString("playsAs", chosen.PlaysAs);
            w.WriteStartObject("world");
            w.WriteString("displayName", world.IsVacant ? null : world.DisplayName);
            w.WriteString("castId", world.HolderCastId);
            w.WriteBoolean("vacant", world.IsVacant);
            w.WriteNumber("officeFactionId", world.OfficeFactionId);
            w.WriteEndObject();

            w.WriteStartObject("persona");
            w.WriteStartObject("goals");
            foreach ((GoalKind kind, int weight) in persona.Goals.OrderBy(entry => entry.Key)) w.WriteNumber(kind.ToString(), weight);
            w.WriteEndObject();
            w.WriteStartObject("families");
            foreach ((CardFamily family, int leaning) in (persona.Families ?? new Dictionary<CardFamily, int>()).OrderBy(entry => entry.Key))
                w.WriteNumber(family.ToString(), leaning);
            w.WriteEndObject();
            w.WriteNumber("risk", persona.Risk);
            w.WriteEndObject();

            w.WriteStartArray("agenda");
            foreach (Goal goal in chosen.Agenda) w.WriteStringValue(goal.ToString());
            w.WriteEndArray();
            if (chosen.Plan is { } plan)
            {
                w.WriteStartObject("plan");
                w.WriteString("goal", plan.Goal.ToString());
                w.WriteNumber("since", plan.Since);
                w.WriteNumber("baseline", plan.Baseline);
                w.WriteEndObject();
            }
            else w.WriteNull("plan");

            w.WriteNumber("prestige", Prestige.Total(board, chosen));
            w.WriteNumber("coin", chosen.Coin);
            w.WriteNumber("favour", chosen.Favour);
            w.WriteNumber("force", chosen.Force);
            w.WriteStartArray("grants");
            foreach (SeatGrant grant in chosen.Grants)
            {
                w.WriteStartObject();
                w.WriteString("resource", grant.Resource.ToString());
                w.WriteNumber("amount", grant.Amount);
                w.WriteNumber("everyWeeks", grant.EveryWeeks);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("reserves", chosen.Reserves);
            w.WriteNumber("contestedWeeks", chosen.ContestedWeeks);
            w.WriteBoolean("reservesCut", Balance.ReservesCut(chosen));
            w.WriteBoolean("arbiter", chosen.Arbiter);

            w.WriteStartArray("grudges");
            foreach (Grudge grudge in chosen.Grudges)
            {
                w.WriteStartObject();
                w.WriteString("against", grudge.Against);
                w.WriteNumber("until", grudge.Until);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("others");
            foreach (BoardSeat other in board.Seats.Where(other => other != chosen))
            {
                w.WriteStartObject();
                w.WriteString("seat", other.Id);
                w.WriteNumber("relation", board.Relation(chosen.Id, other.Id));
                w.WriteNumber("helpedThem", board.Helped(chosen, other));
                w.WriteNumber("helpedUs", board.Helped(other, chosen));
                w.WriteBoolean("grudgeHeld", other.Grudges.Any(grudge => grudge.Against == chosen.Id));
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartObject("deck");
            Strings(w, "hand", [.. deck.Hand.Select(card => card.Id)]);
            w.WriteNumber("drawPile", deck.DrawPile.Count);
            w.WriteNumber("discard", deck.Discard.Count);
            w.WriteEndObject();

            w.WriteStartArray("court");
            foreach (CourtCondition condition in chosen.Court)
            {
                w.WriteStartObject();
                w.WriteString("kind", condition.Kind.ToString());
                w.WriteNumber("magnitude", condition.Magnitude);
                w.WriteString("source", condition.Source);
                w.WriteNumber("end", condition.End);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            Strings(w, "boons", chosen.Boons);

            w.WriteNumber("piecesPlaced", board.PiecesPlaced(chosen));
            w.WriteNumber("piecesLeft", board.PiecesLeft(chosen));
            w.WriteNumber("footholds", board.Footholds(chosen));
            Strings(w, "regionsHeld", [.. board.Regions.Where(region => region.Allegiance == chosen.Id).Select(region => region.Id)]);
            w.WriteStartArray("officesHeld");
            foreach (Region region in board.Regions)
                foreach ((Office office, string holder) in region.Offices)
                    if (holder == chosen.Id) w.WriteStringValue($"{region.Id}:{office.ToString().ToLowerInvariant()}");
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    public DebugCommandResult Turns(int count)
    {
        if (politicalBoard() is not { } owner) return DaggerfallDeveloperReplies.Refused("No political board runs in this session.");
        if (count is < 1 or > DaggerfallPoliticalBoard.RecentTurnsKept)
            return DaggerfallDeveloperReplies.Refused($"Ask for 1 to {DaggerfallPoliticalBoard.RecentTurnsKept} turns.");
        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            WriteClock(w, owner);
            w.WriteNumber("kept", owner.RecentTurns.Count);
            w.WriteStartArray("turns");
            foreach (TurnTrace trace in owner.RecentTurns.Reverse().Take(count))
            {
                w.WriteStartObject();
                w.WriteNumber("turn", trace.Turn);
                w.WriteNumber("day", owner.TurnDay(trace.Turn));
                w.WriteStartArray("nudges");
                foreach (NudgeTrace nudge in trace.Nudges)
                {
                    w.WriteStartObject();
                    w.WriteString("nudge", Describe(nudge.Nudge));
                    w.WriteString("outcome", nudge.Outcome.ToString());
                    Changes(w, nudge.Changes);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteStartObject("upkeep");
                Changes(w, trace.Upkeep);
                w.WriteEndObject();
                if (trace.World is { } world)
                {
                    w.WriteStartObject("world");
                    w.WriteString("card", world.Card);
                    w.WriteString("region", world.Region);
                    w.WriteString("actor", world.Actor);
                    Changes(w, world.Changes);
                    w.WriteEndObject();
                }
                else w.WriteNull("world");
                w.WriteStartArray("arrivals");
                foreach (ArrivalTrace arrival in trace.Arrivals)
                {
                    w.WriteStartObject();
                    w.WriteString("id", arrival.Id);
                    w.WriteString("seat", arrival.Seat);
                    w.WriteString("card", arrival.Card);
                    Strings(w, "targets", arrival.Targets);
                    w.WriteString("visibility", arrival.Visibility.ToString());
                    w.WriteString("outcome", arrival.Outcome.ToString());
                    Changes(w, arrival.Changes);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteStartArray("moves");
                foreach (MoveTrace move in trace.Moves)
                {
                    w.WriteStartObject();
                    w.WriteString("id", move.Id);
                    w.WriteString("seat", move.Seat);
                    w.WriteString("plan", move.Plan);
                    w.WriteString("card", move.Card);
                    Strings(w, "targets", move.Targets);
                    w.WriteNumber("score", move.Score);
                    w.WriteString("visibility", move.Visibility.ToString());
                    w.WriteString("origin", move.Origin);
                    if (move.Arrives is int arrives) w.WriteNumber("arrives", arrives); else w.WriteNull("arrives");
                    w.WriteStartArray("alternatives");
                    foreach (MoveOption option in move.Alternatives)
                        w.WriteStringValue($"{option.Card} {string.Join(",", option.Targets)} ({option.Score})");
                    w.WriteEndArray();
                    Changes(w, move.Changes);
                    WriteCourier(w, owner.CourierFor(move.Id));
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteStartObject("wars");
                Changes(w, trace.Wars);
                w.WriteEndObject();
                w.WriteStartArray("chronicle");
                foreach (ArcRecord arc in trace.Chronicle)
                {
                    w.WriteStartObject();
                    w.WriteString("kind", arc.Kind.ToString());
                    w.WriteString("phase", arc.Phase.ToString());
                    Strings(w, "subjects", arc.Subjects);
                    w.WriteString("headlineKey", arc.HeadlineKey);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    public DebugCommandResult Moves()
    {
        if (politicalBoard() is not { } owner) return DaggerfallDeveloperReplies.Refused("No political board runs in this session.");
        Board board = owner.Board;
        IReadOnlyList<TurnTrace> kept = owner.RecentTurns;
        Dictionary<string, (int Turn, MoveOutcome Outcome)> arrived = new(StringComparer.Ordinal);
        Dictionary<string, List<(int Turn, WorldNudge Nudge)>> moveNudges = new(StringComparer.Ordinal);
        Dictionary<int, List<(ArcKind Kind, string Key, string Phase, IReadOnlyList<string> Subjects, int Started)>> arcs = [];
        HashSet<string> opened = new(StringComparer.Ordinal);
        void Arc(int turn, ArcKind kind, string key, string phase, IReadOnlyList<string> subjects, int started)
        {
            if (!arcs.TryGetValue(turn, out var list)) arcs[turn] = list = [];
            list.Add((kind, key, phase, subjects, started));
        }
        foreach (TurnTrace trace in kept)
        {
            foreach (ArrivalTrace arrival in trace.Arrivals) arrived[arrival.Id] = (trace.Turn, arrival.Outcome);
            foreach (NudgeTrace nudge in trace.Nudges.Where(nudge => nudge.Outcome == NudgeOutcome.Applied))
                if (MoveOf(nudge.Nudge) is { } id)
                    (moveNudges.TryGetValue(id, out var list) ? list : moveNudges[id] = []).Add((trace.Turn, nudge.Nudge));
            foreach (ArcRecord arc in trace.Chronicle)
            {
                Arc(arc.Turn, arc.Kind, arc.Key, arc.Phase.ToString(), arc.Subjects, arc.Started);
                if (opened.Add(arc.Key)) Arc(arc.Started, arc.Kind, arc.Key, "Opened", arc.Subjects, arc.Started);
            }
        }
        foreach (OpenArc arc in board.Arcs.Where(arc => opened.Add(arc.Key)))
            Arc(arc.Started, arc.Kind, arc.Key, "Opened", arc.Subjects, arc.Started);

        string Name(string id) => board.HasSeat(id) ? board.Seat(id).Name : board.HasRegion(id) ? board.Region(id).Name : id;
        void Named(Utf8JsonWriter w, string name, IEnumerable<string> ids)
        {
            w.WriteStartArray(name);
            foreach (string id in ids)
            {
                w.WriteStartObject();
                w.WriteString("id", id);
                w.WriteString("name", Name(id));
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        return DaggerfallDeveloperReplies.Json(w =>
        {
            w.WriteStartObject();
            WriteClock(w, owner);
            w.WriteNumber("kept", kept.Count);
            Named(w, "seats", board.Seats.Select(seat => seat.Id));
            w.WriteStartArray("turns");
            foreach (TurnTrace trace in kept.Reverse())
            {
                w.WriteStartObject();
                w.WriteNumber("turn", trace.Turn);
                long day = owner.TurnDay(trace.Turn);
                w.WriteNumber("day", day);
                w.WriteString("date", Date(day));
                if (trace.World is { } world)
                {
                    w.WriteStartObject("world");
                    w.WriteString("card", owner.CardName(world.Card));
                    w.WriteString("region", Name(world.Region));
                    w.WriteString("actor", world.Actor);
                    Changes(w, world.Changes);
                    w.WriteEndObject();
                }
                else w.WriteNull("world");

                w.WriteStartArray("nudges");
                foreach (NudgeTrace nudge in trace.Nudges)
                {
                    w.WriteStartObject();
                    w.WriteString("kind", nudge.Nudge.GetType().Name);
                    w.WriteString("nudge", Describe(nudge.Nudge));
                    w.WriteString("move", MoveOf(nudge.Nudge));
                    w.WriteString("seat", SeatOf(board, nudge.Nudge));
                    w.WriteString("outcome", nudge.Outcome.ToString());
                    Changes(w, nudge.Changes);
                    w.WriteEndObject();
                }
                w.WriteEndArray();

                w.WriteStartArray("arrivals");
                foreach (ArrivalTrace arrival in trace.Arrivals)
                {
                    w.WriteStartObject();
                    w.WriteString("id", arrival.Id);
                    w.WriteString("seat", arrival.Seat);
                    w.WriteString("card", arrival.Card);
                    w.WriteString("cardName", owner.CardName(arrival.Card));
                    Named(w, "targets", arrival.Targets);
                    w.WriteString("visibility", arrival.Visibility.ToString());
                    if (PlayedTurn(arrival.Id) is int played) w.WriteNumber("played", played); else w.WriteNull("played");
                    w.WriteString("outcome", arrival.Outcome.ToString());
                    Changes(w, arrival.Changes);
                    w.WriteEndObject();
                }
                w.WriteEndArray();

                w.WriteStartArray("moves");
                foreach (MoveTrace move in trace.Moves)
                {
                    w.WriteStartObject();
                    w.WriteString("id", move.Id);
                    w.WriteString("seat", move.Seat);
                    w.WriteString("card", move.Card);
                    w.WriteString("cardName", owner.CardName(move.Card));
                    Named(w, "targets", move.Targets);
                    w.WriteString("visibility", move.Visibility.ToString());
                    w.WriteString("plan", move.Plan);
                    w.WriteString("origin", move.Origin is null ? null : Name(move.Origin));
                    Changes(w, move.Changes);
                    WriteFate(w, owner, move, trace.Turn, arrived, moveNudges);
                    w.WriteEndObject();
                }
                w.WriteEndArray();

                w.WriteStartArray("arcs");
                foreach (var arc in arcs.GetValueOrDefault(trace.Turn) ?? [])
                {
                    w.WriteStartObject();
                    w.WriteString("kind", arc.Kind.ToString());
                    w.WriteString("phase", arc.Phase);
                    w.WriteString("key", arc.Key);
                    Named(w, "subjects", arc.Subjects);
                    w.WriteNumber("started", arc.Started);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    /// <summary>
    /// How <paramref name="move"/>, played in turn <paramref name="played"/>, came out, as far as the kept turns and the board tell: a quick move landed when
    /// played; a slow one is under way (with its courier), or arrived, failed on arrival or was intercepted, with the
    /// delays and early delivery the world's nudges gave it on the way.
    /// </summary>
    private static void WriteFate(Utf8JsonWriter w, DaggerfallPoliticalBoard owner, MoveTrace move, int played,
        Dictionary<string, (int Turn, MoveOutcome Outcome)> arrived, Dictionary<string, List<(int Turn, WorldNudge Nudge)>> moveNudges)
    {
        IReadOnlyList<(int Turn, WorldNudge Nudge)> nudges = moveNudges.GetValueOrDefault(move.Id) ?? [];
        PendingMove? pending = owner.Board.Pending.FirstOrDefault(held => held.Id == move.Id);
        (string state, int? turn) = move.Arrives is null
            ? (move.Card == Gathering.Id ? "Gathered" : "Landed", played)
            : nudges.FirstOrDefault(nudge => nudge.Nudge is MoveIntercepted) is { Nudge: not null } intercepted ? ("Intercepted", intercepted.Turn)
            : arrived.TryGetValue(move.Id, out var arrival) ? (arrival.Outcome == MoveOutcome.Resolved ? "Arrived" : "Failed", arrival.Turn)
            : pending is not null ? ("UnderWay", pending.Arrives)
            : ("Gone", (int?)null);
        w.WriteStartObject("fate");
        w.WriteString("state", state);
        if (turn is int at) w.WriteNumber("turn", at); else w.WriteNull("turn");
        if (move.Arrives is int due) w.WriteNumber("due", due); else w.WriteNull("due");
        w.WriteStartArray("delays");
        foreach ((int at2, WorldNudge nudge) in nudges)
            if (nudge is MoveDelayed delayed)
            {
                w.WriteStartObject();
                w.WriteNumber("turn", at2);
                w.WriteNumber("weeks", delayed.Weeks);
                w.WriteEndObject();
            }
        w.WriteEndArray();
        if (nudges.FirstOrDefault(nudge => nudge.Nudge is MoveDelivered) is { Nudge: not null } delivered) w.WriteNumber("deliveredEarly", delivered.Turn);
        else w.WriteNull("deliveredEarly");
        WriteCourier(w, owner.CourierFor(move.Id));
        w.WriteEndObject();
    }

    /// <summary>The move a nudge speaks of: a courier's delivery, delay or interception.</summary>
    private static string? MoveOf(WorldNudge nudge) => nudge switch
    {
        MoveDelivered delivered => delivered.MoveId,
        MoveDelayed delayed => delayed.MoveId,
        MoveIntercepted intercepted => intercepted.MoveId,
        _ => null,
    };

    /// <summary>The seat a nudge concerns, where it names one: the seat a quest, a holder or a boon is for, or a courier's mover.</summary>
    private static string? SeatOf(Board board, WorldNudge nudge) => nudge switch
    {
        QuestOutcome quest => quest.Seat,
        HolderChanged holder => holder.Seat,
        MainQuestBoon boon => boon.Seat,
        _ => MoveOf(nudge) is { } id && id.IndexOf('-', StringComparison.Ordinal) is > 0 and var dash && board.HasSeat(id[(dash + 1)..]) ? id[(dash + 1)..] : null,
    };

    /// <summary>The turn a move id (<c>turn-seat</c>) was played in.</summary>
    private static int? PlayedTurn(string moveId) =>
        moveId.IndexOf('-', StringComparison.Ordinal) is > 0 and var dash
        && int.TryParse(moveId.AsSpan(0, dash), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int turn)
            ? turn : null;

    private static string Date(long day)
    {
        DaggerfallCalendar date = DaggerfallCalendar.FromAbsoluteSeconds(day * DaggerfallCalendar.SecondsPerDay);
        return $"{date.Day}/{date.Month}/{date.Year}";
    }

    /// <summary>The board's turn, the calendar's day and the next Sunday the board plays on.</summary>
    private void WriteClock(Utf8JsonWriter w, DaggerfallPoliticalBoard owner)
    {
        w.WriteNumber("turn", owner.Board.Turn);
        w.WriteNumber("day", calendar().DayNumber);
        w.WriteNumber("lastTurnDay", owner.LastTurnDay);
        w.WriteNumber("nextTurnDay", owner.NextTurnDay);
        w.WriteString("nextTurnDate", Date(owner.NextTurnDay));
    }

    private static string? WorldHolderName(DaggerfallPoliticalBoard owner, string seat) =>
        owner.WorldHolder(seat) is { IsVacant: false } held ? held.DisplayName : null;

    private static void WriteCourier(Utf8JsonWriter w, DaggerfallCourierDispatchSave? courier)
    {
        if (courier is null) { w.WriteNull("courier"); return; }
        w.WriteStartObject("courier");
        w.WriteString("status", courier.Status.ToString());
        w.WriteNumber("expectedArrivalMinute", courier.ExpectedArrivalMinute);
        w.WriteEndObject();
    }

    private static void Changes(Utf8JsonWriter w, IReadOnlyList<BoardChange> changes)
    {
        w.WriteStartArray("changes");
        foreach (BoardChange change in changes) w.WriteStringValue($"{change.Subject} {change.Field}: {change.From} -> {change.To}");
        w.WriteEndArray();
    }

    private static void Strings(Utf8JsonWriter w, string name, IEnumerable<string> values)
    {
        w.WriteStartArray(name);
        foreach (string value in values) w.WriteStringValue(value);
        w.WriteEndArray();
    }

    /// <summary>A nudge as a line: its kind and its fields.</summary>
    private static string Describe(WorldNudge nudge) => nudge switch
    {
        QuestOutcome quest => $"QuestOutcome {quest.Seat} [{string.Join(",", quest.Targets)}] {string.Join(", ", quest.Effects.Select(Effect))}",
        MainQuestBoon boon => $"MainQuestBoon {boon.Seat} {boon.Boon} [{string.Join(",", boon.Targets)}] {string.Join(", ", boon.Effects.Select(Effect))}",
        MoveDelivered delivered => $"Courier delivered move {delivered.MoveId} early",
        MoveDelayed delayed => $"Courier late: move {delayed.MoveId} delayed {delayed.Weeks} week{(delayed.Weeks == 1 ? "" : "s")}",
        MoveIntercepted intercepted => $"Courier seized: move {intercepted.MoveId} intercepted",
        _ => nudge.ToString(),
    };

    private static string Effect(CardEffect effect) =>
        $"{effect.Kind} {effect.Amount}{(effect.Resource is { } resource ? $" {resource}" : "")}";
}
