using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11162: Active blood-feud vendetta retaliation:
/// - Armed/bold adult kin confront and attack the player on sight for open unmediated blood feuds,
///   emitting a non-blocking hostility reason signal (hook for #11164).
/// - Fighting back against vendetta kin aggressors or hired hunters is lawful self-defense (no watch crime).
/// - Wealthy local households, Great Houses, and royal dynasties hire scaled toughs / bounty hunters /
///   Dark Brotherhood assassins on-site and on the overland map, respecting cooldowns and active caps.
/// - Mediating a truce or settling the feud calms aggressors, retires overland pursuit bands, and prunes
///   sparse vendetta save bytes on settlement.
/// </summary>
public sealed class BloodFeudVendettaTests
{
    [Fact]
    public void Active_blood_feud_kin_confront_player_emit_non_blocking_signal_and_allow_lawful_self_defense()
    {
        using var fixture = new VendettaSessionFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/2/0", session.Sites.ActiveProfile.LogicalId);
        string localHousehold = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 2, 0);
        string unwitnessedHousehold = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 3, 0);

        WorldPoint playerPos = session.State.PlayerControl.Position ?? new WorldPoint(0, 0, 0);
        WorldPoint nearbyPos = new(playerPos.X + 4f, playerPos.Y, playerPos.Z);

        // 1. Unwitnessed murder produces NO blood feud and NO kin confrontation.
        long unseenVictimId = AddCivilianNpc(session, site.Id, npcSite, "t11162/unseen-victim", "Elin Moss", unwitnessedHousehold, "person", nearbyPos);
        long unseenKinId = AddCivilianNpc(session, site.Id, npcSite, "t11162/unseen-kin", "Torin Moss", unwitnessedHousehold, "knight", nearbyPos);
        CommitMurder(session, fixture.Perception, unseenVictimId, witnessId: null, session.Calendar.MinuteNumber);

        List<DaggerfallHostileReasonSignal> signals = [];
        session.FeudVendetta.HostilitySignaled += signals.Add;
        int day = checked((int)session.Calendar.DayNumber);

        Assert.Equal(0, session.FeudVendetta.EvaluateSceneKinConfrontations(site, playerPos, day));
        Assert.False(session.FeudVendetta.IsVendettaAggressor(unseenKinId));
        Assert.Empty(signals);

        // 2. Witnessed murder triggers an open unmediated blood feud -> bold kin confronts player on sight!
        long victimId = AddCivilianNpc(session, site.Id, npcSite, "t11162/victim", "Doran Holt", localHousehold, "person", nearbyPos);
        long boldKinId = AddCivilianNpc(session, site.Id, npcSite, "t11162/bold-kin", "Garrett Holt", localHousehold, "knight", nearbyPos);
        long guardWitnessId = AddCivilianNpc(session, site.Id, npcSite, "t11162/witness", "Watchman Corvus", null, "guard", nearbyPos);

        CommitMurder(session, fixture.Perception, victimId, guardWitnessId, session.Calendar.MinuteNumber);

        int triggered = session.FeudVendetta.EvaluateSceneKinConfrontations(site, playerPos, day);
        Assert.True(triggered >= 1);
        Assert.True(session.FeudVendetta.IsVendettaAggressor(boldKinId));

        ActorState kinActor = session.State.Actors.Get(boldKinId);
        Assert.True(kinActor.Actor.Get<ActorHarmPolicy>().IsCombatant);
        Assert.True(kinActor.Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);

        DaggerfallHostileReasonSignal signal = Assert.Single(signals);
        Assert.Equal(boldKinId, signal.ActorId);
        Assert.Equal(DaggerfallHostileSignalKind.KinVendettaConfrontation, signal.Kind);
        Assert.Equal("Garrett Holt", signal.SpeakerName);
        Assert.Contains("murdered our kin", signal.ReasonLine, StringComparison.OrdinalIgnoreCase);
        Assert.False(session.Law.ModalOpen);

        // Social journal surfaces the active vendetta under the household row.
        DaggerfallSocialJournalView journal = session.SocialJournal.Read();
        DaggerfallSocialJournalHouseholdEntry row = Assert.Single(journal.Households, h => h.IsFeud);
        Assert.Contains("Armed kin hunting you on sight", row.Status, StringComparison.OrdinalIgnoreCase);

        // 3. Fighting back against the confronting vendetta kin is lawful self-defense (zero watch crime!).
        int incidentsBefore = session.State.Crime.Incidents.Count;
        session.Crimes.ObserveAttack(DaggerfallActorIdentity.PlayerEntityId, boldKinId, "self-defense:t11162:1");
        Assert.Equal(incidentsBefore, session.State.Crime.Incidents.Count);
    }

    [Fact]
    public void Mediated_truce_and_feud_settlement_calm_kin_retire_overland_hunters_and_prune_save_bytes()
    {
        using var fixture = new VendettaSessionFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/4/0", session.Sites.ActiveProfile.LogicalId);
        string houseFamily = $"fam:house:r{site.Id.Region}:0";

        WorldPoint playerPos = session.State.PlayerControl.Position ?? new WorldPoint(0, 0, 0);
        WorldPoint nearbyPos = new(playerPos.X + 4f, playerPos.Y, playerPos.Z);
        long houseVictimId = AddCivilianNpc(session, site.Id, npcSite, "t11162/house-victim", "Lord Veren", houseFamily, "noble", nearbyPos);
        long houseKinId = AddCivilianNpc(session, site.Id, npcSite, "t11162/house-kin", "Lady Sira", houseFamily, "knight", nearbyPos);
        long witnessId = AddCivilianNpc(session, site.Id, npcSite, "t11162/house-witness", "Sergeant Hale", null, "guard", nearbyPos);

        int day = checked((int)session.Calendar.DayNumber);
        CommitMurder(session, fixture.Perception, houseVictimId, witnessId, session.Calendar.MinuteNumber);

        // Trigger kin confrontation AND overland vendetta pursuit band dispatch.
        Assert.Equal(1, session.FeudVendetta.EvaluateSceneKinConfrontations(site, playerPos, day));
        Assert.True(session.FeudVendetta.IsVendettaAggressor(houseKinId));

        IReadOnlyList<DaggerfallHostileVendettaStanding> overlandVendettas =
            session.FeudVendetta.ResolveOverlandVendettas(site.Id.Region, site);
        DaggerfallHostileVendettaStanding vendettaStanding = Assert.Single(overlandVendettas);
        Assert.Equal(houseFamily, vendettaStanding.FamilyId);
        Assert.Equal(144, vendettaStanding.PreferredMobileId); // Great House single murder -> Bounty Hunters

        // Advancing 12 hours dispatches an overland vendetta pursuit band via HostileOverlandBands.
        session.AdvanceElapsedTime(12L * 3600L, idleFatigue: false);
        Assert.Contains(
            session.Expedition.PersistentBands,
            b => b.BandId.StartsWith("hostile:vendetta-", StringComparison.Ordinal));

        // Mediating a truce immediately calms live kin aggressors and retires the overland pursuit band!
        Assert.True(session.SocialConsequences.RecordTruceMediated(houseFamily, "Magistrate Vane", day));
        Assert.False(session.FeudVendetta.IsVendettaAggressor(houseKinId));

        ActorState calmedKin = session.State.Actors.Get(houseKinId);
        Assert.False(calmedKin.Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);
        Assert.False(calmedKin.Actor.Get<ActorHarmPolicy>().IsCombatant);
        Assert.DoesNotContain(
            session.Expedition.PersistentBands,
            b => b.BandId.StartsWith("hostile:vendetta-", StringComparison.Ordinal));

        // While truce is mediated, neither kin confrontations nor hunters re-trigger.
        Assert.Equal(0, session.FeudVendetta.EvaluateSceneKinConfrontations(site, playerPos, day));
        Assert.Empty(session.FeudVendetta.ResolveOverlandVendettas(site.Id.Region, site));
        Assert.Empty(session.FeudVendetta.EvaluateSiteHunterDispatch(site, DaggerfallWorldProfileKind.Exterior, playerPos, day));

        // Settling the feud prunes sparse FeudVendetta save state completely (zero save bytes).
        Assert.True(session.SocialConsequences.ClearHouseholdGrudgeAndFeud(houseFamily, site, session.Calendar.MinuteNumber, day));
        Assert.Empty(session.FeudVendetta.Capture().Entries);
    }

    [Fact]
    public void Hired_hunters_scale_by_tier_respect_cooldown_and_cap_and_persist_across_save_load()
    {
        // 1. Tier / wealth scaling matrix:
        Assert.False(DaggerfallFeudVendetta.CanHireHunters(DaggerfallHouseholdScope.Local, murderCount: 1, settlementBlocks: 2));
        Assert.True(DaggerfallFeudVendetta.CanHireHunters(DaggerfallHouseholdScope.Local, murderCount: 1, settlementBlocks: 4));
        Assert.True(DaggerfallFeudVendetta.CanHireHunters(DaggerfallHouseholdScope.Local, murderCount: 2, settlementBlocks: 1));
        Assert.True(DaggerfallFeudVendetta.CanHireHunters(DaggerfallHouseholdScope.GreatHouse, murderCount: 1, settlementBlocks: 1));
        Assert.True(DaggerfallFeudVendetta.CanHireHunters(DaggerfallHouseholdScope.Dynasty, murderCount: 1, settlementBlocks: 1));

        var localTier = DaggerfallFeudVendetta.ResolveHunterTier(DaggerfallHouseholdScope.Local, murderCount: 1, settlementBlocks: 5);
        Assert.Equal(DaggerfallHostileSignalKind.HiredTough, localTier.Kind);
        Assert.Equal(138, localTier.PreferredMobileId);

        var houseTier1 = DaggerfallFeudVendetta.ResolveHunterTier(DaggerfallHouseholdScope.GreatHouse, murderCount: 1, settlementBlocks: 2);
        Assert.Equal(DaggerfallHostileSignalKind.BountyHunter, houseTier1.Kind);
        Assert.Equal(144, houseTier1.PreferredMobileId);

        var houseTier2 = DaggerfallFeudVendetta.ResolveHunterTier(DaggerfallHouseholdScope.GreatHouse, murderCount: 2, settlementBlocks: 2);
        Assert.Equal(DaggerfallHostileSignalKind.DarkBrotherhoodAssassin, houseTier2.Kind);
        Assert.Equal(139, houseTier2.PreferredMobileId);

        var dynastyTier = DaggerfallFeudVendetta.ResolveHunterTier(DaggerfallHouseholdScope.Dynasty, murderCount: 1, settlementBlocks: 2);
        Assert.Equal(DaggerfallHostileSignalKind.DarkBrotherhoodAssassin, dynastyTier.Kind);
        Assert.Equal(139, dynastyTier.PreferredMobileId);

        // 2. Untouched session writes zero FeudVendetta save entries; live royal dynasty blood feud dispatches Dark Brotherhood Assassins.
        using var fixture = new VendettaSessionFixture();
        DaggerfallSession session = fixture.Session;
        Assert.Empty(session.FeudVendetta.Capture().Entries);

        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/6/0", session.Sites.ActiveProfile.LogicalId);
        const string dynastyFamily = "fam:dynasty:daggerfall";
        WorldPoint playerPos = session.State.PlayerControl.Position ?? new WorldPoint(0, 0, 0);
        WorldPoint nearbyPos = new(playerPos.X + 4f, playerPos.Y, playerPos.Z);

        long royalVictimId = AddCivilianNpc(session, site.Id, npcSite, "t11162/royal-victim", "Lord Arthas", dynastyFamily, "noble", nearbyPos);
        long witnessId = AddCivilianNpc(session, site.Id, npcSite, "t11162/royal-witness", "Palace Guard", null, "guard", nearbyPos);
        CommitMurder(session, fixture.Perception, royalVictimId, witnessId, session.Calendar.MinuteNumber);

        List<DaggerfallHostileReasonSignal> signals = [];
        session.FeudVendetta.HostilitySignaled += signals.Add;

        int day = checked((int)session.Calendar.DayNumber);
        IReadOnlyList<long> wave1 = session.FeudVendetta.EvaluateSiteHunterDispatch(
            site, DaggerfallWorldProfileKind.Exterior, playerPos, day);

        Assert.Equal(DaggerfallSocialTuning.Default.MaxActiveVendettaHunters, wave1.Count);
        foreach (long hunterId in wave1)
        {
            Assert.True(session.FeudVendetta.IsVendettaAggressor(hunterId));
            Assert.True(session.State.Actors.Get(hunterId).Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);
        }

        DaggerfallHostileReasonSignal hunterSignal = Assert.Single(signals);
        Assert.Equal(DaggerfallHostileSignalKind.DarkBrotherhoodAssassin, hunterSignal.Kind);
        Assert.Contains("Dark Brotherhood", hunterSignal.ReasonLine, StringComparison.OrdinalIgnoreCase);

        // Active hunter cap prevents immediate second dispatch.
        Assert.Empty(session.FeudVendetta.EvaluateSiteHunterDispatch(site, DaggerfallWorldProfileKind.Exterior, playerPos, day));

        // Kill wave 1 hunters -> cooldown still blocks dispatch on day + 1, then allows wave 2 once cooldown elapses!
        foreach (long hunterId in wave1)
        {
            session.State.Actors.Get(hunterId).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
            session.FeudVendetta.ObserveActorDied(hunterId);
        }

        Assert.Empty(session.FeudVendetta.EvaluateSiteHunterDispatch(site, DaggerfallWorldProfileKind.Exterior, playerPos, day + 1));

        // Save & restore preserves wave count and LastDispatchDay so cooldown cannot be bypassed by reloading!
        RulesetSavePayload save = session.CaptureSave();
        using DaggerfallSession restored = fixture.Restore(save);
        DaggerfallFeudVendettaSave restoredVendetta = restored.FeudVendetta.Capture();
        DaggerfallFeudVendettaEntrySave savedEntry = Assert.Single(restoredVendetta.Entries);
        Assert.Equal(dynastyFamily, savedEntry.FamilyId);
        Assert.Equal(1, savedEntry.DispatchedWaveCount);
        Assert.Equal(day, savedEntry.LastDispatchDay);

        // Still on cooldown at day + 1 after load, then dispatches wave 2 on day + VendettaHunterCooldownDays!
        Assert.Empty(restored.FeudVendetta.EvaluateSiteHunterDispatch(
            restored.Site.ActiveSite, DaggerfallWorldProfileKind.Exterior, restored.State.PlayerControl.Position, day + 1));
        IReadOnlyList<long> wave2 = restored.FeudVendetta.EvaluateSiteHunterDispatch(
            restored.Site.ActiveSite, DaggerfallWorldProfileKind.Exterior, restored.State.PlayerControl.Position,
            day + DaggerfallSocialTuning.Default.VendettaHunterCooldownDays);
        Assert.Equal(DaggerfallSocialTuning.Default.MaxActiveVendettaHunters, wave2.Count);
        Assert.Equal(2, Assert.Single(restored.FeudVendetta.Capture().Entries).DispatchedWaveCount);
    }

    private static void CommitMurder(
        DaggerfallSession session,
        PerceptionFake perception,
        long victimId,
        long? witnessId,
        long minute)
    {
        PerceptionPair[] pairs = witnessId is long wId
            ? [new PerceptionPair(checked((ulong)wId), DaggerfallActorIdentity.PlayerEntityId, 2.0, 1.0, PerceptionPairKind.Visible, 1.0)]
            : [];
        perception.Responder = _ => new PerceptionReadoutResult(
            pairs,
            ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        ActorState victim = session.State.Actors.Get(victimId);
        victim.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(victimId, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            victim,
            minute);
    }

    private static long AddCivilianNpc(
        DaggerfallSession session,
        DaggerfallSiteId siteId,
        DaggerfallNpcSite binding,
        string slotId,
        string displayName,
        string? familyId,
        string role,
        WorldPoint position)
    {
        DaggerfallWorldProfileKey profile = session.Sites.ActiveProfile;
        string slotKey = $"{profile.LogicalId}/{slotId}";
        long id = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, slotKey);
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, Math.Abs(slotId.GetHashCode()), 0);
        session.State.Npcs.RegisterDeterministic(
            id, DaggerfallNpcKind.Civilian, slotId, binding, look, role, ["talk"],
            displayName: displayName, homeProfile: profile, familyId: familyId);
        DaggerfallNpc npc = session.State.Npcs.Require(id);
        session.Roster.MaterializeDeterministicCivilian(
            npc,
            ActorOrigin.Seeded(profile, slotId),
            position,
            new ActorPose(position, 0f),
            Vector3.Zero,
            familyId);
        session.State.Npcs.Bind(id, profile);
        return id;
    }

    private sealed class VendettaSessionFixture : IDisposable
    {
        private readonly DaggerfallSiteProfile _profile;
        private readonly List<string> _releases = [];
        private readonly ContentFake _content;
        private readonly SpatialFake _spatial;

        internal PerceptionFake Perception { get; } = PerceptionFake.Create();
        internal DaggerfallSession Session { get; }

        internal VendettaSessionFixture()
        {
            _profile = ReadInputs(TestData.RepositoryRoot);
            _content = new ContentFake(_releases);
            PopulateContent(_content, _profile);
            _spatial = SpatialFake.Create(_profile.Geometry.Spatial[0].Sha256, _releases);
            EngineContextFake engine = EngineContextFake.Create(_content, _spatial.Service, new AppearanceFake(_releases), Perception.Service);
            Session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, _profile, DaggerfallTuning.Defaults));
        }

        internal DaggerfallSession Restore(RulesetSavePayload save)
        {
            EngineContextFake engine = EngineContextFake.Create(_content, _spatial.Service, new AppearanceFake(_releases), Perception.Service);
            return DaggerfallSession.Restore(engine.Context, new(TestPayload.Definitions, _profile, DaggerfallTuning.Defaults), save);
        }

        public void Dispose() => Session.Dispose();
    }
}
