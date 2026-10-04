using System.Globalization;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Targeting;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallDialogueTone { Polite, Normal, Blunt }
internal enum DaggerfallDialogueTopic { Directions, News, Work, QuestInfo }

internal sealed record DaggerfallDialogueTopicOption(string Id, string Label);
internal sealed record DaggerfallDialogueDestination(string Id, string Name, string Hint, bool Known = true);
internal sealed record DaggerfallDialogueView(
    string Revision,
    string TargetLabel,
    string Greeting,
    string Tone,
    string? Question,
    string? Reply,
    IReadOnlyList<DaggerfallDialogueTopicOption> Topics,
    IReadOnlyList<string> Diagnostics)
{
    internal int ComprehendLanguagesBonus { get; init; }
    internal bool BankAvailable { get; init; }
    internal IReadOnlyList<DaggerfallQuestContact> QuestContacts { get; init; } = [];
    internal DaggerfallSkillTrainingProviderView? Training { get; init; }
}

/// <summary>
/// Daggerfall talk policy over the existing NPC identity and actor owners. The owner admits only a
/// current talk-service target at the active site; a DOM choice is rechecked against the same actor,
/// presence, site, and pose before it can resolve.
/// </summary>
internal sealed class DaggerfallDialogueService : IDaggerfallNpcActivationOwner
{
    private const int Merchants = 1;
    private const int NormalQuestionReaction = 0;
    private const int LocationQuestionReaction = 5;
    private const string RandomScope = "daggerfall.dialogue.v1";

    private static readonly int[] EtiquetteReactionMods = [-10, 5, 10, 15, -15];
    private static readonly int[] StreetwiseReactionMods = [10, 5, -10, -15, 15];
    private static readonly int[] DirectionAnswers =
    [
        7251, 7266, 7281, 7250, 7265, 7280, 7252, 7267, 7282, 7253, 7268, 7283, 7304, 7269, 7284,
        7256, 7271, 7286, 7255, 7270, 7285, 7257, 7272, 7287, 7258, 7273, 7288, 7259, 7274, 7289,
    ];
    private static readonly int[] NonDirectionAnswers =
    [
        7251, 7266, 7281, 7250, 7265, 7280, 7252, 7267, 7282, 7253, 7268, 7283, 7304, 7269, 7284,
        7261, 7276, 7291, 7260, 7275, 7290, 7262, 7277, 7292, 7263, 7278, 7293, 7264, 7279, 7294,
    ];

    private readonly DaggerfallNpcRegistry _npcs;
    private readonly ActorsState _actors;
    private readonly DaggerfallSocialState _social;
    private readonly DaggerfallSkillUseReactions _skillUses;
    private readonly StatsComponent _playerStats;
    private readonly DaggerfallDefinitions _definitions;
    private readonly DaggerfallTextResolver _text;
    private readonly IRandomService _random;
    private readonly Func<DaggerfallSiteRecord?> _activeSite;
    private readonly Func<DaggerfallCharacterIdentity> _playerIdentity;
    private readonly Action<DaggerfallDialogueView?> _publish;
    private readonly Action<string> _setOutcome;
    private readonly Func<(string Name, string Hint)?>? _directions;
    private readonly Func<string?, DaggerfallDialogueDestination?>? _resolveDirection;
    private readonly Func<IReadOnlyList<DaggerfallDialogueTopicOption>>? _directionDirectory;
    private readonly Func<long, IReadOnlyList<DaggerfallQuestDialogueTopic>> _questTopics;
    private readonly Func<long, string, (string Text, IReadOnlyList<string> Diagnostics)?>? _resolveQuestTopic;
    private readonly Func<DaggerfallCalendar> _calendar;
    private readonly Func<long, bool> _muted;
    private readonly Func<long, IReadOnlyList<DaggerfallQuestContact>> _questContacts;
    private readonly Func<long, bool> _workAvailable;
    private TalkSession? _current;
    private long _nextRevision;

    internal DaggerfallDialogueService(
        DaggerfallNpcRegistry npcs,
        ActorsState actors,
        DaggerfallSocialState social,
        DaggerfallSkillUseReactions skillUses,
        StatsComponent playerStats,
        DaggerfallDefinitions definitions,
        IRandomService random,
        Func<DaggerfallSiteRecord?> activeSite,
        Func<DaggerfallCharacterIdentity> playerIdentity,
        Action<DaggerfallDialogueView?> publish,
        Action<string> setOutcome,
        Func<(string Name, string Hint)?>? directions = null, Func<long, bool>? muted = null,
        Func<long, IReadOnlyList<DaggerfallQuestContact>>? questContacts = null,
        Func<string?, DaggerfallDialogueDestination?>? resolveDirection = null,
        Func<IReadOnlyList<DaggerfallDialogueTopicOption>>? directionDirectory = null,
        Func<long, IReadOnlyList<DaggerfallQuestDialogueTopic>>? questTopics = null,
        Func<long, string, (string Text, IReadOnlyList<string> Diagnostics)?>? resolveQuestTopic = null,
        Func<DaggerfallCalendar>? calendar = null,
        Func<long, bool>? workAvailable = null)
    {
        _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _social = social ?? throw new ArgumentNullException(nameof(social));
        _skillUses = skillUses ?? throw new ArgumentNullException(nameof(skillUses));
        _playerStats = playerStats ?? throw new ArgumentNullException(nameof(playerStats));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _text = definitions.TextPresentation;
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _activeSite = activeSite ?? throw new ArgumentNullException(nameof(activeSite));
        _playerIdentity = playerIdentity ?? throw new ArgumentNullException(nameof(playerIdentity));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _directions = directions;
        _resolveDirection = resolveDirection;
        _directionDirectory = directionDirectory;
        _muted = muted ?? (_ => false);
        _questContacts = questContacts ?? (_ => []);
        _questTopics = questTopics ?? (_ => []);
        _resolveQuestTopic = resolveQuestTopic;
        _calendar = calendar ?? (() => DaggerfallCalendar.Start);
        _workAvailable = workAvailable ?? (_ => false);
        _setOutcome = setOutcome ?? throw new ArgumentNullException(nameof(setOutcome));
    }

    public IEnumerable<DaggerfallActivationTarget> NpcTargets()
    {
        DaggerfallSiteRecord? site = _activeSite();
        if (site is null) yield break;
        foreach (DaggerfallNpc npc in _npcs.All)
        {
            if (!TryReadLiveNpc(npc.DurableId, out _, out DaggerfallDialogueNpc? actor)) continue;
            yield return new(
                DaggerfallActivationTargetKind.Npc,
                ActorsState.Identity(npc.DurableId),
                actor!.Entity,
                checked((ulong)npc.DurableId),
                actor.Position,
                Precedence: 1,
                Label: npc.Role);
        }
    }

    public DaggerfallActivationOutcome ActivateNpc(DaggerfallActivationSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Mode != DaggerfallActivationMode.Talk)
            return new(false, "Select Talk mode to speak with someone.");
        if (selection.Target.Kind != DaggerfallActivationTargetKind.Npc
            || !TryReadLiveNpc(checked((long)selection.Target.Identity.Value), out DaggerfallNpc? npc, out DaggerfallDialogueNpc? actor)
            || actor is null
            || actor.Entity != selection.Target.Entity)
            return new(false, "That person is no longer available here.");

        // TalkManager refuses a target whose persistent faction reaction is below -20 before it
        // opens the modal. Faction-zero civilians remain neutral through ReactionForNpc and can
        // still answer; no window-local reaction shadow is created here.
        if (_social.ReactionForNpc(npc!).Value < -20)
            return new(false, "That person refuses to respond.");

        Open(npc!, actor!);
        return new(true, $"You speak with {npc!.Role}.");
    }

    internal DaggerfallActivationOutcome ApplyAction(DaggerfallPlayerUiAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Kind == DaggerfallUiActionKind.DialogueClose)
        {
            if (!MatchesRevision(action.Revision)) return Reject("That conversation has already ended.");
            Close();
            return Report(new(true, "You end the conversation."));
        }

        if (action.Kind is not (DaggerfallUiActionKind.DialogueTone or DaggerfallUiActionKind.DialogueTopic) || !MatchesRevision(action.Revision))
            return Reject("That conversation choice is no longer current.");
        if (!ValidateCurrent(out DaggerfallNpc? npc, out DaggerfallDialogueNpc? actor, out DaggerfallSiteRecord? site))
        {
            Close();
            return Reject("That person has moved or is no longer available.");
        }

        if (action.Kind == DaggerfallUiActionKind.DialogueTone)
        {
            if (!TryParseTone(action.Tone, out DaggerfallDialogueTone tone))
                return Reject("That tone is not available.");
            _current!.Tone = tone;
            _current.Question = null;
            _current.Reply = null;
            _current.Diagnostics.Clear();
            Publish(npc!, site!);
            return Report(new(true, $"You choose a {tone.ToString().ToLowerInvariant()} tone."));
        }

        if (!TryParseTopic(action.Topic, out DaggerfallDialogueTopic topic, out string? topicTarget))
            return Reject("That topic is not available.");
        return ResolveTopic(npc!, actor!, site!, topic, topicTarget);
    }

    internal DaggerfallNpc? CurrentNpc(string? revision = null) =>
        (revision is null || MatchesRevision(revision)) && ValidateCurrent(out var npc, out _, out _) ? npc : null;

    internal void Rebase(System.Numerics.Vector3 delta)
    {
        if (_current is { } current)
            current.Position = DaggerfallExteriorSessionOrigin.Shift(current.Position, delta);
    }

    internal void RefreshEligibility()
    {
        if (_current is null) return;
        if (!ValidateCurrent(out var npc, out _, out var site)) Close();
        else Publish(npc!, site);
    }

    internal void Close()
    {
        _current = null;
        _publish(null);
    }

    private void Open(DaggerfallNpc npc, DaggerfallDialogueNpc actor)
    {
        DaggerfallFactionReaction reaction = _social.ReactionForNpc(npc);
        int greetingId = reaction.Value >= 30 ? 7209 : reaction.Value >= 10 ? 7208 : reaction.Value >= 0 ? 7207 : 7206;
        DaggerfallTextKey greetingKey = Resource(greetingId);
        DaggerfallTextContext greetingContext = Context(npc, _activeSite(), "");
        (string greeting, IReadOnlyList<string> diagnostics) = RenderSelectedRun(
            greetingKey,
            greetingContext,
            $"open:{npc.DurableId}:{_nextRevision}:greeting",
            run => !run.Contains("%oth", StringComparison.Ordinal) || greetingContext.Faction.Oath is not null);

        string revision = checked(++_nextRevision).ToString(CultureInfo.InvariantCulture);
        _current = new TalkSession(npc.DurableId, actor.Entity, actor.Position, npc.Site, revision, greeting)
        {
            Tone = DaggerfallDialogueTone.Normal,
        };
        _current.Diagnostics.AddRange(diagnostics);
        if (TryReadOpeningLine(_current, npc, _activeSite(), out string? opening, out IReadOnlyList<string> openingDiagnostics))
            _current.OpeningLine = opening;
        _current.Diagnostics.AddRange(openingDiagnostics);
        Publish(npc, _activeSite());
    }

    private DaggerfallActivationOutcome ResolveTopic(DaggerfallNpc npc, DaggerfallDialogueNpc actor, DaggerfallSiteRecord site,
        DaggerfallDialogueTopic topic, string? topicTarget)
    {
        TalkSession session = _current!;
        int socialGroup = ResolveSocialGroup(npc);
        bool directions = topic == DaggerfallDialogueTopic.Directions;
        bool questTopic = topic == DaggerfallDialogueTopic.QuestInfo;
        bool work = topic == DaggerfallDialogueTopic.Work;
        DaggerfallDialogueDestination? selectedDirection = directions
            ? _resolveDirection?.Invoke(topicTarget)
            : null;
        if (directions && topicTarget is not null && selectedDirection is null)
            return Reject("That place is no longer in the directory.");
        if (questTopic && (topicTarget is null || !_questTopics(npc.DurableId).Any(value => value.Id == topicTarget)))
            return Reject("That quest topic is no longer available.");

        int questionModifier = directions ? LocationQuestionReaction : NormalQuestionReaction;
        int band = ReactionBand(session, npc, socialGroup, questionModifier, topic);
        (string Name, string Hint)? destination = selectedDirection is { } resolved
            ? (resolved.Name, resolved.Hint)
            : directions ? _directions?.Invoke() : null;
        DaggerfallTextContext context = Context(npc, site, session.OpeningLine ?? string.Empty, destination?.Name, destination?.Hint);

        DaggerfallTextKey questionKey = directions
            ? Resource(7225 + (int)session.Tone)
            : Resource((work || questTopic ? 7212 : 7231) + (int)session.Tone);
        (session.Question, IReadOnlyList<string> questionDiagnostics) = RenderSelectedRun(
            questionKey,
            context,
            $"{session.Revision}:{session.QuestionCount}:{topic}:question");
        session.Diagnostics.Clear();
        session.Diagnostics.AddRange(questionDiagnostics);

        if (directions)
        {
            int responseId = selectedDirection is { Known: false }
                ? NonDirectionAnswers[15 + 3 * socialGroup + band]
                : DirectionAnswers[15 + 3 * socialGroup + band];
            DaggerfallTextContext responseContext = Context(npc, site, session.OpeningLine ?? string.Empty,
                subject: destination?.Name ?? site.Name, hint: destination?.Hint ?? "here");
            (session.Reply, IReadOnlyList<string> responseDiagnostics) = RenderSelectedRun(
                Resource(responseId), responseContext, $"{session.Revision}:{session.QuestionCount}:directions:answer");
            session.Diagnostics.AddRange(responseDiagnostics);
        }
        else if (questTopic)
        {
            if (topicTarget is null || _resolveQuestTopic?.Invoke(npc.DurableId, topicTarget) is not { } questAnswer)
                return Reject("That quest topic is no longer available.");
            session.Reply = questAnswer.Text;
            session.Diagnostics.AddRange(questAnswer.Diagnostics);
        }
        else if (work)
        {
            int workId = _workAvailable(npc.DurableId) ? 8075 + band : 8078;
            (session.Reply, IReadOnlyList<string> workDiagnostics) = RenderSelectedRun(
                Resource(workId), context, $"{session.Revision}:{session.QuestionCount}:work:answer");
            session.Diagnostics.AddRange(workDiagnostics);
        }
        else
        {
            session.Reply = ResolveNews(npc, site, session, context, out IReadOnlyList<string> newsDiagnostics);
            session.Diagnostics.AddRange(newsDiagnostics);
        }

        session.QuestionCount++;
        Publish(npc, site);
        string answer = session.Reply ?? "The person has no answer.";
        return Report(new(true, answer));
    }

    private string ResolveNews(DaggerfallNpc npc, DaggerfallSiteRecord site, TalkSession session,
        DaggerfallTextContext context, out IReadOnlyList<string> diagnostics)
    {
        if (session.NewsAnswered)
        {
            (string repeated, diagnostics) = RenderSelectedRun(Resource(1457), context,
                $"{session.Revision}:{session.QuestionCount}:news:empty");
            return repeated;
        }
        session.NewsAnswered = true;

        long currentMinute = _currentCalendarMinute();
        DaggerfallRumorDefinition[] candidates = _definitions.Rumors.Entries
            .Where(rumor => IsAmbientNewsCandidate(rumor, site, session))
            .Where(rumor => rumor.TimeLimit <= 0 || rumor.TimeLimit > currentMinute)
            .OrderBy(rumor => rumor.Index)
            .ToArray();
        if (candidates.Length == 0)
        {
            (string empty, diagnostics) = RenderSelectedRun(Resource(1457), context,
                $"{session.Revision}:{session.QuestionCount}:news:empty");
            return empty;
        }

        DaggerfallRumorDefinition rumor = candidates[Draw(session, $"{session.QuestionCount}:news:select", 0, candidates.Length - 1)];
        DaggerfallTextRenderResult result = _text.Resolve(rumor.TextKey, RumorContext(npc, site, rumor));
        diagnostics = [.. result.Diagnostics.Select(diagnostic => $"{diagnostic.Kind}: {diagnostic.Detail}")];
        return result.Text;
    }

    private int ReactionBand(TalkSession session, DaggerfallNpc npc, int socialGroup, int questionModifier, DaggerfallDialogueTopic topic)
    {
        if (!session.ToneModifiers.TryGetValue(session.Tone, out int toneModifier))
        {
            toneModifier = session.Tone switch
            {
                DaggerfallDialogueTone.Polite => EtiquetteReactionMods[Math.Min(socialGroup, EtiquetteReactionMods.Length - 1)] + SkillModifier(session, "etiquette", DaggerfallSkillUseReason.DialogueEtiquette),
                DaggerfallDialogueTone.Blunt => StreetwiseReactionMods[Math.Min(socialGroup, StreetwiseReactionMods.Length - 1)] + SkillModifier(session, "streetwise", DaggerfallSkillUseReason.DialogueStreetwise),
                _ => 0,
            };
            session.ToneModifiers.Add(session.Tone, toneModifier);
        }

        int personality = _playerStats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Personality.Value)).ValueInt;
        int reaction = checked(personality / 5 + questionModifier + toneModifier);
        int rollToBeat = Draw(session, $"{session.QuestionCount}:{topic}:reaction", 0, 20);
        return reaction < rollToBeat ? 0 : reaction < rollToBeat + 20 ? 1 : 2;
    }

    private int SkillModifier(TalkSession session, string skill, DaggerfallSkillUseReason reason)
    {
        int skillValue = Math.Clamp(_playerStats.GetStat(StatId.Parse(skill)).ValueInt, 0, 100);
        if (session.SkillReasons.Add(reason))
            _skillUses.Record(new DaggerfallSkillUse(skill, reason, DaggerfallSkillUseOutcome.Accepted));
        return Draw(session, $"{session.QuestionCount}:{session.Tone}:skill", 1, 100) <= skillValue ? 5 : -10;
    }

    private int ResolveSocialGroup(DaggerfallNpc npc)
    {
        if (npc.Appearance.FactionId == 0) return 0;
        if (!_definitions.Factions.Factions.TryGetValue(npc.Appearance.FactionId, out DaggerfallFactionDefinition? faction))
            return 0;
        return faction.SocialGroup >= 5 ? Merchants : Math.Clamp(faction.SocialGroup, 0, 4);
    }

    private bool IsAmbientNewsCandidate(DaggerfallRumorDefinition rumor, DaggerfallSiteRecord site, TalkSession session)
    {
        if (rumor.Region != site.Id.Region || rumor.IsQuestRumor || rumor.QuestId != 0 || rumor.IsSignMessage)
            return false;

        bool factionNews = IsNewsFaction(rumor.Faction1) || IsNewsFaction(rumor.Faction2);
        return !factionNews || Draw(session, $"{session.QuestionCount}:news:{rumor.Index}:faction-frequency", 1, 100) > 75;
    }

    private long _currentCalendarMinute() => _calendar().ToAbsoluteSeconds() / DaggerfallCalendar.SecondsPerMinute;

    private bool IsNewsFaction(int factionId) =>
        _definitions.Factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction)
        && (faction.Flags & 1) != 0;

    private DaggerfallTextContext RumorContext(DaggerfallNpc npc, DaggerfallSiteRecord site, DaggerfallRumorDefinition rumor)
    {
        DaggerfallTextContext context = Context(npc, site, string.Empty);
        string? first = _definitions.Factions.Factions.GetValueOrDefault(rumor.Faction1)?.Name;
        string? second = _definitions.Factions.Factions.GetValueOrDefault(rumor.Faction2)?.Name;
        DaggerfallFactionDefinition? province = _definitions.Factions.Factions.Values
            .Where(faction => faction.Type == 7 && faction.Region == site.Id.Region)
            .OrderBy(faction => faction.Id)
            .FirstOrDefault();
        return context with
        {
            Faction = context.Faction with { NewsFactionOne = first, NewsFactionTwo = second, RegionInContext = province?.Name ?? site.Name },
            Location = context.Location with { Region = province?.Name ?? site.Name },
        };
    }

    private DaggerfallTextContext Context(DaggerfallNpc npc, DaggerfallSiteRecord? site, string opening,
        string? subject = null, string? hint = null)
    {
        DaggerfallCharacterIdentity player = _playerIdentity();
        DaggerfallFactionDefinition? faction = npc.Appearance.FactionId == 0
            ? null
            : _definitions.Factions.Factions.GetValueOrDefault(npc.Appearance.FactionId);
        DaggerfallFactionDefinition? province = site is null ? null : _definitions.Factions.Factions.Values
            .Where(value => value.Type == 7 && value.Region == site.Id.Region)
            .OrderBy(value => value.Id)
            .FirstOrDefault();
        string? oath = ResolveOath(faction?.Race ?? province?.Race);
        return new(
            new DaggerfallTextPlayerContext(Name: player.Name, FirstName: FirstName(player.Name), Race: player.RaceId),
            CalendarContext(),
            new DaggerfallTextLocationContext(
                City: site?.Name,
                Region: province?.Name,
                DialogSubject: subject ?? site?.Name,
                DialogHint: hint,
                AlternateDialogHint: hint),
            new DaggerfallTextFactionContext(
                NpcFaction: faction?.Name,
                FactionName: faction?.Name,
                RegionInContext: province?.Name,
                Oath: oath),
            new DaggerfallTextItemContext(),
            new DaggerfallTextStoryContext(GreetingOrFollowUp: opening));
    }

    private DaggerfallTextCalendarContext CalendarContext()
    {
        DaggerfallCalendar calendar = _calendar();
        return new(
            Date: $"{calendar.Month + 1}/{calendar.Day + 1}/{calendar.Year}",
            Time: $"{calendar.Hour:D2}:{calendar.Minute:D2}",
            DayNumber: (calendar.Day + 1).ToString(CultureInfo.InvariantCulture),
            MonthNumber: (calendar.Month + 1).ToString(CultureInfo.InvariantCulture),
            Year: calendar.Year.ToString(CultureInfo.InvariantCulture),
            Minute: calendar.Minute.ToString(CultureInfo.InvariantCulture),
            Hour: calendar.Hour.ToString(CultureInfo.InvariantCulture),
            Season: calendar.Season.ToString());
    }

    private string? ResolveOath(int? race)
    {
        if (race is not int donorRace || donorRace < 0) return null;
        DaggerfallTextKey key = Resource(201 + donorRace);
        if (_definitions.Text.Resolve(key, out DaggerfallTextValue? value) != DaggerfallTextResolution.Resolved) return null;
        return value!.TextRuns.FirstOrDefault();
    }

    private bool TryReadOpeningLine(TalkSession session, DaggerfallNpc npc, DaggerfallSiteRecord? site,
        out string? opening, out IReadOnlyList<string> diagnostics)
    {
        DaggerfallTextKey key = Resource(7215 + (int)session.Tone);
        (opening, diagnostics) = RenderSelectedRun(key, Context(npc, site, string.Empty),
            $"{session.Revision}:opening", run => !run.Contains("%n", StringComparison.Ordinal));
        return !string.IsNullOrWhiteSpace(opening);
    }

    private (string Text, IReadOnlyList<string> Diagnostics) RenderSelectedRun(
        DaggerfallTextKey key,
        DaggerfallTextContext context,
        string drawKey,
        Func<string, bool>? eligible = null)
    {
        DaggerfallTextValue value = _definitions.Text.Require(key);
        string[] runs = value.TextRuns.Where(run => eligible is null || eligible(run)).ToArray();
        if (runs.Length == 0)
            return (string.Empty, [$"{DaggerfallTextDiagnosticKind.MissingContext}: No source text variant for '{key}' has the context this talk operation can resolve."]);
        int selected = checked((int)_random.DrawKeyed(new KeyedRngRequest(
            CombatRandomKey.Seed,
            RandomScope,
            $"{drawKey}:variant",
            0,
            runs.Length - 1)).Value);
        DaggerfallTextRenderResult rendered = _text.ResolveRaw(runs[selected], key, context);
        return (rendered.Text, [.. rendered.Diagnostics.Select(diagnostic => $"{diagnostic.Kind}: {diagnostic.Detail}")]);
    }

    private bool ValidateCurrent(out DaggerfallNpc? npc, out DaggerfallDialogueNpc? actor, out DaggerfallSiteRecord? site)
    {
        npc = null;
        actor = null;
        site = _activeSite();
        TalkSession? session = _current;
        if (session is null || site is null || !TryReadLiveNpc(session.TargetId, out npc, out actor)) return false;
        return npc!.Site == session.Site
            && actor!.Entity == session.Actor
            && actor.Position == session.Position;
    }

    private bool TryReadLiveNpc(long id, out DaggerfallNpc? npc, out DaggerfallDialogueNpc? actor)
    {
        npc = null;
        actor = null;
        DaggerfallSiteRecord? site = _activeSite();
        if (site is null) return false;
        try { npc = _npcs.Require(id); }
        catch (InvalidOperationException) { return false; }
        if (_muted(id) || !IsTalkableAt(npc!, site)) return false;
        if (_actors.TryGet(id, out ActorState currentActor))
            actor = new(currentActor.Actor.Entity, currentActor.Position);
        else if (_actors.Entities.TryResolve(ActorsState.Identity(id), out var entity)
            && _actors.Store.TryGet<DaggerfallNpcBody>(entity, out var body))
            actor = new(entity, body.Pose.Position);
        return actor is not null;
    }

    private static bool IsTalkableAt(DaggerfallNpc npc, DaggerfallSiteRecord site) =>
        npc.Presence == DaggerfallNpcPresence.Active
        && npc.Services.Contains("talk", StringComparer.Ordinal)
        && (npc.Profile is { } profile ? profile.Site == site.Id
            : npc.Site.Region == site.Id.Region && string.Equals(npc.Site.Location, site.Name, StringComparison.Ordinal));

    private bool MatchesRevision(string? revision) =>
        _current is { } current && string.Equals(current.Revision, revision, StringComparison.Ordinal);

    private void Publish(DaggerfallNpc npc, DaggerfallSiteRecord? site)
    {
        if (_current is not { } session) { _publish(null); return; }
        List<DaggerfallDialogueTopicOption> topics = [new("directions", "Where is this place?"), new("news", "Any news?")];
        if (npc.Services.Contains("quest", StringComparer.Ordinal))
            topics.Add(new("work", "Do you know of any work?"));
        if (_directionDirectory is not null)
            topics.AddRange(_directionDirectory());
        foreach (DaggerfallQuestDialogueTopic topic in _questTopics(npc.DurableId))
            topics.Add(new(topic.Id, topic.Label));
        _publish(new DaggerfallDialogueView(
            session.Revision,
            npc.Role,
            session.Greeting,
            session.Tone.ToString().ToLowerInvariant(),
            session.Question,
            session.Reply,
            topics,
            [.. session.Diagnostics]) { QuestContacts = _questContacts(npc.DurableId) });
    }

    private DaggerfallActivationOutcome Reject(string message) => Report(new(false, message));
    private DaggerfallActivationOutcome Report(DaggerfallActivationOutcome outcome)
    {
        _setOutcome(outcome.Message);
        return outcome;
    }

    private static bool TryParseTone(string? value, out DaggerfallDialogueTone tone)
    {
        tone = value switch
        {
            "polite" => DaggerfallDialogueTone.Polite,
            "normal" => DaggerfallDialogueTone.Normal,
            "blunt" => DaggerfallDialogueTone.Blunt,
            _ => default,
        };
        return value is "polite" or "normal" or "blunt";
    }

    private static bool TryParseTopic(string? value, out DaggerfallDialogueTopic topic, out string? target)
    {
        target = null;
        topic = value switch
        {
            "directions" => DaggerfallDialogueTopic.Directions,
            "news" => DaggerfallDialogueTopic.News,
            "work" => DaggerfallDialogueTopic.Work,
            _ => default,
        };
        if (value is "directions" or "news" or "work") return true;
        if (value is { Length: > 10 } dynamicDirection && dynamicDirection.StartsWith("direction:", StringComparison.Ordinal))
        {
            topic = DaggerfallDialogueTopic.Directions;
            target = dynamicDirection[10..];
            return target.Length > 0;
        }
        if (value is { Length: > 11 } dynamicQuest && (dynamicQuest.StartsWith("quest-info:", StringComparison.Ordinal)
                || dynamicQuest.StartsWith("quest-rumor:", StringComparison.Ordinal)))
        {
            topic = DaggerfallDialogueTopic.QuestInfo;
            target = dynamicQuest;
            return true;
        }
        return false;
    }

    private static DaggerfallTextKey Resource(int id) => new(DaggerfallTextKind.Resource, id.ToString(CultureInfo.InvariantCulture));
    private static string FirstName(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;

    private int Draw(TalkSession session, string purpose, int minimum, int maximum) =>
        checked((int)_random.DrawKeyed(new KeyedRngRequest(
            CombatRandomKey.Seed,
            RandomScope,
            $"session:{session.Revision}:npc:{session.TargetId}:{purpose}",
            minimum,
            maximum)).Value);

    private sealed record DaggerfallDialogueNpc(EntityId Entity, WorldPoint Position);

    private sealed class TalkSession(long targetId, EntityId actor, WorldPoint position, DaggerfallNpcSite site, string revision, string greeting)
    {
        internal long TargetId { get; } = targetId;
        internal EntityId Actor { get; } = actor;
        internal WorldPoint Position { get; set; } = position;
        internal DaggerfallNpcSite Site { get; } = site;
        internal string Revision { get; } = revision;
        internal string Greeting { get; } = greeting;
        internal DaggerfallDialogueTone Tone { get; set; }
        internal string? OpeningLine { get; set; }
        internal string? Question { get; set; }
        internal string? Reply { get; set; }
        internal int QuestionCount { get; set; }
        internal bool NewsAnswered { get; set; }
        internal Dictionary<DaggerfallDialogueTone, int> ToneModifiers { get; } = [];
        internal HashSet<DaggerfallSkillUseReason> SkillReasons { get; } = [];
        internal List<string> Diagnostics { get; } = [];
    }
}

/// <summary>DaggerfallSession composition and strict semantic-action boundary for the dialogue owner.</summary>
internal sealed partial class DaggerfallSession
{
    private bool ApplyDialogueAction(DaggerfallPlayerUiAction action)
    {
        if (action.Kind is not (DaggerfallUiActionKind.DialogueTone or DaggerfallUiActionKind.DialogueTopic or DaggerfallUiActionKind.DialogueClose) || _dialogue is null)
            return false;
        _dialogue.ApplyAction(action);
        return true;
    }
}
