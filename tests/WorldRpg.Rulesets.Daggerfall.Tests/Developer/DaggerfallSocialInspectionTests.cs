using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Runtime social and political diagnostics (#11117): enriched <c>actor.inspect</c> social breakdown,
/// <c>world.word-spread</c> <c>familyId</c>, and <c>social.site</c> / <c>social.region</c> / <c>social.player</c>.
/// </summary>
public sealed class DaggerfallSocialInspectionTests
{
    [Fact]
    public void Actor_inspect_surfaces_reaction_access_vouches_blood_feud_and_ledger_status()
    {
        using var fixture = new SocialDiagFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/1/0", session.Sites.ActiveProfile.LogicalId);
        string household = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 1, 0);

        long merchantId = AddNpcWithFamily(session, site.Id, npcSite, "diag/merchant", "Maren Holt", household, ["talk", "shop"]);
        long siblingId = AddNpcWithFamily(session, site.Id, npcSite, "diag/sibling", "Bram Holt", household, ["talk"]);
        long witnessId = AddNpcWithFamily(session, site.Id, npcSite, "diag/witness", "Oren Watch", null, ["talk"]);

        IDeveloperActorCommands actorCmds = session.Commands<IDeveloperActorCommands>();

        // 1. Before any interaction: untouched ledger, allowed access, 0 vouches, not in blood feud.
        DebugCommandResult beforeRes = actorCmds.Inspect(merchantId.ToString());
        Assert.True(beforeRes.Succeeded, beforeRes.Message);
        using (JsonDocument doc = JsonDocument.Parse(beforeRes.Message))
        {
            JsonElement social = doc.RootElement.GetProperty("social");
            Assert.Equal("Maren Holt", social.GetProperty("displayName").GetString());
            Assert.Equal(household, social.GetProperty("familyId").GetString());
            Assert.Equal("local", social.GetProperty("scope").GetString());
            Assert.False(social.GetProperty("reaction").GetProperty("inBloodFeud").GetBoolean());
            Assert.Equal(0, social.GetProperty("reaction").GetProperty("vouchBonus").GetInt32());
            Assert.True(social.GetProperty("access").GetProperty("allowed").GetBoolean());
            Assert.False(social.GetProperty("access").GetProperty("hasDepositWaiver").GetBoolean());
            Assert.Empty(social.GetProperty("vouches").EnumerateArray());
            Assert.True(social.GetProperty("ledger").GetProperty("untouched").GetBoolean());
            Assert.Contains(social.GetProperty("localKin").EnumerateArray(), k => k.GetProperty("durableId").GetInt64() == siblingId);
        }

        // 2. Earn sibling trust (+10 -> +5 household goodwill) and issue a kin Trade vouch -> actor.inspect reflects vouch bonus, trusted vouch, deposit waiver, and dirty ledger.
        session.Introductions.AdjustActorDisposition(siblingId, 10);
        DaggerfallNpc sibling = session.State.Npcs.Require(siblingId);
        Assert.True(session.Introductions.TryIssueIntroduction(sibling, merchantId, site).Accepted);

        DebugCommandResult vouchedRes = actorCmds.Inspect(merchantId.ToString());
        Assert.True(vouchedRes.Succeeded, vouchedRes.Message);
        using (JsonDocument doc = JsonDocument.Parse(vouchedRes.Message))
        {
            JsonElement social = doc.RootElement.GetProperty("social");
            Assert.Equal(DaggerfallIntroductions.TrustedVouchStandingBonus, social.GetProperty("reaction").GetProperty("vouchBonus").GetInt32());
            Assert.Equal(+5, social.GetProperty("reaction").GetProperty("householdStanding").GetInt32());
            Assert.True(social.GetProperty("access").GetProperty("hasDepositWaiver").GetBoolean());
            JsonElement vouch = Assert.Single(social.GetProperty("vouches").EnumerateArray());
            Assert.Equal(siblingId, vouch.GetProperty("referrerDurableId").GetInt64());
            Assert.Equal(household, vouch.GetProperty("referrerFamilyId").GetString());
            Assert.True(vouch.GetProperty("trusted").GetBoolean());
            Assert.False(social.GetProperty("ledger").GetProperty("untouched").GetBoolean());
        }

        // 3. Witnessed murder of sibling (-20 -> net householdStanding -15 <= BloodFeudDispositionThreshold) -> actor.inspect shows blood feud, access.allowed=false, and witness records witnessedCrimes!
        fixture.Perception.Responder = _ => new PerceptionReadoutResult(
            new[] { new PerceptionPair(checked((ulong)witnessId), DaggerfallActorIdentity.PlayerEntityId, 2.0, 1.0, PerceptionPairKind.Visible, 1.0) },
            ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        ActorState siblingActor = session.State.Actors.Get(siblingId);
        siblingActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(siblingId, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            siblingActor,
            minute: 250L);

        DebugCommandResult feudRes = actorCmds.Inspect(merchantId.ToString());
        Assert.True(feudRes.Succeeded, feudRes.Message);
        using (JsonDocument doc = JsonDocument.Parse(feudRes.Message))
        {
            JsonElement social = doc.RootElement.GetProperty("social");
            JsonElement reaction = social.GetProperty("reaction");
            Assert.Equal(-15, reaction.GetProperty("householdStanding").GetInt32());
            Assert.True(reaction.GetProperty("inBloodFeud").GetBoolean());
            Assert.Equal(0, reaction.GetProperty("vouchBonus").GetInt32());
            JsonElement access = social.GetProperty("access");
            Assert.False(access.GetProperty("allowed").GetBoolean());
            Assert.False(access.GetProperty("hasDepositWaiver").GetBoolean());
            Assert.Contains("blood feud", access.GetProperty("denialReason").GetString()!, StringComparison.OrdinalIgnoreCase);
        }

        DebugCommandResult witnessRes = actorCmds.Inspect(witnessId.ToString());
        Assert.True(witnessRes.Succeeded, witnessRes.Message);
        using (JsonDocument doc = JsonDocument.Parse(witnessRes.Message))
        {
            JsonElement crimes = doc.RootElement.GetProperty("social").GetProperty("ledger").GetProperty("witnessedCrimes");
            Assert.Contains(crimes.EnumerateArray(), c => c.GetString()!.StartsWith($"murder:{siblingId}:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Social_site_region_player_and_word_spread_surface_runtime_households_great_houses_offices_and_standings()
    {
        using var fixture = new SocialDiagFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/2/0", session.Sites.ActiveProfile.LogicalId);
        string localHousehold = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 2, 0);
        const string greatHouse = "fam:house:r17:0";

        long localA = AddNpcWithFamily(session, site.Id, npcSite, "diag/local-a", "Nora Fen", localHousehold, ["talk"]);
        long nobleVictim = AddNpcWithFamily(session, site.Id, npcSite, "diag/noble-v", "Lord Aldric", greatHouse, ["talk"]);
        long witness = AddNpcWithFamily(session, site.Id, npcSite, "diag/w2", "Guard Witness", null, ["talk"]);

        // Trigger local goodwill ripple (+5) and witnessed Great House murder (-20 HouseholdStanding wavefront).
        session.Introductions.AdjustActorDisposition(localA, +10);
        fixture.Perception.Responder = _ => new PerceptionReadoutResult(
            new[] { new PerceptionPair(checked((ulong)witness), DaggerfallActorIdentity.PlayerEntityId, 2.0, 1.0, PerceptionPairKind.Visible, 1.0) },
            ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        ActorState nobleActor = session.State.Actors.Get(nobleVictim);
        nobleActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(nobleVictim, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            nobleActor,
            minute: 300L);

        // Give player a regional standing bump and guild membership for social.player coverage.
        session.State.Social.ChangeRegionalReputation(site.Id.Region, -15);
        session.State.Social.JoinGuild(368, 1);

        IDeveloperSocialCommands socialCmds = session.Commands<IDeveloperSocialCommands>();
        IDeveloperWorldConditionCommands worldCmds = session.Commands<IDeveloperWorldConditionCommands>();

        // 1. world.word-spread includes familyId & headline on HouseholdStanding wavefront.
        DebugCommandResult spreadRes = worldCmds.ListWordSpread();
        Assert.True(spreadRes.Succeeded, spreadRes.Message);
        using (JsonDocument doc = JsonDocument.Parse(spreadRes.Message))
        {
            JsonElement wf = Assert.Single(
                doc.RootElement.GetProperty("wavefronts").EnumerateArray(),
                w => w.GetProperty("kind").GetString() == "HouseholdStanding");
            Assert.Equal(greatHouse, wf.GetProperty("familyId").GetString());
            Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty, wf.GetProperty("delta").GetInt32());
            Assert.Contains("Lord Aldric", wf.GetProperty("headline").GetString()!, StringComparison.Ordinal);
        }

        // 2. social.site surfaces loaded people, grouped households, and sparse local standings.
        DebugCommandResult siteRes = socialCmds.Site();
        Assert.True(siteRes.Succeeded, siteRes.Message);
        using (JsonDocument doc = JsonDocument.Parse(siteRes.Message))
        {
            JsonElement root = doc.RootElement;
            Assert.Equal(site.Name, root.GetProperty("site").GetProperty("name").GetString());
            Assert.Contains(root.GetProperty("loadedPeople").EnumerateArray(), p => p.GetProperty("durableId").GetInt64() == localA);
            JsonElement localGroup = Assert.Single(
                root.GetProperty("households").EnumerateArray(),
                h => h.GetProperty("familyId").GetString() == localHousehold);
            Assert.Equal(+5, localGroup.GetProperty("standingDelta").GetInt32());
            Assert.False(localGroup.GetProperty("inBloodFeud").GetBoolean());
            JsonElement localStanding = Assert.Single(
                root.GetProperty("localStandings").EnumerateArray(),
                s => s.GetProperty("familyId").GetString() == localHousehold);
            Assert.Equal(+5, localStanding.GetProperty("standingDelta").GetInt32());
        }

        // 3. social.region surfaces regional ruler, 2..4 Great Houses (with arrived feud on r17:0), and board seats.
        DebugCommandResult regionRes = socialCmds.Region();
        Assert.True(regionRes.Succeeded, regionRes.Message);
        using (JsonDocument doc = JsonDocument.Parse(regionRes.Message))
        {
            JsonElement root = doc.RootElement;
            Assert.Equal(site.Id.Region, root.GetProperty("region").GetInt32());
            Assert.Equal(JsonValueKind.Object, root.GetProperty("ruler").ValueKind);
            JsonElement[] houses = [.. root.GetProperty("greatHouses").EnumerateArray()];
            Assert.InRange(houses.Length, 2, 4);
            JsonElement feudingHouse = Assert.Single(houses, h => h.GetProperty("familyId").GetString() == greatHouse);
            Assert.Equal(DaggerfallSocialConsequences.KinMurderDispositionPenalty, feudingHouse.GetProperty("arrivedStandingDelta").GetInt32());
            Assert.True(feudingHouse.GetProperty("inBloodFeud").GetBoolean());
            Assert.NotEmpty(root.GetProperty("boardSeats").EnumerateArray());
        }

        // 4. social.player surfaces active region rep, wanted flag, guild affiliations, law summary, and ledger count.
        DebugCommandResult playerRes = socialCmds.Player();
        Assert.True(playerRes.Succeeded, playerRes.Message);
        using (JsonDocument doc = JsonDocument.Parse(playerRes.Message))
        {
            JsonElement root = doc.RootElement;
            Assert.Equal(-15, root.GetProperty("activeRegionReputation").GetInt32());
            Assert.True(root.GetProperty("wantedInActiveRegion").GetBoolean());
            Assert.Contains(root.GetProperty("affiliations").EnumerateArray(), a => a.GetProperty("factionId").GetInt32() == 368);
            Assert.True(root.GetProperty("actorLedgerCount").GetInt32() > 0);
        }
    }

    [Fact]
    public void Social_player_reflects_live_regional_modifiers_and_clears_pending_bounty_charges_once_resolved()
    {
        using var fixture = new SocialDiagFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        int activeRegion = site.Id.Region;
        int otherRegion = activeRegion == 23 ? 17 : 23;

        // 1. Outstanding charges & legal response (R11117-1):
        //    Record a reported theft incident (-8 permanent regional standing via IncidentRecorded) and an active Court legal response.
        const string chargeOp = "diag-crime:theft:1";
        DaggerfallCrimeIncidentSave charge = new(
            OperationId: chargeOp,
            Crime: DaggerfallCrimeKind.Theft,
            Stage: DaggerfallCrimeStage.Completed,
            PerpetratorActorId: DaggerfallActorIdentity.PlayerEntityId,
            AffectedActorOrOwnerId: null,
            Region: activeRegion,
            GameMinute: 400L,
            TargetKind: DaggerfallCrimeTargetKind.Civilian,
            Witnesses: new(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [999L]),
            GuildCredit: DaggerfallCrimeGuildCredit.None,
            Reported: true);
        Assert.True(session.State.Crime.RecordIncident(charge));
        session.State.Crime.SetResponse(new DaggerfallLegalResponseSave(
            Id: "diag-case:1",
            Region: activeRegion,
            Charges: [chargeOp],
            Phase: DaggerfallLegalPhase.Court,
            Profile: DaggerfallWorldProfileKeySave.Capture(session.Sites.ActiveProfile),
            ResponseDelaySeconds: 0d,
            Guards: [1001L],
            Penalty: new DaggerfallCourtPenalty(500, 10, false)));

        // 2. Live regional modifiers (R11117-2):
        //    - Active region has permanent -15 (-8 from Theft + -7) plus live +10 modifier -> effective -5 (NOT wanted!).
        //    - Other region has 0 permanent plus live -12 modifier -> effective -12 (WANTED and present in regionalReputations!).
        session.State.Social.ChangeRegionalReputation(activeRegion, -7);
        session.State.Social.SetRegionalReputationSource(
            activeRegion,
            new IntrinsicSourceIdentity(new EntityId(1), SourceInstanceId.Parse("diag-live-bonus")),
            +10);
        session.State.Social.SetRegionalReputationSource(
            otherRegion,
            new IntrinsicSourceIdentity(new EntityId(1), SourceInstanceId.Parse("diag-live-penalty")),
            -12);

        IDeveloperSocialCommands socialCmds = session.Commands<IDeveloperSocialCommands>();

        // Verify active bounties / pending charges / legal responses AND live regional reputation agreement.
        DebugCommandResult beforeResolve = socialCmds.Player();
        Assert.True(beforeResolve.Succeeded, beforeResolve.Message);
        using (JsonDocument doc = JsonDocument.Parse(beforeResolve.Message))
        {
            JsonElement root = doc.RootElement;
            Assert.Equal(-5, root.GetProperty("activeRegionReputation").GetInt32());
            Assert.False(root.GetProperty("wantedInActiveRegion").GetBoolean());

            JsonElement activeRow = Assert.Single(
                root.GetProperty("regionalReputations").EnumerateArray(),
                r => r.GetProperty("region").GetInt32() == activeRegion);
            Assert.Equal(-5, activeRow.GetProperty("reputation").GetInt32());
            Assert.False(activeRow.GetProperty("wanted").GetBoolean());
            Assert.Equal(1, activeRow.GetProperty("pendingChargeCount").GetInt32());
            Assert.True(activeRow.GetProperty("hasActiveBounty").GetBoolean());

            JsonElement otherRow = Assert.Single(
                root.GetProperty("regionalReputations").EnumerateArray(),
                r => r.GetProperty("region").GetInt32() == otherRegion);
            Assert.Equal(-12, otherRow.GetProperty("reputation").GetInt32());
            Assert.True(otherRow.GetProperty("wanted").GetBoolean());
            Assert.True(otherRow.GetProperty("hasActiveBounty").GetBoolean());

            JsonElement law = root.GetProperty("law");
            Assert.Equal(1, law.GetProperty("pendingChargeCount").GetInt32());
            JsonElement pending = Assert.Single(law.GetProperty("pendingCharges").EnumerateArray());
            Assert.Equal(chargeOp, pending.GetProperty("operationId").GetString());
            JsonElement response = Assert.Single(law.GetProperty("legalResponses").EnumerateArray());
            Assert.Equal("Court", response.GetProperty("phase").GetString());
            Assert.Equal(500, response.GetProperty("fine").GetInt32());
            Assert.Equal(JsonValueKind.Null, Assert.Single(law.GetProperty("recentIncidents").EnumerateArray()).GetProperty("disposition").ValueKind);
        }

        // 3. Resolve the legal case -> pendingCharges & legalResponses clear while historical crime counts and disposition remain!
        session.State.Crime.ResolveResponse(activeRegion, DaggerfallCourtOutcome.Acquitted, 450L);

        DebugCommandResult afterResolve = socialCmds.Player();
        Assert.True(afterResolve.Succeeded, afterResolve.Message);
        using (JsonDocument doc = JsonDocument.Parse(afterResolve.Message))
        {
            JsonElement root = doc.RootElement;
            JsonElement activeRow = Assert.Single(
                root.GetProperty("regionalReputations").EnumerateArray(),
                r => r.GetProperty("region").GetInt32() == activeRegion);
            Assert.Equal(0, activeRow.GetProperty("pendingChargeCount").GetInt32());
            Assert.False(activeRow.GetProperty("hasActiveBounty").GetBoolean());

            JsonElement law = root.GetProperty("law");
            Assert.Equal(0, law.GetProperty("pendingChargeCount").GetInt32());
            Assert.Empty(law.GetProperty("pendingCharges").EnumerateArray());
            Assert.Empty(law.GetProperty("legalResponses").EnumerateArray());
            Assert.Equal(1, law.GetProperty("incidentCount").GetInt32());
            Assert.Equal(1, law.GetProperty("reportedIncidentCount").GetInt32());
            JsonElement resolvedInc = Assert.Single(law.GetProperty("recentIncidents").EnumerateArray());
            Assert.Equal("Acquitted", resolvedInc.GetProperty("disposition").GetProperty("outcome").GetString());
        }
    }

    private static long AddNpcWithFamily(
        DaggerfallSession session,
        DaggerfallSiteId siteId,
        DaggerfallNpcSite binding,
        string slotId,
        string displayName,
        string? familyId,
        string[] services)
    {
        DaggerfallWorldProfileKey profile = session.Sites.ActiveProfile;
        string slotKey = $"{profile.LogicalId}/{slotId}";
        long id = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, slotKey);
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, Math.Abs(slotId.GetHashCode()), 0);
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

    private sealed class SocialDiagFixture : IDisposable
    {
        private readonly DaggerfallSiteProfile _profile;
        private readonly List<string> _releases = [];
        private readonly ContentFake _content;
        private readonly SpatialFake _spatial;

        internal PerceptionFake Perception { get; } = PerceptionFake.Create();
        internal DaggerfallSession Session { get; }

        internal SocialDiagFixture()
        {
            _profile = ReadInputs(TestData.RepositoryRoot);
            _content = new ContentFake(_releases);
            PopulateContent(_content, _profile);
            _spatial = SpatialFake.Create(_profile.Geometry.Spatial[0].Sha256, _releases);
            EngineContextFake engine = EngineContextFake.Create(_content, _spatial.Service, new AppearanceFake(_releases), Perception.Service, random: RandomMinimum.Create());
            Session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, _profile, DaggerfallTuning.Defaults));
        }

        public void Dispose() => Session.Dispose();
    }
}
