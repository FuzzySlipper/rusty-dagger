using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11166: 3-tier settlement watch tuning (Hamlet / Town / Capital) wired through
/// DaggerfallExteriorPopulation, DaggerfallCrackdownGuards, DaggerfallActorRoster,
/// DaggerfallClassEnemyEquipmentPolicy, and DaggerfallLaw crime-response spawns.
/// </summary>
public sealed class SettlementWatchTierTests
{
    [Fact]
    public void Watch_tier_tuning_loads_from_defaults_and_classifies_hamlet_town_and_capital_sites()
    {
        byte[] payload = File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/tuning/daggerfall.defaults/payload.json"));
        DaggerfallTuning loaded = DaggerfallTuning.Read(payload);
        DaggerfallGuardTierTuning tiers = loaded.Law.GuardTiers;

        Assert.Equal(DaggerfallGuardTierTuning.Default.TownMinimumBlocks, tiers.TownMinimumBlocks);
        Assert.Equal(DaggerfallGuardTierTuning.Default.CapitalMinimumBlocks, tiers.CapitalMinimumBlocks);
        Assert.True(tiers.Hamlet.BaselinePatrolGuards < tiers.Town.BaselinePatrolGuards);
        Assert.True(tiers.Town.BaselinePatrolGuards < tiers.Capital.BaselinePatrolGuards);
        Assert.True(tiers.Hamlet.MaxCrackdownReserves < tiers.Town.MaxCrackdownReserves);
        Assert.True(tiers.Town.MaxCrackdownReserves < tiers.Capital.MaxCrackdownReserves);
        Assert.True(tiers.Hamlet.HealthScale < tiers.Town.HealthScale);
        Assert.True(tiers.Town.HealthScale < tiers.Capital.HealthScale);
        Assert.True(tiers.Hamlet.MaximumLevelOffset < tiers.Capital.MinimumLevelOffset);

        DaggerfallSiteRecord hamlet = FindSiteOfTier(tiers, DaggerfallSettlementWatchTier.Hamlet);
        DaggerfallSiteRecord town = FindSiteOfTier(tiers, DaggerfallSettlementWatchTier.Town);
        DaggerfallSiteRecord capital = FindSiteOfTier(tiers, DaggerfallSettlementWatchTier.Capital);

        Assert.Equal(DaggerfallSettlementWatchTier.Hamlet, tiers.ResolveTier(hamlet));
        Assert.Equal(DaggerfallSettlementWatchTier.Town, tiers.ResolveTier(town));
        Assert.Equal(DaggerfallSettlementWatchTier.Capital, tiers.ResolveTier(capital));
    }

    [Fact]
    public void Exterior_population_patrol_density_and_crackdown_reserves_scale_by_settlement_watch_tier()
    {
        DaggerfallGuardTierTuning tiers = DaggerfallGuardTierTuning.Default;
        DaggerfallSiteRecord hamlet = FindSiteOfTier(tiers, DaggerfallSettlementWatchTier.Hamlet);
        DaggerfallSiteRecord town = FindSiteOfTier(tiers, DaggerfallSettlementWatchTier.Town);
        DaggerfallSiteRecord capital = FindSiteOfTier(tiers, DaggerfallSettlementWatchTier.Capital);

        IReadOnlyList<DaggerfallPopulationPlacement> hamletPop = DaggerfallExteriorPopulation.Create(
            TestPayload.Definitions, hamlet, ClearCells(hamlet), guardTiers: tiers);
        IReadOnlyList<DaggerfallPopulationPlacement> townPop = DaggerfallExteriorPopulation.Create(
            TestPayload.Definitions, town, ClearCells(town), guardTiers: tiers);
        IReadOnlyList<DaggerfallPopulationPlacement> capitalPop = DaggerfallExteriorPopulation.Create(
            TestPayload.Definitions, capital, ClearCells(capital), guardTiers: tiers);

        int hamletPatrols = hamletPop.Count(p => p.CrackdownGuard == 0 && p.IsGuard);
        int townPatrols = townPop.Count(p => p.CrackdownGuard == 0 && p.IsGuard);
        int capitalPatrols = capitalPop.Count(p => p.CrackdownGuard == 0 && p.IsGuard);

        Assert.Equal(tiers.Hamlet.BaselinePatrolGuards, hamletPatrols);
        Assert.Equal(tiers.Town.BaselinePatrolGuards, townPatrols);
        Assert.Equal(tiers.Capital.BaselinePatrolGuards, capitalPatrols);

        // Slot 0 remains the canonical WatchCaptainSlotId across all three tiers.
        Assert.Equal(DaggerfallExteriorPopulation.WatchCaptainSlotId(hamlet), hamletPop[0].Id);
        Assert.True(hamletPop[0].IsGuard);
        Assert.Equal(DaggerfallExteriorPopulation.WatchCaptainSlotId(town), townPop[0].Id);
        Assert.True(townPop[0].IsGuard);
        Assert.Equal(DaggerfallExteriorPopulation.WatchCaptainSlotId(capital), capitalPop[0].Id);
        Assert.True(capitalPop[0].IsGuard);

        // Under a max-severity crackdown, each settlement calls out reserves up to its tier cap.
        DaggerfallWorldConditions conditions = new();
        conditions.Set(new DaggerfallWorldConditionToken(
            "board:test:crackdown",
            DaggerfallWorldConditionKind.CrimeCrackdown,
            Region: hamlet.Id.Region,
            SubjectTag: DaggerfallCrackdownGuards.CrackdownTag,
            Severity: DaggerfallWorldConditionToken.MaxSeverity).Validate());
        conditions.Set(new DaggerfallWorldConditionToken(
            "board:test:crackdown:town",
            DaggerfallWorldConditionKind.CrimeCrackdown,
            Region: town.Id.Region,
            SubjectTag: DaggerfallCrackdownGuards.CrackdownTag,
            Severity: DaggerfallWorldConditionToken.MaxSeverity).Validate());
        conditions.Set(new DaggerfallWorldConditionToken(
            "board:test:crackdown:capital",
            DaggerfallWorldConditionKind.CrimeCrackdown,
            Region: capital.Id.Region,
            SubjectTag: DaggerfallCrackdownGuards.CrackdownTag,
            Severity: DaggerfallWorldConditionToken.MaxSeverity).Validate());

        // Under a max-severity crackdown with CrackdownGuardsPerSeverity = 2 (6 regional reserves requested),
        // each settlement calls out reserves up to its own watch tier cap (Hamlet 2, Town 4, Capital 6).
        DaggerfallLawTuning strongCrackdownLaw = (DaggerfallTuning.Defaults.Law with { CrackdownGuardsPerSeverity = 2 }).Validate();
        DaggerfallCrackdownGuards crackdown = new(conditions, () => 0, strongCrackdownLaw);
        Assert.Equal(tiers.Hamlet.MaxCrackdownReserves, crackdown.For(hamlet.Id.Region, hamlet));
        Assert.Equal(tiers.Town.MaxCrackdownReserves, crackdown.For(town.Id.Region, town));
        Assert.Equal(tiers.Capital.MaxCrackdownReserves, crackdown.For(capital.Id.Region, capital));

        DaggerfallPopulationPlacement[] hamletReserves = [.. hamletPop.Where(p => p.CrackdownGuard > 0)];
        Assert.Equal(tiers.Hamlet.MaxCrackdownReserves, hamletReserves.Count(p => !crackdown.HeldBack(hamlet.Id.Region, p, hamlet)));
        DaggerfallPopulationPlacement[] capitalReserves = [.. capitalPop.Where(p => p.CrackdownGuard > 0)];
        Assert.Equal(tiers.Capital.MaxCrackdownReserves, capitalReserves.Count(p => !crackdown.HeldBack(capital.Id.Region, p, capital)));
    }

    [Fact]
    public void Crime_response_and_promoted_guards_scale_stats_and_equipment_loadout_across_tiers()
    {
        using var fixture = new WatchSessionFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallGuardTierTuning tiers = DaggerfallTuning.Defaults.Law.GuardTiers;
        session.State.Progression.AdvanceTo(0, 5); // Level 5 player

        WorldPoint origin = session.State.PlayerControl.Position ?? new WorldPoint(0, 0, 0);
        long hamletGuardId = session.Law.AdmitCityWatch(new ActorPose(new WorldPoint(origin.X + 3f, origin.Y, origin.Z), 0f), tiers.Hamlet);
        long townGuardId = session.Law.AdmitCityWatch(new ActorPose(new WorldPoint(origin.X + 5f, origin.Y, origin.Z), 0f), tiers.Town);
        long capitalGuardId = session.Law.AdmitCityWatch(new ActorPose(new WorldPoint(origin.X + 7f, origin.Y, origin.Z), 0f), tiers.Capital);

        DaggerfallActorDefinition hamletDef = session.Roster.Definitions[hamletGuardId];
        DaggerfallActorDefinition townDef = session.Roster.Definitions[townGuardId];
        DaggerfallActorDefinition capitalDef = session.Roster.Definitions[capitalGuardId];

        // Hamlet guards remain stronger than the player (crime deterrents) while strictly weaker than Town and Capital guards.
        Assert.InRange(hamletDef.Level!.Value, 5 + tiers.Hamlet.MinimumLevelOffset, 5 + tiers.Hamlet.MaximumLevelOffset);
        Assert.InRange(townDef.Level!.Value, 5 + tiers.Town.MinimumLevelOffset, 5 + tiers.Town.MaximumLevelOffset);
        Assert.InRange(capitalDef.Level!.Value, 5 + tiers.Capital.MinimumLevelOffset, 5 + tiers.Capital.MaximumLevelOffset);
        Assert.True(hamletDef.Level < townDef.Level && townDef.Level <= capitalDef.Level);

        double hamletHp = session.State.Actors.Get(hamletGuardId).Stats.GetTrack(TrackId.Parse("health")).Maximum.Value;
        double capitalHp = session.State.Actors.Get(capitalGuardId).Stats.GetTrack(TrackId.Parse("health")).Maximum.Value;
        Assert.True(hamletHp < capitalHp, $"Expected hamlet guard max HP ({hamletHp}) < capital guard max HP ({capitalHp}).");

        // Verify tier-specific weapon and armor material loadouts on spawned guards!
        AssertGuardLoadoutMatchesTier(session, hamletGuardId, tiers.Hamlet);
        AssertGuardLoadoutMatchesTier(session, townGuardId, tiers.Town);
        AssertGuardLoadoutMatchesTier(session, capitalGuardId, tiers.Capital);

        // Promoting an existing calm town guard preserves durable ID and applies tier-scaled stats & gear!
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/1/0", session.Sites.ActiveProfile.LogicalId);
        long calmGuardId = DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, $"{session.Sites.ActiveProfile.LogicalId}/t11166/calm-guard");
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, 42, 0);
        session.State.Npcs.RegisterDeterministic(
            calmGuardId, DaggerfallNpcKind.Civilian, "t11166/calm-guard", npcSite, look, "guard", ["talk"],
            displayName: "Watchman Berek", homeProfile: session.Sites.ActiveProfile);
        session.Roster.MaterializeDeterministicCivilian(
            session.State.Npcs.Require(calmGuardId),
            ActorOrigin.Seeded(session.Sites.ActiveProfile, "t11166/calm-guard"),
            new WorldPoint(origin.X + 4f, origin.Y, origin.Z),
            new ActorPose(new WorldPoint(origin.X + 4f, origin.Y, origin.Z), 0f),
            Vector3.Zero);
        session.State.Npcs.Bind(calmGuardId, session.Sites.ActiveProfile);

        long? promotedId = session.Law.PromoteCityWatch(calmGuardId, tiers.Capital);
        Assert.Equal(calmGuardId, promotedId);
        DaggerfallActorDefinition promotedDef = session.Roster.Definitions[calmGuardId];
        Assert.InRange(promotedDef.Level!.Value, 5 + tiers.Capital.MinimumLevelOffset, 5 + tiers.Capital.MaximumLevelOffset);
        AssertGuardLoadoutMatchesTier(session, calmGuardId, tiers.Capital);
    }

    private static void AssertGuardLoadoutMatchesTier(DaggerfallSession session, long guardId, DaggerfallGuardTierProfile tier)
    {
        MechanicsEquipmentCoordinator eq = session.State.ActorInventories.EquipmentFor(guardId);
        IReadOnlyList<WorldRpg.Kit.Inventory.EquipmentAssignment> assignments = eq.Read().Assignments;
        Assert.NotEmpty(assignments);

        bool hasWeapon = false;
        foreach (WorldRpg.Kit.Inventory.EquipmentAssignment assignment in assignments)
        {
            ulong itemId = eq.GetDurableItemId(new EntityId(assignment.Item.EntityId)).Value;
            DaggerfallItemInstanceMetadata meta = session.State.ItemInstances.RequireUnique(itemId);
            DaggerfallItemDefinition itemDef = TestPayload.Definitions.RequireItem(new DaggerfallItemId(meta.ItemId));
            if (assignment.Slot.Value == "right-hand")
            {
                hasWeapon = true;
                Assert.Contains(meta.Material, tier.WeaponMaterials);
            }
            else if (itemDef.Armor is not null)
            {
                Assert.Contains(meta.Material, tier.ArmorMaterials);
            }
        }
        Assert.True(hasWeapon, $"Watchman {guardId} ({tier.Label}) must equip a primary right-hand weapon.");
    }

    private static DaggerfallSiteRecord FindSiteOfTier(DaggerfallGuardTierTuning tiers, DaggerfallSettlementWatchTier target) =>
        TestPayload.Definitions.Locations.Records.First(site =>
            site.Exterior is { Blocks.Count: > 0 }
            && TestPayload.Definitions.Factions.Factions.Values.Any(f => f.Type == 15 && f.Region == site.Id.Region && f.Sprites.Count > 0)
            && tiers.ResolveTier(site) == target);

    private static IEnumerable<(int X, int Z)> ClearCells(DaggerfallSiteRecord site)
    {
        foreach (DaggerfallSiteBlock block in site.Exterior!.Blocks)
        {
            for (int x = 0; x < 64; x += 8)
                for (int z = 0; z < 64; z += 8)
                    yield return (block.X * 64 + x, block.Y * 64 + z);
        }
    }

    private sealed class WatchSessionFixture : IDisposable
    {
        private readonly DaggerfallSiteProfile _profile;
        private readonly List<string> _releases = [];
        private readonly ContentFake _content;
        private readonly SpatialFake _spatial;

        internal DaggerfallSession Session { get; }

        internal WatchSessionFixture()
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
