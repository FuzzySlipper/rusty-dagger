using System.Globalization;
using System.Text.Json.Serialization;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>How player combat clearing weakened or stripped an active regional <see cref="DaggerfallWorldConditionToken"/>.</summary>
internal enum DaggerfallConditionCounterplayKind
{
    /// <summary>Player cleared a matching dungeon/lair in the affected region.</summary>
    LairCleared = 0,

    /// <summary>Player wiped out a staged condition-driven surge or supply-raider band.</summary>
    SurgeBandDefeated = 1,

    /// <summary>Player defeated enough matching surge or raider forces outdoors in the affected region.</summary>
    WildernessCleared = 2,
}

/// <summary>
/// Typed <c>World -&gt; Board</c> record emitted whenever player clearing weakens or strips a regional condition token,
/// retained sparsely so Track B (<c>#11055</c>) can apply player counter-play on the next Sunday board turn.
/// </summary>
internal sealed record DaggerfallConditionCounterplayRecord(
    [property: JsonRequired] string RecordId,
    [property: JsonRequired] DaggerfallConditionCounterplayKind Kind,
    [property: JsonRequired] string TokenId,
    [property: JsonRequired] DaggerfallWorldConditionKind ConditionKind,
    [property: JsonRequired] int Region,
    [property: JsonRequired] int PreviousSeverity,
    [property: JsonRequired] int NewSeverity,
    [property: JsonRequired] bool Stripped,
    [property: JsonRequired] int Day,
    [property: JsonRequired] long Minute,
    string? SubjectTag = null,
    int? SourceFactionId = null,
    string? SiteName = null,
    string? SourceKey = null,
    string? NewsEventId = null)
{
    internal DaggerfallConditionCounterplayRecord Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RecordId);
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind), Kind, $"Counter-play record '{RecordId}' names an invalid kind.");
        ArgumentException.ThrowIfNullOrWhiteSpace(TokenId);
        if (!Enum.IsDefined(ConditionKind))
            throw new ArgumentOutOfRangeException(nameof(ConditionKind), ConditionKind, $"Counter-play record '{RecordId}' names an invalid condition kind.");
        if (Region is < 0 or > 61)
            throw new ArgumentOutOfRangeException(nameof(Region), Region, $"Counter-play record '{RecordId}' names no classic region.");
        if (PreviousSeverity is < DaggerfallWorldConditionToken.MinSeverity or > DaggerfallWorldConditionToken.MaxSeverity)
            throw new ArgumentOutOfRangeException(nameof(PreviousSeverity), PreviousSeverity, $"Counter-play record '{RecordId}' previous severity is out of range.");
        if (NewSeverity < 0 || NewSeverity >= PreviousSeverity)
            throw new ArgumentOutOfRangeException(nameof(NewSeverity), NewSeverity, $"Counter-play record '{RecordId}' new severity must be strictly less than previous severity.");
        if (Stripped != (NewSeverity == 0))
            throw new ArgumentException($"Counter-play record '{RecordId}' Stripped flag disagrees with NewSeverity.", nameof(Stripped));
        if (Day < 0)
            throw new ArgumentOutOfRangeException(nameof(Day));
        if (Minute < 0)
            throw new ArgumentOutOfRangeException(nameof(Minute));
        return this;
    }
}

/// <summary>One already-credited dungeon or band source so the same cleared lair cannot double-strip the same token.</summary>
internal sealed record DaggerfallCounterplayClearedSourceSave(
    [property: JsonRequired] string SourceKey,
    [property: JsonRequired] string TokenId)
{
    internal DaggerfallCounterplayClearedSourceSave Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(TokenId);
        return this;
    }
}

/// <summary>Sparse in-progress kill tally for one dungeon or regional wilderness scope toward a condition counter-play threshold.</summary>
internal sealed record DaggerfallCounterplayProgressSave(
    [property: JsonRequired] string ScopeKey,
    [property: JsonRequired] string TokenId,
    [property: JsonRequired] long[] CreditedActorIds)
{
    internal DaggerfallCounterplayProgressSave Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ScopeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(TokenId);
        ArgumentNullException.ThrowIfNull(CreditedActorIds);
        return this;
    }
}

/// <summary>Sparse persisted snapshot of condition counter-play progress and unconsumed <c>World -&gt; Board</c> events.</summary>
internal sealed record DaggerfallConditionCounterplaySave(
    DaggerfallCounterplayClearedSourceSave[] ClearedSources,
    DaggerfallCounterplayProgressSave[] Progress,
    DaggerfallConditionCounterplayRecord[] PendingBoardEvents,
    int NextSequence = 0)
{
    internal static DaggerfallConditionCounterplaySave Empty { get; } = new([], [], [], 0);

    internal DaggerfallConditionCounterplaySave Validate()
    {
        ArgumentNullException.ThrowIfNull(ClearedSources);
        ArgumentNullException.ThrowIfNull(Progress);
        ArgumentNullException.ThrowIfNull(PendingBoardEvents);
        if (NextSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(NextSequence));
        foreach (DaggerfallCounterplayClearedSourceSave cleared in ClearedSources)
        {
            ArgumentNullException.ThrowIfNull(cleared);
            cleared.Validate();
        }
        foreach (DaggerfallCounterplayProgressSave prog in Progress)
        {
            ArgumentNullException.ThrowIfNull(prog);
            prog.Validate();
        }
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DaggerfallConditionCounterplayRecord record in PendingBoardEvents)
        {
            ArgumentNullException.ThrowIfNull(record);
            record.Validate();
            if (!ids.Add(record.RecordId))
                throw new ArgumentException($"Saved condition counter-play repeats record id '{record.RecordId}'.", nameof(PendingBoardEvents));
        }
        return this;
    }
}

/// <summary>Immediate outcome returned when a kill weakens or strips an active world condition.</summary>
internal sealed record DaggerfallConditionCounterplayOutcome(
    DaggerfallConditionCounterplayRecord Record,
    string OutcomeMessage);

/// <summary>
/// Closes the <c>World -&gt; Board</c> counter-play loop for dungeon crawling and wilderness clearing (#11081):
/// clearing a matching surge lair, wiping out a staged condition-driven expedition band, or defeating enough
/// matching surge/raider forces outdoors weakens or strips active regional <see cref="DaggerfallWorldConditionToken"/>s,
/// emits a <see cref="DaggerfallWordSpread"/> news wavefront (reflected on town notice boards), and queues a typed
/// <see cref="DaggerfallConditionCounterplayRecord"/> for Track B (<c>#11055</c>) to consume on the next Sunday turn.
/// </summary>
internal sealed class DaggerfallConditionCounterplay(
    DaggerfallDefinitions definitions,
    DaggerfallWorldConditions conditions,
    DaggerfallWordSpread wordSpread,
    ActorsState actors,
    IReadOnlyDictionary<long, DaggerfallActorDefinition> actorDefinitions,
    Func<DaggerfallWorldProfileKey> activeProfile,
    Func<DaggerfallSiteRecord?> activeSite,
    Func<string> playerName,
    Func<long, bool> isPlayerAllied,
    Func<int, ulong, bool>? hasPendingBandMembers = null)
{
    /// <summary>Decisive matching kills inside a large dungeon that break its surge contribution even if distant rooms remain.</summary>
    internal const int DungeonDecisiveKillThreshold = 6;

    /// <summary>Matching non-band-tagged wilderness kills in an affected region required to weaken/strip one severity tier.</summary>
    internal const int WildernessClearKillThreshold = 4;

    private static readonly string[] UndeadDungeonFactions = ["undead"];
    private static readonly string[] DesecratedTempleFactions = ["undead", "daedra"];
    private static readonly string[] OrcDungeonFactions = ["orcs"];
    private static readonly string[] CriminalDungeonFactions = ["criminals"];
    private static readonly string[] CaveMineDungeonFactions = ["orcs", "criminals", "bears", "spiders", "vermin", "werecreatures"];
    private static readonly string[] ArcaneDungeonFactions = ["daedra", "magic", "undead"];
    private static readonly string[] HarpyDungeonFactions = ["harpies"];
    private static readonly string[] SpiderDungeonFactions = ["spiders"];
    private static readonly string[] GiantDungeonFactions = ["giants"];
    private static readonly string[] DragonDungeonFactions = ["dragonlings"];
    private static readonly string[] ScorpionDungeonFactions = ["scorpions"];

    private readonly DaggerfallDefinitions _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly DaggerfallWorldConditions _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
    private readonly DaggerfallWordSpread _wordSpread = wordSpread ?? throw new ArgumentNullException(nameof(wordSpread));
    private readonly ActorsState _actors = actors ?? throw new ArgumentNullException(nameof(actors));
    private readonly IReadOnlyDictionary<long, DaggerfallActorDefinition> _actorDefinitions = actorDefinitions ?? throw new ArgumentNullException(nameof(actorDefinitions));
    private readonly Func<DaggerfallWorldProfileKey> _activeProfile = activeProfile ?? throw new ArgumentNullException(nameof(activeProfile));
    private readonly Func<DaggerfallSiteRecord?> _activeSite = activeSite ?? throw new ArgumentNullException(nameof(activeSite));
    private readonly Func<string> _playerName = playerName ?? throw new ArgumentNullException(nameof(playerName));
    private readonly Func<long, bool> _isPlayerAllied = isPlayerAllied ?? throw new ArgumentNullException(nameof(isPlayerAllied));
    private readonly Func<int, ulong, bool>? _hasPendingBandMembers = hasPendingBandMembers;

    private readonly HashSet<(string SourceKey, string TokenId)> _clearedSources = [];
    private readonly Dictionary<(string ScopeKey, string TokenId), HashSet<long>> _progress = [];
    private readonly List<DaggerfallConditionCounterplayRecord> _pendingBoardEvents = [];
    private int _nextSequence;

    internal int PendingBoardEventCount => _pendingBoardEvents.Count;
    internal IReadOnlyList<DaggerfallConditionCounterplayRecord> PendingBoardEvents => _pendingBoardEvents;

    /// <summary>Raised whenever player counter-play weakens or strips a condition (a notice-board bounty is earned by it).</summary>
    internal event Action<DaggerfallConditionCounterplayRecord>? Countered;

    /// <summary>
    /// Drains all unconsumed <c>World -&gt; Board</c> counter-play records for Track B's weekly Sunday board turn
    /// and clears their corresponding local external-token severity reductions.
    /// </summary>
    internal IReadOnlyList<DaggerfallConditionCounterplayRecord> ConsumeBoardEvents()
    {
        if (_pendingBoardEvents.Count == 0) return [];
        DaggerfallConditionCounterplayRecord[] drained = [.. _pendingBoardEvents];
        _pendingBoardEvents.Clear();
        foreach (DaggerfallConditionCounterplayRecord record in drained)
            _ = _conditions.ClearExternalReduction(record.TokenId);
        return drained;
    }

    internal static bool IsCounterplayable(DaggerfallWorldConditionKind kind) =>
        kind is DaggerfallWorldConditionKind.MonsterSurge or DaggerfallWorldConditionKind.GrainShortage;

    /// <summary>Maps classic <c>DFRegion.DungeonTypes</c> (<c>0..18</c>) onto the creature-family factions inhabiting that lair type.</summary>
    internal static IReadOnlyList<string> DungeonTypeFactions(int dungeonType) => dungeonType switch
    {
        0 or 8 or 18 => UndeadDungeonFactions,          // Crypt, VampireHaunt, Cemetery
        4 => DesecratedTempleFactions,                  // DesecratedTemple
        1 => OrcDungeonFactions,                        // OrcStronghold
        2 or 3 or 11 or 15 => CriminalDungeonFactions,  // HumanStronghold, Prison, RuinedCastle, BarbarianStronghold
        5 or 6 => CaveMineDungeonFactions,              // Mine, NaturalCave
        7 or 9 or 16 => ArcaneDungeonFactions,          // Coven, Laboratory, VolcanicCaves
        10 => HarpyDungeonFactions,                     // HarpyNest
        12 => SpiderDungeonFactions,                    // SpiderNest
        13 => GiantDungeonFactions,                     // GiantStronghold
        14 => DragonDungeonFactions,                    // DragonsDen
        17 => ScorpionDungeonFactions,                  // ScorpionNest
        _ => [],
    };

    /// <summary>Whether a classic dungeon type is an eligible lair for weakening/stripping <paramref name="token"/>.</summary>
    internal static bool DungeonTypeMatchesCondition(int dungeonType, DaggerfallWorldConditionToken token, DaggerfallCreatureFactionSet? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!IsCounterplayable(token.Kind)) return false;
        IReadOnlyList<string> lairFactions = DungeonTypeFactions(dungeonType);
        if (lairFactions.Count == 0) return false;
        IReadOnlyList<string> conditionFactions = DaggerfallCreatureFactionPolicy.FactionsForCondition(token, catalog);
        foreach (string lf in lairFactions)
        {
            if (conditionFactions.Contains(lf, StringComparer.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Evaluates a defeated non-player actor in the active scene for dungeon, surge-band, or wilderness counter-play.</summary>
    internal DaggerfallConditionCounterplayOutcome? ReactToDeath(ActorDiedFact died, ActorState fallen, long currentMinute, int currentDay)
    {
        ArgumentNullException.ThrowIfNull(died);
        ArgumentNullException.ThrowIfNull(fallen);
        if (fallen.DurableId == DaggerfallActorIdentity.PlayerEntityId) return null;

        DaggerfallWorldProfileKey profile = _activeProfile();
        DaggerfallSiteRecord? site = _activeSite();
        int region = site?.Id.Region ?? profile.Site.Region;
        IReadOnlyList<DaggerfallWorldConditionToken> active = _conditions.ActiveForRegion(region, currentDay);
        if (active.Count == 0) return null;

        List<DaggerfallWorldConditionToken> counterplayable = [.. active.Where(t => IsCounterplayable(t.Kind))];
        if (counterplayable.Count == 0) return null;

        string? actorFaction = ResolveActorCreatureFaction(fallen);
        if (string.IsNullOrWhiteSpace(actorFaction) || string.Equals(actorFaction, "player-ally", StringComparison.Ordinal))
            return null;

        bool playerCredited = died.KillerId == DaggerfallActorIdentity.PlayerEntityId || _isPlayerAllied(died.KillerId);

        // 1. Staged condition-driven expedition surge/raider band wipeout.
        if (fallen.Actor.TryGet<DaggerfallEncounterBandMember>(out DaggerfallEncounterBandMember? bandMember)
            && !string.IsNullOrWhiteSpace(bandMember.ConditionTokenId))
        {
            DaggerfallWorldConditionToken? bandToken = counterplayable.FirstOrDefault(t =>
                StringComparer.Ordinal.Equals(t.TokenId, bandMember.ConditionTokenId));
            if (bandToken is not null && ActorMatchesCondition(bandToken, actorFaction))
            {
                string tokenInstanceKey = TokenInstanceKey(bandToken);
                string bandSourceKey = string.Create(CultureInfo.InvariantCulture, $"band:{bandMember.BandId}:{bandMember.Generation}");
                if (!_clearedSources.Contains((bandSourceKey, tokenInstanceKey)))
                {
                    if (playerCredited)
                        AddProgress(bandSourceKey, tokenInstanceKey, fallen.DurableId);

                    bool anyAlive = _actors.All.Any(candidate =>
                        candidate.DurableId != fallen.DurableId
                        && !candidate.IsDefeated
                        && candidate.Actor.TryGet<DaggerfallEncounterBandMember>(out DaggerfallEncounterBandMember? other)
                        && other.BandId == bandMember.BandId
                        && other.Generation == bandMember.Generation);
                    bool anyPending = _hasPendingBandMembers?.Invoke(bandMember.BandId, bandMember.Generation) == true;
                    if (!anyAlive && !anyPending && GetProgressCount(bandSourceKey, tokenInstanceKey) >= 1)
                    {
                        _clearedSources.Add((bandSourceKey, tokenInstanceKey));
                        _progress.Remove((bandSourceKey, tokenInstanceKey));
                        return ApplyCounterplayStrike(
                            DaggerfallConditionCounterplayKind.SurgeBandDefeated,
                            bandToken, region, site, bandSourceKey, currentMinute, currentDay);
                    }
                }
                return null;
            }
        }

        // 2. Dungeon / lair clearing inside a dungeon whose type and slain occupants match an active surge/raider condition.
        if (profile.Kind == DaggerfallWorldProfileKind.Dungeon && site is not null)
        {
            string dungeonSourceKey = $"dungeon:{profile.LogicalId}";
            DaggerfallWorldConditionToken? dungeonToken = counterplayable.FirstOrDefault(t =>
                ActorMatchesCondition(t, actorFaction)
                && DungeonTypeMatchesCondition(site.DungeonType, t, _definitions.CreatureFactions)
                && !_clearedSources.Contains((dungeonSourceKey, TokenInstanceKey(t))));
            if (dungeonToken is null) return null;

            string tokenInstanceKey = TokenInstanceKey(dungeonToken);
            if (playerCredited)
                AddProgress(dungeonSourceKey, tokenInstanceKey, fallen.DurableId);

            int credited = GetProgressCount(dungeonSourceKey, tokenInstanceKey);
            if (credited <= 0) return null;

            bool anyLivingMatching = _actors.All.Any(candidate =>
                candidate.DurableId != DaggerfallActorIdentity.PlayerEntityId
                && candidate.DurableId != fallen.DurableId
                && !candidate.IsDefeated
                && ResolveActorCreatureFaction(candidate) is { Length: > 0 } otherFaction
                && ActorMatchesCondition(dungeonToken, otherFaction));
            if (credited >= DungeonDecisiveKillThreshold || !anyLivingMatching)
            {
                _clearedSources.Add((dungeonSourceKey, tokenInstanceKey));
                _progress.Remove((dungeonSourceKey, tokenInstanceKey));
                return ApplyCounterplayStrike(
                    DaggerfallConditionCounterplayKind.LairCleared,
                    dungeonToken, region, site, dungeonSourceKey, currentMinute, currentDay);
            }
            return null;
        }

        // 3. Wilderness clearing (e.g. caravan raid raiders, creature clashes, or wild surge encounters outdoors in the region).
        if (playerCredited && profile.Kind == DaggerfallWorldProfileKind.Exterior)
        {
            DaggerfallWorldConditionToken? wildToken = counterplayable.FirstOrDefault(t => ActorMatchesCondition(t, actorFaction));
            if (wildToken is null) return null;

            string tokenInstanceKey = TokenInstanceKey(wildToken);
            string wildScope = string.Create(CultureInfo.InvariantCulture, $"wild:r{region}");
            AddProgress(wildScope, tokenInstanceKey, fallen.DurableId);
            if (GetProgressCount(wildScope, tokenInstanceKey) >= WildernessClearKillThreshold)
            {
                _progress.Remove((wildScope, tokenInstanceKey));
                return ApplyCounterplayStrike(
                    DaggerfallConditionCounterplayKind.WildernessCleared,
                    wildToken, region, site, wildScope, currentMinute, currentDay);
            }
        }

        return null;
    }

    private DaggerfallConditionCounterplayOutcome? ApplyCounterplayStrike(
        DaggerfallConditionCounterplayKind kind,
        DaggerfallWorldConditionToken token,
        int region,
        DaggerfallSiteRecord? site,
        string sourceKey,
        long currentMinute,
        int currentDay)
    {
        DaggerfallConditionMutationOutcome? mutation = _conditions.WeakenOrStrip(token.TokenId, region, currentDay, severityDelta: 1);
        if (mutation is null) return null;

        int newSeverity = mutation.Updated?.Severity ?? 0;
        int seq = ++_nextSequence;
        string recordId = string.Create(CultureInfo.InvariantCulture, $"cp:r{region}:d{currentDay}:{seq}");
        string newsEventId = $"wf:counterplay:{recordId}";
        string regionName = _definitions.BuildingNames.RegionName(region);
        DaggerfallSiteRecord? originSite = site ?? _definitions.Locations.Records.FirstOrDefault(r => r.Id.Region == region);
        string siteName = originSite?.Name ?? regionName;
        string threatLabel = FormatThreatLabel(token);
        string hero = string.IsNullOrWhiteSpace(_playerName()) ? "An adventurer" : _playerName().Trim();

        string headline = mutation.Stripped
            ? $"{threatLabel} Broken at {siteName}"
            : $"{threatLabel} Weakened at {siteName}";
        string featPhrase = kind switch
        {
            DaggerfallConditionCounterplayKind.LairCleared => $"cleared the lair at {siteName}",
            DaggerfallConditionCounterplayKind.SurgeBandDefeated => $"routed a {threatLabel.ToLowerInvariant()} warband near {siteName}",
            _ => $"drove back {threatLabel.ToLowerInvariant()} raiders near {siteName}",
        };
        string body = mutation.Stripped
            ? $"Word from {siteName}: {hero} {featPhrase}, breaking the {threatLabel.ToLowerInvariant()} across {regionName}."
            : $"Word from {siteName}: {hero} {featPhrase}, weakening the {threatLabel.ToLowerInvariant()} in {regionName} though danger remains.";

        if (originSite is not null)
            _ = _wordSpread.RecordNews(newsEventId, headline, body, originSite, currentMinute, token.SourceFactionId);

        DaggerfallConditionCounterplayRecord record = new(
            RecordId: recordId,
            Kind: kind,
            TokenId: token.TokenId,
            ConditionKind: token.Kind,
            Region: region,
            PreviousSeverity: mutation.Original.Severity,
            NewSeverity: newSeverity,
            Stripped: mutation.Stripped,
            Day: currentDay,
            Minute: currentMinute,
            SubjectTag: token.SubjectTag,
            SourceFactionId: token.SourceFactionId,
            SiteName: siteName,
            SourceKey: sourceKey,
            NewsEventId: originSite is not null ? newsEventId : null);

        _pendingBoardEvents.Add(record);
        Countered?.Invoke(record);

        string outcomeMessage = mutation.Stripped
            ? $"The {threatLabel.ToLowerInvariant()} in {regionName} has been broken!"
            : $"Your victory weakens the {threatLabel.ToLowerInvariant()} in {regionName}.";
        return new(record, outcomeMessage);
    }

    private bool ActorMatchesCondition(DaggerfallWorldConditionToken token, string actorFaction) =>
        DaggerfallCreatureFactionPolicy.FactionsForCondition(token, _definitions.CreatureFactions)
            .Contains(actorFaction, StringComparer.Ordinal);

    private string? ResolveActorCreatureFaction(ActorState actor)
    {
        if (actor.Actor.TryGet<FactionComponent>(out FactionComponent? fc)
            && !string.IsNullOrWhiteSpace(fc.EffectiveTeam))
        {
            if (string.Equals(fc.EffectiveTeam, "player-ally", StringComparison.Ordinal))
                return "player-ally";
            if (!string.Equals(fc.EffectiveTeam, "player-enemy", StringComparison.Ordinal)
                && _definitions.CreatureFactions.TryGet(fc.EffectiveTeam, out _))
                return fc.EffectiveTeam;
        }
        if (_actorDefinitions.TryGetValue(actor.DurableId, out DaggerfallActorDefinition? def)
            && def.MobileId is int mobileId
            && _definitions.Mobiles.Mobiles.TryGetValue(mobileId, out DaggerfallMobileDefinition? mobile))
        {
            return DaggerfallExpeditionBands.ResolveMobileCreatureFaction(mobile);
        }
        return null;
    }

    private static string TokenInstanceKey(DaggerfallWorldConditionToken token) =>
        string.Create(CultureInfo.InvariantCulture, $"{token.TokenId}@{token.StartedDay}");

    private void AddProgress(string scopeKey, string tokenId, long actorId)
    {
        (string, string) key = (scopeKey, tokenId);
        if (!_progress.TryGetValue(key, out HashSet<long>? set))
        {
            set = [];
            _progress[key] = set;
        }
        set.Add(actorId);
    }

    private int GetProgressCount(string scopeKey, string tokenId) =>
        _progress.TryGetValue((scopeKey, tokenId), out HashSet<long>? set) ? set.Count : 0;

    private static string FormatThreatLabel(DaggerfallWorldConditionToken token)
    {
        if (token.Kind == DaggerfallWorldConditionKind.GrainShortage)
            return "Supply Raider Threat";
        string subject = token.SubjectTag?.Trim() ?? string.Empty;
        if (subject.Length == 0) return "Monster Surge";
        string singular = subject.ToLowerInvariant() switch
        {
            "orcs" or "orc" => "Orc",
            "undead" => "Undead",
            "beasts" or "animals" => "Beast",
            "bandits" or "criminals" => "Bandit",
            "daedra" => "Daedra",
            "harpies" => "Harpy",
            "giants" => "Giant",
            "spiders" => "Spider",
            "scorpions" => "Scorpion",
            "spriggans" => "Spriggan",
            "dragonlings" => "Dragonling",
            "werecreatures" => "Werecreature",
            _ => char.ToUpperInvariant(subject[0]) + subject[1..],
        };
        return $"{singular} Surge";
    }

    internal DaggerfallConditionCounterplaySave Capture()
    {
        if (_clearedSources.Count == 0 && _progress.Count == 0 && _pendingBoardEvents.Count == 0 && _nextSequence == 0)
            return DaggerfallConditionCounterplaySave.Empty;

        return new DaggerfallConditionCounterplaySave(
            ClearedSources: [.. _clearedSources
                .OrderBy(pair => pair.SourceKey, StringComparer.Ordinal)
                .ThenBy(pair => pair.TokenId, StringComparer.Ordinal)
                .Select(pair => new DaggerfallCounterplayClearedSourceSave(pair.SourceKey, pair.TokenId))],
            Progress: [.. _progress
                .Where(pair => pair.Value.Count > 0)
                .OrderBy(pair => pair.Key.ScopeKey, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.TokenId, StringComparer.Ordinal)
                .Select(pair => new DaggerfallCounterplayProgressSave(pair.Key.ScopeKey, pair.Key.TokenId, [.. pair.Value.Order()]))],
            PendingBoardEvents: [.. _pendingBoardEvents],
            NextSequence: _nextSequence);
    }

    internal void Restore(DaggerfallConditionCounterplaySave? saved)
    {
        _clearedSources.Clear();
        _progress.Clear();
        _pendingBoardEvents.Clear();
        _nextSequence = 0;
        if (saved is null) return;
        saved.Validate();
        foreach (DaggerfallCounterplayClearedSourceSave cleared in saved.ClearedSources)
            _clearedSources.Add((cleared.SourceKey, cleared.TokenId));
        foreach (DaggerfallCounterplayProgressSave prog in saved.Progress)
        {
            if (prog.CreditedActorIds.Length > 0)
                _progress[(prog.ScopeKey, prog.TokenId)] = [.. prog.CreditedActorIds];
        }
        _pendingBoardEvents.AddRange(saved.PendingBoardEvents);
        _nextSequence = saved.NextSequence;
    }
}
