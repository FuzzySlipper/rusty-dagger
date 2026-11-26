using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Households 3 (#11107): family vouching in <see cref="DaggerfallIntroductions"/>, blood-feud refusals in
/// <see cref="DaggerfallIntroductions.AssessAccess"/> and <see cref="DaggerfallIntroductions.HasDepositWaiverVouch"/>,
/// cross-town Great House introductions across site crossings, and save/reload continuity with zero ledger bytes for
/// untouched households.
/// </summary>
public sealed class DaggerfallHouseholdIntroductionsTests
{
    private static readonly SharedFixture<AssembledWorld> SharedWorld = new(() => new AssembledWorld());

    [Fact]
    public void Local_household_kin_vouch_grants_trust_deposit_waiver_and_survives_save_reload_with_zero_bytes_for_untouched_households()
    {
        using var fixture = new HouseholdIntroFixture(random: RandomMinimum.Create());
        DaggerfallSession session = fixture.Session;
        SetSocialStats(session, etiquette: 15, streetwise: 15, personality: 30);

        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/1/0", session.Sites.ActiveProfile.LogicalId);
        string householdA = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 1, 0);
        string householdB = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 1, 1);

        long siblingA = AddNpcWithFamily(session, site.Id, npcSite, "local/sib-a", "Maren Holt", householdA, factionId: 0, ["talk"]);
        long siblingB = AddNpcWithFamily(session, site.Id, npcSite, "local/sib-b", "Bram Holt", householdA, factionId: 0, ["talk", "shop"]);
        long untouched = AddNpcWithFamily(session, site.Id, npcSite, "local/untouched", "Talia Vance", householdB, factionId: 0, ["talk"]);

        DaggerfallNpc speaker = session.State.Npcs.Require(siblingA);
        DaggerfallNpc kinTarget = session.State.Npcs.Require(siblingB);

        // Kin target appears in ListIntroductionTargets even with FactionId = 0 and blunt social stats.
        IReadOnlyList<DaggerfallIntroductionTarget> targets = session.Introductions.ListIntroductionTargets(speaker, site);
        Assert.Contains(targets, t => t.TargetDurableId == siblingB);
        Assert.DoesNotContain(targets, t => t.TargetDurableId == untouched);

        // Earn speaker's personal trust -> speaker issues a kin Trade vouch to siblingB.
        session.Introductions.AdjustActorDisposition(siblingA, 20);
        DaggerfallIntroductionResult issued = session.Introductions.TryIssueIntroduction(speaker, siblingB, site);
        Assert.True(issued.Accepted, issued.Message);
        Assert.NotNull(issued.Vouch);
        Assert.Equal(householdA, issued.Vouch!.ReferrerFamilyId);

        Assert.True(session.Introductions.TrustsReferrer(kinTarget, issued.Vouch));
        Assert.True(session.Introductions.HasDepositWaiverVouch(kinTarget));
        Assert.Equal(DaggerfallIntroductions.TrustedVouchStandingBonus, session.Introductions.TrustedVouchReactionBonus(siblingB));

        // Capture save: untouched household member wrote 0 bytes to ActorLedger.
        RulesetSavePayload saved = session.CaptureSave();
        DaggerfallSavePayload raw = DaggerfallSavePayload.Read(saved);
        Assert.DoesNotContain(raw.ActorLedger ?? [], e => e.DurableId == untouched);

        using DaggerfallSession restored = fixture.Restore(saved);
        // Re-materialize deterministic kinTarget in restored session; persisted vouch & deposit waiver survive cleanly.
        long restoredKinId = AddNpcWithFamily(restored, site.Id, npcSite, "local/sib-b", "Bram Holt", householdA, factionId: 0, ["talk", "shop"]);
        Assert.Equal(siblingB, restoredKinId);
        DaggerfallNpc restoredKin = restored.State.Npcs.Require(restoredKinId);

        ActorVouchRecord restoredVouch = Assert.Single(restored.Introductions.ReadVouches(restoredKinId));
        Assert.Equal(siblingA, restoredVouch.ReferrerDurableId);
        Assert.Equal(householdA, restoredVouch.ReferrerFamilyId);
        Assert.True(restored.Introductions.TrustsReferrer(restoredKin, restoredVouch));
        Assert.True(restored.Introductions.HasDepositWaiverVouch(restoredKin));
        Assert.Equal(DaggerfallIntroductions.TrustedVouchStandingBonus, restored.Introductions.TrustedVouchReactionBonus(restoredKinId));
    }

    [Fact]
    public void Great_house_market_factor_vouches_cross_town_to_unvisited_capital_court_kin_across_site_crossing_and_save_reload()
    {
        AssembledWorld world = SharedWorld.Value;

        // Pick a real Region 17 (Daggerfall) site whose palace interior has a deterministic Great House CourtFactor.
        // AssembledWorld starts at Privateer's Hold exterior (also Region 17, a separate site before ever visiting the palace town).
        DaggerfallSiteRecord? palaceTown = null;
        DaggerfallLocalOffice? targetOffice = null;
        string? greatHouseId = null;

        using AssembledWorldRun probeRun = world.Start(new DaggerfallSiteId(17, 0));
        IReadOnlyList<DaggerfallRegionalGreatHouse> houses = probeRun.Session.OfficeLedger.ResolveRegionalGreatHouses(17);
        foreach (DaggerfallRegionalGreatHouse house in houses)
        {
            foreach (DaggerfallLocalOffice office in house.HeldOffices)
            {
                if (office.IsVacant || !office.IsAlive || office.HolderActorId is null || office.BuildingId is null)
                    continue;
                DaggerfallSiteRecord? candidateTown = world.Definitions.Locations.Records
                    .FirstOrDefault(r => r.Id == office.SiteId && r.Exterior is { Buildings.Count: > 0 });
                if (candidateTown is null
                    || !world.Profiles.Contains(DaggerfallWorldProfileIds.Exterior(candidateTown.Id))
                    || !world.Profiles.Contains(office.ProfileKey))
                {
                    continue;
                }

                palaceTown = candidateTown;
                targetOffice = office;
                greatHouseId = house.FamilyId;
                break;
            }
            if (targetOffice is not null) break;
        }

        Assert.NotNull(palaceTown);
        Assert.NotNull(targetOffice);
        Assert.NotNull(greatHouseId);

        long courtFactorId = targetOffice!.HolderActorId!.Value;
        long[] untouchedOfficeIds =
        [
            .. houses
                .SelectMany(h => h.HeldOffices)
                .Where(o => o.HolderActorId is long id && id != courtFactorId)
                .Select(o => o.HolderActorId!.Value)
                .Distinct(),
        ];

        using AssembledWorldRun run = world.Start(palaceTown!.Id, building: targetOffice.BuildingId);
        DaggerfallSession session = run.Session;
        DaggerfallSiteRecord startSite = session.Site.ActiveSite!;
        Assert.NotEqual(palaceTown.Id, startSite.Id);
        SetSocialStats(session, etiquette: 15, streetwise: 15, personality: 30);

        // Place a Great House factor at the starting Region 17 site (Ordinary prominence, FactionId = 0) sharing the same Great House.
        DaggerfallNpcSite startNpcSite = new(startSite.Id.Region, startSite.Name, string.Empty, session.Sites.ActiveProfile.LogicalId);
        long factorId = AddNpcWithFamily(
            session, startSite.Id, startNpcSite, "market/house-factor", "Marcus Valen", greatHouseId, factionId: 0, ["talk", "shop"]);
        DaggerfallNpc factor = session.State.Npcs.Require(factorId);
        Assert.Equal(DaggerfallSocialProminence.Ordinary, session.Introductions.ClassifyProminence(factor));

        // Before ever visiting the palace town, the factor's introduction targets include their off-scene palace kin!
        IReadOnlyList<DaggerfallIntroductionTarget> targets = session.Introductions.ListIntroductionTargets(factor, startSite);
        DaggerfallIntroductionTarget palaceTarget = Assert.Single(targets, t => t.TargetDurableId == courtFactorId);
        Assert.Equal(ActorVouchKind.Court, palaceTarget.VouchKind);
        Assert.True(palaceTarget.TargetProminence >= DaggerfallSocialProminence.MinorCourt);

        // Earn the factor's trust -> issue cross-town Great House court vouch.
        session.Introductions.AdjustActorDisposition(factorId, 25);
        DaggerfallIntroductionResult issued = session.Introductions.TryIssueIntroduction(factor, courtFactorId, startSite);
        Assert.True(issued.Accepted, issued.Message);
        Assert.NotNull(issued.Vouch);
        Assert.Equal(greatHouseId, issued.Vouch!.ReferrerFamilyId);
        Assert.Equal(startSite.Name, issued.Vouch.OriginSiteName);

        // Confirm untouched Great House offices wrote 0 ledger entries.
        DaggerfallSavePayload beforeCrossSave = DaggerfallSavePayload.Read(session.CaptureSave());
        foreach (long untouchedId in untouchedOfficeIds)
            Assert.DoesNotContain(beforeCrossSave.ActorLedger ?? [], e => e.DurableId == untouchedId);

        // Cross sites: travel to palaceTown and enter the palace interior!
        run.Spatial.FloorHit = request => request.Direction.Y < -.99f
            ? default(SpatialHit) with { Present = true, Converged = true, Point = request.Origin - Vector3.UnitY, Normal = Vector3.UnitY }
            : default;
        run.Travel(palaceTown!);
        DebugCommandResult entered = session.Commands<IDeveloperPlacementCommands>()
            .Enter(palaceTown.Name, targetOffice.BuildingId!.Value.ToString());
        Assert.True(entered.Succeeded, entered.Message);
        Assert.Equal(targetOffice.ProfileKey, session.Sites.ActiveProfile);

        // Save inside the palace and restore cleanly (the origin-town factor is now completely unloaded/off-scene!).
        RulesetSavePayload palaceSave = session.CaptureSave();
        using AssembledWorldRun restoredRun = world.Restore(palaceSave, palaceTown.Id, building: targetOffice.BuildingId);
        DaggerfallSession restored = restoredRun.Session;
        Assert.Equal(targetOffice.ProfileKey, restored.Sites.ActiveProfile);

        // Locate the live materialized palace CourtFactor in the restored session.
        Assert.True(restored.State.Npcs.TryGet(courtFactorId, out DaggerfallNpc? palaceKin));
        Assert.NotNull(palaceKin);
        Assert.Equal(greatHouseId, palaceKin!.FamilyId);

        // Verify the market factor is NOT live (off-scene), yet the persisted vouch grants our blunt fighter court access!
        Assert.False(restored.State.Actors.TryGet(factorId, out _));
        ActorVouchRecord hydratedVouch = Assert.Single(restored.Introductions.ReadVouches(courtFactorId));
        Assert.Equal(factorId, hydratedVouch.ReferrerDurableId);
        Assert.Equal(greatHouseId, hydratedVouch.ReferrerFamilyId);

        Assert.True(restored.Introductions.TrustsReferrer(palaceKin, hydratedVouch));
        DaggerfallIntroductionAccess access = restored.Introductions.AssessAccess(palaceKin, inCastleOverride: true);
        Assert.True(access.Allowed, access.DenialReason);
        Assert.NotNull(access.QualifyingVouch);
        Assert.Equal(factorId, access.QualifyingVouch!.ReferrerDurableId);
        Assert.True(restored.Introductions.HasDepositWaiverVouch(palaceKin));
    }

    [Fact]
    public void Blood_feud_blocks_access_introductions_and_deposit_waiver_while_unwitnessed_kill_and_in_transit_news_do_not()
    {
        using var fixture = new HouseholdIntroFixture(random: RandomMinimum.Create());
        DaggerfallSession session = fixture.Session;
        // Even a very silver-tongued character cannot charm past an attributed blood feud!
        SetSocialStats(session, etiquette: 90, streetwise: 90, personality: 90);

        DaggerfallSiteRecord originSite = session.Site.ActiveSite!;
        DaggerfallSiteRecord distantSite = TestPayload.Definitions.Locations.Records
            .First(r => r.Id != originSite.Id
                && double.Hypot(r.MapPixelX - originSite.MapPixelX, r.MapPixelY - originSite.MapPixelY) is >= 25.0 and <= 80.0);

        DaggerfallNpcSite originNpcSite = new(originSite.Id.Region, originSite.Name, "1/1/0", session.Sites.ActiveProfile.LogicalId);
        string localHouseholdA = DaggerfallHouseholds.BuildingHouseholdId(originSite.Id, 1, 1, 0);
        string localHouseholdB = DaggerfallHouseholds.BuildingHouseholdId(originSite.Id, 1, 1, 1);

        // 1. Unwitnessed murder of household A member -> NO blood feud; prior vouch & access remain intact.
        long unwitnessedVictim = AddNpcWithFamily(session, originSite.Id, originNpcSite, "feud/unwit-v", "Corin Hale", localHouseholdA, 0, ["talk"]);
        long unwitnessedKin = AddNpcWithFamily(session, originSite.Id, originNpcSite, "feud/unwit-k", "Tessa Hale", localHouseholdA, 0, ["talk", "shop"]);
        long outsider = AddNpcWithFamily(session, originSite.Id, originNpcSite, "feud/outsider", "Joran Pike", null, 0, ["talk"]);
        session.Introductions.AdjustActorDisposition(outsider, 25);

        DaggerfallNpc outsiderNpc = session.State.Npcs.Require(outsider);
        DaggerfallNpc unwitKinNpc = session.State.Npcs.Require(unwitnessedKin);
        Assert.True(session.Introductions.TryIssueIntroduction(outsiderNpc, unwitnessedKin, originSite).Accepted);
        Assert.True(session.Introductions.HasDepositWaiverVouch(unwitKinNpc));

        fixture.Perception.Responder = _ => new PerceptionReadoutResult(
            ReadOnlyMemory<PerceptionPair>.Empty, ReadOnlyMemory<PerceptionAggregate>.Empty,
            0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        ActorState unwitActor = session.State.Actors.Get(unwitnessedVictim);
        unwitActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(unwitnessedVictim, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            unwitActor,
            minute: 100L);

        Assert.False(session.Introductions.IsInBloodFeud(unwitKinNpc));
        Assert.True(session.Introductions.AssessAccess(unwitKinNpc).Allowed);
        Assert.True(session.Introductions.HasDepositWaiverVouch(unwitKinNpc));

        // 2. Witnessed murder of local household B member -> blood feud blocks AssessAccess, introductions, and deposit waiver!
        long localVictim = AddNpcWithFamily(session, originSite.Id, originNpcSite, "feud/loc-v", "Bram Mercer", localHouseholdB, 0, ["talk"]);
        long localKin = AddNpcWithFamily(session, originSite.Id, originNpcSite, "feud/loc-k", "Lina Mercer", localHouseholdB, 0, ["talk", "shop"]);
        long witness = AddNpcWithFamily(session, originSite.Id, originNpcSite, "feud/witness", "Oren Watch", null, 0, ["talk"]);
        DaggerfallNpc localKinNpc = session.State.Npcs.Require(localKin);

        // Give localKin a trusted vouch BEFORE the murder so we verify the blood feud overrides prior vouches!
        Assert.True(session.Introductions.TryIssueIntroduction(outsiderNpc, localKin, originSite).Accepted);
        Assert.True(session.Introductions.HasDepositWaiverVouch(localKinNpc));
        Assert.Equal(DaggerfallIntroductions.TrustedVouchStandingBonus, session.Introductions.TrustedVouchReactionBonus(localKin));

        fixture.Perception.Responder = _ => new PerceptionReadoutResult(
            new[] { new PerceptionPair(checked((ulong)witness), DaggerfallActorIdentity.PlayerEntityId, 2.0, 1.0, PerceptionPairKind.Visible, 1.0) },
            ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        ActorState locVictimActor = session.State.Actors.Get(localVictim);
        locVictimActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(localVictim, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            locVictimActor,
            minute: 200L);

        Assert.True(session.Introductions.IsInBloodFeud(localKinNpc));
        DaggerfallIntroductionAccess localFeudAccess = session.Introductions.AssessAccess(localKinNpc);
        Assert.False(localFeudAccess.Allowed);
        Assert.Contains("blood feud", localFeudAccess.DenialReason!, StringComparison.OrdinalIgnoreCase);
        Assert.False(session.Introductions.HasDepositWaiverVouch(localKinNpc));
        Assert.Equal(0, session.Introductions.TrustedVouchReactionBonus(localKin));
        Assert.Empty(session.Introductions.ListIntroductionTargets(localKinNpc, originSite));

        // Even an unrelated friend cannot introduce the player into a feuding household.
        DaggerfallIntroductionResult refusedIntro = session.Introductions.TryIssueIntroduction(outsiderNpc, localKin, originSite);
        Assert.False(refusedIntro.Accepted);
        Assert.Contains("blood feud", refusedIntro.Message, StringComparison.OrdinalIgnoreCase);

        // 3. Witnessed murder of a Great House member -> courier-paced blood feud across the province (0 ledger bytes on untouched distant kin!).
        const string greatHouse = "fam:house:r17:0";
        long nobleVictim = AddNpcWithFamily(session, originSite.Id, originNpcSite, "feud/noble-v", "Lord Aldric", greatHouse, 0, ["talk"]);
        DaggerfallWorldProfileKey distantProfile = DaggerfallWorldProfileIds.Exterior(distantSite.Id);
        DaggerfallNpcSite distantNpcSite = new(distantSite.Id.Region, distantSite.Name, string.Empty, distantProfile.LogicalId);
        DaggerfallNpc distantNoble = BuildUntouchedOffSceneNpc(distantSite.Id, distantProfile, distantNpcSite, "feud/noble-distant", "Lady Seraphina", greatHouse, 0, ["talk", "shop"]);
        long distantNobleId = distantNoble.DurableId;

        long startMinute = ((WorldRpg.Rulesets.Daggerfall.Travel.IDaggerfallExpeditionSurroundings)session).Calendar.MinuteNumber;
        ActorState nobleVictimActor = session.State.Actors.Get(nobleVictim);
        nobleVictimActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(nobleVictim, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            nobleVictimActor,
            minute: startMinute);

        // At t0 in distantSite, news is still in transit -> no feud yet!
        Assert.False(session.Introductions.IsInBloodFeud(distantNoble, distantSite));
        Assert.True(session.Introductions.AssessAccess(distantNoble).Allowed);

        // Advance past courier arrival (14 days) -> Great House blood feud now blocks distantNoble!
        session.AdvanceElapsedTime(14L * 24L * 60L * 60L, idleFatigue: false);
        Assert.True(session.Introductions.IsInBloodFeud(distantNoble, distantSite));
        DaggerfallIntroductionAccess distantFeud = session.Introductions.AssessAccess(distantNoble);
        Assert.False(distantFeud.Allowed);
        Assert.Contains("blood feud", distantFeud.DenialReason!, StringComparison.OrdinalIgnoreCase);
        Assert.False(session.Introductions.HasDepositWaiverVouch(distantNoble));

        // Save/reload: distantNoble still has 0 ActorLedger entries (untouched!), yet blood feud persists via WordSpread!
        RulesetSavePayload saved = session.CaptureSave();
        DaggerfallSavePayload raw = DaggerfallSavePayload.Read(saved);
        Assert.DoesNotContain(raw.ActorLedger ?? [], e => e.DurableId == distantNobleId);

        using DaggerfallSession restored = fixture.Restore(saved);
        Assert.True(restored.Introductions.IsInBloodFeud(distantNoble, distantSite));
        Assert.False(restored.Introductions.AssessAccess(distantNoble).Allowed);
        Assert.False(restored.Introductions.HasDepositWaiverVouch(distantNoble));
    }

    [Fact]
    public void Negative_personal_disposition_alone_does_not_create_blood_feud_while_attributed_family_grudges_revoke_prior_vouches()
    {
        using var fixture = new HouseholdIntroFixture(random: RandomMinimum.Create());
        DaggerfallSession session = fixture.Session;
        SetSocialStats(session, etiquette: 60, streetwise: 60, personality: 60);

        DaggerfallSiteRecord originSite = session.Site.ActiveSite!;
        DaggerfallSiteRecord distantSite = TestPayload.Definitions.Locations.Records
            .First(r => r.Id != originSite.Id
                && double.Hypot(r.MapPixelX - originSite.MapPixelX, r.MapPixelY - originSite.MapPixelY) is >= 25.0 and <= 80.0);

        DaggerfallNpcSite originNpcSite = new(originSite.Id.Region, originSite.Name, "1/4/0", session.Sites.ActiveProfile.LogicalId);
        string localHousehold = DaggerfallHouseholds.BuildingHouseholdId(originSite.Id, 1, 4, 0);

        long merchantId = AddNpcWithFamily(session, originSite.Id, originNpcSite, "r11107/merchant", "Maren Holt", localHousehold, 0, ["talk", "shop"]);
        long siblingId = AddNpcWithFamily(session, originSite.Id, originNpcSite, "r11107/sibling", "Bram Holt", localHousehold, 0, ["talk"]);
        long outsiderId = AddNpcWithFamily(session, originSite.Id, originNpcSite, "r11107/outsider", "Joran Pike", null, 0, ["talk"]);
        long witnessId = AddNpcWithFamily(session, originSite.Id, originNpcSite, "r11107/witness", "Oren Watch", null, 0, ["talk"]);

        DaggerfallNpc merchant = session.State.Npcs.Require(merchantId);
        DaggerfallNpc outsider = session.State.Npcs.Require(outsiderId);

        // Give the ordinary family-bearing merchant a trusted vouch from an outsider.
        session.Introductions.AdjustActorDisposition(outsiderId, 25);
        Assert.True(session.Introductions.TryIssueIntroduction(outsider, merchantId, originSite).Accepted);
        Assert.True(session.Introductions.HasDepositWaiverVouch(merchant));
        Assert.Equal(DaggerfallIntroductions.TrustedVouchStandingBonus, session.Introductions.TrustedVouchReactionBonus(merchantId));

        // 1. Apply 4 quest failures (-16 personal disposition) and 2 rude favour attempts (-4 personal -> -20 total).
        //    Household standing remains 0 -> MUST NOT trigger a blood feud, deny access, or revoke the trusted vouch!
        for (int i = 0; i < 4; i++)
            session.Introductions.AdjustActorDisposition(merchantId, DaggerfallIntroductions.QuestFailureDispositionPenalty);
        for (int i = 0; i < 2; i++)
            session.Introductions.AdjustActorDisposition(merchantId, DaggerfallIntroductions.FavourRudeNoblePenalty);

        Assert.Equal(-20, session.SocialConsequences.EffectiveDispositionDelta(merchantId));
        Assert.Equal(0, session.SocialConsequences.HouseholdStandingDelta(localHousehold, originSite));
        Assert.False(session.Introductions.IsInBloodFeud(merchant));
        Assert.True(session.Introductions.AssessAccess(merchant).Allowed);
        Assert.True(session.Introductions.HasDepositWaiverVouch(merchant));
        Assert.Equal(DaggerfallIntroductions.TrustedVouchStandingBonus, session.Introductions.TrustedVouchReactionBonus(merchantId));

        // 2. Now commit an attributed witnessed murder against their sibling -> attributed local grudge revokes prior vouch!
        fixture.Perception.Responder = _ => new PerceptionReadoutResult(
            new[] { new PerceptionPair(checked((ulong)witnessId), DaggerfallActorIdentity.PlayerEntityId, 2.0, 1.0, PerceptionPairKind.Visible, 1.0) },
            ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        ActorState siblingActor = session.State.Actors.Get(siblingId);
        siblingActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(siblingId, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            siblingActor,
            minute: 150L);

        Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty, session.SocialConsequences.HouseholdStandingDelta(localHousehold, originSite));
        Assert.True(session.Introductions.IsInBloodFeud(merchant));
        Assert.False(session.Introductions.AssessAccess(merchant).Allowed);
        Assert.False(session.Introductions.HasDepositWaiverVouch(merchant));
        Assert.Equal(0, session.Introductions.TrustedVouchReactionBonus(merchantId));

        // 3. Same guarantee for a Great House member holding a prior vouch: courier-arrived grudge revokes their prior vouch!
        const string greatHouse = "fam:house:r17:1";
        long houseVictimId = AddNpcWithFamily(session, originSite.Id, originNpcSite, "r11107/house-victim", "Lord Cassian", greatHouse, 0, ["talk"]);
        DaggerfallWorldProfileKey distantProfile = DaggerfallWorldProfileIds.Exterior(distantSite.Id);
        DaggerfallNpcSite distantNpcSite = new(distantSite.Id.Region, distantSite.Name, string.Empty, distantProfile.LogicalId);
        DaggerfallNpc distantHouseKin = BuildUntouchedOffSceneNpc(distantSite.Id, distantProfile, distantNpcSite, "r11107/house-kin", "Lady Elara", greatHouse, 0, ["talk", "shop"]);
        ActorVouchRecord priorVouch = new(outsiderId, 0, ActorVouchKind.Trade, 1, "Joran Pike", originSite.Name);
        session.State.ActorLedger.RecordVouch(distantHouseKin.DurableId, priorVouch, ActorOrigin.Seeded(distantProfile, "r11107/house-kin"));

        Assert.True(session.Introductions.HasDepositWaiverVouch(distantHouseKin));

        long startMinute = ((WorldRpg.Rulesets.Daggerfall.Travel.IDaggerfallExpeditionSurroundings)session).Calendar.MinuteNumber;
        ActorState houseVictimActor = session.State.Actors.Get(houseVictimId);
        houseVictimActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(houseVictimId, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            houseVictimActor,
            minute: startMinute);

        // Still in transit -> prior vouch still holds at distantSite.
        Assert.False(session.Introductions.IsInBloodFeud(distantHouseKin, distantSite));
        Assert.True(session.Introductions.HasDepositWaiverVouch(distantHouseKin));

        // Courier arrives -> Great House blood feud revokes prior vouch!
        session.AdvanceElapsedTime(14L * 24L * 60L * 60L, idleFatigue: false);
        Assert.True(session.Introductions.IsInBloodFeud(distantHouseKin, distantSite));
        Assert.False(session.Introductions.AssessAccess(distantHouseKin).Allowed);
        Assert.False(session.Introductions.HasDepositWaiverVouch(distantHouseKin));
    }

    private static void SetSocialStats(DaggerfallSession session, int etiquette, int streetwise, int personality)
    {
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("etiquette")).BaseValue = etiquette;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("streetwise")).BaseValue = streetwise;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Personality.Value)).BaseValue = personality;
    }

    private static long AddNpcWithFamily(
        DaggerfallSession session,
        DaggerfallSiteId siteId,
        DaggerfallNpcSite binding,
        string slotId,
        string displayName,
        string? familyId,
        int factionId,
        string[] services)
    {
        DaggerfallWorldProfileKey profile = session.Sites.ActiveProfile;
        string slotKey = $"{profile.LogicalId}/{slotId}";
        long id = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, slotKey);
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, Math.Abs(slotId.GetHashCode()), factionId);
        session.State.Npcs.RegisterDeterministic(
            id, DaggerfallNpcKind.Static, slotId, binding, look, "person", services,
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

    private static DaggerfallNpc BuildUntouchedOffSceneNpc(
        DaggerfallSiteId siteId,
        DaggerfallWorldProfileKey profile,
        DaggerfallNpcSite binding,
        string slotId,
        string displayName,
        string? familyId,
        int factionId,
        string[] services)
    {
        string slotKey = $"{profile.LogicalId}/{slotId}";
        long id = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, slotKey);
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, Math.Abs(slotId.GetHashCode()), factionId);
        return new DaggerfallNpc(
            id, DaggerfallNpcKind.Static, slotId, binding, look, "person", services,
            DaggerfallNpcPresence.Active, null, null, null)
        {
            Profile = profile,
            DisplayName = displayName,
            FamilyId = familyId,
        };
    }

    private sealed class HouseholdIntroFixture : IDisposable
    {
        private readonly DaggerfallSiteProfile _profile;
        private readonly List<string> _releases = [];
        private readonly ContentFake _content;
        private readonly SpatialFake _spatial;
        private readonly IRandomService? _random;

        internal PerceptionFake Perception { get; } = PerceptionFake.Create();
        internal DaggerfallSession Session { get; }

        internal HouseholdIntroFixture(IRandomService? random = null)
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
