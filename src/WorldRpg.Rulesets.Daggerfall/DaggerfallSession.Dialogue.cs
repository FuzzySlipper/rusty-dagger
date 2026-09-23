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
internal enum DaggerfallDialogueTopic { Directions, News }

internal sealed record DaggerfallDialogueTopicOption(string Id, string Label);
internal sealed record DaggerfallDialogueView(
    string Revision,
    string TargetLabel,
    string Greeting,
    string Tone,
    string? Question,
    string? Reply,
    IReadOnlyList<DaggerfallDialogueTopicOption> Topics,
    IReadOnlyList<string> Diagnostics);

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
        Action<string> setOutcome)
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
        _setOutcome = setOutcome ?? throw new ArgumentNullException(nameof(setOutcome));
    }

    public IEnumerable<DaggerfallActivationTarget> NpcTargets()
    {
        DaggerfallSiteRecord? site = _activeSite();
        if (site is null) yield break;
        foreach (DaggerfallNpc npc in _npcs.All)
        {
            if (!IsTalkableAt(npc, site) || !_actors.TryGet(npc.DurableId, out ActorState actor)) continue;
            yield return new(
                DaggerfallActivationTargetKind.Npc,
                ActorsState.Identity(npc.DurableId),
                actor.Actor.Entity,
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
            || !TryReadLiveNpc(checked((long)selection.Target.Identity.Value), out DaggerfallNpc? npc, out ActorState? actor)
            || actor is null
            || actor.Actor.Entity != selection.Target.Entity)
            return new(false, "That person is no longer available here.");

        Open(npc!, actor!);
        return new(true, $"You speak with {npc!.Role}.");
    }

    internal DaggerfallActivationOutcome ApplyAction(DaggerfallPlayerUiAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Action == "dialogue-close")
        {
            if (!MatchesRevision(action.Revision)) return Reject("That conversation has already ended.");
            Close();
            return Report(new(true, "You end the conversation."));
        }

        if (action.Action is not ("dialogue-tone" or "dialogue-topic") || !MatchesRevision(action.Revision))
            return Reject("That conversation choice is no longer current.");
        if (!ValidateCurrent(out DaggerfallNpc? npc, out ActorState? actor, out DaggerfallSiteRecord? site))
        {
            Close();
            return Reject("That person has moved or is no longer available.");
        }

        if (action.Action == "dialogue-tone")
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

        if (!TryParseTopic(action.Topic, out DaggerfallDialogueTopic topic))
            return Reject("That topic is not available.");
        return ResolveTopic(npc!, actor!, site!, topic);
    }

    internal void Close()
    {
        _current = null;
        _publish(null);
    }

    private void Open(DaggerfallNpc npc, ActorState actor)
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
        _current = new TalkSession(npc.DurableId, actor.Actor.Entity, actor.Position, npc.Site, revision, greeting)
        {
            Tone = DaggerfallDialogueTone.Normal,
        };
        _current.Diagnostics.AddRange(diagnostics);
        if (TryReadOpeningLine(_current, npc, _activeSite(), out string? opening, out IReadOnlyList<string> openingDiagnostics))
            _current.OpeningLine = opening;
        _current.Diagnostics.AddRange(openingDiagnostics);
        Publish(npc, _activeSite());
    }

    private DaggerfallActivationOutcome ResolveTopic(DaggerfallNpc npc, ActorState actor, DaggerfallSiteRecord site, DaggerfallDialogueTopic topic)
    {
        TalkSession session = _current!;
        int socialGroup = ResolveSocialGroup(npc);
        int questionModifier = topic == DaggerfallDialogueTopic.Directions ? LocationQuestionReaction : NormalQuestionReaction;
        int band = ReactionBand(session, npc, socialGroup, questionModifier, topic);
        DaggerfallTextContext context = Context(npc, site, session.OpeningLine ?? string.Empty);

        DaggerfallTextKey questionKey = topic == DaggerfallDialogueTopic.Directions
            ? Resource(7225 + (int)session.Tone)
            : Resource(7231 + (int)session.Tone);
        (session.Question, IReadOnlyList<string> questionDiagnostics) = RenderSelectedRun(
            questionKey,
            context,
            $"{session.Revision}:{session.QuestionCount}:{topic}:question");
        session.Diagnostics.Clear();
        session.Diagnostics.AddRange(questionDiagnostics);

        if (topic == DaggerfallDialogueTopic.Directions)
        {
            int responseId = DirectionAnswers[15 + 3 * socialGroup + band];
            DaggerfallTextContext responseContext = Context(npc, site, session.OpeningLine ?? string.Empty,
                subject: site.Name, hint: "here");
            (session.Reply, IReadOnlyList<string> responseDiagnostics) = RenderSelectedRun(
                Resource(responseId), responseContext, $"{session.Revision}:{session.QuestionCount}:directions:answer");
            session.Diagnostics.AddRange(responseDiagnostics);
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

        DaggerfallRumorDefinition[] candidates = _definitions.Rumors.Entries
            .Where(rumor => IsAmbientNewsCandidate(rumor, site, session))
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
            new DaggerfallTextCalendarContext(),
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

    private bool ValidateCurrent(out DaggerfallNpc? npc, out ActorState? actor, out DaggerfallSiteRecord? site)
    {
        npc = null;
        actor = null;
        site = _activeSite();
        TalkSession? session = _current;
        if (session is null || site is null || !TryReadLiveNpc(session.TargetId, out npc, out actor)) return false;
        return npc!.Site == session.Site
            && actor!.Actor.Entity == session.Actor
            && actor.Position == session.Position;
    }

    private bool TryReadLiveNpc(long id, out DaggerfallNpc? npc, out ActorState? actor)
    {
        npc = null;
        actor = null;
        DaggerfallSiteRecord? site = _activeSite();
        if (site is null) return false;
        try { npc = _npcs.Require(id); }
        catch (InvalidOperationException) { return false; }
        if (!IsTalkableAt(npc!, site) || !_actors.TryGet(id, out ActorState currentActor)) return false;
        actor = currentActor;
        return true;
    }

    private static bool IsTalkableAt(DaggerfallNpc npc, DaggerfallSiteRecord site) =>
        npc.Presence == DaggerfallNpcPresence.Active
        && npc.Services.Contains("talk", StringComparer.Ordinal)
        && npc.Site.Region == site.Id.Region
        && string.Equals(npc.Site.Location, site.Name, StringComparison.Ordinal);

    private bool MatchesRevision(string? revision) =>
        _current is { } current && string.Equals(current.Revision, revision, StringComparison.Ordinal);

    private void Publish(DaggerfallNpc npc, DaggerfallSiteRecord? site)
    {
        if (_current is not { } session) { _publish(null); return; }
        _publish(new DaggerfallDialogueView(
            session.Revision,
            npc.Role,
            session.Greeting,
            session.Tone.ToString().ToLowerInvariant(),
            session.Question,
            session.Reply,
            [new("directions", "Where is this place?"), new("news", "Any news?")],
            [.. session.Diagnostics]));
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

    private static bool TryParseTopic(string? value, out DaggerfallDialogueTopic topic)
    {
        topic = value switch
        {
            "directions" => DaggerfallDialogueTopic.Directions,
            "news" => DaggerfallDialogueTopic.News,
            _ => default,
        };
        return value is "directions" or "news";
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

    private sealed class TalkSession(long targetId, EntityId actor, WorldPoint position, DaggerfallNpcSite site, string revision, string greeting)
    {
        internal long TargetId { get; } = targetId;
        internal EntityId Actor { get; } = actor;
        internal WorldPoint Position { get; } = position;
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
        if (action.Action is not ("dialogue-tone" or "dialogue-topic" or "dialogue-close") || _dialogue is null)
            return false;
        _dialogue.ApplyAction(action);
        return true;
    }
}
