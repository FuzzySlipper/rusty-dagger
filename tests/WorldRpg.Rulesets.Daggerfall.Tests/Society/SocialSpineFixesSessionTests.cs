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
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Focused end-to-end verification of all seven social-spine fixes in Den #11046:
/// 1. Real WorldSocialSeed flowing through DaggerfallLocationAssembly, DaggerfallSiteProfiles, and street walkers.
/// 2. Thieves Guild (42) and Dark Brotherhood (108) membership granted on invitation-quest success.
/// 3. Killing faction members deducting faction reputation and recording witness memory in ReactToDeath.
/// 4. Service denial for wanted/banished characters at lawful providers, with criminal guild, fence/shady inn, and personal ally exemptions.
/// 5. Social reactions routing through FactionComponent instead of npc.Appearance.FactionId.
/// 6. ActorLocation.Dead persistence writers and hostile persistent overland bands (bounty hunters, guild hit squads, rival patrols).
/// 7. Regional overland traffic return-home lifecycle and home-slot self-pruning/reuse with zero AtSite orphan leak.
/// </summary>
public sealed class SocialSpineFixesSessionTests
{
    private static readonly Lazy<DaggerfallLocationAssembly> AssemblyFixture = new(() =>
    {
        ProductContent content = FullContent(TestData.RepositoryRoot);
        DaggerfallData data = TestSessions.Data(content);
        DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(data);
        Lazy<DaggerfallProductMedia> product = new(() => DaggerfallSiteContent.ReadProductMedia(data, media, TestPayload.Definitions));
        Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(data, media));
        return new DaggerfallLocationAssembly(TestPayload.Definitions, new DaggerfallWorldBlocks(data), () => product.Value, () => meshes.Value);
    });

    private static DaggerfallSiteRecord DaggerfallCity =>
        TestPayload.Definitions.Locations.Records.Single(site => site.Id.Region == 17 && site.Name == "Daggerfall" && site.Exterior is not null);

    private static (int X, int Z)[] AllClear(DaggerfallSiteRecord site) =>
        [.. site.Exterior!.Blocks.SelectMany(block => Enumerable.Range(0, 64 * 64).Select(cell => (block.X * 64 + cell % 64, block.Y * 64 + cell / 64)))];

    [Fact]
    public void Fix1_WorldSocialSeed_varies_walkers_and_assembled_profiles_and_persists_across_save_reload()
    {
        DaggerfallSiteRecord town = DaggerfallCity;
        (int X, int Z)[] clear = AllClear(town);

        IReadOnlyList<DaggerfallPopulationPlacement> seedA = DaggerfallExteriorPopulation.Create(
            TestPayload.Definitions, town, clear, gateAnchor: null, worldSocialSeed: 11111UL);
        IReadOnlyList<DaggerfallPopulationPlacement> seedARepeat = DaggerfallExteriorPopulation.Create(
            TestPayload.Definitions, town, clear, gateAnchor: null, worldSocialSeed: 11111UL);
        IReadOnlyList<DaggerfallPopulationPlacement> seedB = DaggerfallExteriorPopulation.Create(
            TestPayload.Definitions, town, clear, gateAnchor: null, worldSocialSeed: 99999UL);

        Assert.NotEmpty(seedA);
        Assert.Equal(seedA.Select(p => (p.NameSeed, p.BillboardArchive, p.BillboardRecord)),
            seedARepeat.Select(p => (p.NameSeed, p.BillboardArchive, p.BillboardRecord)));
        Assert.NotEqual(seedA.Select(p => p.NameSeed), seedB.Select(p => p.NameSeed));

        // DaggerfallSiteProfiles.ForSession assembles distinct seeded profiles per WorldSocialSeed,
        // and a session's Tuning.Social.WorldSocialSeed survives save/reload.
        DaggerfallSiteProfiles catalog = DaggerfallSiteProfiles.Resolve(AssemblyFixture.Value, admit: null);
        DaggerfallWorldProfileKey charingExt = DaggerfallWorldProfileIds.Exterior(Charing);
        DaggerfallSiteProfile seededProfileA = catalog.ForSession(worldSocialSeed: 424242UL).Require(charingExt);
        DaggerfallSiteProfile seededProfileB = catalog.ForSession(worldSocialSeed: 777777UL).Require(charingExt);
        Assert.NotEmpty(seededProfileA.Population);
        Assert.NotEqual(
            seededProfileA.Population.Select(p => p.NameSeed),
            seededProfileB.Population.Select(p => p.NameSeed));

        DaggerfallTuning customTuning = DaggerfallTuning.Defaults with { Social = new DaggerfallSocialTuning(424242UL) };
        DaggerfallSessionComposition composition = new(TestPayload.Definitions, seededProfileA, customTuning)
        {
            Profiles = new DaggerfallSiteProfiles([seededProfileA]),
        };
        EngineContextFake engineA = SpineFixture.BuildEngine(seededProfileA);
        using DaggerfallSession session = DaggerfallSession.StartNew(engineA.Context, composition);
        Assert.Equal(424242UL, session.WorldSocialSeed);

        RulesetSavePayload saved = session.CaptureSave();
        Assert.Equal(424242UL, DaggerfallSavePayload.Read(saved).WorldSocialSeed);

        EngineContextFake engineB = SpineFixture.BuildEngine(seededProfileA);
        using DaggerfallSession restored = DaggerfallSession.Restore(engineB.Context, composition with { Tuning = DaggerfallTuning.Defaults }, saved);
        Assert.Equal(424242UL, restored.WorldSocialSeed);
    }

    [Fact]
    public void Fix2_Criminal_guild_invitation_quests_grant_membership_on_success_not_failure_and_persist()
    {
        using SpineFixture failFixture = SpineFixture.Create();
        DaggerfallSession failSession = failFixture.Session;
        failSession.ApplyProductMode(ProductMode.Playing);

        failSession.State.Quests.Start(new(
            "crime-invitation:O0A0AL00", "O0A0AL00.txt", "O0A0AL00",
            DaggerfallQuestLifecycle.Active, null, [], [])
        { FactionId = DaggerfallConcreteGuildCatalog.ThievesFactionId });
        failSession.State.Quests.Fail("crime-invitation:O0A0AL00", "timed out");
        Assert.False(failSession.State.Social.GuildEligibility(DaggerfallConcreteGuildCatalog.ThievesFactionId).IsMember);

        using SpineFixture successFixture = SpineFixture.Create();
        DaggerfallSession ok = successFixture.Session;
        ok.ApplyProductMode(ProductMode.Playing);

        ok.State.Quests.Start(new(
            "crime-invitation:O0A0AL00", "O0A0AL00.txt", "O0A0AL00",
            DaggerfallQuestLifecycle.Active, null, [], [])
        { FactionId = DaggerfallConcreteGuildCatalog.ThievesFactionId });
        ok.State.Quests.Complete("crime-invitation:O0A0AL00", "passed qualifying examination");

        ok.State.Quests.Start(new(
            "crime-invitation:L0A01L00", "L0A01L00.txt", "L0A01L00",
            DaggerfallQuestLifecycle.Active, null, [], [])
        { FactionId = DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId });
        ok.State.Quests.Complete("crime-invitation:L0A01L00", "passed acceptance test");

        DaggerfallGuildEligibility thieves = ok.State.Social.GuildEligibility(DaggerfallConcreteGuildCatalog.ThievesFactionId);
        DaggerfallGuildEligibility darkBrotherhood = ok.State.Social.GuildEligibility(DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId);
        Assert.True(thieves.IsMember);
        Assert.Equal(0, thieves.Rank);
        Assert.True(darkBrotherhood.IsMember);
        Assert.Equal(0, darkBrotherhood.Rank);

        RulesetSavePayload saved = ok.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(successFixture.CreateEngine().Context, successFixture.Composition, saved);
        Assert.True(restored.State.Social.GuildEligibility(DaggerfallConcreteGuildCatalog.ThievesFactionId).IsMember);
        Assert.True(restored.State.Social.GuildEligibility(DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId).IsMember);
    }

    [Fact]
    public void Fix3_and_Fix5_FactionComponent_drives_social_reactions_and_killing_faction_member_costs_reputation_and_witness_memory()
    {
        using SpineFixture fixture = SpineFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.ApplyProductMode(ProductMode.Playing);
        session.AdvanceElapsedTime(8 * 60 * 60);

        DaggerfallNpc[] townsfolk = [.. session.State.Npcs.All
            .Where(npc => npc.Kind == DaggerfallNpcKind.Civilian
                && npc.Profile == fixture.Origin.ProfileKey
                && !string.Equals(npc.Role, "guard", StringComparison.OrdinalIgnoreCase)
                && DaggerfallPeopleSlots.IsDeterministicId(npc.DurableId)
                && session.State.Actors.TryGet(npc.DurableId, out _))
            .OrderBy(npc => npc.DurableId)
            .Take(2)];
        Assert.Equal(2, townsfolk.Length);

        DaggerfallNpc victimNpc = townsfolk[0];
        DaggerfallNpc witnessNpc = townsfolk[1];
        ActorState victimActor = session.State.Actors.Get(victimNpc.DurableId);
        ActorState witnessActor = session.State.Actors.Get(witnessNpc.DurableId);

        // Fix 5: FactionComponent.PrimaryFactionId (Mages Guild = 40) drives ReactionForNpc over Appearance.FactionId.
        int regionalPeopleFaction = victimNpc.Appearance.FactionId;
        int magesGuild = DaggerfallConcreteGuildCatalog.MagesFactionId;
        Assert.NotEqual(magesGuild, regionalPeopleFaction);
        victimActor.Actor.Get<FactionComponent>().SetPeopleFactions(magesGuild, regionalPeopleFaction);

        int magesBefore = session.State.Social.FactionReputation(magesGuild);
        session.State.Social.ChangeFactionReputation(magesGuild, 35, DaggerfallFactionReputationChange.Direct);
        DaggerfallFactionReaction reaction = session.State.Social.ReactionForNpc(victimNpc);
        Assert.Equal(magesGuild, reaction.FactionId);
        Assert.Equal(magesBefore + 35, reaction.FactionReputation);

        // Place living witness nearby and configure PerceptionFake so QueryWitnesses sees them cleanly.
        WorldPoint playerPos = session.State.PlayerControl.Position!.Value;
        WorldPoint witnessPos = new(playerPos.X, playerPos.Y, playerPos.Z - 3.0f);
        float yawTowardPlayer = MathF.Atan2(playerPos.X - witnessPos.X, -(playerPos.Z - witnessPos.Z));
        witnessActor.ApplyPose(new ActorPose(witnessPos, yawTowardPlayer));
        fixture.Perception.Responder = req => new PerceptionReadoutResult(
            new[] { new PerceptionPair(checked((ulong)witnessNpc.DurableId), DaggerfallActorIdentity.PlayerEntityId, 3.0, 1.0, PerceptionPairKind.Visible, 1.0) },
            ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        int magesPreKill = session.State.Social.FactionReputation(magesGuild);
        int regionalPreKill = session.State.Social.FactionReputation(regionalPeopleFaction);

        // Fix 3: Player kills the faction member -> primary faction rep drops, regional people rep drops, witness remembers.
        victimActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(victimNpc.DurableId, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            victimActor,
            minute: 100L);

        Assert.True(session.State.Social.FactionReputation(magesGuild) <= magesPreKill + DaggerfallSocialConsequences.PrimaryFactionKillPenalty);
        Assert.True(session.State.Social.FactionReputation(regionalPeopleFaction) <= regionalPreKill + DaggerfallSocialConsequences.RegionalPeopleKillPenalty);

        ActorOverlayComponent witnessOverlay = witnessActor.Actor.Get<ActorOverlayComponent>();
        Assert.Contains(witnessOverlay.WitnessedCrimes, id => id.StartsWith($"murder:{victimNpc.DurableId}:", StringComparison.Ordinal));
        Assert.True(session.State.ActorLedger.TryGet(witnessNpc.DurableId, out ActorLedgerEntry? witnessLedger));
        Assert.True(witnessLedger!.Social!.DispositionDelta <= DaggerfallSocialConsequences.WitnessMurderDispositionPenalty);

        int magesAfterKill = session.State.Social.FactionReputation(magesGuild);
        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, saved);
        Assert.Equal(magesAfterKill, restored.State.Social.FactionReputation(magesGuild));
        ActorOverlayComponent restoredWitnessOverlay = restored.State.Actors.Get(witnessNpc.DurableId).Actor.Get<ActorOverlayComponent>();
        Assert.Contains(restoredWitnessOverlay.WitnessedCrimes, id => id.StartsWith($"murder:{victimNpc.DurableId}:", StringComparison.Ordinal));
        Assert.True(restored.State.ActorLedger.TryGet(witnessNpc.DurableId, out ActorLedgerEntry? restoredWitnessLedger));
        Assert.True(restoredWitnessLedger!.Social!.DispositionDelta <= DaggerfallSocialConsequences.WitnessMurderDispositionPenalty);
    }

    [Fact]
    public void Fix4_Wanted_and_banished_players_denied_at_lawful_services_but_exempt_for_criminal_guilds_fences_shady_inns_and_personal_allies()
    {
        using SpineFixture fixture = SpineFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.ApplyProductMode(ProductMode.Playing);

        DaggerfallSiteRecord originRecord = TestPayload.Definitions.Locations.Records.Single(r => r.Id == fixture.Origin.Site!.Value);
        int region = originRecord.Id.Region;
        DaggerfallNpcSite currentSite = new(region, originRecord.Name, string.Empty, fixture.Origin.ProfileKey.LogicalId);

        long RegisterProvider(string key, int factionId, string role, params string[] services)
        {
            long id = DaggerfallPeopleSlots.SeededSlotIdentity(originRecord.Id, $"test-provider/{key}");
            session.State.Npcs.RegisterDeterministic(
                id, DaggerfallNpcKind.Static, key, currentSite,
                new DaggerfallNpcAppearance("breton", "Male", 210, 4, 1, factionId),
                role, services, key, fixture.Origin.ProfileKey);
            return id;
        }

        long lawfulBankerId = RegisterProvider("lawful-banker", 0, "banker", "banking");
        long lawfulShopId = RegisterProvider("lawful-shop", 0, "merchant", "shop", "buy-items");
        long lawfulGuildId = RegisterProvider("lawful-mages", DaggerfallConcreteGuildCatalog.MagesFactionId, "guild-trainer", "training");
        long thievesProviderId = RegisterProvider("thieves-fence", DaggerfallConcreteGuildCatalog.ThievesFactionId, "thief", "shop", "training");
        long darkBrotherhoodId = RegisterProvider("db-contact", DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId, "assassin", "training");
        long fenceId = RegisterProvider("alley-fence", 0, "fence", "shop", "fence");
        long allyShopId = RegisterProvider("ally-shop", 0, "merchant", "shop");

        Assert.Equal(DaggerfallServiceDenial.None, session.State.Services.ProviderAvailable(new(lawfulBankerId, currentSite, "banking")));
        Assert.Equal(DaggerfallServiceDenial.None, session.State.Services.ProviderAvailable(new(lawfulShopId, currentSite, "shop")));
        Assert.Equal(DaggerfallServiceDenial.None, session.State.Services.ProviderAvailable(new(lawfulGuildId, currentSite, "training")));
        Assert.True(session.Banking.Offers(session.State.Npcs.Require(lawfulBankerId)));

        DaggerfallNpc allyNpc = session.State.Npcs.Require(allyShopId);
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            allyShopId,
            ActorOrigin.Seeded(fixture.Origin.ProfileKey, "test-provider/ally-shop"),
            Social: ActorSocialDiff.FromNpc(allyNpc) with { Met = true, DispositionDelta = DaggerfallLegalServicePolicy.PersonalAllyDispositionThreshold }));

        session.State.Social.ChangeRegionalReputation(region, -25);
        Assert.True(session.LegalServices.IsWantedOrBanished(region));

        Assert.Equal(DaggerfallServiceDenial.WantedOrBanished, session.State.Services.ProviderAvailable(new(lawfulBankerId, currentSite, "banking")));
        Assert.Equal(DaggerfallServiceDenial.WantedOrBanished, session.State.Services.ProviderAvailable(new(lawfulShopId, currentSite, "shop")));
        Assert.Equal(DaggerfallServiceDenial.WantedOrBanished, session.State.Services.ProviderAvailable(new(lawfulGuildId, currentSite, "training")));
        Assert.False(session.Banking.Offers(session.State.Npcs.Require(lawfulBankerId)));
        Assert.Equal("I do not deal with wanted outlaws.", DaggerfallServiceOutcomeText.Common(nameof(DaggerfallServiceDenial.WantedOrBanished)));

        Assert.Equal(DaggerfallServiceDenial.None, session.State.Services.ProviderAvailable(new(thievesProviderId, currentSite, "shop")));
        Assert.Equal(DaggerfallServiceDenial.None, session.State.Services.ProviderAvailable(new(darkBrotherhoodId, currentSite, "training")));
        Assert.Equal(DaggerfallServiceDenial.None, session.State.Services.ProviderAvailable(new(fenceId, currentSite, "shop")));
        Assert.Equal(DaggerfallServiceDenial.None, session.State.Services.ProviderAvailable(new(allyShopId, currentSite, "shop")));

        DaggerfallInteriorBuilding pawnShop = new(0, 0, new("TEST", 0), 12, 0);
        DaggerfallInteriorBuilding shadyInn = new(0, 0, new("TEST", 1), 15, 0);
        DaggerfallInteriorBuilding nobleInn = new(0, 0, new("TEST", 2), 15, 0);
        DaggerfallLegalServicePolicy policy = new(
            session.State.Social, session.State.Crime, session.State.ActorLedger, TestPayload.Definitions,
            _ => null,
            () => null,
            b => b.Building.Index == 1 ? 5 : 16);
        DaggerfallNpc genericInnkeeper = session.State.Npcs.Require(lawfulShopId);
        Assert.True(policy.IsOutlawExempt(genericInnkeeper, pawnShop));
        Assert.True(policy.IsOutlawExempt(genericInnkeeper, shadyInn));
        Assert.False(policy.IsOutlawExempt(genericInnkeeper, nobleInn));
        Assert.False(policy.IsRoomRentalDenied(region, shadyInn));
        Assert.True(policy.IsRoomRentalDenied(region, nobleInn));

        // Banished via court resolution denies lawful service even if legal reputation is reset to 0.
        session.State.Social.ChangeRegionalReputation(region, 25);
        Assert.Equal(0, session.State.Social.RegionalReputation(region));
        string opId = "test-banish-op";
        session.State.Crime.RecordIncident(new(
            opId, DaggerfallCrimeKind.HighTreason, DaggerfallCrimeStage.Completed,
            DaggerfallActorIdentity.PlayerEntityId, null, region, 100L,
            DaggerfallCrimeTargetKind.Unknown, DaggerfallCrimeWitnessEvidence.NotQueried,
            DaggerfallCrimeGuildCredit.None, Reported: true));
        session.State.Crime.SetResponse(new(
            opId, region, [opId], DaggerfallLegalPhase.Court,
            DaggerfallWorldProfileKeySave.Capture(fixture.Origin.ProfileKey), 0, [],
            Penalty: new(100, 2, true)));
        session.State.Crime.ResolveResponse(region, DaggerfallCourtOutcome.Banished, 101L);
        Assert.Contains(region, session.State.Crime.BanishedRegions);
        Assert.Equal(DaggerfallServiceDenial.WantedOrBanished, session.State.Services.ProviderAvailable(new(lawfulShopId, currentSite, "shop")));
        Assert.Equal(DaggerfallServiceDenial.None, session.State.Services.ProviderAvailable(new(allyShopId, currentSite, "shop")));
    }

    [Fact]
    public void Fix6_ActorLocation_Dead_persists_and_hostile_overland_bands_pursue_materialize_and_cleanup()
    {
        using SpineFixture fixture = SpineFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.ApplyProductMode(ProductMode.Playing);
        session.AdvanceElapsedTime(8 * 60 * 60);

        DaggerfallNpc victim = session.State.Npcs.All
            .First(npc => npc.Kind == DaggerfallNpcKind.Civilian
                && npc.Profile == fixture.Origin.ProfileKey
                && !string.Equals(npc.Role, "guard", StringComparison.OrdinalIgnoreCase)
                && DaggerfallPeopleSlots.IsDeterministicId(npc.DurableId)
                && session.State.Actors.TryGet(npc.DurableId, out _));
        ActorState victimActor = session.State.Actors.Get(victim.DurableId);
        victimActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);

        RulesetSavePayload deadSave = session.CaptureSave();
        ActorLedgerEntry savedDead = Assert.Single(DaggerfallSavePayload.Read(deadSave).ActorLedger, e => e.DurableId == victim.DurableId);
        Assert.Equal(ActorLocationKind.Dead, savedDead.Location?.Kind);

        using DaggerfallSession deadRestored = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, deadSave);
        Assert.True(deadRestored.State.ActorLedger.TryGet(victim.DurableId, out ActorLedgerEntry? restoredEntry));
        Assert.Equal(ActorLocationKind.Dead, restoredEntry!.Location?.Kind);
        Assert.True(deadRestored.State.Actors.TryGet(victim.DurableId, out ActorState? restoredVictim));
        Assert.True(restoredVictim!.IsDefeated);

        session.OpenExpedition();
        (double X, double Y) partyPos = session.Expedition.Position;
        Assert.Null(session.HostileOverlandBands.TryDispatchWindow(500L, partyPos));

        int region = fixture.Origin.Site!.Value.Region;
        session.State.Social.ChangeRegionalReputation(region, -25);
        DaggerfallPersistentOverlandBand? bountyBand = session.HostileOverlandBands.TryDispatchWindow(501L, partyPos);
        Assert.NotNull(bountyBand);
        Assert.True(bountyBand!.Hostile);
        Assert.Equal(DaggerfallBandKind.Bandits, bountyBand.Kind);
        Assert.StartsWith("hostile:bounty:", bountyBand.BandId, StringComparison.Ordinal);

        IReadOnlyList<ActorLedgerEntry> hunters = session.State.ActorLedger.EntriesOnOverlandBand(bountyBand.BandId);
        Assert.Equal(2, hunters.Count);
        Assert.All(hunters, h =>
        {
            Assert.Equal(ActorOriginKind.Minted, h.Origin.Kind);
            Assert.True(h.Combat?.ForcedHostile);
        });

        RulesetSavePayload hostileSave = session.CaptureSave();
        using DaggerfallSession hostileRestored = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, hostileSave);
        DaggerfallPersistentOverlandBand restoredBounty = Assert.Single(hostileRestored.Expedition.PersistentBands, b => b.BandId == bountyBand.BandId);
        hostileRestored.OpenExpedition();
        hostileRestored.OverlandParties.OnMeetPersistentBand(restoredBounty, hostileRestored.Expedition.Party, DaggerfallEncounterStaging.Caught);
        hostileRestored.EnterExpedition();

        foreach (ActorLedgerEntry hunter in hunters)
        {
            Assert.True(hostileRestored.State.Actors.TryGet(hunter.DurableId, out ActorState? liveHunter));
            Assert.True(liveHunter!.Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);
        }

        using SpineFixture guildFixture = SpineFixture.Create();
        DaggerfallSession gs = guildFixture.Session;
        gs.ApplyProductMode(ProductMode.Playing);
        gs.OpenExpedition();
        gs.State.Social.ChangeFactionReputation(DaggerfallConcreteGuildCatalog.DarkBrotherhoodFactionId, -35, DaggerfallFactionReputationChange.Direct);
        DaggerfallPersistentOverlandBand? hitSquad = gs.HostileOverlandBands.TryDispatchWindow(502L, gs.Expedition.Position);
        Assert.NotNull(hitSquad);
        Assert.StartsWith("hostile:hitsquad-108:", hitSquad!.BandId, StringComparison.Ordinal);
        long[] hitIds = [.. gs.State.ActorLedger.EntriesOnOverlandBand(hitSquad.BandId).Select(e => e.DurableId)];
        Assert.Equal(2, hitIds.Length);

        DaggerfallPersistentOverlandBand offScreen = new(
            hitSquad.BandId, hitSquad.DisplayId, hitSquad.Name, hitSquad.Kind, hitSquad.X, hitSquad.Y, hitSquad.Heading,
            hitSquad.SpeedPixelsPerHour, hitSquad.Destination,
            DaggerfallWorldProfileKeySave.Capture(guildFixture.Destination.ProfileKey),
            camp: false, spotted: false, hostile: true, mobile: hitSquad.Mobile);
        _ = gs.OverlandParties.OnPersistentBandArrived(offScreen);
        foreach (long id in hitIds)
            Assert.False(gs.State.ActorLedger.TryGet(id, out _));
    }

    [Fact]
    public void Fix7_Overland_traffic_returns_home_self_prunes_untouched_slots_and_preserves_player_deltas()
    {
        using SpineFixture fixture = SpineFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.ApplyProductMode(ProductMode.Playing);
        session.AdvanceElapsedTime(8 * 60 * 60);

        DaggerfallPersistentOverlandBand? outbound = session.OverlandTraffic.TryDispatchWindow(201L, session.Expedition.Position);
        Assert.NotNull(outbound);
        ActorLedgerEntry outboundMember = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(outbound!.BandId));
        long memberId = outboundMember.DurableId;

        _ = session.OverlandParties.OnPersistentBandArrived(outbound);
        string returnBandId = outbound.BandId + DaggerfallOverlandTraffic.ReturnBandSuffix;
        DaggerfallPersistentOverlandBand returnBand = Assert.Single(session.Expedition.PersistentBands, b => b.BandId == returnBandId);
        Assert.Equal(outboundMember.Origin.SiteProfile!.Require(), returnBand.DestinationProfile!.Require());
        Assert.True(session.State.ActorLedger.TryGet(memberId, out ActorLedgerEntry? returningEntry));
        Assert.Equal(ActorLocationKind.OnOverlandBand, returningEntry!.Location?.Kind);
        Assert.Equal(returnBandId, returningEntry.Location?.BandId);

        _ = session.OverlandParties.OnPersistentBandArrived(returnBand);
        Assert.False(session.State.ActorLedger.TryGet(memberId, out _),
            "Untouched returning traffic slot must self-prune back to zero ActorLedger entries.");

        DaggerfallPersistentOverlandBand? outbound2 = session.OverlandTraffic.TryDispatchWindow(203L, session.Expedition.Position);
        Assert.NotNull(outbound2);
        ActorLedgerEntry member2 = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(outbound2!.BandId));
        session.State.ActorLedger.Upsert(member2 with
        {
            Social = member2.Social! with { Met = true, DispositionDelta = 14 },
        });
        _ = session.OverlandParties.OnPersistentBandArrived(outbound2);
        DaggerfallPersistentOverlandBand returnBand2 = Assert.Single(session.Expedition.PersistentBands,
            b => b.BandId == outbound2.BandId + DaggerfallOverlandTraffic.ReturnBandSuffix);
        _ = session.OverlandParties.OnPersistentBandArrived(returnBand2);

        Assert.True(session.State.ActorLedger.TryGet(member2.DurableId, out ActorLedgerEntry? returnedWithDelta));
        Assert.Null(returnedWithDelta!.Location);
        Assert.True(returnedWithDelta.Social!.Met);
        Assert.Equal(14, returnedWithDelta.Social.DispositionDelta);
    }

    [Fact]
    public void Fix7_Active_site_arrivals_and_encountered_traffic_retain_return_lifecycle_across_unload_and_save_reload()
    {
        // Part A: Active-site visible arrival -> save/reload -> player delta -> site transition returns home without AtSite orphan.
        using (SpineFixture fixture = SpineFixture.Create())
        {
            DaggerfallSession session = fixture.Session;
            session.ApplyProductMode(ProductMode.Playing);
            session.AdvanceElapsedTime(8 * 60 * 60);

            DaggerfallPersistentOverlandBand? outbound = session.OverlandTraffic.TryDispatchWindow(301L, session.Expedition.Position);
            Assert.NotNull(outbound);
            ActorLedgerEntry member = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(outbound!.BandId));
            long memberId = member.DurableId;

            // Force arrival at the player's currently active exterior profile so the traveller materializes visibly.
            DaggerfallPersistentOverlandBand visibleArrival = new(
                outbound.BandId, outbound.DisplayId, outbound.Name, outbound.Kind, outbound.X, outbound.Y, outbound.Heading,
                outbound.SpeedPixelsPerHour, outbound.Destination,
                DaggerfallWorldProfileKeySave.Capture(fixture.Origin.ProfileKey),
                camp: false, spotted: false, hostile: false, mobile: outbound.Mobile);
            _ = session.OverlandParties.OnPersistentBandArrived(visibleArrival);
            Assert.True(session.State.Actors.TryGet(memberId, out ActorState? liveVisitor));
            Assert.True(session.State.ActorLedger.TryGet(memberId, out ActorLedgerEntry? visitorEntry));
            Assert.True(visitorEntry!.Location?.TrafficVisitor);

            // Record player interaction (Met + DispositionDelta) and save/reload while visitor is still present.
            liveVisitor!.Actor.Get<ActorOverlayComponent>().Met = true;
            session.State.ActorLedger.Upsert(visitorEntry with
            {
                Social = visitorEntry.Social! with { Met = true, DispositionDelta = 9 },
            });
            RulesetSavePayload savedMidVisit = session.CaptureSave();
            using DaggerfallSession restored = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, savedMidVisit);
            Assert.True(restored.State.Actors.TryGet(memberId, out _));
            Assert.True(restored.State.ActorLedger.TryGet(memberId, out ActorLedgerEntry? restoredVisitor));
            Assert.True(restoredVisitor!.Location?.TrafficVisitor);

            // Leaving the site via TryTransitionTo releases the visitor home: Location clears to null and deltas survive.
            Assert.True(restored.TryTransitionTo(fixture.Destination.ProfileKey));
            Assert.False(restored.State.Actors.TryGet(memberId, out _));
            Assert.True(restored.State.ActorLedger.TryGet(memberId, out ActorLedgerEntry? afterDepart));
            Assert.Null(afterDepart!.Location);
            Assert.True(afterDepart.Social!.Met);
            Assert.Equal(9, afterDepart.Social.DispositionDelta);
        }

        // Part B: Player encounter with untouched traffic -> save/reload -> OpenExpedition dispatches :return leg -> self-prunes to 0 entries.
        using (SpineFixture encounterFixture = SpineFixture.Create())
        {
            DaggerfallSession session = encounterFixture.Session;
            session.ApplyProductMode(ProductMode.Playing);
            session.AdvanceElapsedTime(8 * 60 * 60);
            session.OpenExpedition();

            DaggerfallPersistentOverlandBand? outbound = session.OverlandTraffic.TryDispatchWindow(305L, session.Expedition.Position);
            Assert.NotNull(outbound);
            ActorLedgerEntry member = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(outbound!.BandId));
            long memberId = member.DurableId;

            session.OverlandParties.OnMeetPersistentBand(outbound, session.Expedition.Party, DaggerfallEncounterStaging.Travelling);
            session.EnterExpedition();
            Assert.True(session.State.Actors.TryGet(memberId, out _));
            Assert.True(session.State.ActorLedger.TryGet(memberId, out ActorLedgerEntry? metEntry));
            Assert.True(metEntry!.Location?.TrafficVisitor);

            // Save/reload while standing in the encounter scene, then open expedition: a :return band is dispatched.
            RulesetSavePayload savedEncounter = session.CaptureSave();
            using DaggerfallSession restored = DaggerfallSession.Restore(encounterFixture.CreateEngine().Context, encounterFixture.Composition, savedEncounter);
            Assert.True(restored.State.Actors.TryGet(memberId, out _));
            restored.OpenExpedition();
            Assert.False(restored.State.Actors.TryGet(memberId, out _));

            DaggerfallPersistentOverlandBand returnBand = Assert.Single(restored.Expedition.PersistentBands,
                b => b.BandId.StartsWith(DaggerfallOverlandTraffic.BandIdPrefix, StringComparison.Ordinal)
                    && b.BandId.EndsWith(DaggerfallOverlandTraffic.ReturnBandSuffix, StringComparison.Ordinal));
            Assert.Single(restored.State.ActorLedger.EntriesOnOverlandBand(returnBand.BandId), e => e.DurableId == memberId);

            // When the return band reaches home, the untouched traveller self-prunes back to zero ActorLedger bytes.
            _ = restored.OverlandParties.OnPersistentBandArrived(returnBand);
            Assert.False(restored.State.ActorLedger.TryGet(memberId, out _));
        }
    }

    [Fact]
    public void Fix6_Dead_relocated_actor_suppresses_home_slot_and_stays_corpse_at_death_site_across_transitions_and_save_reload()
    {
        using SpineFixture fixture = SpineFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.ApplyProductMode(ProductMode.Playing);
        session.AdvanceElapsedTime(8 * 60 * 60);

        DaggerfallNpc traveller = session.State.Npcs.All
            .First(npc => npc.Kind == DaggerfallNpcKind.Civilian
                && npc.Profile == fixture.Origin.ProfileKey
                && !string.Equals(npc.Role, "guard", StringComparison.OrdinalIgnoreCase)
                && DaggerfallPeopleSlots.IsDeterministicId(npc.DurableId)
                && session.State.Actors.TryGet(npc.DurableId, out _));
        long actorId = traveller.DurableId;

        // Relocate the seeded townsperson from home A (Origin) to site B (Destination).
        session.OverlandParties.JoinParty(actorId, ActorPartyRole.Companion);
        Assert.True(session.TryTransitionTo(fixture.Destination.ProfileKey));
        WorldPoint deathLocalPose = new(9.5f, 1.0f, -6.25f);
        session.OverlandParties.ReleaseFromParty(actorId, fixture.Destination.ProfileKey, deathLocalPose);
        Assert.True(session.State.Actors.TryGet(actorId, out ActorState? atB));

        // Kill the relocated actor at site B and record death at B.
        atB!.ApplyPose(new ActorPose(deathLocalPose, 1.25f));
        atB.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        RulesetSavePayload saveAtB = session.CaptureSave();
        ActorLedgerEntry deadAtBEntry = Assert.Single(DaggerfallSavePayload.Read(saveAtB).ActorLedger, e => e.DurableId == actorId);
        Assert.Equal(ActorLocationKind.Dead, deadAtBEntry.Location?.Kind);
        Assert.Equal(fixture.Destination.ProfileKey, deadAtBEntry.Location!.Profile!.Require());

        // Transition from B -> home A: home slot A MUST be suppressed; no corpse or clone appears at A.
        Assert.True(session.TryTransitionTo(fixture.Origin.ProfileKey));
        Assert.True(session.State.ActorLedger.IsHomeSlotSuppressed(actorId, fixture.Origin.ProfileKey));
        Assert.False(session.State.Actors.TryGet(actorId, out _));

        // Save and reload while at home A: still suppressed at A and ledger still names death profile B.
        RulesetSavePayload saveAtA = session.CaptureSave();
        using DaggerfallSession restoredAtA = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, saveAtA);
        Assert.True(restoredAtA.State.ActorLedger.IsHomeSlotSuppressed(actorId, fixture.Origin.ProfileKey));
        Assert.False(restoredAtA.State.Actors.TryGet(actorId, out _));
        Assert.True(restoredAtA.State.ActorLedger.TryGet(actorId, out ActorLedgerEntry? ledgerAtA));
        Assert.Equal(ActorLocationKind.Dead, ledgerAtA!.Location?.Kind);
        Assert.Equal(fixture.Destination.ProfileKey, ledgerAtA.Location!.Profile!.Require());

        // Revisit site B: corpse MUST be admitted at B (defeated, at its B-local death pose), and survive save/reload at B.
        Assert.True(restoredAtA.TryTransitionTo(fixture.Destination.ProfileKey));
        Assert.Contains(restoredAtA.State.ActorLedger.EntriesAtSite(fixture.Destination.ProfileKey), e => e.DurableId == actorId);
        Assert.True(restoredAtA.State.Actors.TryGet(actorId, out ActorState? corpseAtB));
        Assert.True(corpseAtB!.IsDefeated);
        Assert.Equal(deathLocalPose.X, corpseAtB.Position.X, precision: 2);
        Assert.Equal(deathLocalPose.Z, corpseAtB.Position.Z, precision: 2);

        RulesetSavePayload saveBackAtB = restoredAtA.CaptureSave();
        using DaggerfallSession restoredBackAtB = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, saveBackAtB);
        Assert.True(restoredBackAtB.State.Actors.TryGet(actorId, out ActorState? reloadedCorpse));
        Assert.True(reloadedCorpse!.IsDefeated);
        Assert.True(restoredBackAtB.State.ActorLedger.TryGet(actorId, out ActorLedgerEntry? finalEntry));
        Assert.Equal(ActorLocationKind.Dead, finalEntry!.Location?.Kind);
        Assert.Equal(fixture.Destination.ProfileKey, finalEntry.Location!.Profile!.Require());
    }

    private sealed class SpineFixture : IDisposable
    {
        private static readonly Lazy<DaggerfallBlocksSnapshot> SharedBlocks = new(() =>
            DaggerfallBlocksContent.Read(TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json"))));

        internal DaggerfallSiteProfile Origin { get; }
        internal DaggerfallSiteProfile Destination { get; }
        internal DaggerfallSessionComposition Composition { get; }
        internal DaggerfallSession Session { get; }
        internal PerceptionFake Perception { get; }

        private SpineFixture(DaggerfallSiteProfile origin, DaggerfallSiteProfile destination,
            DaggerfallSessionComposition composition, DaggerfallSession session, PerceptionFake perception)
        {
            Origin = origin;
            Destination = destination;
            Composition = composition;
            Session = session;
            Perception = perception;
        }

        internal static SpineFixture Create()
        {
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            DaggerfallSiteRecord originRecord = definitions.Locations.Records.Single(r => r.Id == Charing);
            DaggerfallSiteRecord destRecord = definitions.Locations.Records.First(r =>
                r.Exterior is not null
                && r.Id != originRecord.Id
                && r.Id.Region == originRecord.Id.Region
                && Math.Abs(r.MapPixelX - originRecord.MapPixelX) + Math.Abs(r.MapPixelY - originRecord.MapPixelY) is >= 3 and <= 15);

            DaggerfallSiteProfile template = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
            DaggerfallSiteProfile origin = WithPopulation(template, originRecord.Id, count: 3);
            DaggerfallSiteProfile destination = WithPopulation(template, destRecord.Id, count: 1);

            DaggerfallSessionComposition composition = new(definitions, origin, DaggerfallTuning.Defaults)
            {
                Profiles = new DaggerfallSiteProfiles([origin, destination]),
                Blocks = SharedBlocks.Value,
            };
            PerceptionFake perception = PerceptionFake.Create();
            EngineContextFake engine = BuildEngineWithPerception(perception, origin, destination);
            DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
            session.Update(new ProductUpdate(OuterUpdate(1), []));
            return new(origin, destination, composition, session, perception);
        }

        internal EngineContextFake CreateEngine() => BuildEngineWithPerception(null, Origin, Destination);

        internal static EngineContextFake BuildEngine(params DaggerfallSiteProfile[] profiles) => BuildEngineWithPerception(null, profiles);

        internal static EngineContextFake BuildEngineWithPerception(PerceptionFake? perception, params DaggerfallSiteProfile[] profiles)
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            foreach (DaggerfallSiteProfile profile in profiles)
                PopulateContent(content, profile);
            SpatialFake spatial = SpatialFake.Create(profiles[0].Geometry.Spatial[0].Sha256, releases);
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception: perception?.Service);
        }

        private static DaggerfallSiteProfile WithPopulation(DaggerfallSiteProfile template, DaggerfallSiteId siteId, int count)
        {
            DaggerfallSiteProfile exterior = SameContentAt(template, siteId, DaggerfallWorldProfileKind.Exterior,
                DaggerfallWorldProfileIds.Exterior(siteId).LogicalId);
            NormalizedBillboardSprite billboard = new("sprite/population.png", Hash, 8, 8,
                [new NormalizedAtlasFrame(0, 0, 0, 8, 8)], 0, new Vector2(.5F, .5F), Vector2.One);
            Dictionary<(int Archive, int Record), NormalizedBillboardSprite> billboards = template.BillboardSprites.ToDictionary();
            billboards[(210, 4)] = billboard;
            int factionId = TestPayload.Definitions.Factions.Factions.Values.First(f => f.Id > 0 && f.Type != 10
                && !StringComparer.Ordinal.Equals(f.TypeName, "KnightlyGuard")
                && TestPayload.Definitions.Catalogs.Races.Any(r => r.DonorRaceId == f.Race)).Id;
            DaggerfallPopulationPlacement[] pop = [.. Enumerable.Range(0, count).Select(i =>
                new DaggerfallPopulationPlacement($"population/source/0/{i}", new WorldPoint(1.5F + i, 1F, 1.5F),
                    210, 4, factionId, (byte)(i % 2 == 1 ? 0x20 : 0), (ushort)(300 + i * 19)))];
            return new DaggerfallSiteProfile(
                new ProjectFacts(exterior.Project.PlayerPosition, new Dictionary<long, AuthoredActor>()),
                exterior.Geometry,
                exterior.WorldAppearance,
                exterior.InitialLook,
                exterior.Materials,
                exterior.ActorSprites,
                exterior.MobileSprites,
                exterior.Audio,
                exterior.ClassicPresentation,
                exterior.Site,
                exterior.Doors,
                exterior.ProfileKind,
                exterior.ProfileKey.LogicalId,
                Array.Empty<DaggerfallSitePortal>(),
                exterior.Anchors.Values.ToArray(),
                exterior.Lights,
                exterior.GroundContainerSprites,
                exterior.DungeonMap,
                exterior.DungeonActions,
                exterior.DungeonActionModels,
                exterior.InteriorBuilding,
                exterior.QuestMarkers,
                billboardSprites: billboards,
                terrainTextures: template.TerrainTextures,
                population: pop);
        }

        public void Dispose() => Session.Dispose();
    }
}
