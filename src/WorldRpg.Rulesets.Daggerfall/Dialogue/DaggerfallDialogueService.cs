using System.Globalization;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Targeting;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Daggerfall talk policy over the existing NPC identity and actor owners. The owner admits only a
/// current talk-service target at the active site; a DOM choice is rechecked against the same actor,
/// presence, site, and pose before it can resolve.
/// </summary>
internal sealed class DaggerfallDialogueService : IDaggerfallNpcActivationOwner
{
    /// <summary>The guild provider topic a guild dialogue topic asks.</summary>
    private static DaggerfallGuildProviderTopic GuildTopic(DaggerfallDialogueTopic topic) => topic switch
    {
        DaggerfallDialogueTopic.RankReview => DaggerfallGuildProviderTopic.RankReview,
        DaggerfallDialogueTopic.Armor => DaggerfallGuildProviderTopic.Armor,
        DaggerfallDialogueTopic.House => DaggerfallGuildProviderTopic.House,
        DaggerfallDialogueTopic.Join => DaggerfallGuildProviderTopic.Join,
        DaggerfallDialogueTopic.Teleport => DaggerfallGuildProviderTopic.Teleport,
        DaggerfallDialogueTopic.Spymaster => DaggerfallGuildProviderTopic.Spymaster,
        DaggerfallDialogueTopic.RestoreAttributes => DaggerfallGuildProviderTopic.RestoreAttributes,
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic,
            "This dialogue topic is not a guild provider service."),
    };

    private const int Merchants = 1;
    private const int NormalQuestionReaction = 0;
    private const int LocationQuestionReaction = 5;
    private const string RandomScope = "daggerfall.dialogue.v1";

    private static readonly int[] EtiquetteReactionMods = [-10, 5, 10, 15, -15];
    private static readonly int[] StreetwiseReactionMods = [10, 5, -10, -15, 15];
    // FALL.EXE's knowledge modifiers, indexed by classic question type then social group.
    // The two direction question types retained here are LocalBuilding/Regional (0) and
    // Person (1); the remaining source entries stay documented in the donor rather than being
    // copied into a product owner that does not expose those topics yet.
    private static readonly int[] KnowledgeModifiers = [5, 7, 0, 0, 4, 1, 2, -2, 3, 7];
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
    private readonly Func<int> _questRumorCount;
    private readonly Func<int, (string Text, IReadOnlyList<string> Diagnostics)>? _resolveQuestRumor;
    private readonly Func<long, IReadOnlyList<DaggerfallDialogueTopicOption>> _questTopics;
    private readonly Func<long, string, (string Text, IReadOnlyList<string> Diagnostics)?>? _resolveQuestTopic;
    private readonly Func<DaggerfallCalendar> _calendar;
    private readonly Func<DaggerfallTextContext>? _currentText;
    private readonly Func<long, bool> _muted;
    private readonly Func<long, IReadOnlyList<DaggerfallQuestContact>> _questContacts;
    private readonly Func<long, bool> _workAvailable;
    private readonly Func<long, string>? _offerWork;
    private readonly Func<DaggerfallVariableAddress, bool>? _readVariable;
    private readonly Func<DaggerfallWorldProfileKey>? _activeProfile;
    private readonly Func<string, bool>? _discloseDirection;
    private readonly DaggerfallDialogueWorldState _dialogueWorld;
    private readonly Func<DaggerfallNpc, DaggerfallDialogueTopic, ulong?, DaggerfallTempleServiceResult>? _templeService;
    private readonly Func<DaggerfallNpc, DaggerfallGuildProviderTopic, string?, DaggerfallGuildProviderResult>? _guildService;
    private readonly Func<bool> _suppressTalk;
    private readonly Func<DaggerfallNpc, DaggerfallGuildProviderResult?>? _guildWelcome;
    private readonly Func<DaggerfallSiteRecord, DaggerfallNpc, string?>? _issueReferral;
    private readonly Func<DaggerfallNpc, IReadOnlyList<DaggerfallDialogueTopicOption>>? _partyHireTopics;
    private readonly Func<DaggerfallNpc, DaggerfallDialogueTopic, DaggerfallPartyHireResult>? _partyHireService;
    private readonly Func<DaggerfallNpc, DaggerfallIntroductionAccess>? _introductionAccess;
    private readonly Func<DaggerfallNpc, DaggerfallSiteRecord?, IReadOnlyList<DaggerfallIntroductionTarget>>? _introductionTargets;
    private readonly Func<DaggerfallNpc, long, DaggerfallSiteRecord?, DaggerfallIntroductionResult>? _requestIntroduction;
    private readonly Func<DaggerfallNpc, bool>? _canCurryFavour;
    private readonly Func<DaggerfallNpc, bool, bool, DaggerfallFavourResult>? _curryFavour;
    private readonly IDaggerfallWorldConditionSource? _worldConditions;
    private readonly Func<DaggerfallSiteRecord, string>? _readNoticeBoard;
    private readonly Func<int, DaggerfallSiteRecord?, DaggerfallGateScrutinyAssessment>? _evaluateGateScrutiny;
    private readonly DaggerfallWordSpread? _wordSpread;
    private readonly DaggerfallCourierDispatches? _courierDispatches;
    private readonly DaggerfallNoticeBounties? _noticeBounties;
    private readonly DaggerfallOfficeHolders? _officeHolders;
    private readonly DaggerfallOfficeLedger? _officeLedger;
    private readonly DaggerfallActorLedger? _actorLedger;
    private readonly Func<DaggerfallSiteId, DaggerfallSiteRecord?>? _findSite;
    private readonly Func<DaggerfallNpc, DaggerfallOfficeExileSave, DaggerfallExileRestoreResult>? _restoreExile;
    private readonly Func<DaggerfallNpc, DaggerfallSiteRecord?, IReadOnlyList<DaggerfallHolderDenounceTarget>>? _denounceTargets;
    private readonly Func<DaggerfallNpc, long, DaggerfallSiteRecord?, DaggerfallHolderDenounceResult>? _denounceOfficeHolder;
    private readonly Func<ActorVouchRecord, bool>? _isVouchUsable;
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
        Func<long, IReadOnlyList<DaggerfallDialogueTopicOption>>? questTopics = null,
        Func<long, string, (string Text, IReadOnlyList<string> Diagnostics)?>? resolveQuestTopic = null,
        Func<DaggerfallCalendar>? calendar = null,
        Func<long, bool>? workAvailable = null,
        Func<long, string>? offerWork = null,
        Func<DaggerfallVariableAddress, bool>? readVariable = null,
        Func<DaggerfallNpc, DaggerfallDialogueTopic, ulong?, DaggerfallTempleServiceResult>? templeService = null,
        Func<DaggerfallNpc, DaggerfallGuildProviderTopic, string?, DaggerfallGuildProviderResult>? guildService = null,
        Func<DaggerfallWorldProfileKey>? activeProfile = null,
        Func<string, bool>? discloseDirection = null,
        DaggerfallDialogueWorldState? dialogueWorld = null, Func<bool>? suppressTalk = null,
        Func<int>? questRumorCount = null,
        Func<int, (string Text, IReadOnlyList<string> Diagnostics)>? resolveQuestRumor = null,
        Func<DaggerfallNpc, DaggerfallGuildProviderResult?>? guildWelcome = null,
        Func<DaggerfallTextContext>? currentText = null,
        Func<DaggerfallSiteRecord, DaggerfallNpc, string?>? issueReferral = null,
        Func<DaggerfallNpc, IReadOnlyList<DaggerfallDialogueTopicOption>>? partyHireTopics = null,
        Func<DaggerfallNpc, DaggerfallDialogueTopic, DaggerfallPartyHireResult>? partyHireService = null,
        Func<DaggerfallNpc, DaggerfallIntroductionAccess>? introductionAccess = null,
        Func<DaggerfallNpc, DaggerfallSiteRecord?, IReadOnlyList<DaggerfallIntroductionTarget>>? introductionTargets = null,
        Func<DaggerfallNpc, long, DaggerfallSiteRecord?, DaggerfallIntroductionResult>? requestIntroduction = null,
        Func<DaggerfallNpc, bool>? canCurryFavour = null,
        Func<DaggerfallNpc, bool, bool, DaggerfallFavourResult>? curryFavour = null,
        IDaggerfallWorldConditionSource? worldConditions = null,
        Func<DaggerfallSiteRecord, string>? readNoticeBoard = null,
        Func<int, DaggerfallSiteRecord?, DaggerfallGateScrutinyAssessment>? evaluateGateScrutiny = null,
        DaggerfallWordSpread? wordSpread = null,
        DaggerfallCourierDispatches? courierDispatches = null,
        DaggerfallOfficeHolders? officeHolders = null,
        DaggerfallOfficeLedger? officeLedger = null,
        DaggerfallActorLedger? actorLedger = null,
        Func<DaggerfallSiteId, DaggerfallSiteRecord?>? findSite = null,
        Func<DaggerfallNpc, DaggerfallOfficeExileSave, DaggerfallExileRestoreResult>? restoreExile = null,
        Func<DaggerfallNpc, DaggerfallSiteRecord?, IReadOnlyList<DaggerfallHolderDenounceTarget>>? denounceTargets = null,
        Func<DaggerfallNpc, long, DaggerfallSiteRecord?, DaggerfallHolderDenounceResult>? denounceOfficeHolder = null,
        Func<ActorVouchRecord, bool>? isVouchUsable = null,
        DaggerfallNoticeBounties? noticeBounties = null)
    {
        _noticeBounties = noticeBounties;
        _suppressTalk = suppressTalk ?? (() => false);
        _issueReferral = issueReferral;
        _partyHireTopics = partyHireTopics;
        _partyHireService = partyHireService;
        _introductionAccess = introductionAccess;
        _introductionTargets = introductionTargets;
        _requestIntroduction = requestIntroduction;
        _canCurryFavour = canCurryFavour;
        _curryFavour = curryFavour;
        _worldConditions = worldConditions;
        _readNoticeBoard = readNoticeBoard;
        _evaluateGateScrutiny = evaluateGateScrutiny;
        _wordSpread = wordSpread;
        _courierDispatches = courierDispatches;
        _officeHolders = officeHolders;
        _officeLedger = officeLedger;
        _actorLedger = actorLedger;
        _findSite = findSite;
        _restoreExile = restoreExile;
        _denounceTargets = denounceTargets;
        _denounceOfficeHolder = denounceOfficeHolder;
        _isVouchUsable = isVouchUsable;
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
        _questRumorCount = questRumorCount ?? (() => 0);
        _resolveQuestRumor = resolveQuestRumor;
        _questTopics = questTopics ?? (_ => []);
        _resolveQuestTopic = resolveQuestTopic;
        _calendar = calendar ?? (() => DaggerfallCalendar.Start);
        _workAvailable = workAvailable ?? (_ => false);
        _offerWork = offerWork;
        _readVariable = readVariable;
        _activeProfile = activeProfile;
        _discloseDirection = discloseDirection;
        _dialogueWorld = dialogueWorld ?? new();
        _templeService = templeService;
        _guildService = guildService;
        _guildWelcome = guildWelcome;
        _currentText = currentText;
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
                Label: npc.DisplayName ?? npc.Role);
        }
    }

    internal IEnumerable<DaggerfallActivationTarget> QuestNpcTargets(Func<long, bool> bound)
    {
        foreach (var npc in _npcs.All)
            if (bound(npc.DurableId) && TryReadLiveNpc(npc.DurableId, out _, out var actor, allowMuted: true))
                yield return new(DaggerfallActivationTargetKind.Npc, ActorsState.Identity(npc.DurableId), actor!.Entity,
                    checked((ulong)npc.DurableId), actor.Position, 1, Label: npc.DisplayName ?? npc.Role);
    }

    internal bool IsCurrentNpcSelection(DaggerfallActivationSelection selection) =>
        TryReadLiveNpc(checked((long)selection.Target.Identity.Value), out _, out var actor, allowMuted: true)
        && actor!.Entity == selection.Target.Entity;

    public DaggerfallActivationOutcome ActivateNpc(DaggerfallActivationSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Target.Kind != DaggerfallActivationTargetKind.Npc
            || !TryReadLiveNpc(checked((long)selection.Target.Identity.Value), out DaggerfallNpc? npc, out DaggerfallDialogueNpc? actor)
            || actor is null
            || actor.Entity != selection.Target.Entity)
            return new(false, "That person is no longer available here.");
        // As the donor's PlayerActivate, Grab and Talk speak with anyone and Info speaks with a passer-by;
        // only a static person answers Info with their name (PresentNPCInfo). The mode is not a gate on talking.
        if (selection.Mode == DaggerfallActivationMode.Info && npc!.Kind == DaggerfallNpcKind.Static)
            return new(true, $"You see {npc.DisplayName ?? npc.Role}.");
        if (selection.Mode is not (DaggerfallActivationMode.Grab or DaggerfallActivationMode.Talk or DaggerfallActivationMode.Info))
            return new(false, "That person cannot be approached that way.");
        if (_suppressTalk()) return new(false, "You cannot talk while in beast form.");

        // TalkManager refuses a target whose persistent faction reaction is below -20 before it
        // opens the modal. Faction-zero civilians remain neutral through ReactionForNpc and can
        // still answer; unarrived faction standing wavefronts are subtracted outside the bubble.
        if (EffectiveReactionValue(npc!, _activeSite()) < -20)
            return new(false, "That person refuses to respond.");

        ActorOverlayComponent? overlay = null;
        ActorInteractionPolicyKind interactionKind = ActorInteractionPolicyKind.FullDialogue;
        if (_actors.TryGet(npc!.DurableId, out ActorState? spoken))
        {
            if (spoken.Actor.TryGet<ActorInteractionPolicy>(out ActorInteractionPolicy? policy))
                interactionKind = policy.Policy;
            if (spoken.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? attachedOverlay))
                overlay = attachedOverlay;
        }
        if (interactionKind == ActorInteractionPolicyKind.None)
            return new(false, "That person has nothing to say.");

        if (overlay is not null) overlay.Met = true;

        if (interactionKind == ActorInteractionPolicyKind.AmbientBark)
        {
            string bark = ResolveAmbientBark(npc!, overlay, _activeSite());
            return Report(new(true, bark));
        }

        Open(npc!, actor!);
        return new(true, $"You speak with {npc!.DisplayName ?? npc.Role}.");
    }

    /// <summary>Ends the conversation the player sees at this revision.</summary>
    internal DaggerfallActivationOutcome End(string? revision)
    {
        if (!MatchesRevision(revision)) return Reject("That conversation has already ended.");
        Close();
        return Report(new(true, "You end the conversation."));
    }

    /// <summary>Speaks in a tone ("polite", "normal" or "blunt") from now on in the conversation at this revision.</summary>
    internal DaggerfallActivationOutcome ChooseTone(string? revision, string? tone)
    {
        if (!AdmitChoice(revision, out DaggerfallNpc? npc, out _, out DaggerfallSiteRecord? site, out DaggerfallActivationOutcome? refused))
            return refused!;
        if (!TryParseTone(tone, out DaggerfallDialogueTone chosen))
            return Reject("That tone is not available.");
        _current!.Tone = chosen;
        _current.Question = null;
        _current.Reply = null;
        _current.Diagnostics.Clear();
        Publish(npc!, site!);
        return Report(new(true, $"You choose a {chosen.ToString().ToLowerInvariant()} tone."));
    }

    /// <summary>Asks about a topic in the conversation at this revision, with a donation's amount or a provider option's key.</summary>
    internal DaggerfallActivationOutcome Ask(string? revision, string? topic, ulong? amount, string? key)
    {
        if (!AdmitChoice(revision, out DaggerfallNpc? npc, out DaggerfallDialogueNpc? actor, out DaggerfallSiteRecord? site, out DaggerfallActivationOutcome? refused))
            return refused!;
        if (!TryParseTopic(topic, out DaggerfallDialogueTopic asked, out string? topicTarget))
            return Reject("That topic is not available.");
        return ResolveTopic(npc!, actor!, site!, asked, topicTarget, amount, key);
    }

    private bool AdmitChoice(string? revision, out DaggerfallNpc? npc, out DaggerfallDialogueNpc? actor, out DaggerfallSiteRecord? site,
        out DaggerfallActivationOutcome? refused)
    {
        npc = null;
        actor = null;
        site = null;
        refused = null;
        if (!MatchesRevision(revision))
        {
            refused = Reject("That conversation choice is no longer current.");
            return false;
        }
        if (!ValidateCurrent(out npc, out actor, out site))
        {
            Close();
            refused = Reject("That person has moved or is no longer available.");
            return false;
        }
        return true;
    }

    internal DaggerfallNpc? CurrentNpc(string? revision = null) =>
        (revision is null || MatchesRevision(revision)) && ValidateCurrent(out var npc, out _, out _) ? npc : null;

    /// <summary>
    /// The map directory and resolver use the same admission contract as activation. This keeps a
    /// durable registry row from becoming talkable after its actor or current profile has gone away.
    /// </summary>
    internal bool IsLiveTalkTarget(DaggerfallNpc npc) =>
        npc is not null && TryReadLiveNpc(npc.DurableId, out _, out _);

    /// <summary>Reads a live talk target's current local position from its actor or projection.</summary>
    internal bool TryReadLiveTalkPosition(DaggerfallNpc npc, out WorldPoint position)
    {
        position = default;
        if (npc is null || !TryReadLiveNpc(npc.DurableId, out _, out DaggerfallDialogueNpc? actor)) return false;
        position = actor!.Position;
        return true;
    }

    /// <summary>
    /// Reconciles generated spoken-world events with the live variable store. Calendar and quest
    /// owners call this after a transition, while a direct dialogue read calls it as a bounded
    /// catch-up for a variable changed between admitted updates.
    /// </summary>
    internal void SynchronizeWorldState()
    {
        if (_readVariable is { } read)
            _dialogueWorld.Synchronize(_definitions.DialogueWorldRules, read, _currentCalendarMinute());
    }

    internal void Rebase(System.Numerics.Vector3 delta)
    {
        if (_current is { } current)
            current.Position = DaggerfallSceneOrigin.Shift(current.Position, delta);
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
        DaggerfallSiteRecord? activeSite = _activeSite();
        int reactionValue = EffectiveReactionValue(npc, activeSite);
        int greetingId = reactionValue >= 30 ? 7209 : reactionValue >= 10 ? 7208 : reactionValue >= 0 ? 7207 : 7206;
        DaggerfallTextKey greetingKey = Resource(greetingId);
        DaggerfallTextContext greetingContext = Context(npc, activeSite, "");
        (string greeting, IReadOnlyList<string> diagnostics) = RenderSelectedRun(
            greetingKey,
            greetingContext,
            $"open:{npc.DurableId}:{_nextRevision}:greeting",
            run => !run.Contains("%oth", StringComparison.Ordinal) || greetingContext.Faction.Oath is not null);

        var benefit = _guildWelcome?.Invoke(npc);
        if (!string.IsNullOrEmpty(benefit?.Message)) greeting += "\n" + benefit.Message;
        string? factionLine = ResolveFactionGreetingLine(npc, activeSite);
        if (!string.IsNullOrWhiteSpace(factionLine)) greeting += "\n" + factionLine;
        string? officeLine = ResolveOfficeContinuityGreetingLine(npc, activeSite);
        if (!string.IsNullOrWhiteSpace(officeLine)) greeting += "\n" + officeLine;
        if (_officeLedger?.TryGetExile(npc.DurableId) is { } exiledHolder)
            greeting += "\n" + DaggerfallExileDialogue.BuildGreetingLine(npc, exiledHolder, _officeLedger, _officeHolders, _definitions);
        string? scrutinyLine = ResolveGuardScrutinyGreetingLine(npc, activeSite);
        if (!string.IsNullOrWhiteSpace(scrutinyLine)) greeting += "\n" + scrutinyLine;
        if (DaggerfallRoadCourierNews.TryResolveEncounter(npc, _actorLedger, _courierDispatches, _definitions, _findSite) is { } roadEncounter
            && DaggerfallRoadCourierNews.BuildGreetingLine(roadEncounter) is { Length: > 0 } roadGreeting)
            greeting += "\n" + roadGreeting;
        if (_actors.TryGet(npc.DurableId, out ActorState? liveActor)
            && liveActor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? openOverlay))
        {
            ActorVouchRecord? usableVouch = FirstUsableVouch(openOverlay);
            if (usableVouch is not null)
                greeting += "\n" + FormatReferralLine(npc, usableVouch.DisplaySummary());
            else if (openOverlay.ReferralTags.Count > 0)
                greeting += "\n" + FormatReferralLine(npc, openOverlay.ReferralTags[0]);
        }
        string revision = checked(++_nextRevision).ToString(CultureInfo.InvariantCulture);
        _current = new TalkSession(npc.DurableId, actor.Entity, actor.Position, npc.Site, revision, greeting)
        {
            Tone = DaggerfallDialogueTone.Normal,
        };
        if (benefit?.Options is { } options)
            _current.ProviderTopics.AddRange(options.Select(option => new DaggerfallDialogueTopicOption(option.Topic!, option.Label, option.Key)));
        _current.Diagnostics.AddRange(diagnostics);
        if (TryReadOpeningLine(_current, npc, _activeSite(), out string? opening, out IReadOnlyList<string> openingDiagnostics))
            _current.OpeningLine = opening;
        _current.Diagnostics.AddRange(openingDiagnostics);
        Publish(npc, _activeSite());
    }

    private DaggerfallActivationOutcome ResolveTopic(DaggerfallNpc npc, DaggerfallDialogueNpc actor, DaggerfallSiteRecord site,
        DaggerfallDialogueTopic topic, string? topicTarget, ulong? amount, string? key)
    {
        TalkSession session = _current!;
        if (topic == DaggerfallDialogueTopic.CourierNews)
        {
            DaggerfallRoadEncounterContext? encounter = DaggerfallRoadCourierNews.TryResolveEncounter(
                npc, _actorLedger, _courierDispatches, _definitions, _findSite);
            if (encounter is not { IsCourier: true })
                return Reject("This person has no regional courier news to share.");

            long nowMinute = _currentCalendarMinute();
            int nowDay = checked((int)_calendar().DayNumber);
            IReadOnlyList<DaggerfallCourierNewsItem> newsItems = DaggerfallRoadCourierNews.BuildNewsItems(
                encounter,
                npc,
                site,
                nowMinute,
                nowDay,
                _wordSpread,
                _worldConditions,
                _courierDispatches,
                _definitions,
                (rumor, rumorSite) => _text.Resolve(rumor.TextKey, RumorContext(npc, rumorSite, rumor.Faction1, rumor.Faction2)).Text);

            session.Question = $"What news do you bring from {encounter.OriginRegionName}?";
            if (newsItems.Count == 0)
            {
                session.Reply = DaggerfallRoadCourierNews.EmptyReply(encounter, npc, _courierDispatches);
            }
            else if (session.CourierNewsIndex < newsItems.Count)
            {
                session.Reply = newsItems[session.CourierNewsIndex++].Reply;
            }
            else
            {
                session.Reply = $"That is all the word I carried out of {encounter.OriginRegionName}.";
            }
            session.Diagnostics.Clear();
            session.QuestionCount++;
            Publish(npc, site);
            return Report(new(true, session.Reply));
        }
        if (topic is DaggerfallDialogueTopic.DispatchAccept or DaggerfallDialogueTopic.DispatchBuy
            or DaggerfallDialogueTopic.DispatchIntercept or DaggerfallDialogueTopic.DispatchTurnIn)
        {
            if (_courierDispatches is null || string.IsNullOrWhiteSpace(topicTarget))
                return Reject("That dispatch is no longer available.");
            long nowMinute = _currentCalendarMinute();
            int nowDay = checked((int)_calendar().DayNumber);
            DaggerfallDispatchActionResult result = topic switch
            {
                DaggerfallDialogueTopic.DispatchAccept => _courierDispatches.AcceptDelivery(topicTarget, nowMinute),
                DaggerfallDialogueTopic.DispatchBuy => _courierDispatches.BuyDispatch(topicTarget, nowMinute, nowDay),
                DaggerfallDialogueTopic.DispatchIntercept => _courierDispatches.InterceptDispatch(topicTarget, nowMinute, nowDay),
                DaggerfallDialogueTopic.DispatchTurnIn => _courierDispatches.TurnInDelivery(topicTarget, site, nowMinute, nowDay),
                _ => new(false, "That dispatch action is not supported."),
            };
            session.Question = topic switch
            {
                DaggerfallDialogueTopic.DispatchAccept => "I can carry that sealed dispatch for you.",
                DaggerfallDialogueTopic.DispatchBuy => "What is worth paying to see inside that pouch?",
                DaggerfallDialogueTopic.DispatchIntercept => "Hand over the sealed dispatch — now.",
                _ => "I have a sealed dispatch to deliver here.",
            };
            session.Reply = result.Message;
            session.Diagnostics.Clear();
            session.QuestionCount++;
            Publish(npc, site);
            return Report(new(result.Accepted, result.Message));
        }
        if (topic == DaggerfallDialogueTopic.RestoreExile)
        {
            if (_restoreExile is null || _officeLedger?.TryGetExile(npc.DurableId) is not { } exile)
                return Reject("This person is not seeking restoration to office.");
            DaggerfallExileRestoreResult result = _restoreExile(npc, exile);
            session.Question = result.Question;
            session.Reply = result.Message;
            session.Diagnostics.Clear();
            session.QuestionCount++;
            Publish(npc, site);
            return Report(new(result.Accepted, result.Message));
        }
        if (topic == DaggerfallDialogueTopic.ClaimBounty)
        {
            if (_noticeBounties is null || string.IsNullOrWhiteSpace(topicTarget))
                return Reject("No bounty can be claimed here.");
            DaggerfallNoticeBountyClaim result = _noticeBounties.Claim(npc, topicTarget);
            session.Question = result.Question;
            session.Reply = result.Message;
            session.Diagnostics.Clear();
            session.QuestionCount++;
            Publish(npc, site);
            return Report(new(result.Accepted, result.Message));
        }
        if (topic == DaggerfallDialogueTopic.DenounceOfficeHolder)
        {
            if (_denounceOfficeHolder is null
                || !long.TryParse(topicTarget, NumberStyles.Integer, CultureInfo.InvariantCulture, out long targetId))
                return Reject("That accusation cannot be filed here.");
            DaggerfallHolderDenounceResult result = _denounceOfficeHolder(npc, targetId, site);
            session.Question = result.Question;
            session.Reply = result.Message;
            session.Diagnostics.Clear();
            session.QuestionCount++;
            Publish(npc, site);
            return Report(new(result.Accepted, result.Message));
        }
        if (topic is DaggerfallDialogueTopic.HireCompanion or DaggerfallDialogueTopic.HireDriver or DaggerfallDialogueTopic.DismissParty)
        {
            if (_partyHireService is null)
                return Reject("Party hiring is not available here.");
            DaggerfallPartyHireResult result = _partyHireService(npc, topic);
            session.Question = result.Question;
            session.Reply = result.Message;
            session.Diagnostics.Clear();
            session.QuestionCount++;
            Publish(npc, site);
            return Report(new(result.Accepted, result.Message));
        }
        if (topic == DaggerfallDialogueTopic.Introduce)
        {
            if (_requestIntroduction is null
                || !long.TryParse(topicTarget, NumberStyles.Integer, CultureInfo.InvariantCulture, out long targetId))
                return Reject("That introduction is not available.");
            DaggerfallIntroductionResult result = _requestIntroduction(npc, targetId, site);
            session.Question = $"Could you introduce me to {result.TargetName}?";
            session.Reply = result.Message;
            session.Diagnostics.Clear();
            session.QuestionCount++;
            Publish(npc, site);
            return Report(new(result.Accepted, result.Message));
        }
        if (topic == DaggerfallDialogueTopic.CurryFavour)
        {
            if (_curryFavour is null)
                return Reject("That approach is not available here.");
            DaggerfallFavourResult result = _curryFavour(
                npc,
                session.Tone == DaggerfallDialogueTone.Polite,
                session.Tone == DaggerfallDialogueTone.Blunt);
            session.Question = "I wished to pay my respects.";
            session.Reply = result.Message;
            session.Diagnostics.Clear();
            session.QuestionCount++;
            Publish(npc, site);
            return Report(new(result.Applied, result.Message));
        }
        if (topic is DaggerfallDialogueTopic.Donate or DaggerfallDialogueTopic.Cure)
        {
            if (_templeService is null || !OffersTopic(npc, topic))
                return Reject("That temple service is not available here.");
            if (topic == DaggerfallDialogueTopic.Donate && amount is not > 0)
                return Reject("A positive gold donation is required.");
            DaggerfallTempleServiceResult result = _templeService(npc, topic, amount);
            session.Question = topic == DaggerfallDialogueTopic.Donate ? "How much gold will you donate?" : "Can you cure my afflictions?";
            session.Reply = result.Message;
            session.Diagnostics.Clear();
            session.QuestionCount++;
            Publish(npc, site);
            return Report(new(result.Accepted, result.Message));
        }
        if (topic is DaggerfallDialogueTopic.RankReview or DaggerfallDialogueTopic.Armor or DaggerfallDialogueTopic.House
            or DaggerfallDialogueTopic.Join or DaggerfallDialogueTopic.Teleport or DaggerfallDialogueTopic.Spymaster or DaggerfallDialogueTopic.RestoreAttributes)
        {
            if (_guildService is null || !OffersGuildTopic(npc, topic))
                return Reject("That guild service is not available here.");
            DaggerfallGuildProviderResult result = _guildService(npc, GuildTopic(topic), key);
            session.ProviderTopics.Clear();
            if (result.Options is { } options)
                session.ProviderTopics.AddRange(options.Select(option => new DaggerfallDialogueTopicOption(option.Topic ?? topic.ToString().ToLowerInvariant(), option.Label, option.Key)));
            session.Question = topic == DaggerfallDialogueTopic.RestoreAttributes ? "Can the temple restore my attributes?"
                : topic == DaggerfallDialogueTopic.Join ? "May I join the guild?"
                : topic == DaggerfallDialogueTopic.Teleport ? "Can you teleport me?"
                : topic == DaggerfallDialogueTopic.Spymaster ? "What can you tell me?"
                : topic == DaggerfallDialogueTopic.RankReview
                ? "How does the guild judge my standing?"
                : topic == DaggerfallDialogueTopic.Armor
                    ? key is null ? "Which armor will you claim?" : "What did the guild grant?"
                    : "Can the guild grant its house entitlement?";
            session.Reply = result.Message;
            session.Diagnostics.Clear();
            if (result.TextResource is int resource)
            {
                DaggerfallTextContext guildContext = Context(npc, site, string.Empty);
                guildContext = guildContext with { Faction = guildContext.Faction with {
                    NpcFaction = result.GuildName, FactionName = result.GuildName,
                    PlayerFaction = result.GuildName, FactionOrder = result.GuildName, God = result.GodName } };
                (session.Reply, var diagnostics) = RenderSelectedRun(Resource(resource), guildContext,
                    $"guild:{npc.DurableId}:{session.QuestionCount}:{resource}");
                session.Diagnostics.AddRange(diagnostics);
            }
            if (topic == DaggerfallDialogueTopic.Spymaster && result.Accepted) session.Spymaster = true;
            session.QuestionCount++;
            Publish(npc, site);
            return Report(new(result.Accepted, session.Reply));
        }
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
            bool knowsDestination = selectedDirection is not null && KnowsDirection(npc, selectedDirection, topicTarget);
            if (knowsDestination && selectedDirection is { Known: false } undisclosed)
            {
                bool disclosed = _discloseDirection?.Invoke(undisclosed.Id) == true;
                if (disclosed)
                {
                    // The successful disclosure is part of this operation's semantic result. The
                    // next request therefore sees the same map marker without changing the answer
                    // branch after the fact.
                    selectedDirection = undisclosed with { Known = true };
                }
                else
                {
                    // A provider that cannot commit the durable disclosure cannot honestly give
                    // the direct answer. Keep the reply and post-state on the same refusal path.
                    knowsDestination = false;
                }
            }

            int responseId = !knowsDestination
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
            // Donor GetDialogHint2: a spymaster answers a resource's rumor question with its true anyInfo
            // answer; with no anyInfo answer the rumor stands, as the donor falls back the other way.
            if (session.Spymaster && topicTarget!.StartsWith("quest-rumor:", StringComparison.Ordinal)
                && "quest-info:" + topicTarget["quest-rumor:".Length..] is var trueTopic
                && _questTopics(npc.DurableId).Any(value => value.Id == trueTopic))
                topicTarget = trueTopic;
            if (topicTarget is null || _resolveQuestTopic?.Invoke(npc.DurableId, topicTarget) is not { } questAnswer)
                return Reject("That quest topic is no longer available.");
            session.Reply = questAnswer.Text;
            session.Diagnostics.AddRange(questAnswer.Diagnostics);
        }
        else if (topic == DaggerfallDialogueTopic.NoticeBoard)
        {
            session.Question = "What's posted on the town notice board?";
            session.Reply = _readNoticeBoard is not null
                ? _readNoticeBoard(site)
                : "There is no notice board posted here.";
        }
        else if (topic == DaggerfallDialogueTopic.Referral)
        {
            string? referralMessage = _issueReferral?.Invoke(site, npc);
            session.Question = "Do you know anyone nearby I should speak with?";
            session.Reply = referralMessage ?? "No one comes to mind right now.";
        }
        else if (work)
        {
            if (_introductionAccess?.Invoke(npc) is { Allowed: false } denied)
            {
                session.Reply = denied.DenialReason ?? "You need an introduction before seeking work here.";
            }
            else
            {
                int workId = _workAvailable(npc.DurableId) ? 8075 + band : 8078;
                (session.Reply, IReadOnlyList<string> workDiagnostics) = RenderSelectedRun(
                    Resource(workId), context, $"{session.Revision}:{session.QuestionCount}:work:answer");
                session.Diagnostics.AddRange(workDiagnostics);
                if (_offerWork is not null && _workAvailable(npc.DurableId)) session.Reply = _offerWork(npc.DurableId);
            }
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
        List<DaggerfallDialogueNewsCandidate> candidates = [.. _definitions.Rumors.Entries
            .Where(rumor => IsAmbientNewsCandidate(rumor, site, session))
            .Where(rumor => rumor.TimeLimit <= 0 || rumor.TimeLimit > currentMinute)
            .OrderBy(rumor => rumor.Index)
            .Select(rumor => new DaggerfallDialogueNewsCandidate(
                rumor.TextKey,
                rumor.Faction1,
                rumor.Faction2,
                TimeLimit: rumor.TimeLimit))];
        SynchronizeWorldState();
        if (_readVariable is { } read)
        {
            foreach (DaggerfallDialogueWorldNewsRule rule in _definitions.DialogueWorldRules.News)
            {
                int owner = rule.Scope switch
                {
                    DaggerfallVariableScope.Global => 0,
                    DaggerfallVariableScope.Region => site.Id.Region,
                    DaggerfallVariableScope.Faction => npc.Appearance.FactionId,
                    _ => throw new InvalidOperationException($"Dialogue world rule {rule.Type}/{rule.TextId} names an unsupported variable scope."),
                };
                bool enabled = rule.VariableKeys.Count != 0
                    && rule.VariableKeys.All(key => read(new DaggerfallVariableAddress(rule.Scope, owner, key)) == rule.RequiredValue);
                DaggerfallDialogueWorldRumorSave? generated = rule.Type == DaggerfallDialogueWorldState.CrimeWaveType
                    && rule.Scope == DaggerfallVariableScope.Region
                    ? _dialogueWorld.ReadActive(rule.Type, site.Id.Region, rule.TextId, currentMinute)
                    : null;
                if (!enabled || rule.Type == DaggerfallDialogueWorldState.CrimeWaveType && generated is null)
                    continue;
                candidates.Add(new(
                    Resource(rule.TextId),
                    Faction1: 0,
                    Faction2: 0,
                    TimeLimit: generated?.ExpiresAtMinute ?? 0));
            }
        }
        candidates.RemoveAll(candidate => candidate.TimeLimit > 0 && candidate.TimeLimit <= currentMinute);
        List<string> conditionNews = BuildConditionNewsLines(npc, site);
        int questRumors = _resolveQuestRumor is null ? 0 : _questRumorCount();
        if (conditionNews.Count > 0 && (candidates.Count + questRumors == 0 || Draw(session, $"{session.QuestionCount}:news:condition-priority", 1, 100) <= 65))
        {
            int picked = conditionNews.Count == 1 ? 0 : Draw(session, $"{session.QuestionCount}:news:condition-select", 0, conditionNews.Count - 1);
            diagnostics = [];
            return conditionNews[picked];
        }
        if (candidates.Count + questRumors == 0)
        {
            (string empty, diagnostics) = RenderSelectedRun(Resource(1457), context,
                $"{session.Revision}:{session.QuestionCount}:news:empty");
            return empty;
        }

        int selected = Draw(session, $"{session.QuestionCount}:news:select", 0, candidates.Count + questRumors - 1);
        if (selected >= candidates.Count)
        {
            var answer = _resolveQuestRumor!(selected - candidates.Count);
            diagnostics = answer.Diagnostics;
            return answer.Text;
        }
        DaggerfallDialogueNewsCandidate candidate = candidates[selected];
        DaggerfallTextRenderResult result = _text.Resolve(candidate.TextKey, RumorContext(npc, site, candidate.Faction1, candidate.Faction2));
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

    /// <summary>
    /// Retains the donor's distinction between map visibility and an NPC's subject knowledge.
    /// Building and quest-local subjects are known without a roll. Organization membership is an
    /// automatic answer only for a place/topic shape; a named person still uses the donor's
    /// deterministic person-knowledge draw so a repeated conversation does not change knowledge
    /// merely because the player asked again.
    /// </summary>
    private bool KnowsDirection(DaggerfallNpc npc, DaggerfallDialogueDestination destination, string? topicTarget)
    {
        int questionIndex = destination.Id.StartsWith("direction:npc:", StringComparison.Ordinal) ? 1 : 0;
        bool placeSubject = questionIndex == 0;
        if (_current?.Spymaster == true) return true;
        if (destination.SameBuilding || destination.QuestLocality || (placeSubject && destination.SameOrganization))
            return true;

        int socialGroup = Math.Clamp(ResolveSocialGroup(npc), 0, 4);
        int modifier = KnowledgeModifiers[questionIndex * 5 + socialGroup];
        int roll = checked((int)_random.DrawKeyed(new KeyedRngRequest(
            DaggerfallRandomKey.Seed,
            RandomScope,
            $"knowledge:npc:{npc.DurableId}:topic:{topicTarget ?? destination.Id}",
            1,
            20)).Value);
        return roll <= modifier + 10;
    }

    private DaggerfallTextContext RumorContext(DaggerfallNpc npc, DaggerfallSiteRecord site, int factionOne, int factionTwo)
    {
        DaggerfallTextContext context = Context(npc, site, string.Empty);
        string? first = _definitions.Factions.Factions.GetValueOrDefault(factionOne)?.Name;
        string? second = _definitions.Factions.Factions.GetValueOrDefault(factionTwo)?.Name;
        DaggerfallFactionDefinition? province = _definitions.Factions.ProvinceOf(site.Id.Region);
        return context with
        {
            Faction = context.Faction with
            {
                NewsFactionOne = first, NewsFactionTwo = second, RegionInContext = province?.Name ?? site.Name,
                Liege = _social.Liege.SovereignOf(site.Id.Region)?.Name,
            },
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
        DaggerfallFactionDefinition? province = site is null ? null : _definitions.Factions.ProvinceOf(site.Id.Region);
        string? oath = DaggerfallOaths.Of(_definitions, npc.Appearance.FactionId, site?.Id.Region).FirstOrDefault();
        return new(
            _currentText?.Invoke().Player ?? DaggerfallPlayerTextContext.For(_definitions, player, _playerStats),
            CalendarContext(),
            new DaggerfallTextLocationContext(
                City: site?.Name,
                Region: site is null ? null : _definitions.BuildingNames.RegionName(site.Id.Region),
                DialogSubject: subject ?? site?.Name,
                DialogHint: hint,
                AlternateDialogHint: hint),
            new DaggerfallTextFactionContext(
                NpcFaction: faction?.Name,
                FactionName: faction?.Name,
                RegionInContext: province?.Name,
                Oath: oath,
                Liege: site is null ? null : _social.Liege.SovereignOf(site.Id.Region)?.Name),
            new DaggerfallTextItemContext(),
            new DaggerfallTextStoryContext(GreetingOrFollowUp: opening));
    }

    private DaggerfallTextCalendarContext CalendarContext() => DaggerfallCalendarTextContext.For(_definitions, _calendar());

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
            DaggerfallRandomKey.Seed,
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

    private bool TryReadLiveNpc(long id, out DaggerfallNpc? npc, out DaggerfallDialogueNpc? actor, bool allowMuted = false)
    {
        npc = null;
        actor = null;
        DaggerfallSiteRecord? site = _activeSite();
        if (site is null) return false;
        try { npc = _npcs.Require(id); }
        catch (InvalidOperationException) { return false; }
        if (_activeProfile is not null
            && (npc!.Profile is not { } profile || profile != _activeProfile())) return false;
        bool canBark = false;
        if (_actors.TryGet(id, out ActorState currentActor))
        {
            // A defeated person is a corpse, which its own owner offers; they are no one to talk to.
            if (currentActor.IsDefeated) return false;
            if (currentActor.Actor.TryGet<ActorInteractionPolicy>(out ActorInteractionPolicy? interaction))
            {
                if (!interaction.CanTalk && !interaction.CanBark) return false;
                canBark = interaction.CanBark;
            }
            actor = new(currentActor.Actor.Entity, currentActor.Position);
        }
        if ((!allowMuted && _muted(id)) || !IsTalkableAt(npc!, site, canBark))
        {
            actor = null;
            return false;
        }
        return actor is not null;
    }

    private bool IsTalkableAt(DaggerfallNpc npc, DaggerfallSiteRecord site, bool allowBark = false) =>
        _npcs.IsGameplayActive(npc.DurableId)
        && (allowBark || npc.Services.Contains("talk", StringComparer.Ordinal))
        && (npc.Profile is { } profile ? profile.Site == site.Id
            : npc.Site.Region == site.Id.Region && string.Equals(npc.Site.Location, site.Name, StringComparison.Ordinal));

    private ActorVouchRecord? FirstUsableVouch(ActorOverlayComponent? overlay)
    {
        if (overlay is null || overlay.Vouches.Count == 0)
            return null;
        foreach (ActorVouchRecord vouch in overlay.Vouches)
        {
            if (_isVouchUsable is null ? vouch.VoidReason == ActorVouchVoidReason.None : _isVouchUsable(vouch))
                return vouch;
        }
        return null;
    }

    private string ResolveAmbientBark(DaggerfallNpc npc, ActorOverlayComponent? overlay, DaggerfallSiteRecord? site)
    {
        string speaker = npc.DisplayName ?? npc.Role;
        if (FirstUsableVouch(overlay) is { } usableBarkVouch)
            return $"{speaker}: \"{FormatReferralLine(npc, usableBarkVouch.DisplaySummary())}\"";
        if (overlay is { ReferralTags.Count: > 0 })
            return $"{speaker}: \"{FormatReferralLine(npc, overlay.ReferralTags[0])}\"";

        if (_officeLedger?.TryGetExile(npc.DurableId) is { } exiledBarkHolder)
            return $"{speaker}: \"{DaggerfallExileDialogue.BuildAmbientBark(exiledBarkHolder)}\"";

        string? officeBark = ResolveOfficeContinuityAmbientBark(npc, site);
        if (!string.IsNullOrWhiteSpace(officeBark))
            return $"{speaker}: \"{officeBark}\"";

        int regionIndex = site?.Id.Region ?? npc.Site.Region;
        string regionName = _definitions.BuildingNames.RegionName(regionIndex);
        ulong barkSeed = unchecked((ulong)npc.DurableId);

        (int primaryFactionId, string? creatureFactionId) = ResolveLiveFaction(npc);

        // 1. Active regional or faction world conditions drive condition-aware ambient barks.
        if (_worldConditions is not null)
        {
            int day = checked((int)_calendar().DayNumber);
            IReadOnlyList<DaggerfallWorldConditionToken> active = _worldConditions.ActiveForRegion(regionIndex, day);
            DaggerfallWorldConditionToken? matched = SelectMatchingBarkCondition(npc, primaryFactionId, creatureFactionId, active);
            if (matched is not null)
            {
                string? condFactionName = matched.SourceFactionId is int sf && sf > 0
                    && _definitions.Factions.Factions.TryGetValue(sf, out DaggerfallFactionDefinition? sfDef)
                        ? sfDef.Name
                        : null;
                if (_definitions.People.Barks.TryResolveConditionBarkLine(barkSeed, matched.Kind, regionName, condFactionName, out string? condLine)
                    && !string.IsNullOrWhiteSpace(condLine))
                {
                    return $"{speaker}: \"{condLine}\"";
                }
            }
        }

        // 2. Faction-aware ambient barks: faction standing note or authored byFaction line.
        string? factionBark = ResolveFactionAmbientBark(npc, barkSeed, primaryFactionId, creatureFactionId);
        if (!string.IsNullOrWhiteSpace(factionBark))
            return $"{speaker}: \"{factionBark}\"";

        string? climateName = site is not null
            ? _definitions.Grids.Climate.GetCell(site.MapPixelX + 1, site.MapPixelY).Name
            : null;
        string line = _definitions.People.Barks.ResolveBarkLine(
            barkSeed, npc.Role, overlay?.ArchetypeId, regionIndex, regionName, climateName);
        return $"{speaker}: \"{line}\"";
    }

    private int EffectiveReactionValue(DaggerfallNpc npc, DaggerfallSiteRecord? site)
    {
        int raw = _social.ReactionForNpc(npc).Value;
        if (_wordSpread is null || site is null)
            return raw;
        int reactionFactionId = _social.ResolveNpcReactionFactionId(npc);
        if (reactionFactionId <= 0)
            return raw;
        int unarrived = _wordSpread.UnarrivedFactionStandingDelta(
            reactionFactionId, site, _currentCalendarMinute(), checked((int)_calendar().DayNumber));
        return raw - unarrived;
    }

    private (int PrimaryFactionId, string? CreatureFactionId) ResolveLiveFaction(DaggerfallNpc npc)
    {
        if (_actors.TryGet(npc.DurableId, out ActorState? liveActor)
            && liveActor.Actor.TryGet<FactionComponent>(out FactionComponent? fc))
        {
            return (fc.PrimaryFactionId ?? npc.Appearance.FactionId, fc.CreatureFactionId);
        }
        return (npc.Appearance.FactionId, null);
    }

    private string? ResolveFactionGreetingLine(DaggerfallNpc npc, DaggerfallSiteRecord? site)
    {
        (int factionId, string? creatureFactionId) = ResolveLiveFaction(npc);
        if (factionId <= 0)
            return null;
        if (!_definitions.Factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction))
            return null;

        // Skip generic regional people factions unless the player has extreme standing.
        bool isRegionalPeople = faction.Type == 15;
        int standing = _social.FactionReputation(faction.Id);

        // 1. Shared guild membership camaraderie (walking sub-faction lineage up to concrete guild root).
        // Rank recognition requires word of the player's rank to have reached this site (respecting instant bubble & courier spread).
        if (TryResolveConcreteGuild(faction.Id, out Guilds.DaggerfallConcreteGuildDefinition? guild)
            && guild is not null
            && _social.GuildEligibility(guild.FactionId) is { IsMember: true } eligibility)
        {
            if (eligibility.Rank > 0
                && (_wordSpread is null || site is null
                    || _wordSpread.IsGuildRankKnownAt(guild.FactionId, eligibility.Rank, site, _currentCalendarMinute(), checked((int)_calendar().DayNumber))))
            {
                string? rankTitle = Guilds.DaggerfallGuildRankTitles.Title(
                    guild.FactionId, eligibility.Rank, _playerIdentity().Gender == DaggerfallCharacterGender.Female);
                if (!string.IsNullOrWhiteSpace(rankTitle))
                    return $"Well met, {rankTitle} of {guild.DisplayName}.";
            }
            return $"Well met, fellow member of {guild.DisplayName}.";
        }

        // 2. Rival-faction friction: check if the player belongs to an authored enemy of this NPC's faction or parent guild,
        //    or holds positive standing with a direct individual rival (such as Lord Harth <-> Baron Shrike).
        if (TryFindRivalMemberFaction(faction, out DaggerfallFactionDefinition? enemyDef) && enemyDef is not null)
            return $"{faction.Name} keeps a wary eye on friends of {enemyDef.Name}.";
        if (TryFindRivalStandingFaction(faction, out DaggerfallFactionDefinition? rivalDef) && rivalDef is not null)
            return $"{faction.Name} hears you stand well with {rivalDef.Name} — mind where your loyalties lie.";

        // 3. Strong standing or distrust with a non-generic faction (guild, knightly order, temple, noble court, crown).
        // Unarrived standing wavefronts are subtracted so distant towns don't react before couriers bring word.
        if (!isRegionalPeople)
        {
            int effectiveFactionId = guild?.FactionId ?? faction.Id;
            int effectiveStanding = _social.FactionReputation(effectiveFactionId);
            if (_wordSpread is not null && site is not null)
            {
                effectiveStanding -= _wordSpread.UnarrivedFactionStandingDelta(
                    effectiveFactionId, site, _currentCalendarMinute(), checked((int)_calendar().DayNumber));
            }
            string displayFaction = guild?.DisplayName ?? faction.Name;
            if (effectiveStanding >= 20)
                return $"Your name stands high with {displayFaction}.";
            if (effectiveStanding <= -10)
                return $"{displayFaction} has little patience for you right now.";
        }

        // 4. Acute faction-specific world condition note (e.g. GuildEmbargo on this NPC's faction).
        if (_worldConditions is not null && site is not null)
        {
            int day = checked((int)_calendar().DayNumber);
            foreach (DaggerfallWorldConditionToken token in _worldConditions.ActiveForRegion(site.Id.Region, day))
            {
                if (token.Kind == DaggerfallWorldConditionKind.GuildEmbargo && MatchesFactionLineage(faction.Id, token.SourceFactionId))
                    return $"Mind the {guild?.DisplayName ?? faction.Name} embargo — we are keeping close watch on who enters our halls.";
            }
        }

        // 5. Notable classic cast faction identity introductions (Blades HQ, regional courts, underworld & rivalries).
        if (ResolveClassicCastFactionGreeting(faction.Id) is { } castGreeting)
            return castGreeting;

        _ = creatureFactionId;
        return null;
    }

    private bool TryFindRivalStandingFaction(DaggerfallFactionDefinition startFaction, out DaggerfallFactionDefinition? rivalDef)
    {
        rivalDef = null;
        if (startFaction.Type != 4)
            return false;
        foreach (int enemyId in startFaction.Enemies)
        {
            if (enemyId > 0
                && _definitions.Factions.Factions.TryGetValue(enemyId, out DaggerfallFactionDefinition? matched)
                && matched.Type == 4
                && _social.FactionReputation(enemyId) >= 5)
            {
                rivalDef = matched;
                return true;
            }
        }
        foreach (DaggerfallFactionDefinition candidate in _definitions.Factions.Factions.Values)
        {
            if (candidate.Type == 4
                && candidate.Id != startFaction.Id
                && candidate.Enemies.Contains(startFaction.Id)
                && _social.FactionReputation(candidate.Id) >= 5)
            {
                rivalDef = candidate;
                return true;
            }
        }
        return false;
    }

    private static string? ResolveClassicCastFactionGreeting(int factionId) => factionId switch
    {
        351 => "I stand for the Septim Empire and the Blades at Hearthwing Manor, alongside Lady Brisienna.",
        355 => "I am Lord Harth of the Host of the True Horn — no friend to Baron Shrike's rule in Lainlyn.",
        379 => "You stand in Lainlyn's hall before Baron Shrike of the Host of the Horn.",
        359 => "Lord Plessington of the Order of the Cup bids you speak cleanly in Urvaius.",
        361 => "Chulmore Quill of the Quill Circus — my friends in the Dark Brotherhood value discretion.",
        375 => "Lord Bertram Spode, at The Squid's service here in Daggerfall.",
        376 => "Baltham Greyman — if you want the pulse of Daggerfall's court, you're in the right tavern.",
        401 => "House Flyte welcomes worthy visitors to Anticlere's court.",
        402 => "May Mara steady your path — I keep counsel here in Anticlere for House Flyte and the Benevolence.",
        405 => "Baroness Dh'emka of Lainlyn's court — state your business plainly.",
        _ => null,
    };

    private bool TryResolveConcreteGuild(int factionId, out Guilds.DaggerfallConcreteGuildDefinition? guild)
    {
        guild = null;
        int current = factionId;
        for (int depth = 0; depth < 8 && current > 0; depth++)
        {
            if (Guilds.DaggerfallConcreteGuildCatalog.TryGet(current, out Guilds.DaggerfallConcreteGuildDefinition? found))
            {
                guild = found;
                return true;
            }
            if (!_definitions.Factions.Factions.TryGetValue(current, out DaggerfallFactionDefinition? def) || def.Parent == current)
                break;
            current = def.Parent;
        }
        return false;
    }

    private bool TryFindRivalMemberFaction(DaggerfallFactionDefinition startFaction, out DaggerfallFactionDefinition? enemyDef)
    {
        enemyDef = null;
        foreach (DaggerfallFactionDefinition def in _social.Liege.Lineage(startFaction.Id).Take(6))
        {
            if (def.Type == 15)
                break;
            foreach (int enemyId in def.Enemies)
            {
                if (enemyId > 0
                    && _definitions.Factions.Factions.TryGetValue(enemyId, out DaggerfallFactionDefinition? matched)
                    && _social.GuildEligibility(enemyId).IsMember)
                {
                    enemyDef = matched;
                    return true;
                }
            }
        }
        return false;
    }

    private string? ResolveGuardScrutinyGreetingLine(DaggerfallNpc npc, DaggerfallSiteRecord? site)
    {
        if (_evaluateGateScrutiny is null || site is null)
            return null;
        (_, string? creatureFactionId) = ResolveLiveFaction(npc);
        bool isGuard = string.Equals(npc.Role, "guard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(creatureFactionId, "city-watch", StringComparison.OrdinalIgnoreCase);
        if (!isGuard)
            return null;

        DaggerfallGateScrutinyAssessment assessment = _evaluateGateScrutiny(site.Id.Region, site);
        return assessment.Level == DaggerfallScrutinyLevel.Normal ? null : assessment.Reason;
    }

    private string? ResolveFactionAmbientBark(DaggerfallNpc npc, ulong seed, int primaryFactionId, string? creatureFactionId)
    {
        bool isRegionalPeople = primaryFactionId > 0
            && _definitions.Factions.Factions.TryGetValue(primaryFactionId, out DaggerfallFactionDefinition? primaryDef)
            && primaryDef.Type == 15;

        List<string> keys = [];
        // Generic regional townsfolk (Type 15) speak regional/climate/role barks unless they hold a specific
        // non-generic institutional identity (such as "city-watch"); non-generic factions walk up their guild/court lineage.
        if (!isRegionalPeople && primaryFactionId > 0)
        {
            keys.Add(primaryFactionId.ToString(CultureInfo.InvariantCulture));
            keys.AddRange(_social.Liege.Lineage(primaryFactionId).Skip(1).Take(5).Select(faction => faction.Id.ToString(CultureInfo.InvariantCulture)));
        }
        if (!string.IsNullOrWhiteSpace(creatureFactionId))
            keys.Add(creatureFactionId);

        // If the NPC belongs to a specific non-generic faction and the player has notable standing or rivalry, speak to it!
        if (!isRegionalPeople && primaryFactionId > 0
            && _definitions.Factions.Factions.TryGetValue(primaryFactionId, out DaggerfallFactionDefinition? faction))
        {
            if (TryFindRivalMemberFaction(faction, out DaggerfallFactionDefinition? enemyDef) && enemyDef is not null)
                return $"Keep moving — {faction.Name} has no love for {enemyDef.Name}.";

            int standingFactionId = TryResolveConcreteGuild(primaryFactionId, out Guilds.DaggerfallConcreteGuildDefinition? guild) && guild is not null
                ? guild.FactionId
                : primaryFactionId;
            int standing = _social.FactionReputation(standingFactionId);
            if (_wordSpread is not null && _activeSite() is { } barkSite)
            {
                standing -= _wordSpread.UnarrivedFactionStandingDelta(
                    standingFactionId, barkSite, _currentCalendarMinute(), checked((int)_calendar().DayNumber));
            }
            string displayFaction = guild?.DisplayName ?? faction.Name;
            if (standing <= -10)
                return $"{displayFaction} remembers your slights — best walk on.";
            if (standing >= 20)
                return $"Good day — folk in {displayFaction} speak well of you.";
        }

        return keys.Count > 0 && _definitions.People.Barks.TryResolveFactionBarkLine(seed, keys, out string? line) ? line : null;
    }

    private DaggerfallWorldConditionToken? SelectMatchingBarkCondition(
        DaggerfallNpc npc,
        int primaryFactionId,
        string? creatureFactionId,
        IReadOnlyList<DaggerfallWorldConditionToken> active)
    {
        if (active.Count == 0)
            return null;

        bool isGuard = string.Equals(npc.Role, "guard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(creatureFactionId, "city-watch", StringComparison.OrdinalIgnoreCase);
        bool isMerchantOrTavern = string.Equals(npc.Role, "merchant", StringComparison.OrdinalIgnoreCase)
            || string.Equals(npc.Role, "tavern keeper", StringComparison.OrdinalIgnoreCase);
        bool isPriest = string.Equals(npc.Role, "priest", StringComparison.OrdinalIgnoreCase);

        foreach (DaggerfallWorldConditionToken token in active)
        {
            if (token.Kind == DaggerfallWorldConditionKind.GuildEmbargo
                && MatchesFactionLineage(primaryFactionId, token.SourceFactionId))
                return token;
            if (isGuard && token.Kind is DaggerfallWorldConditionKind.CrimeCrackdown or DaggerfallWorldConditionKind.BorderTension or DaggerfallWorldConditionKind.SuccessionCrisis or DaggerfallWorldConditionKind.PlagueScare)
                return token;
            if (isMerchantOrTavern && token.Kind is DaggerfallWorldConditionKind.GrainShortage or DaggerfallWorldConditionKind.GuildEmbargo)
                return token;
            if (isPriest && token.Kind is DaggerfallWorldConditionKind.PlagueScare or DaggerfallWorldConditionKind.MonsterSurge)
                return token;
        }

        // Severe regional conditions (severity >= 2) are talked about on the street by everyone.
        return active.FirstOrDefault(token => token.Severity >= 2);
    }

    private List<string> BuildConditionNewsLines(DaggerfallNpc npc, DaggerfallSiteRecord site)
    {
        int day = checked((int)_calendar().DayNumber);
        List<string> lines = [];
        AppendOfficeContinuityNewsLines(npc, site, lines);
        if (_wordSpread is not null)
        {
            foreach (DaggerfallWordWavefrontSave wf in _wordSpread.ArrivedNewsAt(site, _currentCalendarMinute(), day))
            {
                if (!string.IsNullOrWhiteSpace(wf.Body) && !lines.Contains(wf.Body, StringComparer.Ordinal))
                    lines.Add(wf.Body);
            }
        }
        if (_worldConditions is null)
            return lines;
        IReadOnlyList<DaggerfallWorldConditionToken> active = _worldConditions.ActiveForRegion(site.Id.Region, day);
        if (active.Count == 0)
            return lines;

        string regionName = _definitions.BuildingNames.RegionName(site.Id.Region);
        foreach (DaggerfallWorldConditionToken token in active)
        {
            string? sourceName = token.SourceFactionId is int sf && sf > 0
                && _definitions.Factions.Factions.TryGetValue(sf, out DaggerfallFactionDefinition? sfDef)
                    ? sfDef.Name
                    : null;
            if (!string.IsNullOrWhiteSpace(token.Body))
            {
                lines.Add(token.Body);
                continue;
            }
            if (_definitions.People.Barks.TryResolveConditionBarkLine(
                unchecked((ulong)npc.DurableId + (ulong)token.Kind), token.Kind, regionName, sourceName, out string? line)
                && !string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }
        return lines;
    }

    private bool MatchesFactionLineage(int candidateFactionId, int? targetFactionId)
    {
        if (targetFactionId is not int wanted || wanted <= 0 || candidateFactionId <= 0)
            return false;
        return _social.Liege.Lineage(candidateFactionId).Any(faction => faction.Id == wanted);
    }

    private string? ResolveOfficeContinuityGreetingLine(DaggerfallNpc npc, DaggerfallSiteRecord? site)
    {
        if (site is null || (_officeHolders is null && _officeLedger is null))
            return null;

        // 1. Active local office in a guildhall, temple, or palace interior (or town watch captain for guards).
        if (TryFindLocalOfficeAtActiveProfile(npc, site) is { } localOffice)
        {
            if (localOffice.Kind is DaggerfallOfficeKind.GuildHallMaster or DaggerfallOfficeKind.TempleHead)
            {
                if (localOffice.IsVacant)
                {
                    return !string.IsNullOrWhiteSpace(localOffice.PreviousHolderName)
                        ? $"The post of {localOffice.Title} stands vacant here since {localOffice.PreviousHolderName} left office; we await word of a successor."
                        : $"The post of {localOffice.Title} stands vacant here while a successor is named.";
                }
                if (localOffice.HolderActorId == npc.DurableId)
                {
                    if (localOffice.IsOverridden)
                        return $"I now hold the office of {localOffice.Title} for this hall.";
                }
                else if (!string.IsNullOrWhiteSpace(localOffice.DisplayName))
                {
                    string styledLocal = FormatStyledHolder(localOffice.Title, localOffice.DisplayName);
                    string? majorSeatNote = ResolveMajorSeatContinuityLine(npc, site, forBark: false);
                    return !string.IsNullOrWhiteSpace(majorSeatNote)
                        ? $"This hall answers to {styledLocal}. {majorSeatNote}"
                        : $"This hall answers to {styledLocal}.";
                }
            }
            else if (localOffice.Kind == DaggerfallOfficeKind.CourtFactor && localOffice.IsVacant)
            {
                return $"The post of {localOffice.Title} here stands vacant until a replacement is appointed.";
            }
            else if (localOffice.Kind == DaggerfallOfficeKind.WatchCaptain && (localOffice.IsVacant || localOffice.IsOverridden))
            {
                if (localOffice.IsVacant)
                    return $"Our watch stands without a {localOffice.Title} until a successor is named.";
                if (localOffice.HolderActorId != npc.DurableId && !string.IsNullOrWhiteSpace(localOffice.DisplayName))
                    return $"The watch answers now to {FormatStyledHolder(localOffice.Title, localOffice.DisplayName)}.";
            }
        }

        // 2. Palace / royal court speaker -> reflect live regional ruler, interim regent, or vacant throne.
        if (IsPalaceOrCourtContext(npc, site))
            return ResolveRegionalThroneContinuityLine(npc, site.Id.Region, forBark: false);

        // 3. Guild/order/temple member outside their own hall -> reflect overarching MajorSeat turnover or vacancy.
        if (TryFindSpeakerMajorSeat(npc) is { } majorSeat)
        {
            DaggerfallOfficeOverrideSave? seatOver = _officeLedger?.TryGetOverride(DaggerfallOfficeLedger.FactionOfficeKey(majorSeat.OfficeFactionId));
            if (seatOver is not null)
                return ResolveMajorSeatContinuityLine(npc, site, forBark: false);
        }

        return null;
    }

    private string? ResolveOfficeContinuityAmbientBark(DaggerfallNpc npc, DaggerfallSiteRecord? site)
    {
        if (site is null || (_officeHolders is null && _officeLedger is null))
            return null;

        if (TryFindLocalOfficeAtActiveProfile(npc, site) is { } localOffice)
        {
            if (localOffice.Kind is DaggerfallOfficeKind.GuildHallMaster or DaggerfallOfficeKind.TempleHead)
            {
                if (localOffice.IsVacant)
                    return $"Without a {localOffice.Title}, everyone in this hall is waiting on a successor.";
                string? majorSeatBark = ResolveMajorSeatContinuityLine(npc, site, forBark: true);
                if (!string.IsNullOrWhiteSpace(majorSeatBark))
                    return majorSeatBark;
                if (localOffice.HolderActorId != npc.DurableId && !string.IsNullOrWhiteSpace(localOffice.DisplayName))
                    return $"Business in this hall runs by order of {FormatStyledHolder(localOffice.Title, localOffice.DisplayName)}.";
            }
            else if (localOffice.Kind == DaggerfallOfficeKind.WatchCaptain && (localOffice.IsVacant || localOffice.IsOverridden))
            {
                return localOffice.IsVacant
                    ? $"We are short a {localOffice.Title} — every watchman is pulling double duty."
                    : $"Watch orders come from {FormatStyledHolder(localOffice.Title, localOffice.DisplayName)} now.";
            }
        }

        if (IsPalaceOrCourtContext(npc, site))
            return ResolveRegionalThroneContinuityLine(npc, site.Id.Region, forBark: true);

        if (TryFindSpeakerMajorSeat(npc) is { } majorSeat
            && _officeLedger?.TryGetOverride(DaggerfallOfficeLedger.FactionOfficeKey(majorSeat.OfficeFactionId)) is not null)
        {
            return ResolveMajorSeatContinuityLine(npc, site, forBark: true);
        }

        return null;
    }

    private void AppendOfficeContinuityNewsLines(DaggerfallNpc npc, DaggerfallSiteRecord site, List<string> lines)
    {
        if (_officeHolders is null && _officeLedger is null)
            return;

        static void AddUnique(List<string> target, string? line)
        {
            if (!string.IsNullOrWhiteSpace(line) && !target.Contains(line, StringComparer.Ordinal))
                target.Add(line);
        }

        long nowMinute = _currentCalendarMinute();
        int nowDay = checked((int)_calendar().DayNumber);
        bool inCourt = IsPalaceOrCourtContext(npc, site);
        IReadOnlyList<DaggerfallWordWavefrontSave> arrived = _wordSpread is not null
            ? _wordSpread.ArrivedNewsAt(site, nowMinute, nowDay)
            : [];
        bool HasArrivedOfficeWord(string officeKey) =>
            arrived.Any(wf => wf.EventId.StartsWith($"office:{officeKey}|", StringComparison.Ordinal));

        // 1. Local office status in the active hall or site (always known locally).
        if (TryFindLocalOfficeAtActiveProfile(npc, site) is { } localOffice)
        {
            if (localOffice.IsVacant)
            {
                AddUnique(lines, !string.IsNullOrWhiteSpace(localOffice.PreviousHolderName)
                    ? $"The post of {localOffice.Title} here stands vacant since {localOffice.PreviousHolderName} left office."
                    : $"The post of {localOffice.Title} here stands vacant while a successor is chosen.");
            }
            else if (localOffice.IsOverridden && !string.IsNullOrWhiteSpace(localOffice.DisplayName))
            {
                AddUnique(lines, $"{FormatStyledHolder(localOffice.Title, localOffice.DisplayName)} now leads this hall.");
            }
        }

        // 2. Regional throne status (always known inside palace/court or at origin; outside, respect WordSpread proclamation arrival).
        if (_officeHolders?.ResolveRegionalRuler(site.Id.Region) is { } ruler)
        {
            string throneKey = DaggerfallOfficeLedger.FactionOfficeKey(ruler.OfficeFactionId);
            DaggerfallOfficeOverrideSave? throneOver = _officeLedger?.TryGetOverride(throneKey);
            bool throneNewsKnown = inCourt
                || throneOver is null
                || throneOver.OfficeSiteId == site.Id
                || _wordSpread is null
                || !_wordSpread.HasOfficeProclamation(throneKey)
                || HasArrivedOfficeWord(throneKey);
            if (throneNewsKnown && (inCourt || throneOver is not null))
                AddUnique(lines, ResolveRegionalThroneContinuityLine(npc, site.Id.Region, forBark: false));
        }
        else if (inCourt)
        {
            // A court whose lord the catalog doesn't name still tells whom the region answers to.
            AddUnique(lines, ResolveRegionalThroneContinuityLine(npc, site.Id.Region, forBark: false));
        }

        // 3. Speaker's overarching MajorSeat status (guildhall/temple or arrived proclamation).
        if (TryFindSpeakerMajorSeat(npc) is { } seat)
        {
            string seatKey = DaggerfallOfficeLedger.FactionOfficeKey(seat.OfficeFactionId);
            DaggerfallOfficeOverrideSave? seatOver = _officeLedger?.TryGetOverride(seatKey);
            bool seatNewsKnown = seatOver is null
                || seatOver.OfficeSiteId == site.Id
                || _wordSpread is null
                || !_wordSpread.HasOfficeProclamation(seatKey)
                || HasArrivedOfficeWord(seatKey);
            if (seatNewsKnown && seatOver is not null)
                AddUnique(lines, ResolveMajorSeatContinuityLine(npc, site, forBark: false));
        }
    }

    /// <summary>
    /// What a palace speaker says of <paramref name="region"/>'s throne: its lord and the crown the lord is sworn to, a
    /// regency or a vacancy; the ambient bark when <paramref name="forBark"/>.
    /// </summary>
    internal string? ResolveRegionalThroneContinuityLine(DaggerfallNpc npc, int region, bool forBark)
    {
        if (_officeHolders is null || region is < 0 or > 61)
            return null;

        string regionName = _definitions.BuildingNames.RegionName(region);
        // The crown the region answers to on the board is told whether or not a lord is named, and through a vacancy
        // or a regency; while the crown consolidates a region it has taken, the court's talk is of the new homage.
        string? liege = _social.Liege.LiegeOf(region)?.Name;
        string answers = liege is null ? string.Empty : $" {regionName} answers to the Crown of {liege}.";
        string? homage = _social.Liege.NewLiegeOf(region) is { } sworn ? $"The court of {regionName} has sworn homage to the Crown of {sworn.Name}." : null;

        DaggerfallOfficeHolder? ruler = _officeHolders.ResolveRegionalRuler(region);
        if (ruler is null)
        {
            if (liege is null) return null;
            return forBark
                ? homage ?? $"The court of {regionName} answers to the Crown of {liege}."
                : $"You stand in the court of {regionName}, which answers to the Crown of {liege}.";
        }

        string throneKey = DaggerfallOfficeLedger.FactionOfficeKey(ruler.OfficeFactionId);
        DaggerfallOfficeOverrideSave? over = _officeLedger?.TryGetOverride(throneKey);

        if (over is { Vacant: true } || !ruler.HasLivingHolder)
        {
            string? prevName = over?.PreviousHolderName ?? ruler.PreviousHolderName;
            string? prevFate = !string.IsNullOrWhiteSpace(over?.CauseDetail) ? over.CauseDetail : ruler.PreviousHolderFate;
            if (forBark)
                return $"The throne of {regionName} stands vacant — the court is on edge until a successor is named.{answers}";
            return (!string.IsNullOrWhiteSpace(prevName)
                ? $"The throne of {regionName} stands vacant since {prevName} {prevFate ?? "left the throne"}; the court awaits a lawful successor."
                : $"The throne of {regionName} stands vacant; the court awaits a lawful successor.") + answers;
        }

        if (over is { IsRegency: true })
        {
            string styledRegent = FormatStyledHolder(over.Title ?? "Regent", over.HolderDisplayName ?? ruler.DisplayName);
            if (ruler.ActorId == npc.DurableId)
                return $"I hold interim regency over {regionName} until the succession is settled.{answers}";
            return (forBark
                ? $"{styledRegent} holds interim regency over {regionName} while succession remains unsettled."
                : $"Mind your manners — {styledRegent} governs {regionName} as interim regent until a lawful successor is crowned.") + answers;
        }

        string styledRuler = FormatStyledHolder(ruler.Title, ruler.DisplayName);
        string swornTo = liege is null ? string.Empty : $", sworn to the Crown of {liege}";
        if (ruler.ActorId == npc.DurableId)
            return $"You address {styledRuler} of {regionName}{swornTo}.";
        if (forBark)
            return homage ?? (over is not null
                ? $"The court adjusts to the reign of {styledRuler} of {regionName}."
                : $"All eyes in court turn to {styledRuler} of {regionName}.");
        return over is not null && !string.IsNullOrWhiteSpace(over.PreviousHolderName)
            ? $"You stand in the court of {styledRuler} of {regionName}{swornTo}, who succeeded {over.PreviousHolderName}."
            : $"You stand in the court of {styledRuler} of {regionName}{swornTo}.";
    }

    private string? ResolveMajorSeatContinuityLine(DaggerfallNpc npc, DaggerfallSiteRecord site, bool forBark)
    {
        if (_officeHolders is null || TryFindSpeakerMajorSeat(npc) is not { } seat)
            return null;

        string seatKey = DaggerfallOfficeLedger.FactionOfficeKey(seat.OfficeFactionId);
        DaggerfallOfficeOverrideSave? over = _officeLedger?.TryGetOverride(seatKey);
        DaggerfallOfficeHolder? holder = _officeHolders.ResolveOffice(seat.OfficeFactionId, site.Id.Region);
        if (holder is null && over is null)
            return null;

        if (over is { Vacant: true } || holder is { HasLivingHolder: false })
        {
            return forBark
                ? $"The {seat.SeatName} stands leaderless while the council chooses a successor."
                : $"The high seat of the {seat.SeatName} stands vacant while a successor is chosen.";
        }

        if (over is { IsRegency: true })
        {
            string styledRegent = FormatStyledHolder(over.Title ?? "Regent", over.HolderDisplayName ?? holder?.DisplayName);
            return forBark
                ? $"{styledRegent} holds interim stewardship over the {seat.SeatName}."
                : $"{styledRegent} governs the {seat.SeatName} as interim regent while succession remains unsettled.";
        }

        if (over is not null && holder is { HasLivingHolder: true })
        {
            string styledHead = FormatStyledHolder(holder.Title ?? seat.DefaultTitle, holder.DisplayName);
            if (holder.ActorId == npc.DurableId)
                return $"I now guide the {seat.SeatName} as {styledHead}.";
            return forBark
                ? $"The {seat.SeatName} answers now to {styledHead}."
                : $"The {seat.SeatName} now answers to {styledHead}.";
        }

        return null;
    }

    private DaggerfallLocalOffice? TryFindLocalOfficeAtActiveProfile(DaggerfallNpc npc, DaggerfallSiteRecord site)
    {
        if (_officeLedger is null || _activeProfile is null)
            return null;
        DaggerfallWorldProfileKey profile = _activeProfile();
        IReadOnlyList<DaggerfallLocalOffice> locals = _officeLedger.ResolveLocalOffices(site);
        if (profile.Kind == DaggerfallWorldProfileKind.Interior)
            return locals.FirstOrDefault(o => o.ProfileKey == profile);

        (_, string? creatureFactionId) = ResolveLiveFaction(npc);
        bool isGuard = string.Equals(npc.Role, "guard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(creatureFactionId, "city-watch", StringComparison.OrdinalIgnoreCase);
        if (isGuard && profile.Kind == DaggerfallWorldProfileKind.Exterior)
            return locals.FirstOrDefault(o => o.Kind == DaggerfallOfficeKind.WatchCaptain && o.ProfileKey == profile);
        return null;
    }

    private DaggerfallBoardSeatBinding? TryFindSpeakerMajorSeat(DaggerfallNpc npc)
    {
        (int factionId, _) = ResolveLiveFaction(npc);
        foreach (DaggerfallFactionDefinition faction in _social.Liege.Lineage(factionId))
        {
            if (_definitions.BoardSeats.TryGetByFactionId(faction.Id, out DaggerfallBoardSeatBinding? seat)
                && seat is not null
                && seat.Kind != DaggerfallBoardSeatKind.Ruler)
            {
                return seat;
            }
        }
        return null;
    }

    private bool IsPalaceOrCourtContext(DaggerfallNpc npc, DaggerfallSiteRecord site)
    {
        if (_activeProfile is not null)
        {
            DaggerfallWorldProfileKey profile = _activeProfile();
            if (profile.Kind == DaggerfallWorldProfileKind.Interior
                && DaggerfallWorldProfileIds.TryParse(profile.LogicalId, out _, out DaggerfallSiteBuildingId? buildingId)
                && buildingId is { } b
                && site.Exterior is not null
                && site.Exterior.Buildings.TryGetValue(b, out DaggerfallSiteBuildingSource? source)
                && (source.Source.BuildingType == 16
                    || _definitions.ProvincialNobility.IsCapitalSeatProfile(site.Id, b)))
            {
                return true;
            }
            if (profile.Kind == DaggerfallWorldProfileKind.Dungeon
                && site.Name.Contains("Castle", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        (int factionId, _) = ResolveLiveFaction(npc);
        if (factionId > 0 && _definitions.Factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction))
        {
            if (faction.Type is 7 or 14 or 5)
                return true;
            if (faction.SocialGroup == 3 && faction.Type != 15 && faction.GuildGroup != 9)
                return true;
        }

        return false;
    }

    private static string FormatStyledHolder(string? title, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return !string.IsNullOrWhiteSpace(title) ? title : "an unnamed official";
        if (string.IsNullOrWhiteSpace(title)
            || displayName.StartsWith(title, StringComparison.OrdinalIgnoreCase))
        {
            return displayName;
        }
        return $"{title} {displayName}";
    }

    private string FormatReferralLine(DaggerfallNpc npc, string referralTag) =>
        _definitions.People.Barks.Referrals.FormatResponseLine(
            unchecked((ulong)npc.DurableId), npc.DisplayName ?? npc.Role, referralTag, npc.Role);

    private bool MatchesRevision(string? revision) =>
        _current is { } current && string.Equals(current.Revision, revision, StringComparison.Ordinal);

    private void Publish(DaggerfallNpc npc, DaggerfallSiteRecord? site)
    {
        if (_current is not { } session) { _publish(null); return; }
        const string Talk = DaggerfallDialogueTopicGroups.Talk;
        List<DaggerfallDialogueTopicOption> topics = [new("directions", "Where is this place?", Group: Talk), new("news", "Any news?", Group: Talk)];
        if (DaggerfallRoadCourierNews.TryResolveEncounter(npc, _actorLedger, _courierDispatches, _definitions, _findSite) is { IsCourier: true } courierEncounter)
            topics.Add(new("courier-news", $"News from {courierEncounter.OriginRegionName}", Group: Talk));
        if (_readNoticeBoard is not null && site is { Exterior: not null } && OffersNoticeBoardTopic(npc))
            topics.Add(new("notice-board", "What's on the notice board?", Group: Talk));
        if (_issueReferral is not null)
            topics.Add(new("referral", "Any contacts nearby?", Group: Talk));
        DaggerfallIntroductionAccess? access = _introductionAccess?.Invoke(npc);
        if (npc.Services.Any(service => service is "quest" or "quests" or "quest-candidate")
            || _workAvailable(npc.DurableId)
            || (access is { Allowed: false, Prominence: >= DaggerfallSocialProminence.MinorCourt } && npc.Kind == DaggerfallNpcKind.Static))
            topics.Add(new("work", "Do you know of any work?", Group: Talk));
        if (_curryFavour is not null
            && access is { Allowed: true, Prominence: >= DaggerfallSocialProminence.MinorCourt }
            && _canCurryFavour?.Invoke(npc) == true)
            topics.Add(new("curry-favour", "Pay your respects", Group: Talk));
        if (_introductionTargets is not null && _requestIntroduction is not null)
        {
            foreach (DaggerfallIntroductionTarget target in _introductionTargets(npc, site))
            {
                string label = target.AlreadyVouched
                    ? $"Ask about {target.TargetName} (vouched)"
                    : $"Ask for an introduction to {target.TargetName}";
                topics.Add(new($"introduce:{target.TargetDurableId}", label, Group: Talk, Category: "Introductions"));
            }
        }
        if (_directionDirectory is not null)
            topics.AddRange(_directionDirectory().Select(topic => topic with { Group = DaggerfallDialogueTopicGroups.Places }));
        topics.AddRange(_questTopics(npc.DurableId).Select(topic => topic with { Group = DaggerfallDialogueTopicGroups.Quests }));
        if (_restoreExile is not null && _officeLedger?.TryGetExile(npc.DurableId) is { } exileOption)
        {
            string exileName = npc.DisplayName ?? exileOption.DisplayName ?? "them";
            topics.Add(new("restore-exile", $"Help restore {exileName} as {exileOption.Title}", Group: DaggerfallDialogueTopicGroups.Quests));
        }
        if (_denounceTargets is not null && _denounceOfficeHolder is not null)
        {
            foreach (DaggerfallHolderDenounceTarget target in _denounceTargets(npc, site))
            {
                topics.Add(new(
                    $"denounce-holder:{target.ActorId}",
                    $"Report {target.Title} {target.DisplayName} to the court",
                    Group: DaggerfallDialogueTopicGroups.Quests));
            }
        }
        if (_courierDispatches is not null)
        {
            if (_courierDispatches.TryGetForCourier(npc.DurableId) is { } carried)
            {
                topics.Add(new($"dispatch:deliver:{carried.DispatchId}",
                    $"Offer to deliver the dispatch ({carried.DeliveryRewardGold}g)",
                    Group: DaggerfallDialogueTopicGroups.Quests));
                if (!carried.Revealed)
                {
                    topics.Add(new($"dispatch:buy:{carried.DispatchId}",
                        $"Bribe courier to inspect dispatch ({carried.BribePriceGold}g)",
                        Group: DaggerfallDialogueTopicGroups.Quests));
                }
                topics.Add(new($"dispatch:intercept:{carried.DispatchId}",
                    "Seize the sealed dispatch",
                    Group: DaggerfallDialogueTopicGroups.Quests));
            }
            if (site is not null && OffersNoticeBoardTopic(npc)
                && _courierDispatches.TryGetPlayerCarriedFor(site) is { } deliverable)
            {
                topics.Add(new($"dispatch:turn-in:{deliverable.DispatchId}",
                    $"Deliver sealed dispatch ({deliverable.Summary})",
                    Group: DaggerfallDialogueTopicGroups.Quests));
            }
        }
        if (_templeService is not null && OffersTopic(npc, DaggerfallDialogueTopic.Donate))
            topics.Add(new("donate", "Donate gold"));
        if (_templeService is not null && OffersTopic(npc, DaggerfallDialogueTopic.Cure))
            topics.Add(new("cure", "Cure afflictions"));
        if (_partyHireTopics is not null)
            topics.AddRange(_partyHireTopics(npc));
        if (_noticeBounties is not null)
            foreach (DaggerfallNoticeBountySave bounty in _noticeBounties.Claimable(npc))
                topics.Add(new($"claim-bounty:{bounty.LeadId}", $"Claim the bounty: {bounty.Target} ({bounty.Reward}g)",
                    Group: DaggerfallDialogueTopicGroups.Quests));
        if (session.ProviderTopics.Count != 0)
            topics.AddRange(session.ProviderTopics);
        else
        {
            if (_guildService is not null && OffersGuildTopic(npc, DaggerfallDialogueTopic.Join))
                topics.Add(new("join", "Join or rejoin guild"));
            if (_guildService is not null && OffersGuildTopic(npc, DaggerfallDialogueTopic.Teleport))
                topics.Add(new("teleport", "Guild teleportation"));
            if (_guildService is not null && OffersGuildTopic(npc, DaggerfallDialogueTopic.Spymaster))
                topics.Add(new("spymaster", "Consult spymaster"));
            if (_guildService is not null && OffersGuildTopic(npc, DaggerfallDialogueTopic.RankReview))
                topics.Add(new("rank-review", "Review guild standing"));
            if (_guildService is not null && OffersGuildTopic(npc, DaggerfallDialogueTopic.Armor))
                topics.Add(new("armor", "Claim order armor"));
            if (_guildService is not null && OffersGuildTopic(npc, DaggerfallDialogueTopic.House))
                topics.Add(new("house", "Claim order house"));
        }
        _publish(new DaggerfallDialogueView(
            session.Revision,
            npc.DisplayName ?? npc.Role,
            session.Greeting,
            session.Tone.ToString().ToLowerInvariant(),
            session.Question,
            session.Reply,
            topics,
            [.. session.Diagnostics]) { QuestContacts = _questContacts(npc.DurableId) });
    }

    private static bool OffersNoticeBoardTopic(DaggerfallNpc npc) =>
        npc.Role.Equals("guard", StringComparison.OrdinalIgnoreCase)
        || npc.Role.Equals("tavern keeper", StringComparison.OrdinalIgnoreCase)
        || npc.Role.Equals("merchant", StringComparison.OrdinalIgnoreCase)
        || npc.Role.Equals("priest", StringComparison.OrdinalIgnoreCase)
        || npc.Services.Contains("tavern", StringComparer.OrdinalIgnoreCase)
        || npc.Services.Contains("quests", StringComparer.OrdinalIgnoreCase);

    private static bool OffersTopic(DaggerfallNpc npc, DaggerfallDialogueTopic topic) =>
        npc.Services.Contains(topic == DaggerfallDialogueTopic.Donate ? "donate" : "cure-disease", StringComparer.Ordinal);

    private static bool OffersGuildTopic(DaggerfallNpc npc, DaggerfallDialogueTopic topic) => topic switch
    {
        DaggerfallDialogueTopic.Join or DaggerfallDialogueTopic.RankReview => npc.Services.Any(service => service is "training" or "quests" or "repair"
            or "identify" or "armor" or "house" or "donate" or "cure-disease" or "buy-spells" or "make-spells"
            or "buy-magic-items" or "make-magic-items" or "buy-potions" or "make-potions" or "buy-soulgems"
            or "daedra-summoning" or "spymaster" or "teleport" or "sell-magic-items"),
        DaggerfallDialogueTopic.RestoreAttributes => npc.Services.Any(service => service is "donate" or "cure-disease" or "training" or "quests" or "buy-potions" or "make-potions" or "buy-magic-items" or "make-magic-items" or "buy-spells" or "make-spells" or "buy-soulgems" or "daedra-summoning"),
        DaggerfallDialogueTopic.Teleport => npc.Services.Contains("teleport", StringComparer.Ordinal),
        DaggerfallDialogueTopic.Spymaster => npc.Services.Contains("spymaster", StringComparer.Ordinal),
        DaggerfallDialogueTopic.Armor => npc.Services.Contains("armor", StringComparer.Ordinal),
        DaggerfallDialogueTopic.House => npc.Services.Contains("house", StringComparer.Ordinal),
        _ => false,
    };

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
            "courier-news" => DaggerfallDialogueTopic.CourierNews,
            "notice-board" => DaggerfallDialogueTopic.NoticeBoard,
            "referral" => DaggerfallDialogueTopic.Referral,
            "curry-favour" => DaggerfallDialogueTopic.CurryFavour,
            "work" => DaggerfallDialogueTopic.Work,
            "donate" => DaggerfallDialogueTopic.Donate,
            "cure" => DaggerfallDialogueTopic.Cure,
            "rank-review" => DaggerfallDialogueTopic.RankReview,
            "armor" => DaggerfallDialogueTopic.Armor,
            "house" => DaggerfallDialogueTopic.House,
            "restore-attributes" => DaggerfallDialogueTopic.RestoreAttributes,
            "join" => DaggerfallDialogueTopic.Join,
            "teleport" => DaggerfallDialogueTopic.Teleport,
            "spymaster" => DaggerfallDialogueTopic.Spymaster,
            "hire-companion" => DaggerfallDialogueTopic.HireCompanion,
            "hire-driver" => DaggerfallDialogueTopic.HireDriver,
            "dismiss-party" => DaggerfallDialogueTopic.DismissParty,
            "restore-exile" => DaggerfallDialogueTopic.RestoreExile,
            _ => default,
        };
        if (value is "directions" or "news" or "courier-news" or "notice-board" or "referral" or "curry-favour" or "work" or "donate" or "cure" or "rank-review" or "armor" or "house" or "join" or "teleport" or "spymaster" or "restore-attributes" or "hire-companion" or "hire-driver" or "dismiss-party" or "restore-exile") return true;
        if (value is { Length: > 16 } dynamicDenounce && dynamicDenounce.StartsWith("denounce-holder:", StringComparison.Ordinal))
        {
            topic = DaggerfallDialogueTopic.DenounceOfficeHolder;
            target = dynamicDenounce["denounce-holder:".Length..];
            return target.Length > 0;
        }
        if (value is { Length: > 10 } dynamicIntroduce && dynamicIntroduce.StartsWith("introduce:", StringComparison.Ordinal))
        {
            topic = DaggerfallDialogueTopic.Introduce;
            target = dynamicIntroduce["introduce:".Length..];
            return target.Length > 0;
        }
        if (value is { Length: > 10 } dynamicDirection && dynamicDirection.StartsWith("direction:", StringComparison.Ordinal))
        {
            topic = DaggerfallDialogueTopic.Directions;
            // Keep the canonical directory identity intact. The map resolver and the disclosure
            // owner both use the same prefix, and stripping it here made a valid UI directory
            // option fail to resolve while the generic directions topic still worked.
            target = dynamicDirection;
            return target.Length > 0;
        }
        if (value is { Length: > 17 } dynamicDeliver && dynamicDeliver.StartsWith("dispatch:deliver:", StringComparison.Ordinal))
        {
            topic = DaggerfallDialogueTopic.DispatchAccept;
            target = dynamicDeliver["dispatch:deliver:".Length..];
            return target.Length > 0;
        }
        if (value is { Length: > 13 } dynamicBuy && dynamicBuy.StartsWith("dispatch:buy:", StringComparison.Ordinal))
        {
            topic = DaggerfallDialogueTopic.DispatchBuy;
            target = dynamicBuy["dispatch:buy:".Length..];
            return target.Length > 0;
        }
        if (value is { Length: > 19 } dynamicIntercept && dynamicIntercept.StartsWith("dispatch:intercept:", StringComparison.Ordinal))
        {
            topic = DaggerfallDialogueTopic.DispatchIntercept;
            target = dynamicIntercept["dispatch:intercept:".Length..];
            return target.Length > 0;
        }
        if (value is { Length: > 13 } dynamicBounty && dynamicBounty.StartsWith("claim-bounty:", StringComparison.Ordinal))
        {
            topic = DaggerfallDialogueTopic.ClaimBounty;
            target = dynamicBounty["claim-bounty:".Length..];
            return target.Length > 0;
        }
        if (value is { Length: > 17 } dynamicTurnIn && dynamicTurnIn.StartsWith("dispatch:turn-in:", StringComparison.Ordinal))
        {
            topic = DaggerfallDialogueTopic.DispatchTurnIn;
            target = dynamicTurnIn["dispatch:turn-in:".Length..];
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
            DaggerfallRandomKey.Seed,
            RandomScope,
            $"session:{session.Revision}:npc:{session.TargetId}:{purpose}",
            minimum,
            maximum)).Value);

    private sealed record DaggerfallDialogueNewsCandidate(
        DaggerfallTextKey TextKey,
        int Faction1,
        int Faction2,
        long TimeLimit = 0);

    private sealed record DaggerfallDialogueNpc(EntityId Entity, WorldPoint Position);

    private sealed class TalkSession(long targetId, EntityId actor, WorldPoint position, DaggerfallNpcSite site, string revision, string greeting)
    {
        internal long TargetId { get; } = targetId;
        internal EntityId Actor { get; } = actor;
        internal WorldPoint Position { get; set; } = position;
        internal DaggerfallNpcSite Site { get; } = site;
        internal string Revision { get; } = revision;
        internal string Greeting { get; } = greeting;
        internal bool Spymaster { get; set; }
        internal DaggerfallDialogueTone Tone { get; set; }
        internal string? OpeningLine { get; set; }
        internal string? Question { get; set; }
        internal string? Reply { get; set; }
        internal int QuestionCount { get; set; }
        internal bool NewsAnswered { get; set; }
        internal int CourierNewsIndex { get; set; }
        internal Dictionary<DaggerfallDialogueTone, int> ToneModifiers { get; } = [];
        internal HashSet<DaggerfallSkillUseReason> SkillReasons { get; } = [];
        internal List<string> Diagnostics { get; } = [];
        internal List<DaggerfallDialogueTopicOption> ProviderTopics { get; } = [];
    }
}
