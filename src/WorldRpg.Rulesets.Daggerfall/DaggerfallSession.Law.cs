using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallLegalView(string Revision, string Phase, string Title, string[] Charges,
    string Message, DaggerfallLegalChoice[] Choices);
internal sealed record DaggerfallLegalChoice(string Id, string Label);

internal sealed partial class DaggerfallSession
{
    private DaggerfallLegalResponseSave? CurrentLegalResponse => State.Crime.LegalResponses.FirstOrDefault(response => response.Modal)
        ?? (_site.Region is int region ? State.Crime.Response(region) : null);
    private bool LegalModalOpen => CurrentLegalResponse?.Modal == true;

    // IncidentRecorded is emitted only by the canonical owner after its operation deduplication.
    private void AdmitLegalIncident(DaggerfallCrimeIncidentSave incident)
    {
        if (!incident.Reported && incident.Witnesses.Query != DaggerfallCrimeWitnessQuery.CompletedWithWitnesses) return;
        int loss = DaggerfallCrimePolicy.RegionalReputationLoss(incident.Crime);
        if (incident.Crime != DaggerfallCrimeKind.CriminalConspiracy) ChangeLegalReputation(incident.Region, -loss);
        var response = State.Crime.Response(incident.Region);
        string[] charges = State.Crime.PendingCharges(incident.Region).Select(value => value.OperationId).ToArray();
        if (response is not null)
        {
            // A later incident (including a consequence during prison time) cannot silently
            // join an accusation whose penalty or sentence was already accepted.
            if (response.Phase is DaggerfallLegalPhase.Court or DaggerfallLegalPhase.Sentence or DaggerfallLegalPhase.Prison) return;
            State.Crime.SetResponse(response with { Charges = charges });
            return;
        }
        StartLegalResponse(incident, charges);
    }

    private void StartLegalResponse(DaggerfallCrimeIncidentSave incident, string[] charges)
    {
        bool guardSaw = incident.Witnesses.WitnessActorIds.Any(id => _roster.Definitions.GetValueOrDefault(id)?.MobileId == 146
            || State.Npcs.All.Any(npc => npc.DurableId == id && npc.Role == "guard"));
        State.Crime.SetResponse(new(incident.OperationId, incident.Region, charges, DaggerfallLegalPhase.Pursuit,
            DaggerfallWorldProfileKeySave.Capture(_activeProfileKey), guardSaw ? 0 : CrimeRoll(incident.OperationId, "guard-delay", _tuning.Law.MinimumResponseSeconds, _tuning.Law.MaximumResponseSeconds), []));
    }

    private void ChangeLegalReputation(int region, int change)
    {
        State.Social.ChangeRegionalReputation(region, change);
        var people = _definitions.Factions.Factions.Values.FirstOrDefault(faction => faction.Type == 15 && faction.Region == region);
        if (people is not null) State.Social.ChangeFactionReputation(people.Id, change / 2, DaggerfallFactionReputationChange.Propagate);
    }

    private void CheckStandingLaw(long minuteBefore, long minuteAfter)
    {
        if (_site.Region is not int region || CurrentLegalResponse is not null || LegalModalOpen
            || _activeProfileKey.Kind == DaggerfallWorldProfileKind.Dungeon || State.RacialOverrides.Current?.SuppressCrime == true) return;
        bool banished = State.Crime.BanishedRegions.Contains(region);
        bool wanted = State.Social.RegionalReputation(region) < -10;
        if (!banished && !wanted) return;
        for (long minute = minuteBefore + 1; minute <= minuteAfter; minute++)
        {
            string operation = $"regional-wanted:{region}:{minute}";
            if (wanted && CrimeRoll(operation, "wanted", 1, 100) <= 5 || banished && CrimeRoll(operation, "banished", 1, 100) <= 10)
            {
                ReportCrime(new(operation, DaggerfallCrimeKind.CriminalConspiracy, DaggerfallCrimeStage.Completed,
                    DaggerfallActorIdentity.PlayerEntityId, null, region, minute, DaggerfallCrimeTargetKind.Unknown,
                    DaggerfallCrimeWitnessEvidence.NotQueried, DaggerfallCrimeGuildCredit.None, Reported: true));
                return;
            }
        }
    }

    private void ReconcileLawSite()
    {
        foreach (var response in State.Crime.LegalResponses.ToArray())
        {
            if (response.Modal) continue; // Court relocation preserves the current case.
            if (response.Profile.Require() != _activeProfileKey && response.Phase is DaggerfallLegalPhase.Pursuit or DaggerfallLegalPhase.Resisting)
                State.Crime.SetResponse(response with { Phase = DaggerfallLegalPhase.Escaped });
        }
    }

    private void UpdateLaw(double seconds)
    {
        var response = CurrentLegalResponse;
        if (response is null || response.Modal || State.Actors.Player.IsDefeated
            || State.RacialOverrides.Current?.SuppressCrime == true || !_sites.ActiveLocationLoaded
            || _activeProfileKey.Kind == DaggerfallWorldProfileKind.Dungeon) return;
        if (response.Phase == DaggerfallLegalPhase.Escaped)
        {
            response = response with { Phase = DaggerfallLegalPhase.Pursuit, Profile = DaggerfallWorldProfileKeySave.Capture(_activeProfileKey), ResponseDelaySeconds = _tuning.Law.MinimumResponseSeconds };
            State.Crime.SetResponse(response);
        }
        if (response.ResponseDelaySeconds > 0)
        {
            response = response with { ResponseDelaySeconds = Math.Max(0, response.ResponseDelaySeconds - seconds) };
            State.Crime.SetResponse(response);
            if (response.ResponseDelaySeconds > 0) return;
        }
        var live = response.Guards.Where(id => State.Actors.TryGet(id, out var actor) && !actor.IsDefeated).ToList();
        // A wandering guard is initially a social civilian. As in PlayerEntity's guard
        // conversion, retire that representation and admit a real watch actor at its pose.
        // Its new durable combat identity then remains in this case and the site delta.
        foreach (var npc in State.Npcs.All.Where(npc => npc.Kind == DaggerfallNpcKind.Civilian
            && npc.Role == "guard" && State.Npcs.IsGameplayActive(npc.DurableId)).ToArray())
        {
            if (!State.Actors.TryGet(npc.DurableId, out var actor) || actor.IsDefeated) continue;
            long guard = AdmitCityWatch(actor.Pose);
            RetireNpcActor(npc.DurableId);
            live.Add(guard);
        }
        // Nearby real watchmen join the response; dynamic arrivals use the same roster and safe
        // spatial placement as summoned actors. Detached guard identities remain in their site delta.
        foreach (var actor in State.Actors.All.Where(actor => !actor.IsDefeated
            && _roster.Definitions.GetValueOrDefault(actor.DurableId)?.MobileId == 146))
            if (!live.Contains(actor.DurableId)) live.Add(actor.DurableId);
        if (response.Phase == DaggerfallLegalPhase.Resisting && live.Count == 0)
        {
            State.Crime.SetResponse(response with { Phase = DaggerfallLegalPhase.Escaped, ResponseDelaySeconds = _tuning.Law.MinimumResponseSeconds });
            return;
        }
        if (live.Count == 0 && State.PlayerControl.Position is WorldPoint player)
        {
            int count = CrimeRoll(response.Id, "guard-count", _tuning.Law.MinimumGuards, _tuning.Law.MaximumGuards);
            for (int index = 0; index < count; index++)
            {
                if (!TryGuardArrivalPose($"{response.Id}:{_activeProfileKey.LogicalId}:{response.Guards.Length}:{index}", player, out ActorPose pose)) continue;
                live.Add(AdmitCityWatch(pose));
            }
        }
        response = response with { Guards = response.Guards.Concat(live).Distinct().ToArray() };
        State.Crime.SetResponse(response);
        foreach (long guard in live) _enemyBehavior.MakeHostile(guard);
        if (response.Phase == DaggerfallLegalPhase.Pursuit && State.PlayerControl.Position is WorldPoint position)
        {
            var visible = QueryEnemies(DaggerfallActorIdentity.PlayerEntityId, position, Vector3.UnitZ, _tuning.Law.ArrestReach, -1,
                definition => definition.MobileId == 146).Where(pair => pair.Kind == PerceptionPairKind.Visible);
            if (visible.Any(pair => live.Contains(checked((long)pair.Target)))) OpenArrest(response);
        }
    }

    private long AdmitCityWatch(ActorPose pose)
    {
        var watch = _definitions.Actors.Values.Single(definition => definition.MobileId == 146);
        long id = _roster.Spawn(watch.Id.Value, pose);
        _enemyBehavior.MakeHostile(id);
        return id;
    }

    private bool TryGuardArrivalPose(string operation, WorldPoint player, out ActorPose pose)
    {
        WorldPoint origin = player;
        float minimum = _tuning.Law.MinimumArrivalDistance, maximum = _tuning.Law.MaximumArrivalDistance;
        if (_activeProfileKey.Kind == DaggerfallWorldProfileKind.Interior)
        {
            // PlayerEntity.SpawnCityGuards brings the watch through the lowest outer door.
            // Normalized portals identify that entrance; Engine queries admit clear ground nearby.
            var profile = _sites.Projection.Inputs;
            var entrance = profile.Portals.Where(portal => _sites.Profiles?.Keys.Any(key =>
                    key.LogicalId == portal.DestinationLogicalProfile && key.Kind == DaggerfallWorldProfileKind.Exterior) == true)
                .OrderBy(portal => portal.Position.Y).ThenBy(portal => portal.Id, StringComparer.Ordinal).FirstOrDefault();
            origin = entrance?.Position ?? profile.Anchors["start"].Position;
            origin = WorldPoint.From(origin.ToVector() + Vector3.UnitY * SummonSeparation);
            minimum = 0;
            maximum = SummonSeparation * 2; // Space a small arriving group around the entrance clearance.
        }
        if (!TrySpawnPose(operation, "daggerfall.guard-arrival.v1", origin, minimum, maximum, out pose)) return false;
        pose = new(pose.Position, MathF.Atan2(player.X - pose.Position.X, -(player.Z - pose.Position.Z)));
        return true;
    }

    private bool IsLawGuard(long actor) => CurrentLegalResponse is { Modal: false } response && response.Guards.Contains(actor);

    private DaggerfallActivationOutcome SurrenderToGuard(long actor)
    {
        if (!IsLawGuard(actor)) return new(false, "This guard is not handling your case.");
        OpenArrest(CurrentLegalResponse!);
        return new(true, "The guard offers to accept your surrender.");
    }

    private void OpenArrest(DaggerfallLegalResponseSave response)
    {
        State.Crime.SetResponse(response with { Phase = DaggerfallLegalPhase.Arrest });
        _input.ClearHeldInput();
        _interactions.SetMenuOpen(false);
        Presentation.SetOutcome("Halt! The city watch demands your surrender.");
    }

    internal DaggerfallLegalView? LegalView
    {
        get
        {
            var response = CurrentLegalResponse;
            if (response?.Modal != true) return null;
            string[] charges = response.Charges.Select(id => CrimeLabel(State.Crime.Incidents.Single(value => value.OperationId == id).Crime)).ToArray();
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

    internal void ChooseLegal(string revision, string choice)
    {
        var response = CurrentLegalResponse;
        if (response is null || response.Revision != revision || !response.Modal || State.Actors.Player.IsDefeated) return;
        if (response.Phase == DaggerfallLegalPhase.Arrest)
        {
            if (choice is "resist" or "escape")
            {
                State.Crime.SetResponse(response with { Phase = DaggerfallLegalPhase.Resisting });
                foreach (long guard in response.Guards) _enemyBehavior.MakeHostile(guard);
                _input.ClearHeldInput();
                Presentation.SetOutcome(choice == "escape" ? "You flee. The charges remain in this region." : "You resist arrest. The watch attacks.");
            }
            else if (choice == "yield") BeginCourt(response);
            return;
        }
        if (response.Phase == DaggerfallLegalPhase.Court && choice is "guilty" or "etiquette" or "streetwise")
        {
            var plea = choice == "guilty" ? DaggerfallCourtPlea.Guilty : choice == "etiquette" ? DaggerfallCourtPlea.Etiquette : DaggerfallCourtPlea.Streetwise;
            int skill = plea == DaggerfallCourtPlea.Guilty ? 0 : State.Actors.Player.Stats.GetStat(StatId.Parse(choice)).ValueInt;
            int personality = State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).ValueInt;
            int draw = 0;
            var sentence = DaggerfallCourtPolicy.Decide(response.Penalty!, plea, State.Social.RegionalReputation(response.Region), skill, personality,
                (min, max) => CrimeRoll(response.Id, $"trial:{draw++}", min, max));
            State.Crime.SetResponse(response with { Phase = DaggerfallLegalPhase.Sentence, Sentence = sentence });
            if (plea != DaggerfallCourtPlea.Guilty)
                State.SkillUses.Record(new(choice, plea == DaggerfallCourtPlea.Etiquette ? DaggerfallSkillUseReason.CourtEtiquette : DaggerfallSkillUseReason.CourtStreetwise, DaggerfallSkillUseOutcome.Accepted));
            return;
        }
        if (response.Phase == DaggerfallLegalPhase.Sentence && choice == "accept") ApplyCourtSentence(response);
        else if (response.Phase == DaggerfallLegalPhase.Prison && choice == "continue") ServePrison(response);
    }

    private void BeginCourt(DaggerfallLegalResponseSave response)
    {
        State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1);
        int draw = 0;
        DaggerfallCourtPenalty penalty = new(0, 0, false);
        bool rescued = true;
        // The current owner retains every pending charge. Apply the classic table to each one,
        // then fit the combined accusation to the one actual purse before the plea reduction.
        foreach (string id in response.Charges)
        {
            var charge = State.Crime.Incidents.Single(incident => incident.OperationId == id);
            var part = DaggerfallCourtPolicy.Accuse(charge.Crime, State.Social.RegionalReputation(response.Region), ulong.MaxValue,
                (min, max) => CrimeRoll(response.Id, $"accusation:{id}:{draw++}", min, max));
            penalty = new(checked(penalty.Fine + part.Fine), checked(penalty.PrisonDays + part.PrisonDays), penalty.Severe || part.Severe);
            int? rescuingGuild = charge.Crime is DaggerfallCrimeKind.Assault or DaggerfallCrimeKind.Murder ? 108
                : charge.Crime is DaggerfallCrimeKind.AttemptedBreakingAndEntering or DaggerfallCrimeKind.Trespassing or DaggerfallCrimeKind.BreakingAndEntering or DaggerfallCrimeKind.Pickpocketing ? 42 : null;
            rescued &= rescuingGuild is int faction && _definitions.Factions.Factions.ContainsKey(faction)
                && State.Social.GuildEligibility(faction) is { IsMember: true } guild && guild.Rank >= CrimeRoll(response.Id, $"guild-rescue:{id}", 0, 19);
        }
        var affordable = DaggerfallCourtPolicy.FitAvailableGold(new(DaggerfallCourtOutcome.Convicted, penalty.Fine, penalty.PrisonDays), State.Currency.Read().Gold);
        penalty = penalty with { Fine = affordable.Fine, PrisonDays = affordable.PrisonDays };
        State.Crime.SetResponse(response with { Phase = rescued ? DaggerfallLegalPhase.Sentence : DaggerfallLegalPhase.Court,
            Profile = DaggerfallWorldProfileKeySave.Capture(CourtReleaseProfile()),
            Penalty = penalty, Sentence = rescued ? new(DaggerfallCourtOutcome.GuildRescue, 0, 0) : null });
        RelocateCourtPlayer(State.Crime.Response(response.Region)!.Profile.Require());
    }

    private void ApplyCourtSentence(DaggerfallLegalResponseSave response)
    {
        var sentence = DaggerfallCourtPolicy.FitAvailableGold(response.Sentence!, State.Currency.Read().Gold);
        if (sentence.Outcome == DaggerfallCourtOutcome.Convicted)
        {
            State.Currency.TrySpendGold(checked((ulong)sentence.Fine), []);
            ConfiscateStolenProperty();
        }
        // The persisted prison phase means money, confiscation and the plea cannot replay after save/load.
        response = response with { Phase = DaggerfallLegalPhase.Prison, Sentence = sentence,
            PrisonSecondsRemaining = checked((long)sentence.PrisonDays * 86400 + 240 * 60) };
        State.Crime.SetResponse(response);
        if (sentence.PrisonDays == 0) ServePrison(response);
    }

    private void ServePrison(DaggerfallLegalResponseSave response)
    {
        var elapsed = AdvanceElapsedTime(response.PrisonSecondsRemaining);
        response = response with { PrisonSecondsRemaining = elapsed.RemainingSeconds };
        State.Crime.SetResponse(response);
        if (elapsed.RemainingSeconds > 0 || State.Actors.Player.IsDefeated) return;
        // Resolve only after the shared time owners settle, including any loan, quest or disease consequences.
        foreach (string id in response.Sentence!.Outcome == DaggerfallCourtOutcome.Banished ? [] : response.Charges)
        {
            var charge = State.Crime.Incidents.Single(value => value.OperationId == id);
            ChangeLegalReputation(response.Region, DaggerfallCrimePolicy.RegionalReputationLoss(charge.Crime) / 2 - 1);
        }
        State.Crime.ResolveResponse(response.Region, response.Sentence!.Outcome, MinuteIndex(_time.Calendar));
        foreach (long id in response.Guards)
        {
            if (State.Actors.TryGet(id, out _)) _roster.RemoveQuestActor(id);
            else RetireDetachedActor(id);
        }
        foreach (string id in new[] { "health", "stamina", "magicka" })
        {
            var track = State.Actors.Player.Stats.GetTrack(TrackId.Parse(id)); track.SetCurrent(track.Maximum.Value);
        }
        RelocateCourtPlayer(response.Profile.Require());
        var pending = State.Crime.PendingCharges(response.Region);
        if (pending.Count > 0) StartLegalResponse(pending[0], pending.Select(charge => charge.OperationId).ToArray());
        _input.ClearHeldInput();
        Presentation.SetOutcome(CourtReleaseText(response.Sentence.Outcome));
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

    private DaggerfallWorldProfileKey CourtReleaseProfile()
    {
        if (_sites.ReturnProfile is { Kind: DaggerfallWorldProfileKind.Exterior } outside) return outside;
        return _sites.Profiles?.Keys.FirstOrDefault(key => key.Site == _activeProfileKey.Site && key.Kind == DaggerfallWorldProfileKind.Exterior)
            is { LogicalId: not null } exterior ? exterior : _activeProfileKey;
    }

    private void RelocateCourtPlayer(DaggerfallWorldProfileKey destination)
    {
        var profile = destination == _activeProfileKey ? _sites.Projection.Inputs : _sites.RequireProfiles().Require(destination);
        if (profile.Project.PlayerPosition is WorldPoint entrance)
            _sites.TryRelocatePlayer(destination, new("court-release", entrance, 0, 0));
    }

    private void ConfiscateStolenProperty()
    {
        var before = State.Equipment.Read();
        var inventory = State.Inventory.Read();
        var removed = inventory.UniqueItems.Where(item => State.ItemInstances.RequireUnique(State.Inventory.GetDurableItemId(item.Entity).Value).Stolen).ToArray();
        foreach (var item in removed) DestroyUniqueItem(State.Inventory.GetDurableItemId(item.Entity).Value);
        foreach (var stack in State.ItemInstances.StackItems.Where(value => value.Owner == DaggerfallItemOwner.Player && value.Metadata.Stolen).ToArray())
            ConsumeItemStack(stack.Owner, stack.Stack);
        _equipmentMoves.NotifyRemoved(before, before.Assignments.Where(value => removed.Any(item => item.Entity.Value == value.Item.EntityId)).Select(value => value.Item).DistinctBy(value => value.EntityId).ToArray());
        State.HeldEnchantments.Refresh();
    }
}
