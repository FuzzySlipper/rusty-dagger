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

        // Unwalled hamlet (no gates) keeps purely local rank-and-file StreetWatch patrols (0 gate officers).
        IReadOnlyList<DaggerfallPopulationPlacement> hamletPop = DaggerfallExteriorPopulation.Create(
            TestPayload.Definitions, unwalledHamlet, ClearCells(unwalledHamlet), guardTiers: tiers, gateAnchors: null);
        Assert.DoesNotContain(hamletPop, p => p.GuardPost == DaggerfallGuardPostKind.WalledCityGateOfficer);
        Assert.Contains(hamletPop, p => p.CrackdownGuard == 0 && p.IsGuard && p.GuardPost == DaggerfallGuardPostKind.StreetWatch);
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
