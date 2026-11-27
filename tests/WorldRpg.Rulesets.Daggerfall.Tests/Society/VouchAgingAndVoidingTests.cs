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
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11133: vouches age, weaken while fading, lapse after their validity window, and are voided when the
/// voucher is wronged by the player (robbed/assaulted/murdered), dies, or turns hostile — reflected in
/// <see cref="DaggerfallIntroductions.AssessAccess"/>, trade/deposit bonuses, <c>actor.inspect</c>, and saves.
/// </summary>
public sealed class VouchAgingAndVoidingTests
{
    private const int DaggerfallRegion = 17;
    private const int LadyBridwellFaction = 377; // CourtFavourite
    private const int KingGothrydFaction = 364;  // Prominent
    private const int MobarFaction = 371;        // MinorCourt

    [Fact]
    public void Vouches_weaken_when_fading_lapse_with_time_can_be_renewed_and_surface_in_actor_inspect()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // Blunt stranger (social skill skip = 0, home standing = 0).
        SetSocialStats(session, etiquette: 15, streetwise: 15, personality: 30);

        long ladyId = SpawnNpc(session, "vouch-age/lady", "Lady Bridwell", LadyBridwellFaction, DaggerfallRegion, ["talk", "quest"]);
        long kingId = SpawnNpc(session, "vouch-age/king", "King Gothryd", KingGothrydFaction, DaggerfallRegion, ["talk", "quest"]);
        long mobarId = SpawnNpc(session, "vouch-age/mobar", "Mobar", MobarFaction, DaggerfallRegion, ["talk", "shop"]);
        long merchantId = SpawnNpc(session, "vouch-age/merchant", "Maren the Clothier", 0, DaggerfallRegion, ["talk", "shop", "buy-items"]);

        DaggerfallNpc lady = session.State.Npcs.Require(ladyId);
        DaggerfallNpc king = session.State.Npcs.Require(kingId);
        DaggerfallNpc mobar = session.State.Npcs.Require(mobarId);
        DaggerfallNpc merchant = session.State.Npcs.Require(merchantId);

        // Earn Lady Bridwell's trust and get Court vouches to King Gothryd & Mobar plus a Trade vouch to a local shopkeeper.
        session.Introductions.AdjustActorDisposition(ladyId, 40);
        DaggerfallIntroductionResult toKing = session.Introductions.TryIssueIntroduction(lady, kingId, session.Site.ActiveSite);
        DaggerfallIntroductionResult toMobar = session.Introductions.TryIssueIntroduction(lady, mobarId, session.Site.ActiveSite);
        DaggerfallIntroductionResult toMerchant = session.Introductions.TryIssueIntroduction(lady, merchantId, session.Site.ActiveSite);
        Assert.True(toKing.Accepted, toKing.Message);
        Assert.True(toMobar.Accepted, toMobar.Message);
        Assert.True(toMerchant.Accepted, toMerchant.Message);
        Assert.Equal(ActorVouchKind.Trade, toMerchant.Vouch!.Kind);

        // 1. Fresh (day 0): full reach (opens King), full +15 standing bonus, deposit waiver active, actor.inspect reports Active.
        DaggerfallIntroductionAccess kingFresh = session.Introductions.AssessAccess(king);
        Assert.True(kingFresh.Allowed);
        Assert.Equal(DaggerfallVouchStatus.Active, kingFresh.QualifyingVouchEvaluation?.Status);
        Assert.Equal(0, kingFresh.QualifyingVouchEvaluation?.ReachPenalty);
        Assert.Equal(DaggerfallIntroductions.TrustedVouchStandingBonus, session.Introductions.TrustedVouchReactionBonus(mobarId));
        Assert.True(session.Introductions.HasDepositWaiverVouch(mobar));

        IDeveloperActorCommands actorCmds = session.Commands<IDeveloperActorCommands>();
        using (JsonDocument doc = JsonDocument.Parse(actorCmds.Inspect(kingId.ToString()).Message))
        {
            JsonElement social = doc.RootElement.GetProperty("social");
            Assert.Equal("Active", social.GetProperty("access").GetProperty("qualifyingVouchStatus").GetString());
            JsonElement vouchEl = Assert.Single(social.GetProperty("vouches").EnumerateArray());
            Assert.Equal("Active", vouchEl.GetProperty("status").GetString());
            Assert.True(vouchEl.GetProperty("usable").GetBoolean());
            Assert.Equal(0, vouchEl.GetProperty("ageDays").GetInt32());
            Assert.Equal(DaggerfallIntroductions.CourtVouchLapseDays, vouchEl.GetProperty("daysUntilLapse").GetInt32());
        }

        // 2. Advance 75 days into the Fading window (60..119 days for Court vouches):
        //    - Reach drops by 1: King (Prominent, req 3) now sees reach 2 < 3 -> denied with stale-word reason!
        //    - Mobar (MinorCourt, req 1) still grants access (reach 2 >= 1), but standing bonus weakens to +7 and deposit waiver drops!
        session.AdvanceElapsedTime(75L * 24L * 60L * 60L, idleFatigue: false);

        DaggerfallIntroductionAccess kingFading = session.Introductions.AssessAccess(king);
        Assert.False(kingFading.Allowed);
        Assert.Equal(DaggerfallVouchStatus.Fading, kingFading.QualifyingVouchEvaluation?.Status);
        Assert.Equal(1, kingFading.QualifyingVouchEvaluation?.ReachPenalty);
        Assert.Contains("stale", kingFading.DenialReason!, StringComparison.OrdinalIgnoreCase);

        DaggerfallIntroductionAccess mobarFading = session.Introductions.AssessAccess(mobar);
        Assert.True(mobarFading.Allowed);
        Assert.Equal(DaggerfallVouchStatus.Fading, mobarFading.QualifyingVouchEvaluation?.Status);
        Assert.Equal(DaggerfallIntroductions.FadingVouchStandingBonus, session.Introductions.TrustedVouchReactionBonus(mobarId));
        Assert.False(session.Introductions.HasDepositWaiverVouch(mobar));

        using (JsonDocument doc = JsonDocument.Parse(actorCmds.Inspect(kingId.ToString()).Message))
        {
            JsonElement vouchEl = Assert.Single(doc.RootElement.GetProperty("social").GetProperty("vouches").EnumerateArray());
            Assert.Equal("Fading", vouchEl.GetProperty("status").GetString());
            Assert.True(vouchEl.GetProperty("usable").GetBoolean());
            Assert.Equal(75, vouchEl.GetProperty("ageDays").GetInt32());
            Assert.Equal(DaggerfallIntroductions.CourtVouchLapseDays - 75, vouchEl.GetProperty("daysUntilLapse").GetInt32());
            Assert.Equal(1, vouchEl.GetProperty("reachPenalty").GetInt32());
        }

        // 3. Advance another 50 days (total 125 days >= 120-day Court lapse):
        //    - Both King and Mobar vouches lapse completely (Allowed=false, bonus=0, usable=false).
        session.AdvanceElapsedTime(50L * 24L * 60L * 60L, idleFatigue: false);

        DaggerfallIntroductionAccess kingLapsed = session.Introductions.AssessAccess(king);
        Assert.False(kingLapsed.Allowed);
        Assert.Null(kingLapsed.QualifyingVouch);
        Assert.Equal(DaggerfallVouchStatus.Lapsed, kingLapsed.QualifyingVouchEvaluation?.Status);
        Assert.Contains("lapsed", kingLapsed.DenialReason!, StringComparison.OrdinalIgnoreCase);

        DaggerfallIntroductionAccess mobarLapsed = session.Introductions.AssessAccess(mobar);
        Assert.False(mobarLapsed.Allowed);
        Assert.Equal(0, session.Introductions.TrustedVouchReactionBonus(mobarId));

        using (JsonDocument doc = JsonDocument.Parse(actorCmds.Inspect(kingId.ToString()).Message))
        {
            JsonElement vouchEl = Assert.Single(doc.RootElement.GetProperty("social").GetProperty("vouches").EnumerateArray());
            Assert.Equal("Lapsed", vouchEl.GetProperty("status").GetString());
            Assert.False(vouchEl.GetProperty("usable").GetBoolean());
            Assert.Equal(125, vouchEl.GetProperty("ageDays").GetInt32());
            Assert.Equal(0, vouchEl.GetProperty("daysUntilLapse").GetInt32());
        }

        // 4. Save/reload preserves IssuedDay and lapsed status; asking Lady Bridwell again renews the lapsed Trade vouch cleanly!
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.False(restored.Introductions.AssessAccess(restored.State.Npcs.Require(kingId)).Allowed);
        Assert.Equal(DaggerfallVouchStatus.Lapsed, restored.Introductions.EvaluateVouch(Assert.Single(restored.Introductions.ReadVouches(kingId))).Status);
        Assert.Equal(DaggerfallVouchStatus.Lapsed, restored.Introductions.EvaluateVouch(Assert.Single(restored.Introductions.ReadVouches(merchantId))).Status);
        Assert.Equal(0, restored.Introductions.TrustedVouchReactionBonus(merchantId));
        Assert.False(restored.Introductions.HasDepositWaiverVouch(restored.State.Npcs.Require(merchantId)));

        DaggerfallIntroductionResult renewed = restored.Introductions.TryIssueIntroduction(
            restored.State.Npcs.Require(ladyId), merchantId, restored.Site.ActiveSite);
        Assert.True(renewed.Accepted, renewed.Message);
        Assert.False(renewed.AlreadyVouched);
        Assert.Equal(DaggerfallVouchStatus.Active, restored.Introductions.EvaluateVouch(Assert.Single(restored.Introductions.ReadVouches(merchantId))).Status);
        Assert.Equal(DaggerfallIntroductions.TrustedVouchStandingBonus, restored.Introductions.TrustedVouchReactionBonus(merchantId));
        Assert.True(restored.Introductions.HasDepositWaiverVouch(restored.State.Npcs.Require(merchantId)));
    }

    [Fact]
    public void Vouches_are_voided_when_voucher_is_wronged_dies_or_turns_hostile_and_survive_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        SetSocialStats(session, etiquette: 15, streetwise: 15, personality: 30);

        long ladyRobbedId = SpawnNpc(session, "vouch-void/lady-robbed", "Lady Bridwell", LadyBridwellFaction, DaggerfallRegion, ["talk", "quest"]);
        long ladyDeadId = SpawnNpc(session, "vouch-void/lady-dead", "Lady Ashworth", LadyBridwellFaction, DaggerfallRegion, ["talk", "quest"]);
        long ladyHostileId = SpawnNpc(session, "vouch-void/lady-hostile", "Lady Corvus", LadyBridwellFaction, DaggerfallRegion, ["talk", "quest"]);
        long kingAId = SpawnNpc(session, "vouch-void/king-a", "King Gothryd", KingGothrydFaction, DaggerfallRegion, ["talk", "quest"]);
        long kingBId = SpawnNpc(session, "vouch-void/king-b", "King Eadwyre", KingGothrydFaction, DaggerfallRegion, ["talk", "quest"]);
        long kingCId = SpawnNpc(session, "vouch-void/king-c", "King Lysandus", KingGothrydFaction, DaggerfallRegion, ["talk", "quest"]);

        foreach (long vId in new[] { ladyRobbedId, ladyDeadId, ladyHostileId })
            session.Introductions.AdjustActorDisposition(vId, 40);

        Assert.True(session.Introductions.TryIssueIntroduction(session.State.Npcs.Require(ladyRobbedId), kingAId, session.Site.ActiveSite).Accepted);
        Assert.True(session.Introductions.TryIssueIntroduction(session.State.Npcs.Require(ladyDeadId), kingBId, session.Site.ActiveSite).Accepted);
        Assert.True(session.Introductions.TryIssueIntroduction(session.State.Npcs.Require(ladyHostileId), kingCId, session.Site.ActiveSite).Accepted);

        Assert.True(session.Introductions.AssessAccess(session.State.Npcs.Require(kingAId)).Allowed);
        Assert.True(session.Introductions.AssessAccess(session.State.Npcs.Require(kingBId)).Allowed);
        Assert.True(session.Introductions.AssessAccess(session.State.Npcs.Require(kingCId)).Allowed);

        // 1. Player robs Lady Bridwell -> attributed Theft crime voids her vouch as VoidedWronged!
        DaggerfallCrimeIncidentSave theft = new(
            OperationId: "op-rob-voucher",
            Crime: DaggerfallCrimeKind.Theft,
            Stage: DaggerfallCrimeStage.Completed,
            PerpetratorActorId: DaggerfallActorIdentity.PlayerEntityId,
            AffectedActorOrOwnerId: ladyRobbedId,
            Region: DaggerfallRegion,
            GameMinute: session.Calendar.MinuteNumber,
            TargetKind: DaggerfallCrimeTargetKind.Civilian,
            Witnesses: new DaggerfallCrimeWitnessEvidence(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [ladyRobbedId]),
            GuildCredit: DaggerfallCrimeGuildCredit.None,
            Reported: true);
        Assert.True(session.Introductions.ObserveAttributedCrime(theft) > 0);

        DaggerfallIntroductionAccess accessAfterRobbery = session.Introductions.AssessAccess(session.State.Npcs.Require(kingAId));
        Assert.False(accessAfterRobbery.Allowed);
        Assert.Equal(DaggerfallVouchStatus.VoidedWronged, accessAfterRobbery.QualifyingVouchEvaluation?.Status);
        Assert.Contains("wronged", accessAfterRobbery.DenialReason!, StringComparison.OrdinalIgnoreCase);

        // 2. Lady Ashworth dies (non-player death) -> her vouch is voided as VoidedDead!
        Assert.True(session.Introductions.ObserveActorDied(ladyDeadId, killedByPlayer: false) > 0);
        DaggerfallIntroductionAccess accessAfterDeath = session.Introductions.AssessAccess(session.State.Npcs.Require(kingBId));
        Assert.False(accessAfterDeath.Allowed);
        Assert.Equal(DaggerfallVouchStatus.VoidedDead, accessAfterDeath.QualifyingVouchEvaluation?.Status);
        Assert.Contains("dead", accessAfterDeath.DenialReason!, StringComparison.OrdinalIgnoreCase);

        // 3. Lady Corvus turns hostile (forced-hostile perception + ObserveActorHostile) -> her vouch is VoidedHostile!
        session.State.Actors.Get(ladyHostileId).Actor.Get<DaggerfallEnemyPerceptionMemory>().SetForcedHostile(true);
        Assert.True(session.Introductions.ObserveActorHostile(ladyHostileId) > 0);
        DaggerfallIntroductionAccess accessAfterHostility = session.Introductions.AssessAccess(session.State.Npcs.Require(kingCId));
        Assert.False(accessAfterHostility.Allowed);
        Assert.Equal(DaggerfallVouchStatus.VoidedHostile, accessAfterHostility.QualifyingVouchEvaluation?.Status);
        Assert.Contains("hostile", accessAfterHostility.DenialReason!, StringComparison.OrdinalIgnoreCase);

        // 4. actor.inspect reports each voided status, usable=false, and reason; save/load preserves all three!
        IDeveloperActorCommands actorCmds = session.Commands<IDeveloperActorCommands>();
        AssertInspectVouchStatus(actorCmds, kingAId, "VoidedWronged");
        AssertInspectVouchStatus(actorCmds, kingBId, "VoidedDead");
        AssertInspectVouchStatus(actorCmds, kingCId, "VoidedHostile");

        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.False(restored.Introductions.AssessAccess(restored.State.Npcs.Require(kingAId)).Allowed);
        Assert.False(restored.Introductions.AssessAccess(restored.State.Npcs.Require(kingBId)).Allowed);
        Assert.False(restored.Introductions.AssessAccess(restored.State.Npcs.Require(kingCId)).Allowed);
        Assert.Equal(DaggerfallVouchStatus.VoidedWronged, restored.Introductions.AssessAccess(restored.State.Npcs.Require(kingAId)).QualifyingVouchEvaluation?.Status);
        Assert.Equal(DaggerfallVouchStatus.VoidedDead, restored.Introductions.AssessAccess(restored.State.Npcs.Require(kingBId)).QualifyingVouchEvaluation?.Status);
        Assert.Equal(DaggerfallVouchStatus.VoidedHostile, restored.Introductions.AssessAccess(restored.State.Npcs.Require(kingCId)).QualifyingVouchEvaluation?.Status);

        // 5. Player murdering a voucher via ObserveActorDied(killedByPlayer: true) also voids as VoidedWronged.
        long freshPatronId = SpawnNpc(session, "vouch-void/patron", "Lord Bridwell", LadyBridwellFaction, DaggerfallRegion, ["talk"]);
        long freshTargetId = SpawnNpc(session, "vouch-void/target", "Mobar", MobarFaction, DaggerfallRegion, ["talk"]);
        session.Introductions.AdjustActorDisposition(freshPatronId, 40);
        Assert.True(session.Introductions.TryIssueIntroduction(session.State.Npcs.Require(freshPatronId), freshTargetId, session.Site.ActiveSite).Accepted);
        Assert.True(session.Introductions.AssessAccess(session.State.Npcs.Require(freshTargetId)).Allowed);
        Assert.True(session.Introductions.ObserveActorDied(freshPatronId, killedByPlayer: true) > 0);
        DaggerfallIntroductionAccess murderedPatronAccess = session.Introductions.AssessAccess(session.State.Npcs.Require(freshTargetId));
        Assert.False(murderedPatronAccess.Allowed);
        Assert.Equal(DaggerfallVouchStatus.VoidedWronged, murderedPatronAccess.QualifyingVouchEvaluation?.Status);
    }

    [Fact]
    public void Successful_pickpocket_of_voucher_voids_grants_and_survives_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        // Force success (roll=1 <= chance), valuable found (roll=80 > 33), and 4 stolen gold pieces.
        fixture.Random = new PickpocketSuccessRandom();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        SetSocialStats(session, etiquette: 15, streetwise: 15, personality: 30);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("pickpocket")).BaseValue = 100;

        long ladyId = SpawnNpc(session, "vouch-pickpocket/lady", "Lady Bridwell", LadyBridwellFaction, DaggerfallRegion, ["talk", "quest"]);
        long kingId = SpawnNpc(session, "vouch-pickpocket/king", "King Gothryd", KingGothrydFaction, DaggerfallRegion, ["talk", "quest"]);

        session.Introductions.AdjustActorDisposition(ladyId, 40);
        DaggerfallIntroductionResult issued = session.Introductions.TryIssueIntroduction(
            session.State.Npcs.Require(ladyId), kingId, session.Site.ActiveSite);
        Assert.True(issued.Accepted, issued.Message);
        Assert.True(session.Introductions.AssessAccess(session.State.Npcs.Require(kingId)).Allowed);

        // Silent successful pickpocket transfers stolen gold (PropertyTransferred) with zero crime incident reported.
        DaggerfallActivationOutcome pickpocket = session.Crimes.Pickpocket(ladyId);
        Assert.True(pickpocket.Applied, pickpocket.Message);
        Assert.Contains("pinched", pickpocket.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(session.State.Crime.Incidents);
        Assert.Contains(session.State.Crime.Attempts,
            a => a.AffectedActorOrOwnerId == ladyId && a.Outcome == DaggerfallCrimeAttemptOutcome.PropertyTransferred);

        // Voucher's grant is persistently voided as VoidedWronged and survives save/reload!
        DaggerfallIntroductionAccess accessAfterPickpocket = session.Introductions.AssessAccess(session.State.Npcs.Require(kingId));
        Assert.False(accessAfterPickpocket.Allowed);
        Assert.Equal(DaggerfallVouchStatus.VoidedWronged, accessAfterPickpocket.QualifyingVouchEvaluation?.Status);

        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        DaggerfallIntroductionAccess restoredAccess = restored.Introductions.AssessAccess(restored.State.Npcs.Require(kingId));
        Assert.False(restoredAccess.Allowed);
        Assert.Equal(DaggerfallVouchStatus.VoidedWronged, restoredAccess.QualifyingVouchEvaluation?.Status);
    }

    [Fact]
    public void Hostility_transition_via_enemy_behavior_permanently_voids_vouch_even_after_pacify_and_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        SetSocialStats(session, etiquette: 15, streetwise: 15, personality: 30);

        long ladyId = SpawnNpc(session, "vouch-pacify/lady", "Lady Corvus", LadyBridwellFaction, DaggerfallRegion, ["talk", "quest"]);
        long kingId = SpawnNpc(session, "vouch-pacify/king", "King Gothryd", KingGothrydFaction, DaggerfallRegion, ["talk", "quest"]);

        session.Introductions.AdjustActorDisposition(ladyId, 40);
        DaggerfallIntroductionResult issued = session.Introductions.TryIssueIntroduction(
            session.State.Npcs.Require(ladyId), kingId, session.Site.ActiveSite);
        Assert.True(issued.Accepted, issued.Message);
        Assert.True(session.Introductions.AssessAccess(session.State.Npcs.Require(kingId)).Allowed);

        // Hostility transition via canonical EnemyBehavior.MakeHostile (used by QuestFoes, OverlandParties, Law, Crimes, Magic)
        // must persistently write VoidReason=Hostile onto the grant so Pacify does NOT resurrect it!
        session.EnemyBehavior.MakeHostile(ladyId, includePlayerAllies: true);
        Assert.True(session.State.Actors.Get(ladyId).Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);
        Assert.Equal(DaggerfallVouchStatus.VoidedHostile,
            session.Introductions.AssessAccess(session.State.Npcs.Require(kingId)).QualifyingVouchEvaluation?.Status);

        // Pacifying the voucher clears ForcedHostile, yet the original grant MUST remain VoidedHostile and unusable!
        session.EnemyBehavior.Pacify(ladyId);
        Assert.False(session.State.Actors.Get(ladyId).Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);
        DaggerfallIntroductionAccess afterPacify = session.Introductions.AssessAccess(session.State.Npcs.Require(kingId));
        Assert.False(afterPacify.Allowed);
        Assert.Equal(DaggerfallVouchStatus.VoidedHostile, afterPacify.QualifyingVouchEvaluation?.Status);

        // Save/reload preserves the permanent VoidedHostile state!
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        DaggerfallIntroductionAccess restoredAccess = restored.Introductions.AssessAccess(restored.State.Npcs.Require(kingId));
        Assert.False(restoredAccess.Allowed);
        Assert.Equal(DaggerfallVouchStatus.VoidedHostile, restoredAccess.QualifyingVouchEvaluation?.Status);
    }

    private static void AssertInspectVouchStatus(IDeveloperActorCommands cmds, long actorId, string expectedStatus)
    {
        DebugCommandResult res = cmds.Inspect(actorId.ToString());
        Assert.True(res.Succeeded, res.Message);
        using JsonDocument doc = JsonDocument.Parse(res.Message);
        JsonElement vouchEl = Assert.Single(doc.RootElement.GetProperty("social").GetProperty("vouches").EnumerateArray());
        Assert.Equal(expectedStatus, vouchEl.GetProperty("status").GetString());
        Assert.False(vouchEl.GetProperty("usable").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(vouchEl.GetProperty("reason").GetString()));
    }

    private static void SetSocialStats(DaggerfallSession session, int etiquette, int streetwise, int personality)
    {
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("etiquette")).BaseValue = etiquette;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("streetwise")).BaseValue = streetwise;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Personality.Value)).BaseValue = personality;
    }

    private static long SpawnNpc(
        DaggerfallSession session,
        string stableKey,
        string displayName,
        int factionId,
        int region,
        string[] services)
    {
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(region, site.Name, string.Empty, session.Sites.ActiveProfile.LogicalId);
        DaggerfallNpcAppearance appearance = new("breton", "Female", 334, 8, Math.Abs(stableKey.GetHashCode()), factionId);
        long id = session.State.Npcs.RegisterCivilian(npcSite, appearance, displayName, services);
        session.State.Npcs.SetDisplayName(id, displayName);
        WorldPoint playerPos = session.State.PlayerControl.Position ?? new WorldPoint(0F, 0F, 0F);
        session.Civilians.Materialize(id, new ActorPose(new WorldPoint(playerPos.X + 2F, playerPos.Y, playerPos.Z + 2F), 0F));
        session.State.Npcs.Bind(id, session.Sites.ActiveProfile);
        ActorState live = session.State.Actors.Get(id);
        live.Actor.Get<FactionComponent>().SetPeopleFactions(factionId > 0 ? factionId : null, null);
        return id;
    }

    private sealed class PickpocketSuccessRandom : IRandomService
    {
        private readonly IRandomService _fallback = RandomMinimum.Create();

        public KeyedRngReceipt DrawKeyed(KeyedRngRequest request)
        {
            if (request.Key.EndsWith(":valuable", StringComparison.Ordinal))
                return new KeyedRngReceipt(Math.Clamp(80, request.Minimum, request.Maximum));
            if (request.Key.EndsWith(":gold", StringComparison.Ordinal))
                return new KeyedRngReceipt(Math.Clamp(4, request.Minimum, request.Maximum));
            return _fallback.DrawKeyed(request);
        }

        public Lcg15Receipt DrawLcg15(Lcg15Request request) => _fallback.DrawLcg15(request);
        public Rng CreateScoped(ScopedRngCreateRequest request) => _fallback.CreateScoped(request);
        public Rng ForkScoped(ScopedRngForkRequest request) => _fallback.ForkScoped(request);
        public RngValue NextU64(Rng rng) => _fallback.NextU64(rng);
        public RngValue NextBoundedU32(ScopedRngBoundedRequest request) => _fallback.NextBoundedU32(request);
        public RngValue NextBool(Rng rng) => _fallback.NextBool(rng);
    }
}
