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

/// <summary>Sparse local severity reduction applied to an external (B2 board-game) condition token until the next weekly board turn consumes it.</summary>
internal sealed record DaggerfallConditionReductionSave(
    [property: JsonRequired] string TokenId,
    [property: JsonRequired] int SeverityReduction)
{
    internal DaggerfallConditionReductionSave Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TokenId);
        if (SeverityReduction <= 0)
            throw new ArgumentOutOfRangeException(nameof(SeverityReduction), SeverityReduction, $"Condition reduction for '{TokenId}' must be positive.");
        return this;
    }
}

/// <summary>Result of weakening or stripping an active <see cref="DaggerfallWorldConditionToken"/> via player counter-play.</summary>
internal sealed record DaggerfallConditionMutationOutcome(
    DaggerfallWorldConditionToken Original,
    DaggerfallWorldConditionToken? Updated,
    bool Stripped);

/// <summary>Sparse persisted world-condition tokens. Empty when no authored/stubbed condition tokens are active.</summary>
internal sealed record DaggerfallWorldConditionsSave(
    DaggerfallWorldConditionToken[] Tokens,
    DaggerfallConditionReductionSave[]? ExternalReductions = null)
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
        if (ExternalReductions is not null)
        {
            HashSet<string> reducedIds = new(StringComparer.Ordinal);
            foreach (DaggerfallConditionReductionSave reduction in ExternalReductions)
            {
                ArgumentNullException.ThrowIfNull(reduction);
                reduction.Validate();
                if (!reducedIds.Add(reduction.TokenId))
                    throw new ArgumentException($"Saved world conditions repeat external reduction for '{reduction.TokenId}'.", nameof(ExternalReductions));
            }
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
    private readonly Dictionary<string, int> _externalReductions = new(StringComparer.Ordinal);
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

    /// <summary>Whether a stored (not external) token <paramref name="tokenId"/> is held.</summary>
    internal bool Has(string tokenId) => _tokens.ContainsKey(tokenId);

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
        if (_tokens.Count == 0 && _externalReductions.Count == 0)
            return;
        BeforeMutation?.Invoke();
        _tokens.Clear();
        _externalReductions.Clear();
    }

    /// <summary>
    /// Weakens an active condition token in <paramref name="region"/> by <paramref name="severityDelta"/> or strips it
    /// entirely when severity drops to zero. Works uniformly for sparse authored tokens and external B2 board-game
    /// tokens (recording a sparse local reduction until the next Sunday board turn consumes it).
    /// </summary>
    internal DaggerfallConditionMutationOutcome? WeakenOrStrip(string tokenId, int region, int? currentDay = null, int severityDelta = 1)
    {
        if (string.IsNullOrWhiteSpace(tokenId) || severityDelta <= 0)
            return null;
        DaggerfallWorldConditionToken? active = ActiveForRegion(region, currentDay)
            .FirstOrDefault(t => StringComparer.Ordinal.Equals(t.TokenId, tokenId));
        if (active is null)
            return null;

        BeforeMutation?.Invoke();
        if (_tokens.TryGetValue(tokenId, out DaggerfallWorldConditionToken? local))
        {
            if (local.Severity <= severityDelta)
            {
                _tokens.Remove(tokenId);
                return new(local, Updated: null, Stripped: true);
            }
            DaggerfallWorldConditionToken updatedLocal = local with { Severity = local.Severity - severityDelta };
            _tokens[tokenId] = updatedLocal;
            return new(local, updatedLocal, Stripped: false);
        }

        int prior = _externalReductions.GetValueOrDefault(tokenId);
        _externalReductions[tokenId] = checked(prior + severityDelta);
        int remaining = active.Severity - severityDelta;
        return remaining <= 0
            ? new(active, Updated: null, Stripped: true)
            : new(active, active with { Severity = remaining }, Stripped: false);
    }

    /// <summary>Clears a consumed local counter-play reduction for an external B2 board-game token.</summary>
    internal bool ClearExternalReduction(string tokenId)
    {
        if (string.IsNullOrWhiteSpace(tokenId) || !_externalReductions.ContainsKey(tokenId))
            return false;
        BeforeMutation?.Invoke();
        return _externalReductions.Remove(tokenId);
    }

    /// <summary>Clears all local counter-play reductions after Track B consumes pending counter-play on its weekly turn.</summary>
    internal void ClearExternalReductions()
    {
        if (_externalReductions.Count == 0) return;
        BeforeMutation?.Invoke();
        _externalReductions.Clear();
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
                DaggerfallWorldConditionToken effective = token;
                if (_externalReductions.TryGetValue(token.TokenId, out int reduction) && reduction > 0)
                {
                    if (token.Severity <= reduction) continue;
                    effective = token with { Severity = token.Severity - reduction };
                }
                merged[effective.TokenId] = effective;
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
        new(
            [.. _tokens.Values.OrderBy(token => token.TokenId, StringComparer.Ordinal)],
            _externalReductions.Count > 0
                ? [.. _externalReductions.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new DaggerfallConditionReductionSave(pair.Key, pair.Value))]
                : null);

    internal void Restore(DaggerfallWorldConditionsSave? saved)
    {
        _tokens.Clear();
        _externalReductions.Clear();
        if (saved is null)
            return;
        saved.Validate();
        foreach (DaggerfallWorldConditionToken token in saved.Tokens)
            _tokens[token.TokenId] = token;
        if (saved.ExternalReductions is not null)
        {
            foreach (DaggerfallConditionReductionSave reduction in saved.ExternalReductions)
                _externalReductions[reduction.TokenId] = reduction.SeverityReduction;
        }
    }

    /// <summary>Builds a deterministic authored token id for a region/kind pair.</summary>
    internal static string RegionTokenId(DaggerfallWorldConditionKind kind, int region, string? tag = null) =>
        string.IsNullOrWhiteSpace(tag)
            ? string.Create(CultureInfo.InvariantCulture, $"cond:{DaggerfallWorldConditionToken.KindSlug(kind)}:r{region}")
            : string.Create(CultureInfo.InvariantCulture, $"cond:{DaggerfallWorldConditionToken.KindSlug(kind)}:r{region}:{tag}");

    /// <summary>Deterministic token id for a regional <see cref="DaggerfallWorldConditionKind.SuccessionCrisis"/> owned by a vacant office.</summary>
    internal static string OfficeSuccessionCrisisTokenId(string officeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(officeKey);
        return $"office:crisis:{officeKey}";
    }

    /// <summary>Raises or refreshes a regional <see cref="DaggerfallWorldConditionKind.SuccessionCrisis"/> tied to a vacant ruler office.</summary>
    internal DaggerfallWorldConditionToken RaiseSuccessionCrisis(
        string officeKey,
        int region,
        int? sourceFactionId,
        int startedDay,
        string? headline = null,
        string? body = null,
        int severity = 2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(officeKey);
        return Set(new DaggerfallWorldConditionToken(
            TokenId: OfficeSuccessionCrisisTokenId(officeKey),
            Kind: DaggerfallWorldConditionKind.SuccessionCrisis,
            Region: region,
            SourceFactionId: sourceFactionId,
            Severity: Math.Clamp(severity, DaggerfallWorldConditionToken.MinSeverity, DaggerfallWorldConditionToken.MaxSeverity),
            StartedDay: Math.Max(0, startedDay),
            Headline: string.IsNullOrWhiteSpace(headline) ? null : headline,
            Body: string.IsNullOrWhiteSpace(body) ? null : body));
    }

    /// <summary>Clears any active <see cref="DaggerfallWorldConditionKind.SuccessionCrisis"/> tied to <paramref name="officeKey"/> once a lawful successor is seated.</summary>
    internal bool ClearSuccessionCrisis(string officeKey) =>
        !string.IsNullOrWhiteSpace(officeKey) && Remove(OfficeSuccessionCrisisTokenId(officeKey));

    /// <summary>
    /// Reacts directly to a typed <see cref="DaggerfallOfficeChangedNotification"/> inside the <c>Content</c> owner:
    /// raises a regional <see cref="DaggerfallWorldConditionKind.SuccessionCrisis"/> whenever a regional ruler office
    /// enters vacancy, non-lethal deposition, or unsettled regency, and clears it once a lawful successor is seated.
    /// Non-ruler offices do not raise a regional succession crisis.
    /// </summary>
    internal void ObserveOfficeChanged(
        DaggerfallOfficeChangedNotification change,
        Func<int, string>? regionName = null)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Kind != DaggerfallOfficeKind.Ruler || change.Region is not int rulerRegion)
            return;

        if (change.ChangeKind is DaggerfallOfficeChangeKind.VacancyOpened
            or DaggerfallOfficeChangeKind.HolderDeposed
            or DaggerfallOfficeChangeKind.RegencyDeclared)
        {
            string resolvedRegion = regionName?.Invoke(rulerRegion) ?? $"Region {rulerRegion}";
            string former = !string.IsNullOrWhiteSpace(change.PreviousHolderName)
                ? change.PreviousHolderName
                : "the former ruler";
            string headline = change.ChangeKind == DaggerfallOfficeChangeKind.RegencyDeclared
                ? $"Regency Unsettled in {resolvedRegion}"
                : $"Succession Crisis in {resolvedRegion}";
            string body = change.ChangeKind == DaggerfallOfficeChangeKind.RegencyDeclared
                ? $"{change.NewHolderName ?? "An interim regent"} governs {resolvedRegion} in regency after {former}. Guards remain on strict watch until a lawful successor secures the throne."
                : $"The throne of {resolvedRegion} stands vacant after {former}. Gate and court scrutiny remain tightened across {resolvedRegion} until a lawful successor takes the seat.";
            RaiseSuccessionCrisis(
                change.OfficeKey,
                rulerRegion,
                change.OfficeFactionId,
                change.ChangedDay,
                headline: headline,
                body: body);
            return;
        }

        if (change.ChangeKind == DaggerfallOfficeChangeKind.SuccessorSeated)
            ClearSuccessionCrisis(change.OfficeKey);
    }

    /// <summary>Clears any succession crisis tied to an office override that was reset back to baseline.</summary>
    internal void ObserveOfficeOverrideCleared(string officeKey) =>
        ClearSuccessionCrisis(officeKey);
}
