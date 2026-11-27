using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11136 social cleanups:
/// 1. Caravan and courier travellers preserve their home household FamilyId on the road so a witnessed roadside
///    murder starts a kin grudge at their origin settlement, while untouched returning travellers still prune cleanly.
/// 2. The ledger's <c>Met</c> flag is read in returning dialogue greetings, ambient barks, and <c>TryCurryFavour</c>.
/// 3. <see cref="DaggerfallWordSpread.SuppressInRegion"/> automatically holds cross-region news back from regions
///    sealed by severe plague quarantine, war-border lockdown, or cut-off blockade until lifted or delivered.
/// 4. The late-night <c>underworld-night</c> pulse tags walkers with role <c>underworld</c>, gives them distinct
///    late-night barks, and keeps them discreet about non-violent property crimes.
/// </summary>
public sealed class SocialCleanupsTests
{
    private const int DaggerfallRegion = 17;
    private const int WayrestRegion = 23;
    private const int SentinelRegion = 20;

    [Fact]
    public void Caravan_travellers_preserve_origin_household_and_witnessed_roadside_murder_starts_home_kin_grudge()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // Advance across daytime dispatch windows (06:00, 12:00, 18:00) so OverlandTraffic stages persistent bands.
        session.AdvanceElapsedTime(18L * 60L * 60L, idleFatigue: false);
        DaggerfallPersistentOverlandBand[] trafficBands =
        [
            .. session.Expedition.PersistentBands
                .Where(b => b.BandId.StartsWith(DaggerfallOverlandTraffic.BandIdPrefix, StringComparison.Ordinal)),
        ];
        Assert.NotEmpty(trafficBands);

        // Staged traffic members carry a valid home FamilyId in ActorLedger matching their origin site.
        DaggerfallPersistentOverlandBand? victimBand = null;
        ActorLedgerEntry? caravanMember = null;
        DaggerfallSiteRecord? caravanOrigin = null;
        foreach (DaggerfallPersistentOverlandBand band in trafficBands)
        {
            foreach (ActorLedgerEntry entry in session.State.ActorLedger.EntriesOnOverlandBand(band.BandId))
            {
                if (entry.Social is { FamilyId.Length: > 0 } social
                    && DaggerfallHouseholds.TryParseLocalSite(social.FamilyId, out DaggerfallSiteId originSiteId))
                {
                    victimBand = band;
                    caravanMember = entry;
                    caravanOrigin = TestPayload.Definitions.Locations.Records.First(r => r.Id == originSiteId);
                    break;
                }
            }
            if (caravanMember is not null)
                break;
        }
        Assert.NotNull(victimBand);
        Assert.NotNull(caravanMember);
        Assert.NotNull(caravanOrigin);
        string homeFamilyId = caravanMember!.Social!.FamilyId!;
        Assert.True(DaggerfallHouseholds.IsValidFamilyId(homeFamilyId));

        // Simulate a witnessed roadside murder of the caravan traveller -> attributed household ripple targets origin town!
        DaggerfallCrimeIncidentSave roadsideMurder = new(
            OperationId: "op-roadside-caravan-murder",
            Crime: DaggerfallCrimeKind.Murder,
            Stage: DaggerfallCrimeStage.Completed,
            PerpetratorActorId: DaggerfallActorIdentity.PlayerEntityId,
            AffectedActorOrOwnerId: caravanMember.DurableId,
            Region: WayrestRegion, // Even in another region on the road!
            GameMinute: session.Calendar.MinuteNumber,
            TargetKind: DaggerfallCrimeTargetKind.Civilian,
            Witnesses: new DaggerfallCrimeWitnessEvidence(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [999_001L]),
            GuildCredit: DaggerfallCrimeGuildCredit.CivilianMurder,
            Reported: true);
        session.SocialConsequences.ReactToAttributedCrime(roadsideMurder, originSite: null, minute: session.Calendar.MinuteNumber);

        int homeDelta = session.SocialConsequences.HouseholdStandingDelta(homeFamilyId, caravanOrigin);
        Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty, homeDelta);
        Assert.True(homeDelta <= DaggerfallIntroductions.BloodFeudDispositionThreshold);

        // An untouched traffic member who completes their outbound + return legs still prunes cleanly from ActorLedger despite carrying FamilyId!
        DaggerfallPersistentOverlandBand untouchedBand = trafficBands.FirstOrDefault(b => b.BandId != victimBand!.BandId) ?? victimBand!;
        ActorLedgerEntry untouchedMember = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(untouchedBand.BandId));
        if (untouchedMember.DurableId == caravanMember.DurableId)
        {
            Assert.False(untouchedMember.Social?.Met == true);
        }
        // Complete outbound arrival (which stages the return band unless already arriving home), then complete the return arrival!
        session.OverlandParties.OnPersistentBandArrived(untouchedBand);
        string returnBandId = untouchedBand.BandId + DaggerfallOverlandTraffic.ReturnBandSuffix;
        DaggerfallPersistentOverlandBand? returnLeg = session.Expedition.PersistentBands
            .FirstOrDefault(b => StringComparer.Ordinal.Equals(b.BandId, returnBandId));
        if (returnLeg is not null)
            session.OverlandParties.OnPersistentBandArrived(returnLeg);
        Assert.False(session.State.ActorLedger.TryGet(untouchedMember.DurableId, out _));
    }

    [Fact]
    public void Met_flag_drives_returning_dialogue_greeting_ambient_bark_and_curry_favour_bonus()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        session.State.Actors.Player.Stats.GetStat(StatId.Parse("etiquette")).BaseValue = 50;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("streetwise")).BaseValue = 50;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Personality.Value)).BaseValue = 50;

        long npcId = SpawnNpc(session, "met-test/citizen", "Alaric Voss", 0, DaggerfallRegion, ["talk"]);
        DaggerfallNpc npc = session.State.Npcs.Require(npcId);

        Assert.False(session.Introductions.HasMetActor(npcId));
        int chanceBeforeMet = session.Introductions.ComputeCurryFavourChance(npc, politeTone: true, bluntTone: false);

        // 1. First conversation: not yet met -> no returning-acquaintance line, sets Met = true.
        OpenDialogueWith(session, npcId);
        DaggerfallDialogueView firstView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.DoesNotContain("We meet again.", firstView.Greeting, StringComparison.Ordinal);
        Assert.DoesNotContain("Good to see you again.", firstView.Greeting, StringComparison.Ordinal);
        Assert.True(session.Introductions.HasMetActor(npcId));
        session.Dialogue.Close();

        // 2. After meeting: ComputeCurryFavourChance gains FamiliarAcquaintanceFavourBonus (+5), and returning dialogue recognizes the player!
        int chanceAfterMet = session.Introductions.ComputeCurryFavourChance(npc, politeTone: true, bluntTone: false);
        Assert.Equal(chanceBeforeMet + DaggerfallIntroductions.FamiliarAcquaintanceFavourBonus, chanceAfterMet);

        OpenDialogueWith(session, npcId);
        DaggerfallDialogueView secondView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains("We meet again.", secondView.Greeting, StringComparison.Ordinal);
        session.Dialogue.Close();

        // 3. Save/reload preserves Met = true and familiarity bonus!
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.True(restored.Introductions.HasMetActor(npcId));
        Assert.Equal(chanceAfterMet, restored.Introductions.ComputeCurryFavourChance(
            restored.State.Npcs.Require(npcId), politeTone: true, bluntTone: false));
    }

    [Fact]
    public void WordSpread_suppresses_cross_region_news_in_quarantined_or_blockaded_regions_until_delivered_or_lifted()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        DaggerfallSiteRecord origin = session.Site.ActiveSite!;
        DaggerfallSiteRecord wayrestTown = TestPayload.Definitions.Locations.Records
            .First(r => r.Exterior is not null && r.Id.Region == WayrestRegion);
        DaggerfallSiteRecord sentinelTown = TestPayload.Definitions.Locations.Records
            .First(r => r.Exterior is not null && r.Id.Region == SentinelRegion);
        int today = checked((int)session.Calendar.DayNumber);
        long nowMinute = session.Calendar.MinuteNumber;

        // Seal Wayrest under a severe PlagueScare (severity 3) while Sentinel stays open.
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.PlagueScare, WayrestRegion),
            DaggerfallWorldConditionKind.PlagueScare,
            WayrestRegion,
            Severity: 3,
            StartedDay: today));

        DaggerfallWordWavefrontSave wf = session.WordSpread.RecordNews(
            "wf:cleanup-suppression",
            "Court Decree in Daggerfall",
            "Royal proclamation issued from Daggerfall.",
            origin,
            nowMinute);

        Assert.NotNull(wf.SuppressedRegions);
        Assert.Contains(WayrestRegion, wf.SuppressedRegions!);
        Assert.DoesNotContain(SentinelRegion, wf.SuppressedRegions!);
        Assert.DoesNotContain(DaggerfallRegion, wf.SuppressedRegions!);

        // Wayrest is blocked ("suppressed"); Sentinel is in transit normally; origin stays instant.
        DaggerfallWordArrivalEvaluation atWayrest = session.WordSpread.EvaluateAt(wf, wayrestTown, nowMinute, today);
        Assert.Equal(DaggerfallWordArrivalStatus.Blocked, atWayrest.Status);
        Assert.Equal("suppressed", atWayrest.DisruptionReason);
        Assert.False(atWayrest.HasArrived);

        DaggerfallWordArrivalEvaluation atSentinel = session.WordSpread.EvaluateAt(wf, sentinelTown, nowMinute, today);
        Assert.NotEqual(DaggerfallWordArrivalStatus.Blocked, atSentinel.Status);

        // Save/reload preserves regional suppression.
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.True(restored.WordSpread.TryGet("wf:cleanup-suppression", out DaggerfallWordWavefrontSave? restoredWf));
        Assert.Equal(DaggerfallWordArrivalStatus.Blocked, restored.WordSpread.EvaluateAt(restoredWf!, wayrestTown, nowMinute, today).Status);

        // Player-delivered dispatch (AccelerateInRegion) breaks through regional suppression cleanly.
        Assert.True(restored.WordSpread.AccelerateInRegion("wf:cleanup-suppression", WayrestRegion));
        Assert.True(restored.WordSpread.TryGet("wf:cleanup-suppression", out DaggerfallWordWavefrontSave? deliveredWf));
        Assert.Equal(DaggerfallWordArrivalStatus.AcceleratedOverride, restored.WordSpread.EvaluateAt(deliveredWf!, wayrestTown, nowMinute, today).Status);

        // Removing a temporary quarantine lifts condition-owned suppression so normal arrival resumes AND old wavefronts prune cleanly!
        DaggerfallWordWavefrontSave removableWf = restored.WordSpread.RecordNews(
            "wf:cleanup-reopen", "Reopen Test Bulletin", "Held during quarantine then delivered after lifting.", origin, nowMinute);
        Assert.Equal(DaggerfallWordArrivalStatus.Blocked, restored.WordSpread.EvaluateAt(removableWf, wayrestTown, nowMinute, today).Status);

        string plagueTokenId = DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.PlagueScare, WayrestRegion);
        Assert.True(restored.WorldConditions.Remove(plagueTokenId));
        Assert.True(restored.WordSpread.TryGet("wf:cleanup-reopen", out DaggerfallWordWavefrontSave? reopenedWf));
        Assert.Null(reopenedWf!.SuppressedRegions);
        DaggerfallWordArrivalEvaluation reopenedEval = restored.WordSpread.EvaluateAt(reopenedWf, wayrestTown, nowMinute, today);
        Assert.Equal(DaggerfallWordArrivalStatus.InTransit, reopenedEval.Status);
        long arriveMinute = nowMinute + reopenedEval.RemainingMinutes + 5;
        Assert.Equal(DaggerfallWordArrivalStatus.ArrivedByCourier,
            restored.WordSpread.EvaluateAt(reopenedWf, wayrestTown, arriveMinute, checked((int)(arriveMinute / 1440L))).Status);

        // Expiring a timed quarantine also lifts suppression, resumes arrival, and allows PruneExpired to clean up after 30 days.
        restored.WorldConditions.Set(new DaggerfallWorldConditionToken(
            plagueTokenId, DaggerfallWorldConditionKind.PlagueScare, WayrestRegion,
            Severity: 3, StartedDay: today, ExpiresDay: today + 2));
        DaggerfallWordWavefrontSave expiringWf = restored.WordSpread.RecordNews(
            "wf:cleanup-expiry", "Expiry Test Bulletin", "Held until timed quarantine expires.", origin, nowMinute);
        Assert.Equal(DaggerfallWordArrivalStatus.Blocked, restored.WordSpread.EvaluateAt(expiringWf, wayrestTown, nowMinute, today).Status);

        long afterExpiryMinute = checked((long)(today + 3) * 1440L);
        int afterExpiryDay = today + 3;
        DaggerfallWordArrivalEvaluation afterExpiryEval = restored.WordSpread.EvaluateAt(expiringWf, wayrestTown, afterExpiryMinute, afterExpiryDay);
        Assert.NotEqual(DaggerfallWordArrivalStatus.Blocked, afterExpiryEval.Status);
        long afterPruneWindow = nowMinute + DaggerfallWordSpread.MaxRetainedWavefrontMinutes + 10;
        Assert.True(restored.WordSpread.PruneExpired(afterPruneWindow) > 0);
        Assert.False(restored.WordSpread.TryGet("wf:cleanup-expiry", out _));

        // A newly imposed seal mid-flight also blocks still-travelling cross-region news!
        Assert.True(restored.WorldConditions.Remove(plagueTokenId));
        DaggerfallWordWavefrontSave midFlightWf = restored.WordSpread.RecordNews(
            "wf:cleanup-midflight", "Midflight Bulletin", "Left before quarantine was declared.", origin, nowMinute);
        Assert.Equal(DaggerfallWordArrivalStatus.InTransit, restored.WordSpread.EvaluateAt(midFlightWf, wayrestTown, nowMinute, today).Status);

        restored.WorldConditions.Set(new DaggerfallWorldConditionToken(
            plagueTokenId, DaggerfallWorldConditionKind.PlagueScare, WayrestRegion, Severity: 3, StartedDay: today));
        Assert.True(restored.WordSpread.TryGet("wf:cleanup-midflight", out DaggerfallWordWavefrontSave? sealedMidFlight));
        Assert.Equal(DaggerfallWordArrivalStatus.Blocked, restored.WordSpread.EvaluateAt(sealedMidFlight!, wayrestTown, nowMinute, today).Status);
    }

    [Fact]
    public void Underworld_night_pulse_walkers_receive_underworld_role_distinct_barks_and_property_crime_discretion()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // Verify SourcePopulation.ResolvePlacementIdentity tags underworld-night placements with role "underworld".
        DaggerfallPopulationPlacement samplePlacement = new(
            "population/test/dynamic/005",
            new WorldPoint(4F, 0F, 4F),
            210,
            4,
            0,
            0,
            1005);
        (_, string underworldRole, _) = DaggerfallSourcePopulation.ResolvePlacementIdentity(
            TestPayload.Definitions, samplePlacement, "breton", underworldNight: true);
        (_, string civilianRole, _) = DaggerfallSourcePopulation.ResolvePlacementIdentity(
            TestPayload.Definitions, samplePlacement, "breton", underworldNight: false);
        Assert.Equal("underworld", underworldRole);
        Assert.Equal("civilian", civilianRole);

        // Spawn an ambient-bark underworld night contact and verify distinct underworld barks (normal + crackdown).
        long shadyId = SpawnNpc(session, "underworld/contact", "Night Contact", 0, DaggerfallRegion, ["talk"], role: "underworld", interaction: ActorInteractionPolicyKind.AmbientBark);
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(), t => checked((long)t.Identity.Value) == shadyId);

        DaggerfallActivationOutcome normalBark = session.Dialogue.ActivateNpc(new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, target));
        Assert.True(normalBark.Applied);
        Assert.Contains("Night Contact:", normalBark.Message, StringComparison.Ordinal);

        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.CrimeCrackdown, DaggerfallRegion),
            DaggerfallWorldConditionKind.CrimeCrackdown,
            DaggerfallRegion,
            Severity: 2,
            StartedDay: checked((int)session.Calendar.DayNumber)));
        DaggerfallActivationOutcome crackdownBark = session.Dialogue.ActivateNpc(new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, target));
        Assert.True(crackdownBark.Applied);
        Assert.Contains("Watch is thick", crackdownBark.Message, StringComparison.OrdinalIgnoreCase);

        // Witness discretion: underworld contact stays silent on a stranger's Theft (UnderworldDiscretion)
        // but still rolls normally on violent Murder!
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("etiquette")).BaseValue = 10;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("streetwise")).BaseValue = 10;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Personality.Value)).BaseValue = 10;

        DaggerfallCrimeWitnessEvidence rawEvidence = new(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [shadyId]);
        DaggerfallWitnessDiscretionResult theftEval = DaggerfallWitnessDiscretion.Evaluate(
            operation: "op-underworld-theft",
            crime: DaggerfallCrimeKind.Theft,
            victim: new DaggerfallCrimeVictimContext(ActorId: 888_001L),
            rawWitnesses: rawEvidence,
            definitions: TestPayload.Definitions,
            actors: session.State.Actors,
            npcs: session.State.Npcs,
            actorLedger: session.State.ActorLedger,
            isCityWatch: _ => false,
            roll: (_, _, _, _) => 1);
        DaggerfallWitnessDecision theftDecision = Assert.Single(theftEval.Decisions);
        Assert.False(theftDecision.Reported);
        Assert.Equal(DaggerfallWitnessReportReason.UnderworldDiscretion, theftDecision.Reason);
        Assert.Empty(theftEval.ReportingEvidence.WitnessActorIds);

        DaggerfallWitnessDiscretionResult murderEval = DaggerfallWitnessDiscretion.Evaluate(
            operation: "op-underworld-murder",
            crime: DaggerfallCrimeKind.Murder,
            victim: new DaggerfallCrimeVictimContext(ActorId: 888_001L),
            rawWitnesses: rawEvidence,
            definitions: TestPayload.Definitions,
            actors: session.State.Actors,
            npcs: session.State.Npcs,
            actorLedger: session.State.ActorLedger,
            isCityWatch: _ => false,
            roll: (_, _, _, _) => 1);
        DaggerfallWitnessDecision murderDecision = Assert.Single(murderEval.Decisions);
        Assert.True(murderDecision.Reported);
        Assert.Equal(DaggerfallWitnessReportReason.DiscretionRollReported, murderDecision.Reason);
    }

    private static long SpawnNpc(
        DaggerfallSession session,
        string stableKey,
        string displayName,
        int factionId,
        int region,
        string[] services,
        string? role = null,
        ActorInteractionPolicyKind interaction = ActorInteractionPolicyKind.FullDialogue)
    {
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(region, site.Name, string.Empty, session.Sites.ActiveProfile.LogicalId);
        DaggerfallNpcAppearance appearance = new("breton", "Male", 334, 8, Math.Abs(stableKey.GetHashCode()), factionId);
        WorldPoint playerPos = session.State.PlayerControl.Position ?? new WorldPoint(0F, 0F, 0F);
        WorldPoint spawnPos = new(playerPos.X + 2F, playerPos.Y, playerPos.Z + 2F);
        long id;
        if (interaction == ActorInteractionPolicyKind.FullDialogue)
        {
            id = session.State.Npcs.RegisterCivilian(npcSite, appearance, role ?? displayName, services);
            session.State.Npcs.SetDisplayName(id, displayName);
            session.Civilians.Materialize(id, new ActorPose(spawnPos, 0F));
        }
        else
        {
            id = DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, $"{session.Sites.ActiveProfile.LogicalId}/{stableKey}");
            session.State.Npcs.RegisterDeterministic(id, DaggerfallNpcKind.Civilian, stableKey, npcSite, appearance, role ?? "civilian", services, displayName, session.Sites.ActiveProfile);
            session.Civilians.MaterializeDeterministic(
                id,
                ActorOrigin.Seeded(session.Sites.ActiveProfile, stableKey, interactionPolicy: interaction),
                spawnPos,
                new ActorPose(spawnPos, 0F),
                System.Numerics.Vector3.Zero);
        }
        session.State.Npcs.Bind(id, session.Sites.ActiveProfile);
        ActorState live = session.State.Actors.Get(id);
        live.Actor.Get<FactionComponent>().SetPeopleFactions(factionId > 0 ? factionId : null, null);
        return id;
    }

    private static void OpenDialogueWith(DaggerfallSession session, long npcId)
    {
        DaggerfallActivationTarget target = Assert.Single(
            session.Dialogue.NpcTargets(),
            t => checked((long)t.Identity.Value) == npcId);
        DaggerfallActivationOutcome opened = session.Dialogue.ActivateNpc(
            new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, target));
        Assert.True(opened.Applied, opened.Message);
    }
}
