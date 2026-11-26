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
/// </summary>
internal sealed class DaggerfallPoliticalBoard
{
    internal const int DaysPerWeek = 7;
    private const long MinutesPerDay = 24 * 60;
    internal const string TokenPrefix = "board:";

    private readonly DaggerfallPoliticalCanon _canon;
    private readonly DaggerfallBoardSeatSet _seats;
    private readonly DaggerfallWorldConditions _conditions;
    private readonly Board _board;
    private readonly DaggerfallPoliticalNews _news;
    private readonly Dictionary<int, List<DaggerfallWorldConditionToken>> _tokens = [];
    private readonly Dictionary<string, (string Region, ConditionKind Kind, string? Detail)> _tokenConditions = new(StringComparer.Ordinal);
    private long _lastTurnDay;

    /// <summary>
    /// The board for a new game starting on <paramref name="day"/> (<paramref name="saved"/> null), or the board a save
    /// carried. A save made before the board existed starts the canon board on its own day.
    /// </summary>
    internal DaggerfallPoliticalBoard(DaggerfallPoliticalCanon canon, DaggerfallBoardSeatSet seats, DaggerfallWorldConditions conditions,
        IReadOnlyList<DaggerfallSiteRecord> sites, long day, DaggerfallPoliticalBoardSave? saved)
    {
        _canon = canon;
        _seats = seats;
        _conditions = conditions;
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
        _news = new DaggerfallPoliticalNews(_board, canon.Cards, sites, Faction);
        Rebuild();
        conditions.BindExternalSource(TokensFor);
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
            TurnTrace trace = BoardTurn.Play(_board);
            _lastTurnDay += DaysPerWeek;
            LatestTrace = trace;
            Latest = BoardOutputs.Of(_board, _canon.Cards, trace);
            news.AddRange(_news.Of(trace, warsBefore, _lastTurnDay * MinutesPerDay));
            Rebuild();
        }
        return news;
    }

    internal DaggerfallPoliticalBoardSave Capture()
    {
        using JsonDocument board = JsonDocument.Parse(_board.Save().ToJson());
        return new DaggerfallPoliticalBoardSave(_lastTurnDay, board.RootElement.Clone());
    }

    /// <summary>The board's tokens on classic region <paramref name="region"/>.</summary>
    internal IReadOnlyList<DaggerfallWorldConditionToken> TokensFor(int region) =>
        _tokens.TryGetValue(region, out List<DaggerfallWorldConditionToken>? tokens) ? tokens : [];

    /// <summary>The day turn <paramref name="turn"/> was played on.</summary>
    internal long TurnDay(int turn) => _lastTurnDay - (long)(_board.Turn - turn) * DaysPerWeek;

    private static long SundayOnOrBefore(long day) => day - ((day % DaysPerWeek) + DaysPerWeek) % DaysPerWeek;

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

    /// <summary>The office faction of seat <paramref name="seat"/>, or null for none (the world laid it).</summary>
    private int? Faction(string? seat) =>
        seat is not null && _seats.TryGetBySeatId(seat, out DaggerfallBoardSeatBinding? binding) ? binding!.OfficeFactionId : null;

    private int Day(int turn) => checked((int)Math.Max(0, TurnDay(turn)));

    /// <summary>A board condition kind in token ids and tags: <c>MonsterSurge</c> is <c>monster-surge</c>.</summary>
    private static string Slug(ConditionKind kind) =>
        string.Concat(kind.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? $"-{char.ToLowerInvariant(c)}" : char.ToLowerInvariant(c).ToString()));
}
