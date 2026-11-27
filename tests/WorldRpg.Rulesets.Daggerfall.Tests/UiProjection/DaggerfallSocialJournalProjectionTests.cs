using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Focused C# projection tests for Den #11163 (Player journal Social / Relations tab).
/// </summary>
public sealed class DaggerfallSocialJournalProjectionTests
{
    [Fact]
    public void Social_journal_projects_feuds_mourning_settlement_progress_vouches_and_standings_with_no_internal_ids()
    {
        using var fixture = new SocialJournalFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/4/0", session.Sites.ActiveProfile.LogicalId);

        string localFamily = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 4, 0);
        const string greatHouse = "fam:house:r17:1";

        long victimId = AddNpcWithFamily(session, site.Id, npcSite, "t11163/victim", "Hector Vale", localFamily);
        long witnessId = AddNpcWithFamily(session, site.Id, npcSite, "t11163/witness", "Oren Pike", null);
        long nobleMemberId = AddNpcWithFamily(session, site.Id, npcSite, "t11163/noble", "Lady Seraphina", greatHouse);
        long voucherId = AddNpcWithFamily(session, site.Id, npcSite, "t11163/voucher", "Captain Vael", null, role: "guard");
        long targetContactId = AddNpcWithFamily(session, site.Id, npcSite, "t11163/target", "Lord Gothryd", null, role: "noble");

        // 1. Witnessed murder of local household member -> permanent blood feud + mourning + unmediated settlement hint.
        fixture.Perception.Responder = _ => new PerceptionReadoutResult(
            new[] { new PerceptionPair(checked((ulong)witnessId), DaggerfallActorIdentity.PlayerEntityId, 2.0, 1.0, PerceptionPairKind.Visible, 1.0) },
            ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        ActorState victimActor = session.State.Actors.Get(victimId);
        victimActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(victimId, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            victimActor,
            minute: session.Calendar.MinuteNumber);

        // 2. Great House minor arrest grudge (-10, decaying).
        _ = session.Law.ArrestOrImprisonNpc(
            nobleMemberId, site.Id.Region, DaggerfallCrimeKind.CriminalConspiracy, banished: false, detail: "warrant");

        // 3. Record an active Court vouch for targetContactId from voucherId.
        int day = checked((int)session.Calendar.DayNumber);
        ActorVouchRecord courtVouch = new(
            ReferrerDurableId: voucherId,
            ReferrerFactionId: 518,
            Kind: ActorVouchKind.Court,
            IssuedDay: day,
            ReferrerName: "Captain Vael",
            OriginSiteName: site.Name);
        ActorState targetActor = session.State.Actors.Get(targetContactId);
        targetActor.Actor.Get<ActorOverlayComponent>().RecordVouch(courtVouch);

        // 4. Regional reputation boost for character-relevant standings.
        session.State.Social.ChangeRegionalReputation(site.Id.Region, 15);

        // Read the C# projection and verify all sections and human-readable wording.
        DaggerfallSocialJournalView view = session.SocialJournal.Read();

        // Households sorted with blood feuds first, then minor grudges, with zero raw fam:... IDs!
        Assert.Equal(2, view.Households.Count);
        DaggerfallSocialJournalHouseholdEntry feudRow = view.Households[0];
        Assert.True(feudRow.IsFeud);
        Assert.Equal("Local Household", feudRow.Tier);
        Assert.DoesNotContain("fam:", feudRow.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Blood Feud", feudRow.Standing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mourning", feudRow.Status, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(feudRow.SettlementHint);
        Assert.Contains("mediate a truce", feudRow.SettlementHint!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gp", feudRow.SettlementHint!, StringComparison.OrdinalIgnoreCase);

        DaggerfallSocialJournalHouseholdEntry grudgeRow = view.Households[1];
        Assert.False(grudgeRow.IsFeud);
        Assert.Equal("Great House", grudgeRow.Tier);
        Assert.DoesNotContain("fam:", grudgeRow.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("decays over time", grudgeRow.Status, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(grudgeRow.SettlementHint);
        Assert.Contains("gp", grudgeRow.SettlementHint!, StringComparison.OrdinalIgnoreCase);

        // Mediating a truce + recording reconciliation service & pardon updates settlement hint and lowers wergild!
        session.SocialConsequences.RecordTruceMediated(localFamily, "Captain Vael", day);
        session.SocialConsequences.RecordReconciliationServiceCompleted(localFamily, day);
        session.SocialConsequences.RecordPardonGranted(localFamily, "Temple of Mara", day);
        DaggerfallSocialJournalHouseholdEntry updatedFeud = session.SocialJournal.Read().Households[0];
        Assert.Contains("truce mediated by Captain Vael", updatedFeud.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reconciliation service completed", updatedFeud.SettlementHint!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Temple of Mara", updatedFeud.SettlementHint!, StringComparison.OrdinalIgnoreCase);

        // Active vouches section shows the court vouch cleanly.
        DaggerfallSocialJournalVouchEntry vouchRow = Assert.Single(view.Vouches);
        Assert.Equal("Lord Gothryd", vouchRow.TargetName);
        Assert.Equal("Captain Vael", vouchRow.VoucherName);
        Assert.Equal("Court Introduction", vouchRow.Kind);
        Assert.Contains("Active", vouchRow.Status, StringComparison.OrdinalIgnoreCase);

        // Factions & Standings section includes the current region with positive standing.
        Assert.Contains(view.Factions, f =>
            f.Name.Contains("(Region)", StringComparison.OrdinalIgnoreCase)
            && f.Standing.Contains("+15", StringComparison.Ordinal)
            && f.Detail.Contains("Current region", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Social_journal_preserves_settlement_progress_outside_home_town_and_resolves_unmaterialized_court_vouch_names()
    {
        using var fixture = new SocialJournalFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord currentSite = session.Site.ActiveSite!;
        DaggerfallSiteRecord otherTown = TestPayload.Definitions.Locations.Records
            .First(r => r.Id != currentSite.Id && r.Exterior is not null);

        int day = checked((int)session.Calendar.DayNumber);

        // 1. Local blood feud in ANOTHER town (outside currentSite) with reconciliation service + pardon recorded.
        string distantLocalFamily = DaggerfallHouseholds.BuildingHouseholdId(otherTown.Id, 0, 0, 0);
        session.SocialConsequences.SetHouseholdStanding(distantLocalFamily, -20, atSite: otherTown, minute: session.Calendar.MinuteNumber, dayOverride: day);
        session.SocialConsequences.RecordTruceMediated(distantLocalFamily, "Magistrate Vane", day);
        session.SocialConsequences.RecordReconciliationServiceCompleted(distantLocalFamily, day);
        session.SocialConsequences.RecordPardonGranted(distantLocalFamily, "Court of Wayrest", day);

        // 2. Issue an introduction to the region's UNMATERIALIZED ruler (not in State.Npcs or State.Actors)!
        DaggerfallOfficeHolder? ruler = session.OfficeHolders.ResolveRegionalRuler(currentSite.Id.Region);
        Assert.NotNull(ruler);
        Assert.True(ruler!.HasLivingHolder && ruler.ActorId is long rulerId && !string.IsNullOrWhiteSpace(ruler.DisplayName));
        long targetRulerId = ruler.ActorId!.Value;
        Assert.False(session.State.Actors.TryGet(targetRulerId, out _));
        Assert.False(session.State.Npcs.TryGet(targetRulerId, out _));

        DaggerfallNpcSite npcSite = new(currentSite.Id.Region, currentSite.Name, "1/5/0", session.Sites.ActiveProfile.LogicalId);
        long nobleReferrerId = AddNpcWithFamily(session, currentSite.Id, npcSite, "t11163/noble-ref", "Lord Cassian", "fam:house:r17:0", role: "noble");
        session.State.Social.ChangeRegionalReputation(currentSite.Id.Region, 40);
        session.Introductions.AdjustActorDisposition(nobleReferrerId, 30);
        DaggerfallNpc referrerNpc = session.State.Npcs.Require(nobleReferrerId);

        DaggerfallIntroductionResult issued = session.Introductions.TryIssueIntroduction(referrerNpc, targetRulerId, currentSite);
        Assert.True(issued.Accepted, issued.Message);

        // Read SocialJournal from currentSite:
        DaggerfallSocialJournalView view = session.SocialJournal.Read();

        // Off-site local feud MUST retain settlement progress & wergild hint (never null!).
        DaggerfallSocialJournalHouseholdEntry offSiteFeud = Assert.Single(view.Households, h => h.IsFeud);
        Assert.True(offSiteFeud.IsFeud);
        Assert.NotNull(offSiteFeud.SettlementHint);
        Assert.Contains("Reconciliation service completed", offSiteFeud.SettlementHint!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Court of Wayrest", offSiteFeud.SettlementHint!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gp", offSiteFeud.SettlementHint!, StringComparison.OrdinalIgnoreCase);

        // Unmaterialized regional ruler vouch MUST resolve the ruler's actual DisplayName (never "Local Contact"!).
        DaggerfallSocialJournalVouchEntry rulerVouch = Assert.Single(view.Vouches);
        Assert.Equal(ruler.DisplayName, rulerVouch.TargetName);
        Assert.NotEqual("Local Contact", rulerVouch.TargetName);
        Assert.Equal("Lord Cassian", rulerVouch.VoucherName);

        // Even if a pre-existing referral entry lacked DisplayNameOverride, office/cast fallback still resolves the real name!
        if (session.State.ActorLedger.TryGet(targetRulerId, out ActorLedgerEntry? rulerEntry) && rulerEntry?.Referral is { } rulerRef)
            session.State.ActorLedger.Upsert(rulerEntry with { Referral = rulerRef with { DisplayNameOverride = null } });
        DaggerfallSocialJournalVouchEntry fallbackVouch = Assert.Single(session.SocialJournal.Read().Vouches);
        Assert.Equal(ruler.DisplayName, fallbackVouch.TargetName);
    }

    private static long AddNpcWithFamily(
        DaggerfallSession session,
        DaggerfallSiteId siteId,
        DaggerfallNpcSite npcSite,
        string slotId,
        string displayName,
        string? familyId,
        string role = "civilian")
    {
        DaggerfallWorldProfileKey profile = session.Sites.ActiveProfile;
        long id = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, $"{profile.LogicalId}/{slotId}");
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, Math.Abs(slotId.GetHashCode(StringComparison.Ordinal)), 518);
        session.State.Npcs.RegisterDeterministic(
            id, DaggerfallNpcKind.Static, slotId, npcSite, look, role, ["talk"],
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

    private sealed class SocialJournalFixture : IDisposable
    {
        private readonly DaggerfallSiteProfile _profile;
        private readonly List<string> _releases = [];
        private readonly ContentFake _content;
        private readonly SpatialFake _spatial;

        internal PerceptionFake Perception { get; } = PerceptionFake.Create();
        internal DaggerfallSession Session { get; }

        internal SocialJournalFixture()
        {
            _profile = ReadInputs(TestData.RepositoryRoot);
            _content = new ContentFake(_releases);
            PopulateContent(_content, _profile);
            _spatial = SpatialFake.Create(_profile.Geometry.Spatial[0].Sha256, _releases);
            EngineContextFake engine = EngineContextFake.Create(_content, _spatial.Service, new AppearanceFake(_releases), Perception.Service);
            Session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, _profile, DaggerfallTuning.Defaults));
        }

        public void Dispose() => Session.Dispose();
    }
}
