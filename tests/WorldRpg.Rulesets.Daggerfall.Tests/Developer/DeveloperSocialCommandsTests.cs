using System.Text.Json;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11134 developer tools:
/// 1. <c>social.schedule</c> live NPC schedule and shelter readout (per-NPC entry, outdoors/sheltered reason,
///    shelter target building or overflow, and site summary).
/// 2. State-changing <c>social.*</c> commands routing through canonical owners (<c>social.office.vacate</c>,
///    <c>social.household.standing</c>, <c>social.vouch.grant</c>, <c>social.vouch.revoke</c>) so saves and
///    notifications behave identically to play.
/// </summary>
public sealed class DeveloperSocialCommandsTests
{
    private const int DaggerfallRegion = 17;
    private const int LadyBridwellFaction = 377;
    private const int KingGothrydFaction = 364;

    [Fact]
    public void Social_schedule_reports_per_npc_decisions_shelter_targets_and_site_summary_across_weather_and_time()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // Advance to sunny midday (12:00) so ordinary daytime walkers are outdoors.
        long middayOffset = checked((12 - session.Calendar.Hour) * DaggerfallCalendar.MinutesPerHour * DaggerfallCalendar.SecondsPerMinute);
        if (middayOffset > 0)
            session.AdvanceElapsedTime(middayOffset, idleFatigue: false);
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Sunny);
        session.ResetScheduleWeatherBaseline();

        IDeveloperSocialCommands cmds = session.Commands<IDeveloperSocialCommands>();
        DebugCommandResult sunnyResult = cmds.Schedule();
        Assert.True(sunnyResult.Succeeded, sunnyResult.Message);

        using (JsonDocument doc = JsonDocument.Parse(sunnyResult.Message))
        {
            JsonElement root = doc.RootElement;
            Assert.Equal("Sunny", root.GetProperty("weather").GetString());
            JsonElement summary = root.GetProperty("summary");
            Assert.True(summary.GetProperty("totalNpcCount").GetInt32() > 0);
            Assert.True(summary.GetProperty("outdoorsCount").GetInt32() > 0);

            JsonElement npcs = root.GetProperty("npcs");
            Assert.Contains(npcs.EnumerateArray(), e =>
                e.GetProperty("outdoors").GetBoolean()
                && e.GetProperty("reason").GetString() is "daytime-street" or "city-guard");
        }

        // Now trigger a sustained thunderstorm (> 15m elapsed) so rain/storm-sensitive walkers shelter into buildings or homes!
        session.LocalWeather.State.Force(DaggerfallWeatherKind.Thunder);
        session.ObserveScheduleWeatherElapsed();
        session.AdvanceElapsedTime(20L * DaggerfallCalendar.SecondsPerMinute, idleFatigue: false);

        DebugCommandResult stormResult = cmds.Schedule();
        Assert.True(stormResult.Succeeded, stormResult.Message);

        using (JsonDocument doc = JsonDocument.Parse(stormResult.Message))
        {
            JsonElement root = doc.RootElement;
            Assert.Equal("Thunder", root.GetProperty("weather").GetString());
            JsonElement summary = root.GetProperty("summary");
            int shelteredTotal = summary.GetProperty("shelteredInBuildingCount").GetInt32()
                + summary.GetProperty("shelteredInHomeCount").GetInt32()
                + summary.GetProperty("offStageOverflowCount").GetInt32();
            Assert.True(shelteredTotal > 0, "Sustained thunderstorm should shelter exterior walkers.");

            JsonElement npcs = root.GetProperty("npcs");
            Assert.Contains(npcs.EnumerateArray(), e =>
                !e.GetProperty("outdoors").GetBoolean()
                && (e.GetProperty("reason").GetString() ?? string.Empty).Contains("storm-shelter", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Social_office_vacate_routes_through_OfficeLedger_emitting_proclamations_and_persisting_across_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        IDeveloperSocialCommands cmds = session.Commands<IDeveloperSocialCommands>();

        // Depose the active region's ruler via social.office.vacate -> triggers OfficeLedger.OfficeChanged + WordSpread proclamation + exile!
        DebugCommandResult result = cmds.VacateOffice("ruler", "card");
        Assert.True(result.Succeeded, result.Message);

        string vacatedOfficeKey;
        using (JsonDocument doc = JsonDocument.Parse(result.Message))
        {
            Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
            JsonElement first = Assert.Single(doc.RootElement.GetProperty("vacated").EnumerateArray());
            vacatedOfficeKey = first.GetProperty("officeKey").GetString()!;
            Assert.True(first.GetProperty("vacant").GetBoolean());
        }

        // Canonical owners updated: OfficeLedger holds override AND WordSpread emitted the proclamation wavefront.
        Assert.True(session.OfficeLedger.TryGetOverride(vacatedOfficeKey)?.Vacant == true);
        Assert.True(session.WordSpread.HasOfficeProclamation(vacatedOfficeKey));

        // Save & reload preserves the vacant office and proclamation identically to play!
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.True(restored.OfficeLedger.TryGetOverride(vacatedOfficeKey)?.Vacant == true);
        Assert.True(restored.WordSpread.HasOfficeProclamation(vacatedOfficeKey));

        // Invalid cause is cleanly refused.
        Assert.False(cmds.VacateOffice("ruler", "bogus-cause").Succeeded);
    }

    [Fact]
    public void Social_household_standing_mutates_local_and_great_house_standings_and_survives_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        IDeveloperSocialCommands cmds = session.Commands<IDeveloperSocialCommands>();

        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        string localFamilyId = DaggerfallHouseholds.TownPoolHouseholdId(session.WorldSocialSeed, site.Id, "dev-test-slot");
        string greatHouseId = DaggerfallHouseholds.GreatHouseId(DaggerfallRegion, 0);

        // 1. Local household into blood feud (-25) -> reflected in SocialConsequences, social.site, and save/load.
        DebugCommandResult localRes = cmds.SetHouseholdStanding(localFamilyId, -25);
        Assert.True(localRes.Succeeded, localRes.Message);
        using (JsonDocument doc = JsonDocument.Parse(localRes.Message))
        {
            Assert.Equal(-25, doc.RootElement.GetProperty("standingDelta").GetInt32());
            Assert.True(doc.RootElement.GetProperty("inBloodFeud").GetBoolean());
        }
        Assert.Equal(-25, session.SocialConsequences.HouseholdStandingDelta(localFamilyId, site));

        // 2. Great House standing (-20) -> routed through WordSpread HouseholdStanding wavefront!
        DebugCommandResult houseRes = cmds.SetHouseholdStanding(greatHouseId, -20);
        Assert.True(houseRes.Succeeded, houseRes.Message);
        Assert.Equal(-20, session.SocialConsequences.HouseholdStandingDelta(greatHouseId, site));
        Assert.Contains(session.WordSpread.Wavefronts, wf => wf.Kind == DaggerfallWordEventKind.HouseholdStanding && wf.FamilyId == greatHouseId);

        // Save/reload preserves both standings.
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.Equal(-25, restored.SocialConsequences.HouseholdStandingDelta(localFamilyId, site));
        Assert.Equal(-20, restored.SocialConsequences.HouseholdStandingDelta(greatHouseId, site));

        // Setting local household back to 0 cleans up its sparse entry.
        DebugCommandResult clearLocal = restored.Commands<IDeveloperSocialCommands>().SetHouseholdStanding(localFamilyId, 0);
        Assert.True(clearLocal.Succeeded, clearLocal.Message);
        Assert.Equal(0, restored.SocialConsequences.HouseholdStandingDelta(localFamilyId, site));
        Assert.DoesNotContain(restored.SocialConsequences.Capture().Standings, s => s.FamilyId == localFamilyId);

        // Invalid familyId or out-of-range standing is refused.
        Assert.False(cmds.SetHouseholdStanding("not a valid id!", -10).Succeeded);
        Assert.False(cmds.SetHouseholdStanding(localFamilyId, -200).Succeeded);
    }

    [Fact]
    public void Social_vouch_grant_and_revoke_route_through_Introductions_and_persist_across_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("etiquette")).BaseValue = 15;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("streetwise")).BaseValue = 15;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Personality.Value)).BaseValue = 30;

        long ladyId = SpawnNpc(session, "dev-vouch/lady", "Lady Bridwell", LadyBridwellFaction, DaggerfallRegion, ["talk", "quest"]);
        long kingId = SpawnNpc(session, "dev-vouch/king", "King Gothryd", KingGothrydFaction, DaggerfallRegion, ["talk", "quest"]);

        Assert.False(session.Introductions.AssessAccess(session.State.Npcs.Require(kingId)).Allowed);

        IDeveloperSocialCommands cmds = session.Commands<IDeveloperSocialCommands>();

        // 1. Grant a Court vouch via social.vouch.grant -> opens access and survives save/load!
        DebugCommandResult grant = cmds.GrantVouch(ladyId.ToString(), kingId.ToString(), "court");
        Assert.True(grant.Succeeded, grant.Message);
        using (JsonDocument doc = JsonDocument.Parse(grant.Message))
        {
            Assert.Equal("Active", doc.RootElement.GetProperty("status").GetString());
            Assert.True(doc.RootElement.GetProperty("usable").GetBoolean());
        }
        Assert.True(session.Introductions.AssessAccess(session.State.Npcs.Require(kingId)).Allowed);

        // 2. Revoke with reason "wronged" -> persistently marks VoidedWronged and blocks access across save/load!
        DebugCommandResult voidRes = cmds.RevokeVouch(kingId.ToString(), ladyId.ToString(), "wronged");
        Assert.True(voidRes.Succeeded, voidRes.Message);
        using (JsonDocument doc = JsonDocument.Parse(voidRes.Message))
            Assert.True(doc.RootElement.GetProperty("affectedCount").GetInt32() > 0);
        Assert.False(session.Introductions.AssessAccess(session.State.Npcs.Require(kingId)).Allowed);
        Assert.Equal(DaggerfallVouchStatus.VoidedWronged,
            session.Introductions.AssessAccess(session.State.Npcs.Require(kingId)).QualifyingVouchEvaluation?.Status);

        using (DaggerfallSession restored = fixture.Restore(session.CaptureSave()))
        {
            Assert.Equal(DaggerfallVouchStatus.VoidedWronged,
                restored.Introductions.AssessAccess(restored.State.Npcs.Require(kingId)).QualifyingVouchEvaluation?.Status);

            // 3. Revoke with reason "remove" -> completely strips the vouch record!
            DebugCommandResult removeRes = restored.Commands<IDeveloperSocialCommands>().RevokeVouch(kingId.ToString(), "all", "remove");
            Assert.True(removeRes.Succeeded, removeRes.Message);
            Assert.Empty(restored.Introductions.ReadVouches(kingId));
        }

        // Invalid kind or self-vouch is refused cleanly.
        Assert.False(cmds.GrantVouch(ladyId.ToString(), ladyId.ToString(), "court").Succeeded);
        Assert.False(cmds.GrantVouch(ladyId.ToString(), kingId.ToString(), "invalid").Succeeded);
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
}
