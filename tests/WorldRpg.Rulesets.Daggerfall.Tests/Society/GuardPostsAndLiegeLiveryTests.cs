using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11167: Post-differentiated watch officers and liege-livery guards:
/// - Rank-and-file street patrols retain their region's local watch identity across political ownership changes.
/// - Special posts — palaces, keeps, local crown/governor offices, and one watch officer/knight at each gate
///   of proper walled cities (unwalled hamlets/villages keep purely local street watch) — wear the current
///   liege's livery (falling back to the region's own arms when independent).
/// - Board Sunday liege swaps update special posts live; conquest occupations layer liege-livery garrison
///   patrols on top of local watch.
/// - Exposed in both <c>actor.inspect</c> and <c>social.site</c>.
/// </summary>
public sealed class GuardPostsAndLiegeLiveryTests
{
    private const int BetonyRegion = 19;
    private const int DaggerfallRegion = 17;
    private const int WayrestRegion = 23;

    [Fact]
    public void Walled_cities_post_one_gate_officer_per_gate_while_unwalled_hamlets_keep_street_watch_only()
    {
        DaggerfallGuardTierTuning tiers = DaggerfallGuardTierTuning.Default;
        DaggerfallSiteRecord walledCity = TestPayload.Definitions.Locations.Records.First(site =>
            site.Exterior is { Blocks.Count: >= 25 }
            && TestPayload.Definitions.Factions.Factions.Values.Any(f => f.Type == 15 && f.Region == site.Id.Region && f.Sprites.Count > 0));
        DaggerfallSiteRecord unwalledHamlet = TestPayload.Definitions.Locations.Records.First(site =>
            site.Exterior is { Blocks.Count: > 0 and < 9 }
            && site.Kind != DaggerfallSiteKind.TownCity
            && !site.Exterior.BuildingTypes.Contains(16)
            && TestPayload.Definitions.Factions.Factions.Values.Any(f => f.Type == 15 && f.Region == site.Id.Region && f.Sprites.Count > 0));

        WorldPoint[] threeGates =
        [
            new(0f, 0f, 0f),
            new(320f, 0f, 0f),
            new(0f, 0f, -320f),
        ];

        IReadOnlyList<DaggerfallPopulationPlacement> walledPop = DaggerfallExteriorPopulation.Create(
            TestPayload.Definitions, walledCity, ClearCells(walledCity),
            gateAnchor: threeGates[0], guardTiers: tiers, gateAnchors: threeGates);
        DaggerfallPopulationPlacement[] walledGateOfficers = [.. walledPop.Where(p => p.CrackdownGuard == 0 && p.GuardPost == DaggerfallGuardPostKind.WalledCityGateOfficer)];
        DaggerfallPopulationPlacement[] walledStreetWatch = [.. walledPop.Where(p => p.CrackdownGuard == 0 && p.IsGuard && p.GuardPost == DaggerfallGuardPostKind.StreetWatch)];

        Assert.Equal(threeGates.Length, walledGateOfficers.Length);
        Assert.Equal(DaggerfallExteriorPopulation.WatchCaptainSlotId(walledCity), walledGateOfficers[0].Id);
        Assert.NotEmpty(walledStreetWatch);
        // Each gate officer is placed directly on the nearest clear outdoor cell to its assigned gate
        // and carries that gate anchor (R11167-2).
        for (int i = 0; i < threeGates.Length; i++)
        {
            DaggerfallPopulationPlacement officer = Assert.Single(walledGateOfficers, p => p.GatePostAnchor == threeGates[i]);
            float nearestPossibleDist = ClearCells(walledCity)
                .Select(c =>
                {
                    const float cellSize = (DaggerfallWorldGrid.HorizontalSize / 8f) / 64f;
                    int bx = c.X / 64;
                    int bz = c.Z / 64;
                    int sx = c.X - bx * 64;
                    int sy = 63 - (c.Z - bz * 64);
                    float x = bx * (DaggerfallWorldGrid.HorizontalSize / 8f) + (sx * 64 + 32) * (cellSize / 64f);
                    float z = bz * (DaggerfallWorldGrid.HorizontalSize / 8f) + (sy * 64 + 32) * (cellSize / 64f);
                    return new WorldPoint(x, 0f, -z).HorizontalDistanceTo(threeGates[i]);
                })
                .Min();
            Assert.True(
                officer.Position.HorizontalDistanceTo(threeGates[i]) <= nearestPossibleDist + 12f,
                $"Gate officer {i} was placed {officer.Position.HorizontalDistanceTo(threeGates[i]):F1}m from gate {threeGates[i]} (nearest clear cell {nearestPossibleDist:F1}m).");
        }

        // Unwalled hamlet (no gates) keeps purely local rank-and-file StreetWatch patrols (0 gate officers).
        IReadOnlyList<DaggerfallPopulationPlacement> hamletPop = DaggerfallExteriorPopulation.Create(
            TestPayload.Definitions, unwalledHamlet, ClearCells(unwalledHamlet), guardTiers: tiers, gateAnchors: null);
        Assert.DoesNotContain(hamletPop, p => p.GuardPost == DaggerfallGuardPostKind.WalledCityGateOfficer);
        Assert.Contains(hamletPop, p => p.CrackdownGuard == 0 && p.IsGuard && p.GuardPost == DaggerfallGuardPostKind.StreetWatch);
    }

    [Fact]
    public void Live_guard_appearances_and_sprite_tints_swap_on_liege_change_while_street_watch_stays_regional()
    {
        using var fixture = new WatchPostsFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord betonySite = TestPayload.Definitions.Locations.Records.First(site =>
            site.Id.Region == BetonyRegion && site.Exterior is { Blocks.Count: > 0 });
        DaggerfallWorldProfileKey betonyExteriorKey = DaggerfallWorldProfileIds.Exterior(betonySite.Id);
        DaggerfallFactionDefinition betonyPeople = TestPayload.Definitions.Factions.Factions.Values
            .Where(f => f.Type == 15 && f.Region == BetonyRegion && f.Sprites.Count > 0)
            .OrderBy(f => f.Id)
            .First();

        WorldPoint gatePos = new(10f, 0f, -10f);
        WorldPoint streetPos = new(24f, 0f, -18f);
        DaggerfallPopulationPlacement gatePlacement = new("population/betony-test/dynamic/000", gatePos, 182, 0, betonyPeople.Id, 0, 11)
        {
            IsGuard = true,
            GuardPost = DaggerfallGuardPostKind.WalledCityGateOfficer,
            GatePostAnchor = gatePos,
        };
        DaggerfallPopulationPlacement streetPlacement = new("population/betony-test/dynamic/001", streetPos, 182, 0, betonyPeople.Id, 0, 12)
        {
            IsGuard = true,
            GuardPost = DaggerfallGuardPostKind.StreetWatch,
        };
        DaggerfallPopulationPlacement[] placements = [gatePlacement, streetPlacement];

        // ResolvePlacementIdentity consumes GuardPosts.ResolvePlacementAppearance:
        // Gate officer receives Daggerfall sovereign livery/faction, street watch keeps Betony local faction.
        DaggerfallGuardAppearanceOverride gateOverrideBefore = session.GuardPosts.ResolvePlacementAppearance(betonySite, gatePlacement);
        DaggerfallGuardAppearanceOverride streetOverrideBefore = session.GuardPosts.ResolvePlacementAppearance(betonySite, streetPlacement);
        Assert.True(gateOverrideBefore.UsesLiegeLivery);
        Assert.Equal("crown:daggerfall", gateOverrideBefore.LiveryKey);
        Assert.NotNull(gateOverrideBefore.Tint);
        Assert.False(streetOverrideBefore.UsesLiegeLivery);
        Assert.Null(streetOverrideBefore.Tint);
        Assert.Equal(betonyPeople.Id, streetOverrideBefore.FactionId);

        (DaggerfallNpcAppearance gateLookBefore, _, _) = DaggerfallSourcePopulation.ResolvePlacementIdentity(
            TestPayload.Definitions, gatePlacement, "breton", guardAppearance: gateOverrideBefore);
        (DaggerfallNpcAppearance streetLookBefore, _, _) = DaggerfallSourcePopulation.ResolvePlacementIdentity(
            TestPayload.Definitions, streetPlacement, "breton", guardAppearance: streetOverrideBefore);
        Assert.Equal(gateOverrideBefore.FactionId, gateLookBefore.FactionId);
        Assert.NotEqual(betonyPeople.Id, gateLookBefore.FactionId);
        Assert.Equal(betonyPeople.Id, streetLookBefore.FactionId);

        // Materialize both live guards with real ActorVisuals in ActiveAppearance and verify live livery sync!
        DaggerfallNpcSite npcSite = new(betonySite.Id.Region, betonySite.Name, string.Empty, betonyExteriorKey.LogicalId);
        long gateId = DaggerfallPeopleSlots.SeededSlotIdentity(betonySite.Id, $"{betonyExteriorKey.LogicalId}/{gatePlacement.Id}");
        long streetId = DaggerfallPeopleSlots.SeededSlotIdentity(betonySite.Id, $"{betonyExteriorKey.LogicalId}/{streetPlacement.Id}");
        session.State.Npcs.RegisterDeterministic(gateId, DaggerfallNpcKind.Civilian, gatePlacement.Id, npcSite, gateLookBefore, "guard", ["talk"], homeProfile: betonyExteriorKey);
        session.State.Npcs.RegisterDeterministic(streetId, DaggerfallNpcKind.Civilian, streetPlacement.Id, npcSite, streetLookBefore, "guard", ["talk"], homeProfile: betonyExteriorKey);
        session.Roster.MaterializeDeterministicCivilian(session.State.Npcs.Require(gateId), ActorOrigin.Seeded(betonyExteriorKey, gatePlacement.Id), gatePos, new ActorPose(gatePos, 0f), Vector3.Zero);
        session.Roster.MaterializeDeterministicCivilian(session.State.Npcs.Require(streetId), ActorOrigin.Seeded(betonyExteriorKey, streetPlacement.Id), streetPos, new ActorPose(streetPos, 0f), Vector3.Zero);

        NormalizedActorSprite sprite = session.Sites.Projection.Inputs.Project.Actors.Values
            .Select(p => session.Sites.Projection.Inputs.ActorSprites[p.EntityId])
            .First();
        session.ActiveAppearance.AdmitActor(gateId, sprite);
        session.ActiveAppearance.AdmitActor(streetId, sprite);

        // 1. Before board transfer: Betony -> Daggerfall.
        session.GuardPosts.ApplyLiveGuardAppearances(betonySite, session.Sites.Projection.Inputs, placements, session.State.Actors, session.State.Npcs, session.ActiveAppearance);
        ActorState gateActor = session.State.Actors.Get(gateId);
        ActorState streetActor = session.State.Actors.Get(streetId);
        Color daggerfallTint = gateOverrideBefore.Tint!.Value;
        Assert.Equal("crown:daggerfall", gateActor.Actor.Get<ActorRenderComponent>().Spec.LiveryKey);
        Assert.Equal(daggerfallTint, gateActor.Actor.Get<ActorRenderComponent>().Spec.Tint);
        Assert.Equal(daggerfallTint, session.ActiveAppearance.ActiveTintFor(gateId));
        Assert.Equal(gateOverrideBefore.FactionId, session.State.Npcs.Require(gateId).Appearance.FactionId);

        Assert.Null(streetActor.Actor.Get<ActorRenderComponent>().Spec.Tint);
        Assert.Equal(new Color(1f, 1f, 1f, 1f), session.ActiveAppearance.ActiveTintFor(streetId));
        Assert.Equal(betonyPeople.Id, session.State.Npcs.Require(streetId).Appearance.FactionId);

        // 2. Political board transfers Betony to Wayrest -> gate officer swaps live appearance & tint; street watch stays Betony!
        Board board = session.PoliticalBoard!.Board;
        Region betonyBoardRegion = board.Region("betony");
        board.SetAllegiance(betonyBoardRegion, board.Seat("wayrest"));
        board.Consolidate(betonyBoardRegion, board.Turn + Card.ConsolidationWeeks);

        session.GuardPosts.ApplyLiveGuardAppearances(betonySite, session.Sites.Projection.Inputs, placements, session.State.Actors, session.State.Npcs, session.ActiveAppearance);
        DaggerfallGuardAppearanceOverride gateOverrideWayrest = session.GuardPosts.ResolvePlacementAppearance(betonySite, gatePlacement);
        Color wayrestTint = gateOverrideWayrest.Tint!.Value;
        Assert.NotEqual(daggerfallTint, wayrestTint);
        Assert.Equal("crown:wayrest", gateActor.Actor.Get<ActorRenderComponent>().Spec.LiveryKey);
        Assert.Equal(wayrestTint, gateActor.Actor.Get<ActorRenderComponent>().Spec.Tint);
        Assert.Equal(wayrestTint, session.ActiveAppearance.ActiveTintFor(gateId));
        Assert.Equal(gateOverrideWayrest.FactionId, session.State.Npcs.Require(gateId).Appearance.FactionId);

        // Street watch MUST remain untouched local Betony look.
        Assert.Null(streetActor.Actor.Get<ActorRenderComponent>().Spec.Tint);
        Assert.Equal(new Color(1f, 1f, 1f, 1f), session.ActiveAppearance.ActiveTintFor(streetId));
        Assert.Equal(betonyPeople.Id, session.State.Npcs.Require(streetId).Appearance.FactionId);

        // 3. Independent fallback (Allegiance = null) -> gate officer swaps to Betony regional arms tint!
        board.SetAllegiance(betonyBoardRegion, null);
        board.Consolidate(betonyBoardRegion, board.Turn);
        session.GuardPosts.ApplyLiveGuardAppearances(betonySite, session.Sites.Projection.Inputs, placements, session.State.Actors, session.State.Npcs, session.ActiveAppearance);
        DaggerfallGuardAppearanceOverride gateOverrideIndep = session.GuardPosts.ResolvePlacementAppearance(betonySite, gatePlacement);
        Assert.Equal($"region:{BetonyRegion}", gateActor.Actor.Get<ActorRenderComponent>().Spec.LiveryKey);
        Assert.Equal(gateOverrideIndep.Tint, gateActor.Actor.Get<ActorRenderComponent>().Spec.Tint);
        Assert.NotEqual(wayrestTint, gateActor.Actor.Get<ActorRenderComponent>().Spec.Tint);
        Assert.Equal(gateOverrideIndep.Tint!.Value, session.ActiveAppearance.ActiveTintFor(gateId));
    }

    [Fact]
    public void Walled_city_gate_officers_hold_their_assigned_gate_post_instead_of_wandering_away()
    {
        using var fixture = new WatchPostsFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord walledCity = TestPayload.Definitions.Locations.Records.First(site =>
            site.Exterior is { Blocks.Count: >= 25 }
            && TestPayload.Definitions.Factions.Factions.Values.Any(f => f.Type == 15 && f.Region == site.Id.Region && f.Sprites.Count > 0));
        DaggerfallWorldProfileKey exteriorKey = DaggerfallWorldProfileIds.Exterior(walledCity.Id);
        DaggerfallFactionDefinition people = TestPayload.Definitions.Factions.Factions.Values
            .Where(f => f.Type == 15 && f.Region == walledCity.Id.Region && f.Sprites.Count > 0)
            .OrderBy(f => f.Id)
            .First();

        WorldPoint gatePos = new(16f, 0f, -16f);
        WorldPoint gateAnchor = new(16f, 0f, -12f);
        WorldPoint walkerPos = new(32f, 0f, -24f);
        DaggerfallPopulationPlacement gatePlacement = new("population/walled-gate-test/dynamic/000", gatePos, 182, 0, people.Id, 0, 21)
        {
            IsGuard = true,
            GuardPost = DaggerfallGuardPostKind.WalledCityGateOfficer,
            GatePostAnchor = gateAnchor,
        };
        DaggerfallPopulationPlacement streetPlacement = new("population/walled-gate-test/dynamic/001", walkerPos, 182, 0, people.Id, 0, 22)
        {
            IsGuard = true,
            GuardPost = DaggerfallGuardPostKind.StreetWatch,
        };

        // Construct a minimal exterior profile carrying these two placements and drive DaggerfallSourcePopulation.Step directly!
        DaggerfallSiteProfile baseProfile = session.Sites.Projection.Inputs;
        DaggerfallSiteProfile exteriorProfile = new(
            baseProfile.Project,
            baseProfile.Geometry,
            baseProfile.WorldAppearance,
            baseProfile.InitialLook,
            baseProfile.Materials,
            baseProfile.ActorSprites,
            baseProfile.MobileSprites,
            baseProfile.Audio,
            baseProfile.ClassicPresentation,
            site: walledCity.Id,
            doors: baseProfile.Doors,
            profileKind: DaggerfallWorldProfileKind.Exterior,
            logicalProfileId: exteriorKey.LogicalId,
            billboardSprites: baseProfile.BillboardSprites,
            population: [gatePlacement, streetPlacement]);

        DaggerfallNpcSite npcSite = new(walledCity.Id.Region, walledCity.Name, string.Empty, exteriorKey.LogicalId);
        long gateId = DaggerfallPeopleSlots.SeededSlotIdentity(walledCity.Id, $"{exteriorKey.LogicalId}/{gatePlacement.Id}");
        long streetId = DaggerfallPeopleSlots.SeededSlotIdentity(walledCity.Id, $"{exteriorKey.LogicalId}/{streetPlacement.Id}");
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, 21, people.Id);
        session.State.Npcs.RegisterDeterministic(gateId, DaggerfallNpcKind.Civilian, gatePlacement.Id, npcSite, look, "guard", ["talk"], homeProfile: exteriorKey);
        session.State.Npcs.RegisterDeterministic(streetId, DaggerfallNpcKind.Civilian, streetPlacement.Id, npcSite, look, "guard", ["talk"], homeProfile: exteriorKey);
        session.Roster.MaterializeDeterministicCivilian(session.State.Npcs.Require(gateId), ActorOrigin.Seeded(exteriorKey, gatePlacement.Id), gatePos, new ActorPose(gatePos, 0f), Vector3.Zero);
        session.Roster.MaterializeDeterministicCivilian(session.State.Npcs.Require(streetId), ActorOrigin.Seeded(exteriorKey, streetPlacement.Id), walkerPos, new ActorPose(walkerPos, 0f), Vector3.Zero);
        session.State.Npcs.Bind(gateId, exteriorKey);
        session.State.Npcs.Bind(streetId, exteriorKey);

        List<(long ActorId, IReadOnlyList<WorldPoint>? Route, float WaypointDistance)> observedSteps = [];
        DaggerfallSourcePopulation pop = new(
            session.State.Npcs,
            session.State.Actors,
            session.State.PlayerControl,
            TestPayload.Definitions,
            DaggerfallTuning.Defaults.CivilianWander,
            session.Civilians,
            () => exteriorProfile,
            () => exteriorKey,
            () => session.ActiveAppearance,
            () => true,
            () => true,
            point => point,
            point => point,
            _ => walledCity,
            () => true,
            () => false,
            _ => false,
            (actor, policy, step, dt) =>
            {
                observedSteps.Add((actor.DurableId, actor.Wander.Route, policy.WaypointDistance));
                // Simulate movement toward staged route or random wander direction.
                if (actor.Wander.Route is [var post])
                {
                    Vector3 delta = post.ToVector() - actor.Position.ToVector();
                    if (delta.Length() > 0.2f)
                    {
                        Vector3 next = actor.Position.ToVector() + Vector3.Normalize(delta) * MathF.Min(delta.Length(), policy.MovementSpeedUnitsPerSecond * dt);
                        actor.ApplyPose(new ActorPose(WorldPoint.From(next), actor.HeadingYawRadians));
                    }
                }
                else
                {
                    actor.ApplyPose(new ActorPose(WorldPoint.From(actor.Position.ToVector() + new Vector3(policy.MovementSpeedUnitsPerSecond * dt, 0f, 0f)), 0f));
                }
            },
            _ => { },
            session.State.ActorLedger,
            resolveGuardAppearance: session.GuardPosts.ResolvePlacementAppearance);

        ActorState gateActor = session.State.Actors.Get(gateId);
        ActorState streetActor = session.State.Actors.Get(streetId);

        // Step 1..20 while standing at gate: gate officer stays staged at gatePos and never wanders away,
        // while the street watchman patrols freely without a fixed gate tether.
        for (ulong step = 1; step <= 20; step++)
            pop.Step(step, 0.5f);

        Assert.NotNull(gateActor.Wander.Route);
        Assert.Single(gateActor.Wander.Route);
        Assert.Equal(gatePos, gateActor.Wander.Route[0]);
        Assert.True(gateActor.Position.HorizontalDistanceTo(gatePos) <= 0.25f,
            $"Gate officer drifted {gateActor.Position.HorizontalDistanceTo(gatePos):F2}m from gate post.");
        Assert.Null(streetActor.Wander.Route);
        Assert.True(streetActor.Position.HorizontalDistanceTo(walkerPos) > 5f,
            "Street watchman should continue patrolling rather than holding a fixed post.");

        // Displace the gate officer away from the gate and step again: the officer routes back to their gate post!
        gateActor.ApplyPose(new ActorPose(new WorldPoint(gatePos.X + 8f, gatePos.Y, gatePos.Z), 0f));
        for (ulong step = 21; step <= 40; step++)
            pop.Step(step, 0.5f);
        Assert.True(gateActor.Position.HorizontalDistanceTo(gatePos) <= 0.25f,
            $"Displaced gate officer failed to return to gate post (distance {gateActor.Position.HorizontalDistanceTo(gatePos):F2}m).");
    }

    [Fact]
    public void Special_posts_swap_livery_on_liege_change_while_street_watch_stay_regional_and_occupation_adds_garrison()
    {
        using var fixture = new WatchPostsFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord betonySite = TestPayload.Definitions.Locations.Records.First(site =>
            site.Id.Region == BetonyRegion && site.Exterior is { Blocks.Count: > 0 });

        // 1. Before board change: Betony answers to Daggerfall in the catalog.
        DaggerfallGuardLiveryResolution streetBefore = session.GuardPosts.ResolveLivery(betonySite, BetonyRegion, DaggerfallGuardPostKind.StreetWatch);
        DaggerfallGuardLiveryResolution gateBefore = session.GuardPosts.ResolveLivery(betonySite, BetonyRegion, DaggerfallGuardPostKind.WalledCityGateOfficer);
        DaggerfallGuardLiveryResolution palaceBefore = session.GuardPosts.ResolveLivery(betonySite, BetonyRegion, DaggerfallGuardPostKind.PalaceGuard);
        DaggerfallGuardLiveryResolution officeBefore = session.GuardPosts.ResolveLivery(betonySite, BetonyRegion, DaggerfallGuardPostKind.CrownOfficeGuard);

        Assert.False(streetBefore.UsesLiegeLivery);
        Assert.Equal("Betony", streetBefore.LiveryOwnerName);
        Assert.True(gateBefore.UsesLiegeLivery && gateBefore.IsOfficer);
        Assert.True(palaceBefore.UsesLiegeLivery && palaceBefore.IsOfficer);
        Assert.True(officeBefore.UsesLiegeLivery && officeBefore.IsOfficer);
        Assert.Equal("Daggerfall", gateBefore.LiveryOwnerName);
        Assert.Equal("Daggerfall", palaceBefore.LiveryOwnerName);
        Assert.Equal("Daggerfall", officeBefore.LiveryOwnerName);
        Assert.False(session.GuardPosts.IsRegionOccupied(BetonyRegion));
        Assert.Equal(0, session.GuardPosts.OccupationGarrisonCount(betonySite));

        // 2. Political board transfers Betony to Wayrest under active conquest consolidation (Turn < ContestableFrom)!
        Board board = session.PoliticalBoard!.Board;
        Region betonyBoardRegion = board.Region("betony");
        board.SetAllegiance(betonyBoardRegion, board.Seat("wayrest"));
        board.Consolidate(betonyBoardRegion, board.Turn + Card.ConsolidationWeeks);

        // Rank-and-file street watch STAY Betony; gate officers, palace guards, crown office guards & occupation garrison SWAP to Wayrest!
        DaggerfallGuardLiveryResolution streetAfter = session.GuardPosts.ResolveLivery(betonySite, BetonyRegion, DaggerfallGuardPostKind.StreetWatch);
        DaggerfallGuardLiveryResolution gateAfter = session.GuardPosts.ResolveLivery(betonySite, BetonyRegion, DaggerfallGuardPostKind.WalledCityGateOfficer);
        DaggerfallGuardLiveryResolution palaceAfter = session.GuardPosts.ResolveLivery(betonySite, BetonyRegion, DaggerfallGuardPostKind.PalaceGuard);
        DaggerfallGuardLiveryResolution garrisonAfter = session.GuardPosts.ResolveLivery(betonySite, BetonyRegion, DaggerfallGuardPostKind.OccupationGarrison);

        Assert.Equal("Betony", streetAfter.LiveryOwnerName);
        Assert.False(streetAfter.UsesLiegeLivery);
        Assert.Equal("Wayrest", gateAfter.LiveryOwnerName);
        Assert.Equal("wayrest", gateAfter.LiverySlug);
        Assert.Equal("Wayrest", palaceAfter.LiveryOwnerName);
        Assert.Equal("Wayrest", garrisonAfter.LiveryOwnerName);
        Assert.True(garrisonAfter.IsOccupyingGarrison);

        // Conquest occupation layers liege-livery garrison patrols on top of local watch with ZERO CrimeCrackdown tokens!
        Assert.True(session.GuardPosts.IsRegionOccupied(BetonyRegion));
        int garrisonCount = session.GuardPosts.OccupationGarrisonCount(betonySite);
        Assert.True(garrisonCount >= 1);

        DaggerfallPopulationPlacement garrisonSlot1 = new("occ-1", new WorldPoint(0, 0, 0), 182, 0, 518, 0, 1) { IsGuard = true, CrackdownGuard = 1 };
        Assert.Equal(DaggerfallGuardPostKind.OccupationGarrison, session.GuardPosts.ClassifyPlacement(betonySite, garrisonSlot1));
        DaggerfallScheduleDecision garrisonDecision = session.EvaluateExteriorPlacementSchedule(betonySite.Id, garrisonSlot1, 999_001L, 24);
        Assert.Equal(DaggerfallSchedulePlacement.ExteriorStreet, garrisonDecision.Placement);

        // 3. Independent region (Allegiance = null) falls back to the region's own arms for special posts!
        board.SetAllegiance(betonyBoardRegion, null);
        board.Consolidate(betonyBoardRegion, board.Turn);
        DaggerfallGuardLiveryResolution independentGate = session.GuardPosts.ResolveLivery(betonySite, BetonyRegion, DaggerfallGuardPostKind.WalledCityGateOfficer);
        Assert.Equal("Betony", independentGate.LiveryOwnerName);
        Assert.Equal("betony", independentGate.LiverySlug);
        Assert.False(session.GuardPosts.IsRegionOccupied(BetonyRegion));
        Assert.Equal(0, session.GuardPosts.OccupationGarrisonCount(betonySite));
    }

    [Fact]
    public void Actor_inspect_and_social_site_expose_guard_tier_post_kind_and_active_livery()
    {
        using var fixture = new WatchPostsFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/1/0", session.Sites.ActiveProfile.LogicalId);
        WorldPoint origin = session.State.PlayerControl.Position ?? new WorldPoint(0, 0, 0);

        // Register a live city guard NPC and inspect it via actor.inspect + social.site!
        long guardId = DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, $"{session.Sites.ActiveProfile.LogicalId}/t11167/guard");
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, 77, 0);
        session.State.Npcs.RegisterDeterministic(
            guardId, DaggerfallNpcKind.Civilian, "t11167/guard", npcSite, look, "guard", ["talk"],
            displayName: "Sergeant Aldric", homeProfile: session.Sites.ActiveProfile);
        session.Roster.MaterializeDeterministicCivilian(
            session.State.Npcs.Require(guardId),
            ActorOrigin.Seeded(session.Sites.ActiveProfile, "t11167/guard"),
            new WorldPoint(origin.X + 3f, origin.Y, origin.Z),
            new ActorPose(new WorldPoint(origin.X + 3f, origin.Y, origin.Z), 0f),
            Vector3.Zero);
        session.State.Npcs.Bind(guardId, session.Sites.ActiveProfile);

        IDeveloperActorCommands actorCmds = session.DebugCommands.OfType<IDeveloperActorCommands>().Single();
        DebugCommandResult inspectResult = actorCmds.Inspect(guardId.ToString());
        Assert.True(inspectResult.Succeeded, inspectResult.Message);
        using (JsonDocument doc = JsonDocument.Parse(inspectResult.Message))
        {
            JsonElement watch = doc.RootElement.GetProperty("watch");
            Assert.False(string.IsNullOrWhiteSpace(watch.GetProperty("tier").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(watch.GetProperty("tierLabel").GetString()));
            Assert.Equal("StreetWatch", watch.GetProperty("postKind").GetString());
            Assert.Equal("Street Watch", watch.GetProperty("postLabel").GetString());
            Assert.False(watch.GetProperty("usesLiegeLivery").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(watch.GetProperty("liveryOwner").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(watch.GetProperty("liverySlug").GetString()));
        }

        // Verify social.site exposes the site watch summary and loaded guard post list!
        IDeveloperSocialCommands socialCmds = session.DebugCommands.OfType<IDeveloperSocialCommands>().Single();
        DebugCommandResult siteResult = socialCmds.Site();
        Assert.True(siteResult.Succeeded, siteResult.Message);
        using (JsonDocument doc = JsonDocument.Parse(siteResult.Message))
        {
            JsonElement watch = doc.RootElement.GetProperty("watch");
            Assert.False(string.IsNullOrWhiteSpace(watch.GetProperty("tier").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(watch.GetProperty("localLiveryOwner").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(watch.GetProperty("liegeLiveryOwner").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(watch.GetProperty("liegeLiverySlug").GetString()));
            JsonElement posts = watch.GetProperty("posts");
            Assert.Contains(posts.EnumerateArray(), p => p.GetProperty("durableId").GetInt64() == guardId);
        }
    }

    private static IEnumerable<(int X, int Z)> ClearCells(DaggerfallSiteRecord site)
    {
        foreach (DaggerfallSiteBlock block in site.Exterior!.Blocks)
        {
            for (int x = 0; x < 64; x += 8)
                for (int z = 0; z < 64; z += 8)
                    yield return (block.X * 64 + x, block.Y * 64 + z);
        }
    }

    private sealed class WatchPostsFixture : IDisposable
    {
        private readonly DaggerfallSiteProfile _profile;
        private readonly List<string> _releases = [];
        private readonly ContentFake _content;
        private readonly SpatialFake _spatial;

        internal DaggerfallSession Session { get; }

        internal WatchPostsFixture()
        {
            _profile = ReadInputs(TestData.RepositoryRoot);
            _content = new ContentFake(_releases);
            PopulateContent(_content, _profile);
            _spatial = SpatialFake.Create(_profile.Geometry.Spatial[0].Sha256, _releases);
            EngineContextFake engine = EngineContextFake.Create(_content, _spatial.Service, new AppearanceFake(_releases), PerceptionFake.Create().Service);
            Session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, _profile, DaggerfallTuning.Defaults));
        }

        public void Dispose() => Session.Dispose();
    }
}
