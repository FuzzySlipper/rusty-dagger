using System.Globalization;
using System.Text.Json.Serialization;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Coarse regional or Bay-wide political/world condition token consumed by visible world surfaces
/// (notice boards, expedition band staging, gate/service scrutiny, prices, and faction-aware talk/barks).
/// Populated by authored/stubbed tokens now and driven by the weekly political board game (B2) later.
/// </summary>
internal enum DaggerfallWorldConditionKind
{
    /// <summary>Trade disruption or harvest shortage pushing regional prices up and staging supply/raider bands.</summary>
    GrainShortage = 0,

    /// <summary>Military build-up or border dispute tightening gate/watch scrutiny and staging patrol bands.</summary>
    BorderTension = 1,

    /// <summary>Regional monster pressure (orcs, undead, beasts, bandits) shifting band staging and posting bounties.</summary>
    MonsterSurge = 2,

    /// <summary>Heightened watch sweeps or martial curfew tightening gate and lawful service scrutiny.</summary>
    CrimeCrackdown = 3,

    /// <summary>Guild or faction embargo restricting services for outsiders or rival members.</summary>
    GuildEmbargo = 4,

    /// <summary>Contested or vacated throne/major office heightening court/gate scrutiny and postings.</summary>
    SuccessionCrisis = 5,

    /// <summary>Quarantine or contagion scare tightening gate/temple inspection and postings.</summary>
    PlagueScare = 6,

    /// <summary>General authored or board-issued proclamation/bounty posted to regional notice boards.</summary>
    Decree = 7,
}

/// <summary>
/// One sparse world-condition token (<c>region, kind, severity, source/target factions, optional expiry &amp; copy</c>).
/// <see cref="Region"/> is <c>null</c> for Bay-wide conditions or <c>0..61</c> for a specific classic region.
/// </summary>
internal sealed record DaggerfallWorldConditionToken(
    [property: JsonRequired] string TokenId,
    [property: JsonRequired] DaggerfallWorldConditionKind Kind,
    int? Region = null,
    int? SourceFactionId = null,
    int? TargetFactionId = null,
    string? SubjectTag = null,
    int Severity = 1,
    int StartedDay = 0,
    int? ExpiresDay = null,
    string? Headline = null,
    string? Body = null)
{
    internal const int MinSeverity = 1;
    internal const int MaxSeverity = 3;

    internal bool AppliesToRegion(int region) => Region is null || Region == region;

    internal bool IsExpired(int currentDay) => ExpiresDay is int end && currentDay >= end;

    internal DaggerfallWorldConditionToken Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TokenId);
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, $"World condition '{TokenId}' names an invalid kind.");
        if (Region is < 0 or > 61)
            throw new ArgumentOutOfRangeException(nameof(Region), Region, $"World condition '{TokenId}' names no classic region.");
        if (SourceFactionId is < 0)
            throw new ArgumentOutOfRangeException(nameof(SourceFactionId), SourceFactionId, $"World condition '{TokenId}' cannot have a negative source faction.");
        if (TargetFactionId is < 0)
            throw new ArgumentOutOfRangeException(nameof(TargetFactionId), TargetFactionId, $"World condition '{TokenId}' cannot have a negative target faction.");
        if (Severity is < MinSeverity or > MaxSeverity)
            throw new ArgumentOutOfRangeException(nameof(Severity), Severity, $"World condition '{TokenId}' severity must be in [{MinSeverity}, {MaxSeverity}].");
        if (StartedDay < 0)
            throw new ArgumentOutOfRangeException(nameof(StartedDay), StartedDay, $"World condition '{TokenId}' started before day 0.");
        if (ExpiresDay is int exp && exp < StartedDay)
            throw new ArgumentOutOfRangeException(nameof(ExpiresDay), ExpiresDay, $"World condition '{TokenId}' expires before it starts.");
        if (SubjectTag is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(SubjectTag);
        if (Headline is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(Headline);
        if (Body is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(Body);
        return this;
    }

    internal static string KindSlug(DaggerfallWorldConditionKind kind) => kind switch
    {
        DaggerfallWorldConditionKind.GrainShortage => "grain-shortage",
        DaggerfallWorldConditionKind.BorderTension => "border-tension",
        DaggerfallWorldConditionKind.MonsterSurge => "monster-surge",
        DaggerfallWorldConditionKind.CrimeCrackdown => "crime-crackdown",
        DaggerfallWorldConditionKind.GuildEmbargo => "guild-embargo",
        DaggerfallWorldConditionKind.SuccessionCrisis => "succession-crisis",
        DaggerfallWorldConditionKind.PlagueScare => "plague-scare",
        DaggerfallWorldConditionKind.Decree => "decree",
        _ => "decree",
    };

    internal static bool TryParseKind(string? value, out DaggerfallWorldConditionKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        string normalized = value.Trim();
        if (Enum.TryParse(normalized, ignoreCase: true, out DaggerfallWorldConditionKind parsed) && Enum.IsDefined(parsed))
        {
            kind = parsed;
            return true;
        }
        switch (normalized.ToLowerInvariant())
        {
            case "grain-shortage": kind = DaggerfallWorldConditionKind.GrainShortage; return true;
            case "border-tension": kind = DaggerfallWorldConditionKind.BorderTension; return true;
            case "monster-surge": kind = DaggerfallWorldConditionKind.MonsterSurge; return true;
            case "crime-crackdown": kind = DaggerfallWorldConditionKind.CrimeCrackdown; return true;
            case "guild-embargo": kind = DaggerfallWorldConditionKind.GuildEmbargo; return true;
            case "succession-crisis": kind = DaggerfallWorldConditionKind.SuccessionCrisis; return true;
            case "plague-scare": kind = DaggerfallWorldConditionKind.PlagueScare; return true;
            case "decree": kind = DaggerfallWorldConditionKind.Decree; return true;
            default: return false;
        }
    }
}

/// <summary>Sparse persisted world-condition tokens. Empty when no authored/stubbed condition tokens are active.</summary>
internal sealed record DaggerfallWorldConditionsSave(DaggerfallWorldConditionToken[] Tokens)
{
    internal static DaggerfallWorldConditionsSave Empty { get; } = new([]);

    internal DaggerfallWorldConditionsSave Validate()
    {
        ArgumentNullException.ThrowIfNull(Tokens);
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DaggerfallWorldConditionToken token in Tokens)
        {
            ArgumentNullException.ThrowIfNull(token);
            token.Validate();
            if (!ids.Add(token.TokenId))
                throw new ArgumentException($"Saved world conditions repeat token id '{token.TokenId}'.", nameof(Tokens));
        }
        return this;
    }
}

/// <summary>
/// Read-only consumer seam for active world conditions. Consumers in <c>Travel</c>, <c>Society</c>,
/// <c>Economy</c>, and <c>Dialogue</c> read through this seam regardless of whether tokens come from
/// sparse authored/stubbed tokens or the live weekly board game (B2).
/// </summary>
internal interface IDaggerfallWorldConditionSource
{
    IReadOnlyList<DaggerfallWorldConditionToken> ActiveForRegion(int region, int? currentDay = null);
    bool HasActive(int region, DaggerfallWorldConditionKind kind, int? currentDay = null);
    int MaxSeverity(int region, DaggerfallWorldConditionKind kind, int? currentDay = null);
}

/// <summary>
/// World-scoped sparse owner for active <see cref="DaggerfallWorldConditionToken"/>s plus an optional external
/// provider seam (<see cref="BindExternalSource"/>) for the weekly board game (B2) to contribute derived tokens
/// without changing any downstream consumer. Untouched worlds persist an empty array (zero token bytes).
/// </summary>
internal sealed class DaggerfallWorldConditions : IDaggerfallWorldConditionSource
{
    private readonly Dictionary<string, DaggerfallWorldConditionToken> _tokens = new(StringComparer.Ordinal);
    private Func<int, IReadOnlyList<DaggerfallWorldConditionToken>>? _externalSource;

    /// <summary>Fired immediately before authored or external world conditions change so elapsed-time spread can checkpoint accumulated progress.</summary>
    internal event Action? BeforeMutation;

    internal int Count => _tokens.Count;
    internal IReadOnlyCollection<DaggerfallWorldConditionToken> AuthoredTokens => _tokens.Values;

    /// <summary>Binds an external live provider (such as the B2 weekly board game) alongside sparse stored tokens.</summary>
    internal void BindExternalSource(Func<int, IReadOnlyList<DaggerfallWorldConditionToken>>? provider)
    {
        BeforeMutation?.Invoke();
        _externalSource = provider;
    }

    /// <summary>Notifies listeners immediately before an external condition provider updates its active tokens.</summary>
    internal void NotifyBeforeExternalMutation() => BeforeMutation?.Invoke();

    /// <summary>Seeds initial authored/scenario condition tokens on new game start (empty when baseline has none).</summary>
    internal void SeedAuthored(IEnumerable<DaggerfallWorldConditionToken>? tokens)
    {
        if (tokens is null)
            return;
        foreach (DaggerfallWorldConditionToken token in tokens)
        {
            ArgumentNullException.ThrowIfNull(token);
            token.Validate();
            _tokens[token.TokenId] = token;
        }
    }

    /// <summary>Adds or replaces a sparse authored/stubbed condition token.</summary>
    internal DaggerfallWorldConditionToken Set(DaggerfallWorldConditionToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        token.Validate();
        BeforeMutation?.Invoke();
        _tokens[token.TokenId] = token;
        return token;
    }

    /// <summary>Removes an authored/stubbed condition token by id.</summary>
    internal bool Remove(string tokenId)
    {
        if (string.IsNullOrWhiteSpace(tokenId) || !_tokens.ContainsKey(tokenId))
            return false;
        BeforeMutation?.Invoke();
        return _tokens.Remove(tokenId);
    }

    /// <summary>Clears all authored/stubbed condition tokens.</summary>
    internal void Clear()
    {
        if (_tokens.Count == 0)
            return;
        BeforeMutation?.Invoke();
        _tokens.Clear();
    }

    /// <summary>Removes expired authored tokens at or after <paramref name="currentDay"/>.</summary>
    internal int PruneExpired(int currentDay)
    {
        List<string> expired = [.. _tokens.Values.Where(token => token.IsExpired(currentDay)).Select(token => token.TokenId)];
        if (expired.Count == 0)
            return 0;
        BeforeMutation?.Invoke();
        foreach (string id in expired) _tokens.Remove(id);
        return expired.Count;
    }

    public IReadOnlyList<DaggerfallWorldConditionToken> ActiveForRegion(int region, int? currentDay = null)
    {
        if (region is < 0 or > 61)
            return [];

        Dictionary<string, DaggerfallWorldConditionToken> merged = new(StringComparer.Ordinal);
        if (_externalSource is { } external)
        {
            foreach (DaggerfallWorldConditionToken token in external(region))
            {
                if (token is null || !token.AppliesToRegion(region)) continue;
                if (currentDay is int d && (d < token.StartedDay || token.IsExpired(d))) continue;
                merged[token.TokenId] = token;
            }
        }

        foreach (DaggerfallWorldConditionToken token in _tokens.Values)
        {
            if (!token.AppliesToRegion(region)) continue;
            if (currentDay is int d && (d < token.StartedDay || token.IsExpired(d))) continue;
            merged[token.TokenId] = token;
        }

        if (merged.Count == 0)
            return [];

        return [.. merged.Values
            .OrderByDescending(token => token.Severity)
            .ThenByDescending(token => token.StartedDay)
            .ThenBy(token => token.TokenId, StringComparer.Ordinal)];
    }

    public bool HasActive(int region, DaggerfallWorldConditionKind kind, int? currentDay = null) =>
        ActiveForRegion(region, currentDay).Any(token => token.Kind == kind);

    public int MaxSeverity(int region, DaggerfallWorldConditionKind kind, int? currentDay = null)
    {
        int max = 0;
        foreach (DaggerfallWorldConditionToken token in ActiveForRegion(region, currentDay))
        {
            if (token.Kind == kind && token.Severity > max)
                max = token.Severity;
        }
        return max;
    }

    internal DaggerfallWorldConditionsSave Capture() =>
        new([.. _tokens.Values.OrderBy(token => token.TokenId, StringComparer.Ordinal)]);

    internal void Restore(DaggerfallWorldConditionsSave? saved)
    {
        _tokens.Clear();
        if (saved is null)
            return;
        saved.Validate();
        foreach (DaggerfallWorldConditionToken token in saved.Tokens)
            _tokens[token.TokenId] = token;
    }

    /// <summary>Builds a deterministic authored token id for a region/kind pair.</summary>
    internal static string RegionTokenId(DaggerfallWorldConditionKind kind, int region, string? tag = null) =>
        string.IsNullOrWhiteSpace(tag)
            ? string.Create(CultureInfo.InvariantCulture, $"cond:{DaggerfallWorldConditionToken.KindSlug(kind)}:r{region}")
            : string.Create(CultureInfo.InvariantCulture, $"cond:{DaggerfallWorldConditionToken.KindSlug(kind)}:r{region}:{tag}");
}
