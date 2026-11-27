using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11159: Persistent multi-tier household standings (local, Great House, royal dynasty),
/// tier-scaled minor grudge decay with zero-byte self-pruning, non-decaying blood feuds,
/// arrest/imprisonment grudges (ordinary kin vs. household/office head), and inspection surface fields.
/// </summary>
public sealed class HouseholdGrudgePersistenceAndDecayTests
{
    [Fact]
    public void Great_house_and_royal_dynasty_standings_persist_past_word_spread_expiry_and_save_load_while_preserving_courier_arrival()
    {
        using var fixture = new MultiTierHouseholdFixture(random: RandomMinimum.Create());
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord originSite = session.Site.ActiveSite!;
        DaggerfallSiteRecord distantSite = TestPayload.Definitions.Locations.Records
            .First(r => r.Id != originSite.Id
                && double.Hypot(r.MapPixelX - originSite.MapPixelX, r.MapPixelY - originSite.MapPixelY) is >= 30.0 and <= 80.0);

        DaggerfallNpcSite originNpcSite = new(originSite.Id.Region, originSite.Name, "1/1/0", session.Sites.ActiveProfile.LogicalId);
        const string greatHouse = "fam:house:r17:1";
        const string royalDynasty = "fam:dynasty:daggerfall";

        long houseVictimId = AddNpcWithFamily(session, originSite.Id, originNpcSite, "t11159/house-victim", "Lord Cassian", greatHouse);
        long dynastyVictimId = AddNpcWithFamily(session, originSite.Id, originNpcSite, "t11159/dynasty-victim", "Prince Lhotun", royalDynasty);
        long witnessId = AddNpcWithFamily(session, originSite.Id, originNpcSite, "t11159/witness", "Oren Watch", null);

        fixture.Perception.Responder = _ => new PerceptionReadoutResult(
            new[] { new PerceptionPair(checked((ulong)witnessId), DaggerfallActorIdentity.PlayerEntityId, 2.0, 1.0, PerceptionPairKind.Visible, 1.0) },
            ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        long t0Minute = session.Calendar.MinuteNumber;
        int t0Day = checked((int)session.Calendar.DayNumber);

        // 1. Witnessed murder of Great House noble -> -20 feud + mourning window.
        ActorState houseVictim = session.State.Actors.Get(houseVictimId);
        houseVictim.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(houseVictimId, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            houseVictim,
            minute: t0Minute);

        // 2. Reported assault on royal dynasty member -> -10 minor grudge (not a blood feud).
        DaggerfallCrimeIncidentSave dynastyAssault = new(
            OperationId: "t11159:dynasty-assault:1",
            Crime: DaggerfallCrimeKind.Assault,
            Stage: DaggerfallCrimeStage.Completed,
            PerpetratorActorId: DaggerfallActorIdentity.PlayerEntityId,
            AffectedActorOrOwnerId: dynastyVictimId,
            Region: originSite.Id.Region,
            GameMinute: t0Minute,
            TargetKind: DaggerfallCrimeTargetKind.Civilian,
            Witnesses: new(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [witnessId]),
            GuildCredit: DaggerfallCrimeGuildCredit.None,
            Reported: true);
        session.SocialConsequences.ReactToAttributedCrime(dynastyAssault, originSite, t0Minute, victimFamilyId: royalDynasty);

        // Immediate visibility at originSite; courier-delayed (0 / no feud) at distantSite!
        Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty, session.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
        Assert.True(session.SocialConsequences.IsFeudActiveAt(greatHouse, originSite));
        Assert.Equal(DaggerfallSocialConsequences.KinAssaultDispositionPenalty, session.SocialConsequences.HouseholdStandingDelta(royalDynasty, originSite));
        Assert.False(session.SocialConsequences.IsFeudActiveAt(royalDynasty, originSite));

        Assert.Equal(0, session.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));
        Assert.False(session.SocialConsequences.IsFeudActiveAt(greatHouse, distantSite));
        Assert.Equal(0, session.SocialConsequences.HouseholdStandingDelta(royalDynasty, distantSite));

        // Canonical persistent snapshots exist immediately for both Great House and royal dynasty.
        Assert.True(session.SocialConsequences.TryGetHouseholdSnapshot(greatHouse, out DaggerfallHouseholdSnapshot houseSnap));
        Assert.Equal(-20, houseSnap.StandingDelta);
        Assert.True(houseSnap.FeudActive);
        Assert.Equal(t0Day + DaggerfallSocialTuning.Default.MourningDurationDays, houseSnap.MourningUntilDay);
        Assert.Equal(t0Day, houseSnap.LastUpdatedDay);

        // 3. Advance 14 days -> courier arrives at distantSite!
        session.AdvanceElapsedTime(14L * 24L * 60L * 60L, idleFatigue: false);
        Assert.Equal(-20, session.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));
        Assert.True(session.SocialConsequences.IsFeudActiveAt(greatHouse, distantSite));
        Assert.Equal(-10, session.SocialConsequences.HouseholdStandingDelta(royalDynasty, distantSite));

        // 4. Advance 35 more days (49 total, well past 30-day WordSpread.MaxRetainedWavefrontMinutes):
        //    wavefronts prune out of WordSpread, YET Great House feud AND royal dynasty grudge persist cleanly!
        session.AdvanceElapsedTime(35L * 24L * 60L * 60L, idleFatigue: false);
        Assert.DoesNotContain(session.WordSpread.Wavefronts, wf => wf.FamilyId is greatHouse or royalDynasty);

        Assert.Equal(-20, session.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
        Assert.Equal(-20, session.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));
        Assert.True(session.SocialConsequences.IsFeudActiveAt(greatHouse, distantSite));
        // Dynasty decay is 60 days/pt -> after 49 days it has not decayed yet (-10 still intact).
        Assert.Equal(-10, session.SocialConsequences.HouseholdStandingDelta(royalDynasty, distantSite));
        // Mourning window (7 days) expired cleanly after 49 days.
        Assert.True(session.SocialConsequences.TryGetHouseholdSnapshot(greatHouse, out DaggerfallHouseholdSnapshot afterMourning));
        Assert.Null(afterMourning.MourningUntilDay);
        Assert.True(afterMourning.FeudActive);

        // 5. Save/load preserves both multi-tier entries and writes zero entries for untouched households.
        RulesetSavePayload saved = session.CaptureSave();
        DaggerfallSavePayload raw = DaggerfallSavePayload.Read(saved);
        Assert.Equal(2, raw.LocalHouseholds.Standings.Length);
        Assert.Contains(raw.LocalHouseholds.Standings, s => s.FamilyId == greatHouse && s.StandingDelta == -20 && s.FeudActive);
        Assert.Contains(raw.LocalHouseholds.Standings, s => s.FamilyId == royalDynasty && s.StandingDelta == -10 && !s.FeudActive);

        using DaggerfallSession restored = fixture.Restore(saved);
        Assert.Equal(-20, restored.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));
        Assert.True(restored.SocialConsequences.IsFeudActiveAt(greatHouse, distantSite));
        Assert.Equal(-10, restored.SocialConsequences.HouseholdStandingDelta(royalDynasty, distantSite));
        Assert.False(restored.SocialConsequences.TryGetHouseholdSnapshot("fam:house:r17:0", out _));
    }

    [Fact]
    public void Minor_grudges_decay_toward_zero_at_tier_rates_and_self_prune_while_blood_feuds_never_decay_from_time()
    {
        using var fixture = new MultiTierHouseholdFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        int startDay = checked((int)session.Calendar.DayNumber);
        long startMinute = session.Calendar.MinuteNumber;

        string localMinor = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 2, 0);
        const string houseMinor = "fam:house:r17:0";
        const string dynastyMinor = "fam:dynasty:wayrest";

        string localFeud = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 2, 1);
        const string houseFeud = "fam:house:r17:1";
        const string dynastyFeud = "fam:dynasty:sentinel";

        // Seed minor grudges (-3) and blood feuds (-15 / -20) across all three tiers.
        session.SocialConsequences.SetHouseholdStanding(localMinor, -3, site, startMinute, startDay);
        session.SocialConsequences.SetHouseholdStanding(houseMinor, -3, site, startMinute, startDay);
        session.SocialConsequences.SetHouseholdStanding(dynastyMinor, -3, site, startMinute, startDay);

        session.SocialConsequences.SetHouseholdStanding(localFeud, DaggerfallIntroductions.BloodFeudDispositionThreshold, site, startMinute, startDay);
        session.SocialConsequences.SetHouseholdStanding(houseFeud, -20, site, startMinute, startDay);
        session.SocialConsequences.SetHouseholdStanding(dynastyFeud, -20, site, startMinute, startDay);

        // +15 days: local (15d/pt) decays -3 -> -2; Great House (30d/pt) & dynasty (60d/pt) unchanged.
        session.AdvanceElapsedTime(15L * 24L * 60L * 60L, idleFatigue: false);
        Assert.Equal(-2, session.SocialConsequences.HouseholdStandingDelta(localMinor, site));
        Assert.Equal(-3, session.SocialConsequences.HouseholdStandingDelta(houseMinor, site));
        Assert.Equal(-3, session.SocialConsequences.HouseholdStandingDelta(dynastyMinor, site));

        // +15 more days (30 total): local -1, Great House -2, dynasty -3.
        session.AdvanceElapsedTime(15L * 24L * 60L * 60L, idleFatigue: false);
        Assert.Equal(-1, session.SocialConsequences.HouseholdStandingDelta(localMinor, site));
        Assert.Equal(-2, session.SocialConsequences.HouseholdStandingDelta(houseMinor, site));
        Assert.Equal(-3, session.SocialConsequences.HouseholdStandingDelta(dynastyMinor, site));

        // +15 more days (45 total): local reaches 0 and self-prunes to zero save bytes!
        session.AdvanceElapsedTime(15L * 24L * 60L * 60L, idleFatigue: false);
        Assert.Equal(0, session.SocialConsequences.HouseholdStandingDelta(localMinor, site));
        Assert.False(session.SocialConsequences.TryGetHouseholdSnapshot(localMinor, out _));
        Assert.Equal(-2, session.SocialConsequences.HouseholdStandingDelta(houseMinor, site));
        Assert.Equal(-3, session.SocialConsequences.HouseholdStandingDelta(dynastyMinor, site));

        // +15 more days (60 total): Great House -1, dynasty -2.
        session.AdvanceElapsedTime(15L * 24L * 60L * 60L, idleFatigue: false);
        Assert.Equal(-1, session.SocialConsequences.HouseholdStandingDelta(houseMinor, site));
        Assert.Equal(-2, session.SocialConsequences.HouseholdStandingDelta(dynastyMinor, site));

        // +365 more days (425 total): all minor grudges reach 0 and self-prune; ALL THREE blood feuds NEVER decay!
        session.AdvanceElapsedTime(365L * 24L * 60L * 60L, idleFatigue: false);
        Assert.False(session.SocialConsequences.TryGetHouseholdSnapshot(localMinor, out _));
        Assert.False(session.SocialConsequences.TryGetHouseholdSnapshot(houseMinor, out _));
        Assert.False(session.SocialConsequences.TryGetHouseholdSnapshot(dynastyMinor, out _));

        Assert.Equal(DaggerfallIntroductions.BloodFeudDispositionThreshold, session.SocialConsequences.HouseholdStandingDelta(localFeud, site));
        Assert.True(session.SocialConsequences.IsFeudActiveAt(localFeud, site));
        Assert.Equal(-20, session.SocialConsequences.HouseholdStandingDelta(houseFeud, site));
        Assert.True(session.SocialConsequences.IsFeudActiveAt(houseFeud, site));
        Assert.Equal(-20, session.SocialConsequences.HouseholdStandingDelta(dynastyFeud, site));
        Assert.True(session.SocialConsequences.IsFeudActiveAt(dynastyFeud, site));

        DaggerfallSavePayload raw = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Equal(3, raw.LocalHouseholds.Standings.Length);
        Assert.All(raw.LocalHouseholds.Standings, s => Assert.True(s.FeudActive));
    }

    [Fact]
    public void Arrest_grudges_scale_for_member_vs_head_decay_over_time_never_open_blood_feud_and_wire_into_office_holder_conviction()
    {
        using var fixture = new MultiTierHouseholdFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/4/0", session.Sites.ActiveProfile.LogicalId);

        string localMemberFamily = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 4, 0);
        const string nobleHeadFamily = "fam:house:r17:2";

        long ordinaryMemberId = AddNpcWithFamily(session, site.Id, npcSite, "t11159/member", "Doran Mills", localMemberFamily);
        long nobleHeadId = AddNpcWithFamily(session, site.Id, npcSite, "t11159/head", "Baron Aldric", nobleHeadFamily);
        long watchmanId = AddNpcWithFamily(session, site.Id, npcSite, "t11159/watchman", "Captain Vael", null, role: "guard");

        // Activate a regional crackdown and give the player standing with the watch so live dialogue denunciation is open.
        int day = checked((int)session.Calendar.DayNumber);
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            "t11159:crackdown",
            DaggerfallWorldConditionKind.CrimeCrackdown,
            Region: site.Id.Region,
            Severity: 1,
            StartedDay: day,
            ExpiresDay: day + 30));
        session.State.Social.ChangeRegionalReputation(site.Id.Region, 10);

        // Open real dialogue with the watchman: BOTH ordinary member and untitled noble head are offered as denounce targets!
        DaggerfallActivationTarget guardTarget = Assert.Single(
            session.Dialogue.NpcTargets(),
            candidate => checked((long)candidate.Identity.Value) == watchmanId);
        DaggerfallActivationOutcome opened = session.Dialogue.ActivateNpc(
            new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, guardTarget));
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView view1 = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains(view1.Topics, t => t.Id == $"denounce-holder:{ordinaryMemberId}");
        Assert.Contains(view1.Topics, t => t.Id == $"denounce-holder:{nobleHeadId}");

        // 1. Denouncing an ordinary household member through live dialogue takes them into jail custody (removed from live scene,
        //    OffStage in ActorLedger) and inflicts the lighter decaying arrest grudge (-5), never a blood feud!
        DaggerfallActivationOutcome memberDenounce = session.Dialogue.Ask(view1.Revision, $"denounce-holder:{ordinaryMemberId}", null, null);
        Assert.True(memberDenounce.Applied, memberDenounce.Message);
        Assert.Contains("custody", memberDenounce.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(session.State.Actors.TryGet(ordinaryMemberId, out _));
        Assert.True(session.State.ActorLedger.TryGet(ordinaryMemberId, out ActorLedgerEntry? memberLedger));
        Assert.Equal(ActorLocationKind.OffStage, memberLedger!.Location?.Kind);
        Assert.Equal(-5, session.SocialConsequences.HouseholdStandingDelta(localMemberFamily, site));
        Assert.False(session.SocialConsequences.IsFeudActiveAt(localMemberFamily, site));

        // 2. Denouncing an untitled noble/household head through live dialogue takes them into custody (OffStage)
        //    and inflicts the noticeably heavier (still decaying) arrest grudge (-10), never a blood feud!
        DaggerfallDialogueView view2 = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        DaggerfallActivationOutcome headDenounce = session.Dialogue.Ask(view2.Revision, $"denounce-holder:{nobleHeadId}", null, null);
        Assert.True(headDenounce.Applied, headDenounce.Message);
        Assert.Contains("custody", headDenounce.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(session.State.Actors.TryGet(nobleHeadId, out _));
        Assert.True(session.State.ActorLedger.TryGet(nobleHeadId, out ActorLedgerEntry? headLedger));
        Assert.Equal(ActorLocationKind.OffStage, headLedger!.Location?.Kind);
        Assert.Equal(-10, session.SocialConsequences.HouseholdStandingDelta(nobleHeadFamily, site));
        Assert.False(session.SocialConsequences.IsFeudActiveAt(nobleHeadFamily, site));
        session.Dialogue.Close();

        // Verify jail custody persists cleanly across save/load (jailed actors stay off-stage).
        using (DaggerfallSession restoredCustody = fixture.Restore(session.CaptureSave()))
        {
            Assert.True(restoredCustody.State.ActorLedger.TryGet(ordinaryMemberId, out ActorLedgerEntry? savedMember));
            Assert.Equal(ActorLocationKind.OffStage, savedMember!.Location?.Kind);
            Assert.True(restoredCustody.State.ActorLedger.TryGet(nobleHeadId, out ActorLedgerEntry? savedHead));
            Assert.Equal(ActorLocationKind.OffStage, savedHead!.Location?.Kind);
        }

        // 3. Repeated arrests alone NEVER cross the -15 blood-feud threshold (capped at -14).
        session.AdvanceElapsedTime(60L, idleFatigue: false);
        DaggerfallNpcArrestOutcome secondArrest = session.Law.ArrestOrImprisonNpc(
            nobleHeadId, site.Id.Region, DaggerfallCrimeKind.CriminalConspiracy, banished: false, detail: "second warrant");
        Assert.Equal(-4, secondArrest.HouseholdStandingDelta);
        Assert.Equal(-14, session.SocialConsequences.HouseholdStandingDelta(nobleHeadFamily, site));
        Assert.False(session.SocialConsequences.IsFeudActiveAt(nobleHeadFamily, site));

        // 4. Both arrest grudges decay over time (local 15d/pt, Great House 30d/pt).
        session.AdvanceElapsedTime(30L * 24L * 60L * 60L, idleFatigue: false);
        Assert.Equal(-3, session.SocialConsequences.HouseholdStandingDelta(localMemberFamily, site));
        Assert.Equal(-13, session.SocialConsequences.HouseholdStandingDelta(nobleHeadFamily, site));

        // 5. Live office-holder conviction/banishment via DaggerfallLaw automatically fires a head arrest grudge (even when off-scene)!
        using var guildFixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession liveSession = guildFixture.Start(guildFixture.ExteriorProfile);
        DaggerfallSiteRecord charing = liveSession.Site.ActiveSite!;
        DaggerfallLocalOffice officeWithFamily = liveSession.OfficeLedger.ResolveLocalOffices(charing)
            .First(o => !o.IsVacant && o.HolderActorId is long && !string.IsNullOrWhiteSpace(o.FamilyId));
        long holderId = officeWithFamily.HolderActorId!.Value;
        string holderFamily = officeWithFamily.FamilyId!;

        int deposed = liveSession.Law.ConvictOrBanishOfficeHolder(
            holderId, charing.Id.Region, DaggerfallCrimeKind.CriminalConspiracy, banished: true, detail: "corruption uncovered");
        Assert.True(deposed > 0);
        Assert.Equal(DaggerfallSocialTuning.Default.KinHeadArrestGrudgePenalty,
            liveSession.SocialConsequences.HouseholdStandingDelta(holderFamily, charing));
        Assert.False(liveSession.SocialConsequences.IsFeudActiveAt(holderFamily, charing));
    }

    [Fact]
    public void Intercepted_courier_grudge_decays_without_invented_goodwill_or_resurrection_across_save_load()
    {
        using var fixture = new MultiTierHouseholdFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord originSite = session.Site.ActiveSite!;
        DaggerfallSiteRecord distantSite = TestPayload.Definitions.Locations.Records
            .First(r => r.Id != originSite.Id
                && double.Hypot(r.MapPixelX - originSite.MapPixelX, r.MapPixelY - originSite.MapPixelY) is >= 30.0 and <= 80.0);

        DaggerfallNpcSite npcSite = new(originSite.Id.Region, originSite.Name, "1/6/0", session.Sites.ActiveProfile.LogicalId);
        const string greatHouse = "fam:house:r17:3";
        long ordinaryKinId = AddNpcWithFamily(session, originSite.Id, npcSite, "t11159/intercept-kin", "Edric Vane", greatHouse);

        // Emit a -5 Great House arrest grudge via canonical DaggerfallLaw.ArrestOrImprisonNpc, then intercept its courier before distantSite.
        DaggerfallNpcArrestOutcome arrest = session.Law.ArrestOrImprisonNpc(
            ordinaryKinId, originSite.Id.Region, DaggerfallCrimeKind.Smuggling, banished: false, detail: "smuggling");
        Assert.False(arrest.IsHouseholdHead);
        Assert.Equal(-5, arrest.HouseholdStandingDelta);

        DaggerfallWordWavefrontSave wf = Assert.Single(session.WordSpread.Wavefronts, w => w.FamilyId == greatHouse);
        Assert.True(session.WordSpread.InterceptWavefront(wf.EventId, session.Calendar.MinuteNumber));
        Assert.Equal(-5, session.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
        Assert.Equal(0, session.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));

        // 1. Partial decay (+30 days -> -5 decays to -4): distantSite MUST remain 0 (NEVER +1 invented goodwill!).
        session.AdvanceElapsedTime(30L * 24L * 60L * 60L, idleFatigue: false);
        Assert.Equal(-4, session.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
        Assert.Equal(0, session.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));

        // Partial decay survives save/load cleanly with zero invented goodwill.
        RulesetSavePayload partialSave = session.CaptureSave();
        using (DaggerfallSession restoredPartial = fixture.Restore(partialSave))
        {
            Assert.Equal(-4, restoredPartial.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
            Assert.Equal(0, restoredPartial.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));
        }

        // 2. Full decay (+120 more days = 150 total -> -4 decays to 0 and self-prunes):
        //    retained intercepted wavefront is reconciled & cleared; originSite MUST be 0 (NEVER resurrected to -5!).
        session.AdvanceElapsedTime(120L * 24L * 60L * 60L, idleFatigue: false);
        Assert.False(session.SocialConsequences.TryGetHouseholdSnapshot(greatHouse, out _));
        Assert.DoesNotContain(session.WordSpread.Wavefronts, w => w.FamilyId == greatHouse);
        Assert.Equal(0, session.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
        Assert.Equal(0, session.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));

        // Full decay survives save/load with zero standing entries and zero resurrection.
        RulesetSavePayload finalSave = session.CaptureSave();
        DaggerfallSavePayload rawFinal = DaggerfallSavePayload.Read(finalSave);
        Assert.DoesNotContain(rawFinal.LocalHouseholds.Standings, s => s.FamilyId == greatHouse);
        using DaggerfallSession restoredFinal = fixture.Restore(finalSave);
        Assert.Equal(0, restoredFinal.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
        Assert.Equal(0, restoredFinal.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));
    }

    [Fact]
    public void Mixed_goodwill_and_unarrived_arrest_preserves_arrived_positive_standing_until_courier_arrives()
    {
        using var fixture = new MultiTierHouseholdFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord originSite = session.Site.ActiveSite!;
        DaggerfallSiteRecord distantSite = TestPayload.Definitions.Locations.Records
            .First(r => r.Id != originSite.Id
                && double.Hypot(r.MapPixelX - originSite.MapPixelX, r.MapPixelY - originSite.MapPixelY) is >= 30.0 and <= 80.0);

        DaggerfallNpcSite npcSite = new(originSite.Id.Region, originSite.Name, "1/7/0", session.Sites.ActiveProfile.LogicalId);
        const string greatHouse = "fam:house:r17:4";
        long headId = AddNpcWithFamily(session, originSite.Id, npcSite, "t11159/mixed-head", "Lady Seraphina", greatHouse);

        // 1. Earn +5 Great House goodwill (direct +10 -> +5 kin ripple) and let courier deliver it to distantSite.
        session.SocialConsequences.ReactToKinGoodwill(headId, directPositiveDelta: 10, originSite: originSite, minute: session.Calendar.MinuteNumber);
        session.AdvanceElapsedTime(14L * 24L * 60L * 60L, idleFatigue: false);
        Assert.Equal(5, session.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
        Assert.Equal(5, session.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));

        // 2. Arrest the Great House head at originSite (-10 -> global persistent standing becomes -5) while courier is still in transit!
        DaggerfallNpcArrestOutcome arrest = session.Law.ArrestOrImprisonNpc(
            headId, originSite.Id.Region, DaggerfallCrimeKind.CriminalConspiracy, banished: false, detail: "court warrant");
        Assert.True(arrest.IsHouseholdHead);
        Assert.Equal(-10, arrest.HouseholdStandingDelta);

        // originSite immediately sees -5, while distantSite MUST preserve its already-known +5 goodwill until courier arrives!
        Assert.Equal(-5, session.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
        Assert.Equal(5, session.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));

        // 3. Advance past courier arrival -> distantSite now sees -5!
        session.AdvanceElapsedTime(14L * 24L * 60L * 60L, idleFatigue: false);
        Assert.Equal(-5, session.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));
    }

    [Fact]
    public void Mixed_goodwill_and_intercepted_arrest_preserves_arrived_positive_standing_through_partial_and_full_decay_and_save_load()
    {
        using var fixture = new MultiTierHouseholdFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord originSite = session.Site.ActiveSite!;
        DaggerfallSiteRecord distantSite = TestPayload.Definitions.Locations.Records
            .First(r => r.Id != originSite.Id
                && double.Hypot(r.MapPixelX - originSite.MapPixelX, r.MapPixelY - originSite.MapPixelY) is >= 30.0 and <= 80.0);

        DaggerfallNpcSite npcSite = new(originSite.Id.Region, originSite.Name, "1/8/0", session.Sites.ActiveProfile.LogicalId);
        const string greatHouse = "fam:house:r17:5";
        long headId = AddNpcWithFamily(session, originSite.Id, npcSite, "t11159/mixed-intercept-head", "Duke Corvus", greatHouse);

        // 1. Earn +5 Great House goodwill and let it arrive at distantSite.
        session.SocialConsequences.ReactToKinGoodwill(headId, directPositiveDelta: 10, originSite: originSite, minute: session.Calendar.MinuteNumber);
        session.AdvanceElapsedTime(14L * 24L * 60L * 60L, idleFatigue: false);
        Assert.Equal(5, session.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
        Assert.Equal(5, session.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));

        // 2. Arrest the Great House head (-10 -> canonical -5) and intercept the arrest wavefront before it reaches distantSite.
        DaggerfallNpcArrestOutcome arrest = session.Law.ArrestOrImprisonNpc(
            headId, originSite.Id.Region, DaggerfallCrimeKind.CriminalConspiracy, banished: false, detail: "intercepted warrant");
        Assert.Equal(-10, arrest.HouseholdStandingDelta);
        DaggerfallWordWavefrontSave arrestWavefront = Assert.Single(
            session.WordSpread.Wavefronts,
            wf => wf.Kind == DaggerfallWordEventKind.HouseholdStanding
                && wf.FamilyId == greatHouse
                && wf.Delta == -10);
        Assert.True(session.WordSpread.InterceptWavefront(arrestWavefront.EventId, session.Calendar.MinuteNumber));

        Assert.Equal(-5, session.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
        Assert.Equal(5, session.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));

        // 3. Partial decay (120 days = 4 Great House decay steps): canonical -1, distant MUST stay +5!
        session.AdvanceElapsedTime(120L * 24L * 60L * 60L, idleFatigue: false);
        Assert.Equal(-1, session.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
        Assert.Equal(5, session.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));

        // 4. Full decay (150 days total = 5th Great House step): canonical reaches 0 (sparse entry pruned),
        //    yet distantSite NEVER received the arrest report so its known +5 goodwill MUST be preserved!
        session.AdvanceElapsedTime(30L * 24L * 60L * 60L, idleFatigue: false);
        Assert.Equal(0, session.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
        Assert.Equal(5, session.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));
        Assert.False(session.SocialConsequences.TryGetHouseholdSnapshot(greatHouse, out _));

        // 5. Save/load after full decay preserves origin=0, distant=+5, and sparse LocalHouseholds!
        RulesetSavePayload save = session.CaptureSave();
        DaggerfallSavePayload raw = DaggerfallSavePayload.Read(save);
        Assert.DoesNotContain(raw.LocalHouseholds.Standings, s => s.FamilyId == greatHouse);
        using DaggerfallSession restored = fixture.Restore(save);
        Assert.Equal(0, restored.SocialConsequences.HouseholdStandingDelta(greatHouse, originSite));
        Assert.Equal(5, restored.SocialConsequences.HouseholdStandingDelta(greatHouse, distantSite));
    }

    [Fact]
    public void Inspection_surfaces_expose_persistent_standing_feud_mourning_and_last_updated_day_across_site_region_and_actor()
    {
        using var fixture = new MultiTierHouseholdFixture(random: RandomMinimum.Create());
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/5/0", session.Sites.ActiveProfile.LogicalId);

        string localFamily = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 5, 0);
        string regionGreatHouse = DaggerfallHouseholds.GreatHouseId(site.Id.Region, 0);

        long victimId = AddNpcWithFamily(session, site.Id, npcSite, "t11159/inspect-victim", "Tomas Reed", localFamily);
        long livingKinId = AddNpcWithFamily(session, site.Id, npcSite, "t11159/inspect-kin", "Mina Reed", localFamily);
        long witnessId = AddNpcWithFamily(session, site.Id, npcSite, "t11159/inspect-witness", "Garrick", null);

        fixture.Perception.Responder = _ => new PerceptionReadoutResult(
            new[] { new PerceptionPair(checked((ulong)witnessId), DaggerfallActorIdentity.PlayerEntityId, 2.0, 1.0, PerceptionPairKind.Visible, 1.0) },
            ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        int day = checked((int)session.Calendar.DayNumber);
        long minute = session.Calendar.MinuteNumber;

        // Witnessed murder on local household -> -20, feudActive=true, mourningUntilDay = day + 7.
        ActorState victim = session.State.Actors.Get(victimId);
        victim.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(victimId, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            victim,
            minute);

        IDeveloperSocialCommands socialCmds = session.Commands<IDeveloperSocialCommands>();
        IDeveloperActorCommands actorCmds = session.Commands<IDeveloperActorCommands>();

        // Set a Great House grudge via developer command (and verify reply fields).
        DebugCommandResult setResult = socialCmds.SetHouseholdStanding(regionGreatHouse, -10);
        Assert.Equal(DebugCommandStatus.Success, setResult.Status);
        using (JsonDocument setDoc = JsonDocument.Parse(setResult.Message))
        {
            Assert.Equal(-10, setDoc.RootElement.GetProperty("persistentStandingDelta").GetInt32());
            Assert.False(setDoc.RootElement.GetProperty("feudActive").GetBoolean());
            Assert.Equal(JsonValueKind.Null, setDoc.RootElement.GetProperty("mourningUntilDay").ValueKind);
            Assert.Equal(day, setDoc.RootElement.GetProperty("lastUpdatedDay").GetInt32());
        }

        // Verify social.site reports persistentStandingDelta, feudActive, mourningUntilDay, lastUpdatedDay.
        DebugCommandResult siteResult = socialCmds.Site();
        Assert.Equal(DebugCommandStatus.Success, siteResult.Status);
        using (JsonDocument siteDoc = JsonDocument.Parse(siteResult.Message))
        {
            JsonElement localHouseholdJson = siteDoc.RootElement.GetProperty("households").EnumerateArray()
                .Single(e => e.GetProperty("familyId").GetString() == localFamily);
            Assert.Equal(-20, localHouseholdJson.GetProperty("standingDelta").GetInt32());
            Assert.Equal(-20, localHouseholdJson.GetProperty("persistentStandingDelta").GetInt32());
            Assert.True(localHouseholdJson.GetProperty("inBloodFeud").GetBoolean());
            Assert.True(localHouseholdJson.GetProperty("feudActive").GetBoolean());
            Assert.Equal(day + DaggerfallSocialTuning.Default.MourningDurationDays, localHouseholdJson.GetProperty("mourningUntilDay").GetInt32());
            Assert.Equal(day, localHouseholdJson.GetProperty("lastUpdatedDay").GetInt32());

            JsonElement localStandingJson = siteDoc.RootElement.GetProperty("localStandings").EnumerateArray()
                .Single(e => e.GetProperty("familyId").GetString() == localFamily);
            Assert.Equal(-20, localStandingJson.GetProperty("persistentStandingDelta").GetInt32());
            Assert.True(localStandingJson.GetProperty("feudActive").GetBoolean());
            Assert.Equal(day + DaggerfallSocialTuning.Default.MourningDurationDays, localStandingJson.GetProperty("mourningUntilDay").GetInt32());
            Assert.Equal(day, localStandingJson.GetProperty("lastUpdatedDay").GetInt32());
        }

        // Verify social.region reports the Great House persistent fields.
        DebugCommandResult regionResult = socialCmds.Region();
        Assert.Equal(DebugCommandStatus.Success, regionResult.Status);
        using (JsonDocument regionDoc = JsonDocument.Parse(regionResult.Message))
        {
            JsonElement houseJson = regionDoc.RootElement.GetProperty("greatHouses").EnumerateArray()
                .Single(e => e.GetProperty("familyId").GetString() == regionGreatHouse);
            Assert.Equal(-10, houseJson.GetProperty("arrivedStandingDelta").GetInt32());
            Assert.Equal(-10, houseJson.GetProperty("persistentStandingDelta").GetInt32());
            Assert.False(houseJson.GetProperty("inBloodFeud").GetBoolean());
            Assert.False(houseJson.GetProperty("feudActive").GetBoolean());
            Assert.Equal(JsonValueKind.Null, houseJson.GetProperty("mourningUntilDay").ValueKind);
            Assert.Equal(day, houseJson.GetProperty("lastUpdatedDay").GetInt32());
        }

        // Verify actor.inspect reports persistentStandingDelta, feudActive, mourningUntilDay, lastUpdatedDay on living kin.
        DebugCommandResult inspectResult = actorCmds.Inspect(livingKinId.ToString());
        Assert.Equal(DebugCommandStatus.Success, inspectResult.Status);
        using (JsonDocument actorDoc = JsonDocument.Parse(inspectResult.Message))
        {
            JsonElement reaction = actorDoc.RootElement.GetProperty("social").GetProperty("reaction");
            Assert.Equal(-20, reaction.GetProperty("householdStanding").GetInt32());
            Assert.Equal(-20, reaction.GetProperty("persistentStandingDelta").GetInt32());
            Assert.True(reaction.GetProperty("inBloodFeud").GetBoolean());
            Assert.True(reaction.GetProperty("feudActive").GetBoolean());
            Assert.Equal(day + DaggerfallSocialTuning.Default.MourningDurationDays, reaction.GetProperty("mourningUntilDay").GetInt32());
            Assert.Equal(day, reaction.GetProperty("lastUpdatedDay").GetInt32());
        }
    }

    private static long AddNpcWithFamily(
        DaggerfallSession session,
        DaggerfallSiteId siteId,
        DaggerfallNpcSite binding,
        string slotId,
        string displayName,
        string? familyId,
        string role = "person")
    {
        DaggerfallWorldProfileKey profile = session.Sites.ActiveProfile;
        string slotKey = $"{profile.LogicalId}/{slotId}";
        long id = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, slotKey);
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, Math.Abs(slotId.GetHashCode()), 0);
        session.State.Npcs.RegisterDeterministic(
            id, DaggerfallNpcKind.Static, slotId, binding, look, role, ["talk"],
            displayName: displayName, homeProfile: profile, familyId: familyId);
        DaggerfallNpc npc = session.State.Npcs.Require(id);
        WorldPoint pose = new(2, 0, -2);
        session.Roster.MaterializeDeterministicCivilian(
            npc,
            ActorOrigin.Seeded(profile, slotId),
            pose,
            new ActorPose(pose, 0f),
            Vector3.Zero,
            familyId);
        session.State.Npcs.Bind(id, profile);
        return id;
    }

    private sealed class MultiTierHouseholdFixture : IDisposable
    {
        private readonly DaggerfallSiteProfile _profile;
        private readonly List<string> _releases = [];
        private readonly ContentFake _content;
        private readonly SpatialFake _spatial;
        private readonly IRandomService? _random;

        internal PerceptionFake Perception { get; } = PerceptionFake.Create();
        internal DaggerfallSession Session { get; }

        internal MultiTierHouseholdFixture(IRandomService? random = null)
        {
            _random = random;
            _profile = ReadInputs(TestData.RepositoryRoot);
            _content = new ContentFake(_releases);
            PopulateContent(_content, _profile);
            _spatial = SpatialFake.Create(_profile.Geometry.Spatial[0].Sha256, _releases);
            EngineContextFake engine = EngineContextFake.Create(_content, _spatial.Service, new AppearanceFake(_releases), Perception.Service, random: _random);
            Session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, _profile, DaggerfallTuning.Defaults));
        }

        internal DaggerfallSession Restore(RulesetSavePayload save)
        {
            EngineContextFake engine = EngineContextFake.Create(_content, _spatial.Service, new AppearanceFake(_releases), Perception.Service, random: _random);
            return DaggerfallSession.Restore(engine.Context, new(TestPayload.Definitions, _profile, DaggerfallTuning.Defaults), save);
        }

        public void Dispose() => Session.Dispose();
    }
}
