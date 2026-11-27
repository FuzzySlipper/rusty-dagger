using System.Text.Json;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.World;
using BoardSeat = WorldRpg.Politics.Seat;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Live developer inspection of the political board the session runs (<c>board.state</c>, <c>board.seat</c>,
/// <c>board.turns</c>). Reads the board owner on demand and owns no state of its own.
/// </summary>
internal sealed class DaggerfallBoardInspection(Func<DaggerfallPoliticalBoard?> politicalBoard, Func<DaggerfallCalendar> calendar)
    : IDeveloperBoardCommands
{
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

    /// <summary>The board's turn, the calendar's day and the next Sunday the board plays on.</summary>
    private void WriteClock(Utf8JsonWriter w, DaggerfallPoliticalBoard owner)
    {
        w.WriteNumber("turn", owner.Board.Turn);
        w.WriteNumber("day", calendar().DayNumber);
        w.WriteNumber("lastTurnDay", owner.LastTurnDay);
        w.WriteNumber("nextTurnDay", owner.NextTurnDay);
        DaggerfallCalendar next = DaggerfallCalendar.FromAbsoluteSeconds(owner.NextTurnDay * DaggerfallCalendar.SecondsPerDay);
        w.WriteString("nextTurnDate", $"{next.Day}/{next.Month}/{next.Year}");
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
        _ => nudge.ToString(),
    };

    private static string Effect(CardEffect effect) =>
        $"{effect.Kind} {effect.Amount}{(effect.Resource is { } resource ? $" {resource}" : "")}";
}
