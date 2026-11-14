using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Crime;

internal sealed record DaggerfallLegalView(string Revision, string Phase, string Title, string[] Charges,
    string Message, DaggerfallLegalChoice[] Choices);
internal sealed record DaggerfallLegalChoice(string Id, string Label);

/// <summary>What a legal choice leaves for the session's lists to carry out.</summary>
internal enum DaggerfallLegalStep
{
    /// <summary>The player resisted or ran: the watch attacks and the line says so.</summary>
    Resisted,
    /// <summary>The player yielded to the watch and goes before the court.</summary>
    Surrendered,
    /// <summary>The player accepted the court's judgment.</summary>
    SentenceAccepted,
    /// <summary>The player serves on in prison.</summary>
    PrisonContinued,
}

/// <summary>A legal choice the law admitted, the case it acted on and, for a resisted arrest, its line.</summary>
internal sealed record DaggerfallLegalChoiceResult(DaggerfallLegalStep Step, DaggerfallLegalResponseSave Response, string? Line = null);

/// <summary>
/// The law: the legal response to the player's reported crimes, from pursuit and arrest through the court and its
/// sentence to prison time and release, with the standing law that sends the watch after a wanted or banished player.
/// What a step does in other domains (controls, the menu, the guard's call, vitals, the fine and confiscation,
/// relocation, retiring the guards, the outcome line) is the session's lists: Arrest, ChooseLegal, BeginCourt,
/// ApplyCourtSentence and ServePrison.
/// </summary>
/// <param name="passTime">Passes a sentence's time through the session's one calendar, with no per-minute fatigue loss.</param>
/// <param name="gold">The gold the player carries, which the court fits a penalty to.</param>
/// <param name="locationLoaded">Whether the active location's people are loaded, so a pursuit can muster the watch.</param>
/// <param name="activeInputs">The active profile's inputs, whose portals bring the watch into an interior.</param>
/// <param name="returnProfile">The exterior the player entered the current interior from, if any.</param>
/// <param name="spawn">Admits an actor of this definition at this pose through the roster.</param>
/// <param name="retireCivilian">Retires a civilian whose guard the watch replaces.</param>
internal sealed class DaggerfallLaw(
    DaggerfallCrimeState crime,
    DaggerfallSocialState social,
    DaggerfallDefinitions definitions,
    DaggerfallLawTuning tuning,
    DaggerfallCrimes crimes,
    ActorsState actors,
    DaggerfallNpcRegistry npcs,
    PlayerControlState player,
    DaggerfallSiteContext site,
    DaggerfallActorPlacement placement,
    DaggerfallSkillUseReactions skillUses,
    DaggerfallRacialOverrides racialOverrides,
    Func<DaggerfallWorldProfileKey> activeProfile,
    Func<DaggerfallCalendar> calendar,
    Func<long, DaggerfallCalendarAdvance> passTime,
    Func<ulong> gold,
    Func<bool> locationLoaded,
    Func<DaggerfallSiteProfile> activeInputs,
    Func<DaggerfallSiteProfiles?> profiles,
    Func<DaggerfallWorldProfileKey?> returnProfile,
    Func<string, ActorPose, long> spawn,
    Action<long> makeHostile,
    Action<long> retireCivilian)
{
    /// <summary>The case in front of the player: the one holding the legal modal, else the active region's.</summary>
    internal DaggerfallLegalResponseSave? Current => crime.LegalResponses.FirstOrDefault(response => response.Modal)
        ?? (site.Region is int region ? crime.Response(region) : null);

    /// <summary>Whether the arrest, court, sentence or prison holds the player.</summary>
    internal bool ModalOpen => Current?.Modal == true;

    // IncidentRecorded is emitted only by the canonical owner after its operation deduplication.
    internal void Admit(DaggerfallCrimeIncidentSave incident)
    {
        if (!incident.Reported && incident.Witnesses.Query != DaggerfallCrimeWitnessQuery.CompletedWithWitnesses) return;
        int loss = DaggerfallCrimePolicy.RegionalReputationLoss(incident.Crime);
        if (incident.Crime != DaggerfallCrimeKind.CriminalConspiracy) ChangeReputation(incident.Region, -loss);
        var response = crime.Response(incident.Region);
        string[] charges = crime.PendingCharges(incident.Region).Select(value => value.OperationId).ToArray();
        if (response is not null)
        {
            // A later incident (including a consequence during prison time) cannot silently
            // join an accusation whose penalty or sentence was already accepted.
            if (response.Phase is DaggerfallLegalPhase.Court or DaggerfallLegalPhase.Sentence or DaggerfallLegalPhase.Prison) return;
            crime.SetResponse(response with { Charges = charges });
            return;
        }
        Start(incident, charges);
    }

    private void Start(DaggerfallCrimeIncidentSave incident, string[] charges)
    {
        bool guardSaw = incident.Witnesses.WitnessActorIds.Any(id => crimes.IsCityWatch(id)
            || npcs.All.Any(npc => npc.DurableId == id && npc.Role == "guard"));
        crime.SetResponse(new(incident.OperationId, incident.Region, charges, DaggerfallLegalPhase.Pursuit,
            DaggerfallWorldProfileKeySave.Capture(activeProfile()), guardSaw ? 0 : crimes.Roll(incident.OperationId, "guard-delay", tuning.MinimumResponseSeconds, tuning.MaximumResponseSeconds), []));
    }

    private void ChangeReputation(int region, int change)
    {
        social.ChangeRegionalReputation(region, change);
        var people = definitions.Factions.Factions.Values.FirstOrDefault(faction => faction.Type == 15 && faction.Region == region);
        if (people is not null) social.ChangeFactionReputation(people.Id, change / 2, DaggerfallFactionReputationChange.Propagate);
    }

    /// <summary>Each passing minute in a region that wants or banished the player may report them to the watch.</summary>
    internal void CheckStanding(long minuteBefore, long minuteAfter)
    {
        if (site.Region is not int region || Current is not null || ModalOpen
            || activeProfile().Kind == DaggerfallWorldProfileKind.Dungeon || racialOverrides.Current?.SuppressCrime == true) return;
        bool banished = crime.BanishedRegions.Contains(region);
        bool wanted = social.RegionalReputation(region) < -10;
        if (!banished && !wanted) return;
        for (long minute = minuteBefore + 1; minute <= minuteAfter; minute++)
        {
            string operation = $"regional-wanted:{region}:{minute}";
            if (wanted && crimes.Roll(operation, "wanted", 1, 100) <= 5 || banished && crimes.Roll(operation, "banished", 1, 100) <= 10)
            {
                crimes.Report(new(operation, DaggerfallCrimeKind.CriminalConspiracy, DaggerfallCrimeStage.Completed,
                    DaggerfallActorIdentity.PlayerEntityId, null, region, minute, DaggerfallCrimeTargetKind.Unknown,
                    DaggerfallCrimeWitnessEvidence.NotQueried, DaggerfallCrimeGuildCredit.None, Reported: true));
                return;
            }
        }
    }

    /// <summary>A pursuit or resistance left behind at another profile becomes an escape; the court's case stays.</summary>
    internal void ReconcileSite()
    {
        foreach (var response in crime.LegalResponses.ToArray())
        {
            if (response.Modal) continue; // Court relocation preserves the current case.
            if (response.Profile.Require() != activeProfile() && response.Phase is DaggerfallLegalPhase.Pursuit or DaggerfallLegalPhase.Resisting)
                crime.SetResponse(response with { Phase = DaggerfallLegalPhase.Escaped });
        }
    }

    /// <summary>
    /// One step of the pursuit: an escaped case resumes, the response delay runs down, the watch musters and turns
    /// hostile. Returns the case to arrest when a watchman of it sees the player within reach; otherwise null.
    /// </summary>
    internal DaggerfallLegalResponseSave? Update(double seconds)
    {
        var response = Current;
        var profile = activeProfile();
        if (response is null || response.Modal || actors.Player.IsDefeated
            || racialOverrides.Current?.SuppressCrime == true || !locationLoaded()
            || profile.Kind == DaggerfallWorldProfileKind.Dungeon) return null;
        if (response.Phase == DaggerfallLegalPhase.Escaped)
        {
            response = response with { Phase = DaggerfallLegalPhase.Pursuit, Profile = DaggerfallWorldProfileKeySave.Capture(profile), ResponseDelaySeconds = tuning.MinimumResponseSeconds };
            crime.SetResponse(response);
        }
        if (response.ResponseDelaySeconds > 0)
        {
            response = response with { ResponseDelaySeconds = Math.Max(0, response.ResponseDelaySeconds - seconds) };
            crime.SetResponse(response);
            if (response.ResponseDelaySeconds > 0) return null;
        }
        var live = response.Guards.Where(id => actors.TryGet(id, out var actor) && !actor.IsDefeated).ToList();
        // A wandering guard is initially a social civilian. As in PlayerEntity's guard
        // conversion, retire that representation and admit a real watch actor at its pose.
        // Its new durable combat identity then remains in this case and the site delta.
        foreach (var npc in npcs.All.Where(npc => npc.Kind == DaggerfallNpcKind.Civilian
            && npc.Role == "guard" && npcs.IsGameplayActive(npc.DurableId)).ToArray())
        {
            if (!actors.TryGet(npc.DurableId, out var actor) || actor.IsDefeated) continue;
            long guard = AdmitCityWatch(actor.Pose);
            retireCivilian(npc.DurableId);
            live.Add(guard);
        }
        // Nearby real watchmen join the response; dynamic arrivals use the same roster and safe
        // spatial placement as summoned actors. Detached guard identities remain in their site delta.
        foreach (var actor in actors.All.Where(actor => !actor.IsDefeated && crimes.IsCityWatch(actor.DurableId)))
            if (!live.Contains(actor.DurableId)) live.Add(actor.DurableId);
        if (response.Phase == DaggerfallLegalPhase.Resisting && live.Count == 0)
        {
            crime.SetResponse(response with { Phase = DaggerfallLegalPhase.Escaped, ResponseDelaySeconds = tuning.MinimumResponseSeconds });
            return null;
        }
        if (live.Count == 0 && player.Position is WorldPoint standing)
        {
            int count = crimes.Roll(response.Id, "guard-count", tuning.MinimumGuards, tuning.MaximumGuards);
            for (int index = 0; index < count; index++)
            {
                if (!TryGuardArrivalPose($"{response.Id}:{activeProfile().LogicalId}:{response.Guards.Length}:{index}", standing, out ActorPose pose)) continue;
                live.Add(AdmitCityWatch(pose));
            }
        }
        response = response with { Guards = response.Guards.Concat(live).Distinct().ToArray() };
        crime.SetResponse(response);
        foreach (long guard in live) makeHostile(guard);
        if (response.Phase == DaggerfallLegalPhase.Pursuit && player.Position is WorldPoint position)
        {
            var visible = placement.QueryEnemies(DaggerfallActorIdentity.PlayerEntityId, position, Vector3.UnitZ, tuning.ArrestReach, -1,
                definition => definition.MobileId == 146).Where(pair => pair.Kind == PerceptionPairKind.Visible);
            if (visible.Any(pair => live.Contains(checked((long)pair.Target)))) return response;
        }
        return null;
    }

    /// <summary>Admits a city watchman at this pose, hostile to the player.</summary>
    internal long AdmitCityWatch(ActorPose pose)
    {
        var watch = definitions.Actors.Values.Single(definition => definition.MobileId == 146);
        long id = spawn(watch.Id.Value, pose);
        makeHostile(id);
        return id;
    }

    /// <summary>Where an arriving watchman stands: around the player outdoors, around the lowest outer door indoors, facing the player.</summary>
    internal bool TryGuardArrivalPose(string operation, WorldPoint standing, out ActorPose pose)
    {
        WorldPoint origin = standing;
        float minimum = tuning.MinimumArrivalDistance, maximum = tuning.MaximumArrivalDistance;
        if (activeProfile().Kind == DaggerfallWorldProfileKind.Interior)
        {
            // PlayerEntity.SpawnCityGuards brings the watch through the lowest outer door.
            // Normalized portals identify that entrance; Engine queries admit clear ground nearby.
            var profile = activeInputs();
            var entrance = profile.Portals.Where(portal => profiles()?.TryGetLogicalProfile(portal.DestinationLogicalProfile, out DaggerfallSiteProfile? destination) == true
                    && destination.ProfileKind == DaggerfallWorldProfileKind.Exterior)
                .OrderBy(portal => portal.Position.Y).ThenBy(portal => portal.Id, StringComparer.Ordinal).FirstOrDefault();
            origin = entrance?.Position ?? profile.Anchors["start"].Position;
            origin = WorldPoint.From(origin.ToVector() + Vector3.UnitY * DaggerfallActorPlacement.SummonSeparation);
            minimum = 0;
            maximum = DaggerfallActorPlacement.SummonSeparation * 2; // Space a small arriving group around the entrance clearance.
        }
        if (!placement.TrySpawnPose(operation, "daggerfall.guard-arrival.v1", origin, minimum, maximum, out pose)) return false;
        pose = new(pose.Position, MathF.Atan2(standing.X - pose.Position.X, -(standing.Z - pose.Position.Z)));
        return true;
    }

    /// <summary>Whether this actor guards the case still in pursuit.</summary>
    internal bool IsGuard(long actor) => Current is { Modal: false } response && response.Guards.Contains(actor);

    /// <summary>The watch halts the player: the case holds the arrest's modal.</summary>
    internal void Arrest(DaggerfallLegalResponseSave response) =>
        crime.SetResponse(response with { Phase = DaggerfallLegalPhase.Arrest });

    /// <summary>The legal modal's page: the charges, the phase's message and its choices; null when no case holds the modal.</summary>
    internal DaggerfallLegalView? View
    {
        get
        {
            var response = Current;
            if (response?.Modal != true) return null;
            string[] charges = response.Charges.Select(id => CrimeLabel(crime.Incidents.Single(value => value.OperationId == id).Crime)).ToArray();
            return response.Phase switch
            {
                DaggerfallLegalPhase.Arrest => new(response.Revision, "arrest", "Halt! City watch", charges,
                    "You are wanted in this region. Surrender to face the charges, or resist arrest.",
                    [new("yield", "Surrender"), new("resist", "Resist arrest"), new("escape", "Run for it")]),
                DaggerfallLegalPhase.Court => new(response.Revision, "court", "Before the court", charges,
                    $"You stand accused. The proposed penalty is {response.Penalty!.Fine} gold and {response.Penalty.PrisonDays} days in prison.",
                    [new("guilty", "Plead guilty"), new("etiquette", "Not guilty — debate (Etiquette)"), new("streetwise", "Not guilty — lie (Streetwise)")]),
                DaggerfallLegalPhase.Sentence => new(response.Revision, "sentence", "Judgment", charges,
                    response.Sentence!.Outcome == DaggerfallCourtOutcome.Convicted
                        ? $"Convicted: {response.Sentence.Fine} gold and {response.Sentence.PrisonDays} days. Stolen property will be confiscated."
                        : response.Sentence.Outcome switch { DaggerfallCourtOutcome.Acquitted => "You are acquitted.", DaggerfallCourtOutcome.GuildRescue => "Your guild has secured your release.", _ => "You are banished from this region." },
                    [new("accept", response.Sentence.PrisonDays > 0 ? "Serve sentence" : "Leave court")]),
                _ => new(response.Revision, "prison", "In prison", charges,
                    $"Time remaining: {(response.PrisonSecondsRemaining + 86399) / 86400} days.", [new("continue", "Continue sentence")]),
            };
        }
    }

    /// <summary>
    /// Admits a choice on the legal modal's current page. A plea is decided here; what resisting, surrendering,
    /// accepting the judgment or serving on does is left to the session's lists. Null when nothing is left to do.
    /// </summary>
    internal DaggerfallLegalChoiceResult? Choose(string revision, string choice)
    {
        var response = Current;
        if (response is null || response.Revision != revision || !response.Modal || actors.Player.IsDefeated) return null;
        if (response.Phase == DaggerfallLegalPhase.Arrest)
        {
            if (choice is "resist" or "escape")
            {
                crime.SetResponse(response with { Phase = DaggerfallLegalPhase.Resisting });
                foreach (long guard in response.Guards) makeHostile(guard);
                return new(DaggerfallLegalStep.Resisted, response,
                    choice == "escape" ? "You flee. The charges remain in this region." : "You resist arrest. The watch attacks.");
            }
            return choice == "yield" ? new(DaggerfallLegalStep.Surrendered, response) : null;
        }
        if (response.Phase == DaggerfallLegalPhase.Court && choice is "guilty" or "etiquette" or "streetwise")
        {
            var plea = choice == "guilty" ? DaggerfallCourtPlea.Guilty : choice == "etiquette" ? DaggerfallCourtPlea.Etiquette : DaggerfallCourtPlea.Streetwise;
            int skill = plea == DaggerfallCourtPlea.Guilty ? 0 : actors.Player.Stats.GetStat(StatId.Parse(choice)).ValueInt;
            int personality = actors.Player.Stats.GetStat(StatId.Parse("personality")).ValueInt;
            int draw = 0;
            var sentence = DaggerfallCourtPolicy.Decide(response.Penalty!, plea, social.RegionalReputation(response.Region), skill, personality,
                (min, max) => crimes.Roll(response.Id, $"trial:{draw++}", min, max));
            crime.SetResponse(response with { Phase = DaggerfallLegalPhase.Sentence, Sentence = sentence });
            if (plea != DaggerfallCourtPlea.Guilty)
                skillUses.Record(new(choice, plea == DaggerfallCourtPlea.Etiquette ? DaggerfallSkillUseReason.CourtEtiquette : DaggerfallSkillUseReason.CourtStreetwise, DaggerfallSkillUseOutcome.Accepted));
            return null;
        }
        if (response.Phase == DaggerfallLegalPhase.Sentence && choice == "accept") return new(DaggerfallLegalStep.SentenceAccepted, response);
        if (response.Phase == DaggerfallLegalPhase.Prison && choice == "continue") return new(DaggerfallLegalStep.PrisonContinued, response);
        return null;
    }

    /// <summary>
    /// Puts the case before the court: the classic table's penalty for every pending charge, fitted to the purse,
    /// or the guild's rescue. Returns the profile the court releases the player into, where the player now stands.
    /// </summary>
    internal DaggerfallWorldProfileKey BeginCourt(DaggerfallLegalResponseSave response)
    {
        int draw = 0;
        DaggerfallCourtPenalty penalty = new(0, 0, false);
        bool rescued = true;
        // The current owner retains every pending charge. Apply the classic table to each one,
        // then fit the combined accusation to the one actual purse before the plea reduction.
        foreach (string id in response.Charges)
        {
            var charge = crime.Incidents.Single(incident => incident.OperationId == id);
            var part = DaggerfallCourtPolicy.Accuse(charge.Crime, social.RegionalReputation(response.Region), ulong.MaxValue,
                (min, max) => crimes.Roll(response.Id, $"accusation:{id}:{draw++}", min, max));
            penalty = new(checked(penalty.Fine + part.Fine), checked(penalty.PrisonDays + part.PrisonDays), penalty.Severe || part.Severe);
            int? rescuingGuild = charge.Crime is DaggerfallCrimeKind.Assault or DaggerfallCrimeKind.Murder ? 108
                : charge.Crime is DaggerfallCrimeKind.AttemptedBreakingAndEntering or DaggerfallCrimeKind.Trespassing or DaggerfallCrimeKind.BreakingAndEntering or DaggerfallCrimeKind.Pickpocketing ? 42 : null;
            rescued &= rescuingGuild is int faction && definitions.Factions.Factions.ContainsKey(faction)
                && social.GuildEligibility(faction) is { IsMember: true } guild && guild.Rank >= crimes.Roll(response.Id, $"guild-rescue:{id}", 0, 19);
        }
        var affordable = DaggerfallCourtPolicy.FitAvailableGold(new(DaggerfallCourtOutcome.Convicted, penalty.Fine, penalty.PrisonDays), gold());
        penalty = penalty with { Fine = affordable.Fine, PrisonDays = affordable.PrisonDays };
        crime.SetResponse(response with { Phase = rescued ? DaggerfallLegalPhase.Sentence : DaggerfallLegalPhase.Court,
            Profile = DaggerfallWorldProfileKeySave.Capture(CourtReleaseProfile()),
            Penalty = penalty, Sentence = rescued ? new(DaggerfallCourtOutcome.GuildRescue, 0, 0) : null });
        return crime.Response(response.Region)!.Profile.Require();
    }

    /// <summary>The judgment fitted to the gold the player carries now.</summary>
    internal DaggerfallCourtSentence FitSentence(DaggerfallLegalResponseSave response) =>
        DaggerfallCourtPolicy.FitAvailableGold(response.Sentence!, gold());

    /// <summary>
    /// Records the judgment as served from now: the persisted prison phase means money, confiscation and the plea
    /// cannot replay after save/load. Returns the case in prison.
    /// </summary>
    internal DaggerfallLegalResponseSave Imprison(DaggerfallLegalResponseSave response, DaggerfallCourtSentence sentence)
    {
        response = response with { Phase = DaggerfallLegalPhase.Prison, Sentence = sentence,
            PrisonSecondsRemaining = checked((long)sentence.PrisonDays * 86400 + 240 * 60) };
        crime.SetResponse(response);
        return response;
    }

    /// <summary>
    /// Serves the rest of the sentence. Once it is served and the player stands, the charges cost their release
    /// reputation and the case closes; returns the case released, else null.
    /// </summary>
    internal DaggerfallLegalResponseSave? Serve(DaggerfallLegalResponseSave response)
    {
        // The donor's court raises the clock by the sentence with no per-minute fatigue loss.
        var elapsed = passTime(response.PrisonSecondsRemaining);
        response = response with { PrisonSecondsRemaining = elapsed.RemainingSeconds };
        crime.SetResponse(response);
        if (elapsed.RemainingSeconds > 0 || actors.Player.IsDefeated) return null;
        // Resolve only after the shared time owners settle, including any loan, quest or disease consequences.
        foreach (string id in response.Sentence!.Outcome == DaggerfallCourtOutcome.Banished ? [] : response.Charges)
        {
            var charge = crime.Incidents.Single(value => value.OperationId == id);
            ChangeReputation(response.Region, DaggerfallCrimePolicy.RegionalReputationLoss(charge.Crime) / 2 - 1);
        }
        crime.ResolveResponse(response.Region, response.Sentence!.Outcome, calendar().MinuteNumber);
        return response;
    }

    /// <summary>A charge still pending in the region after a release opens the next case.</summary>
    internal void ReopenPending(int region)
    {
        var pending = crime.PendingCharges(region);
        if (pending.Count > 0) Start(pending[0], pending.Select(charge => charge.OperationId).ToArray());
    }

    /// <summary>The player name of a charge, as the court reads it.</summary>
    internal static string CrimeLabel(DaggerfallCrimeKind crime) => crime switch
    {
        DaggerfallCrimeKind.AttemptedBreakingAndEntering => "Attempted breaking and entering",
        DaggerfallCrimeKind.Trespassing => "Trespassing",
        DaggerfallCrimeKind.BreakingAndEntering => "Breaking and entering",
        DaggerfallCrimeKind.Assault => "Assault",
        DaggerfallCrimeKind.Murder => "Murder",
        DaggerfallCrimeKind.TaxEvasion => "Tax evasion",
        DaggerfallCrimeKind.CriminalConspiracy => "Criminal conspiracy",
        DaggerfallCrimeKind.Vagrancy => "Vagrancy",
        DaggerfallCrimeKind.Smuggling => "Smuggling",
        DaggerfallCrimeKind.Piracy => "Piracy",
        DaggerfallCrimeKind.HighTreason => "High treason",
        DaggerfallCrimeKind.Pickpocketing => "Pickpocketing",
        DaggerfallCrimeKind.Theft => "Theft",
        DaggerfallCrimeKind.Treason => "Treason",
        DaggerfallCrimeKind.LoanDefault => "Loan default",
        _ => "An unnamed crime",
    };

    /// <summary>The player sentence that closes a court case once its disposition has been served.</summary>
    internal static string CourtReleaseText(DaggerfallCourtOutcome outcome) => outcome switch
    {
        DaggerfallCourtOutcome.Convicted => "You have served your sentence. You are free to leave.",
        DaggerfallCourtOutcome.Acquitted => "You were acquitted. You are free to leave.",
        DaggerfallCourtOutcome.GuildRescue => "Your guild secured your release. You are free to leave.",
        DaggerfallCourtOutcome.Banished => "You have been banished from this region.",
        _ => "Your case is closed. You are free to leave.",
    };

    /// <summary>The exterior the court releases the player into: the one they entered from, else their location's own.</summary>
    private DaggerfallWorldProfileKey CourtReleaseProfile()
    {
        if (returnProfile() is { Kind: DaggerfallWorldProfileKind.Exterior } outside) return outside;
        DaggerfallWorldProfileKey active = activeProfile();
        DaggerfallWorldProfileKey exterior = DaggerfallWorldProfileIds.Exterior(active.Site);
        return profiles()?.Contains(exterior) == true ? exterior : active;
    }
}
