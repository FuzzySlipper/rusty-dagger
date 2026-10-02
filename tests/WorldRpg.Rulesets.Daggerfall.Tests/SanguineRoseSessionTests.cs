using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class SanguineRoseSessionTests
{
    [Fact]
    public void Ordinary_inventory_use_queries_twelve_world_metres_and_spawns_one_real_allied_daedroth()
    {
        using Fixture f = new();
        int count = f.Session.State.Actors.All.Count();
        f.Use();
        Assert.Equal(count + 1, f.Session.State.Actors.All.Count());
        long ally = Assert.Single(f.Allies());
        Assert.Equal(27, f.Session.DefinitionsByActor[ally].MobileId);
        Assert.Equal("daedroth", f.Session.State.Actors.Get(ally).Actor.TypeId.Value);
        Assert.Equal(1400, f.Condition);
        Assert.Contains("allied Daedroth", f.Message);
        var query = Assert.Single(f.Perception.Requests.Where(request => request.Observers.Span[0].MaximumDistance == 12d));
        Assert.Equal(-1d, query.Observers.Span[0].MinimumFacingCosine);
        Assert.DoesNotContain(query.Targets.ToArray(), target => target.Entity == 1);
        Assert.NotEmpty(f.Spatial.FloorProbes);
        Assert.NotEmpty(f.Spatial.OverlapRequests);
        Assert.All(f.Spatial.FloorProbes.Where(probe => probe.Direction.Y == 0f), probe =>
            Assert.DoesNotContain(probe.Entities.ToArray(), collider => collider.Entity == f.Session.State.Actors.Player.Actor.Entity.Value));
        Assert.InRange(Vector3.Distance(f.Session.State.PlayerControl.Position!.Value.ToVector(), f.Session.State.Actors.Get(ally).Position.ToVector()), 4f, 20.2f);
        using var restored = f.Restore();
        Assert.Equal("player-ally", restored.DefinitionsByActor[ally].Team);
        Assert.Equal(f.Session.State.Actors.Get(ally).Pose, restored.State.Actors.Get(ally).Pose);
        Assert.Equal(1400, restored.State.ItemInstances.RequireUnique(f.Source).CurrentCondition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_ground_or_blocked_clearance_refuses_without_actor_or_source_mutation(bool blocked)
    {
        using Fixture f = new();
        if (blocked) f.Spatial.OverlapHit = _ => default(SpatialHit) with { Present = true, Converged = true };
        else f.Spatial.FloorHit = _ => default;
        int count = f.Session.State.Actors.All.Count();
        f.Use();
        Assert.Equal(count, f.Session.State.Actors.All.Count());
        Assert.Equal(1500, f.Condition);
        Assert.Empty(f.Allies());
        Assert.Contains("no clear ground", f.Message);
    }

    [Fact]
    public void Unpublished_summon_appearance_is_a_clear_unavailable_site_outcome()
    {
        using Fixture f = new(appearance: false);
        f.Use();
        Assert.Empty(f.Allies());
        Assert.Equal(1500, f.Condition);
        Assert.Contains("cannot display", f.Message);
    }

    [Fact]
    public void Outside_radius_and_existing_player_allies_do_not_satisfy_the_enemy_check()
    {
        using Fixture f = new();
        f.Session.State.Actors.Get(f.Enemy).ApplyPose(new(new WorldPoint(0, 0, -13), 0));
        f.Use();
        Assert.Equal("No monsters nearby.", f.Message);
        Assert.Equal(1500, f.Condition);
        f.Session.State.Actors.Get(f.Enemy).ApplyPose(new(new WorldPoint(0, 0, -6), 0));
        f.Use();
        long ally = Assert.Single(f.Allies());
        f.Session.State.Actors.Get(f.Enemy).ApplyPose(new(new WorldPoint(0, 0, -100), 0));
        f.Session.State.Actors.Get(ally).ApplyPose(new(new WorldPoint(0, 0, -4), 0));
        f.Use();
        Assert.Equal("No monsters nearby.", f.Message);
        Assert.Single(f.Allies());
        Assert.Equal(1400, f.Condition);
    }

    [Fact]
    public void Source_removal_refuses_reuse_but_existing_summon_has_independent_site_lifetime()
    {
        using Fixture f = new();
        f.Use();
        long ally = Assert.Single(f.Allies());
        f.Session.State.Inventory.Destroy(f.Item);
        f.Session.State.ItemInstances.RemoveUnique(f.Source);
        f.Session.State.Actors.Entities.Destroy(new(WorldRpg.Kit.World.DurableIdentityKind.Item, f.Source));
        f.Session.RemoveUniqueItemIdentity(f.Source);
        f.Use();
        Assert.Contains("Inventory changed", f.Message);
        f.Update();
        f.Use();
        Assert.Single(f.Allies());
        Assert.Contains("no longer", f.Message);
        using var restored = f.Restore();
        Assert.Equal("player-ally", restored.DefinitionsByActor[ally].Team);
        Assert.False(restored.State.ItemInstances.ContainsUnique(f.Source));
        var entity = f.Session.State.Actors.Get(ally).Actor.Entity;
        f.Session.RetireActor(ally);
        Assert.False(f.Session.State.Actors.Store.IsAlive(entity));
        Assert.False(f.Session.DefinitionsByActor.ContainsKey(ally));
        Assert.DoesNotContain(ally, DaggerfallSavePayload.Read(f.Session.CaptureSave()).DynamicActors.Select(actor => actor.EntityId));
        using var retired = f.Restore();
        Assert.False(retired.State.Actors.TryGet(ally, out _));
    }

    [Fact]
    public void Durability_is_charged_in_classic_units_and_a_broken_source_cannot_summon()
    {
        using Fixture f = new();
        _ = f.Session.ItemCondition.Damage(f.Item, 1450);
        f.Update();
        f.Use();
        Assert.Single(f.Allies());
        Assert.Equal(0, f.Condition);
        Assert.Contains("broke", f.Message);
        f.Use();
        Assert.Single(f.Allies());
        Assert.Contains("broken", f.Message);
    }

    [Fact]
    public void Ally_uses_existing_pursuit_and_attacks_an_enemy_without_targeting_the_player()
    {
        using Fixture f = new();
        f.Use();
        long ally = Assert.Single(f.Allies());
        f.Session.State.Actors.Get(ally).ApplyPose(new(new WorldPoint(0, 0, -5.9f), 0));
        var senses = f.Session.State.Actors.Get(f.Enemy).Actor.Get<DaggerfallEnemyPerceptionMemory>();
        senses.Pacified = false; senses.ForcedHostile = true;
        f.Perception.Responder = request => f.Respond(request, combat: true);
        f.Session.State.Kit.Rules.RegisterAction(f.Session.DefinitionsByActor[ally].ActionId!, new ForceHit());
        double playerHealth = Health(f.Session.State.Actors.Player.Actor);
        double enemyHealth = Health(f.Session.State.Actors.Get(f.Enemy).Actor);
        // EnemyBasics Daedroth alternate 2 is [4,-1,5,0]; the minimum draw selects its 33% branch.
        Assert.Equal(new[] { 4, -1, 5, 0 }, f.Inputs.MobileSprites[27].AttackSequences[1].SourceFrames);
        f.Appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: 2);
        f.Update();
        double after = Health(f.Session.State.Actors.Get(f.Enemy).Actor);
        f.Update();
        Assert.Equal(after, Health(f.Session.State.Actors.Get(f.Enemy).Actor));
        Assert.Equal(playerHealth, Health(f.Session.State.Actors.Player.Actor));
        Assert.True(after < enemyHealth);
        Assert.Equal(EnemyBehaviorState.Attack, f.Session.LastEnemyBehavior[ally].State);
        Assert.Contains(f.Perception.Requests, request => request.Observers.Span[0].Entity == (ulong)ally
            && request.Targets.ToArray().Any(target => target.Entity == (ulong)f.Enemy));
        Assert.DoesNotContain(f.Perception.Requests, request => request.Observers.Span[0].Entity == (ulong)ally
            && request.Targets.ToArray().Any(target => target.Entity == 1));
        Assert.DoesNotContain("hit you", f.Session.Presentation.LastOutcome, StringComparison.Ordinal);
    }

    [Fact]
    public void Unloaded_summon_resumes_once_when_returning_to_its_saved_site()
    {
        using Fixture f = new();
        f.Use();
        long ally = Assert.Single(f.Allies());
        ActorPose pose = f.Session.State.Actors.Get(ally).Pose;
        var profiles = new DaggerfallSiteProfiles([f.Inputs, f.Castle]);
        f.Session.AdmitSiteProfiles(profiles);
        Assert.True(f.Session.TryTransitionTo(f.Castle.ProfileKey));
        Assert.False(f.Session.State.Actors.TryGet(ally, out _));
        using var restored = f.Restore(profiles);
        Assert.True(restored.TryTransitionTo(f.Inputs.ProfileKey));
        Assert.Equal(pose, restored.State.Actors.Get(ally).Pose);
        Assert.Equal("player-ally", restored.DefinitionsByActor[ally].Team);
        Assert.Equal(27, restored.DefinitionsByActor[ally].MobileId);
        Assert.Single(restored.DefinitionsByActor.Where(entry => entry.Value.Team == "player-ally"));
    }

    [Fact]
    public void Ally_selection_reads_later_perception_pages_before_choosing_the_nearest_enemy()
    {
        using Fixture f = new(); f.Use();
        long ally = Assert.Single(f.Allies());
        long near = f.Session.SpawnActor("orc", new(new WorldPoint(0, 0, -5.8f), 0));
        f.Session.State.Actors.Get(ally).ApplyPose(new(new WorldPoint(0, 0, -5.9f), 0));
        f.Session.State.Actors.Get(f.Enemy).ApplyPose(new(new WorldPoint(0, 0, -10), 0));
        foreach (long target in new[] { near, f.Enemy })
        {
            var memory = f.Session.State.Actors.Get(target).Actor.Get<DaggerfallEnemyPerceptionMemory>();
            memory.Pacified = false; memory.ForcedHostile = true;
        }
        f.Perception.Responder = request =>
        {
            if (request.Observers.Span[0].Entity != (ulong)ally || request.Targets.Length == 1)
                return f.Respond(request, combat: true);
            return request.PairCursor == 0
                ? Receipt(new PerceptionPair((ulong)ally, (ulong)f.Enemy, 4.1, 1, PerceptionPairKind.Visible, 1)) with
                    { PairTotal = 2, HasNextPairCursor = true, NextPairCursor = 1, ProjectionIdentity = 91 }
                : Receipt(new PerceptionPair((ulong)ally, (ulong)near, .1, 1, PerceptionPairKind.Visible, 1)) with
                    { PairTotal = 2, ProjectionIdentity = 91 };
        };
        f.Update();
        Assert.Equal((ulong)near, Assert.Single(f.Session.LastEnemyBehavior[ally].Visibility!.Value.Pairs.ToArray()).Target);
        Assert.Contains(f.Perception.Requests, request => request.PairCursor == 1 && request.ExpectedProjectionIdentity == 91);
    }

    [Fact]
    public void Wabbajack_replacement_keeps_the_summons_current_allegiance_through_save()
    {
        using Fixture f = new(); f.Use();
        long ally = Assert.Single(f.Allies());
        var created = new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random)
            .Create(new("Magic", "rose-wabbajack", DaggerfallItemOwner.Player, MagicItemKey: "magic-item.0006"));
        var identity = f.Session.UniqueItemAllocator.AllocateReference();
        var weapon = f.Session.State.Equipment.Materialize(identity, created.Item);
        f.Session.State.ItemInstances.RegisterUnique(identity.Value, created.Metadata);
        Assert.Equal(EquipmentMoveOutcome.Applied,
            f.Session.EquipmentMoves.MoveToSlot(weapon, new WorldRpg.Kit.Inventory.EquipmentSlotId("right-hand")).Outcome);
        f.Session.State.Kit.Rules.RegisterAction(f.Session.DefinitionsByActor[1].ActionId!, new ContactMiss());
        f.Session.ResolveExplicitMelee(new(1, ally, 1, 10000, .125));
        Assert.NotNull(DaggerfallWabbajack.DefinitionOf(f.Session.State.Actors.Get(ally).Actor));
        Assert.Equal("player-ally", f.Session.DefinitionsByActor[ally].Team);
        using var restored = f.Restore();
        Assert.Equal("player-ally", restored.DefinitionsByActor[ally].Team);
        Assert.Equal(f.Session.DefinitionsByActor[ally].Id, restored.DefinitionsByActor[ally].Id);
    }

    private sealed class ContactMiss : ICombatContribution
    {
        public void Hit(TryHitEvent value) => value.Hit = false;
    }

    private static double Health(Rusty.Engine.Entities.Actor actor) => actor.Get<StatsComponent>().GetTrack(TrackId.Parse("health")).Current;
    private sealed class ForceHit : ICombatContribution
    {
        public void Hit(TryHitEvent value) => value.Hit = true;
        public void Damage(DamageEvent value) => value.Damage = 1;
    }

    internal sealed class Fixture : IDisposable
    {
        internal readonly DaggerfallSiteProfile Inputs;
        internal readonly DaggerfallSiteProfile Castle;
        internal readonly DaggerfallSessionComposition Composition;
        internal readonly EngineContextFake Engine;
        internal readonly SpatialFake Spatial;
        internal readonly PerceptionFake Perception;
        internal readonly AppearanceFake Appearance;
        internal readonly DaggerfallSession Session;
        internal readonly KitUniqueInventoryItem Item;
        internal readonly ulong Source;
        internal readonly long Enemy;
        private ulong _step;
        internal int Condition => Session.State.ItemInstances.RequireUnique(Source).CurrentCondition;
        internal string Message => Engine.PublishedNested("inventory", "message")!;
        internal Fixture(bool appearance = true, string magicItemKey = "magic-item.0004")
        {
            var inputs = ReadInputs(TestData.RepositoryRoot);
            Inputs = appearance ? inputs : new DaggerfallSiteProfile(inputs.Project, inputs.SpatialArtifact, inputs.StaticMesh,
                inputs.WorldAppearance, inputs.InitialLook, inputs.Materials, inputs.ActorSprites,
                inputs.MobileSprites.Where(entry => entry.Key != 27).ToDictionary(), inputs.Audio, inputs.ClassicPresentation,
                inputs.Site, inputs.Doors, inputs.ProfileKind, inputs.ProfileKey.LogicalId, inputs.Portals, inputs.Anchors.Values.ToArray(),
                inputs.Lights, inputs.GroundContainerSprite, inputs.DungeonMap, inputs.DungeonActions, inputs.DungeonActionModels,
                inputs.InteriorBuilding, inputs.Music, inputs.AudioBundle);
            Castle = DaggerfallSiteContent.Read(FullContent(TestData.RepositoryRoot),
                File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), TestPayload.Definitions);
            var identity = GameCompositionResolver.Resolve(FullContent(TestData.RepositoryRoot), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
            Composition = new(TestPayload.Definitions, Inputs, DaggerfallTuning.Defaults, identity);
            (Engine, Spatial, Perception, Appearance) = CreateEngine();
            Session = DaggerfallSession.StartNew(Engine.Context, Composition);
            Session.State.PlayerControl.MoveTo(new Vector3(0, 2, 0));
            foreach (var actor in Session.State.Actors.All) actor.ApplyPose(new(new WorldPoint(0, 0, -100), 0));
            Enemy = Inputs.Project.Actors.Values.First(actor => TestPayload.Definitions.RequireActor(actor.ActorId).MobileId == 7).EntityId;
            Session.State.Actors.Get(Enemy).ApplyPose(new(new WorldPoint(0, 0, -6), 0));
            Perception.Responder = request => Respond(request);
            var created = new DaggerfallItemFactory(TestPayload.Definitions, Engine.Context.Random)
                .Create(new("Magic", "sanguine-rose", DaggerfallItemOwner.Player, MagicItemKey: magicItemKey));
            var identityItem = Session.UniqueItemAllocator.AllocateReference(); Source = identityItem.Value;
            Item = Session.State.Equipment.Materialize(identityItem, created.Item);
            Session.State.ItemInstances.RegisterUnique(Source, created.Metadata);
            Update();

        }
        internal IEnumerable<long> Allies() => Session.DefinitionsByActor.Where(entry => entry.Value.Team == "player-ally").Select(entry => entry.Key);
        internal PerceptionReadoutResult Respond(PerceptionQueryRequest request, bool combat = false)
        {
            var observer = request.Observers.Span[0];
            var pairs = request.Targets.ToArray().Select(target => new PerceptionPair(observer.Entity, target.Entity,
                    Vector3.Distance(observer.Origin, target.Center), 1d,
                    observer.Entity == 1 || combat && Allies().Contains((long)observer.Entity) ? PerceptionPairKind.Visible : PerceptionPairKind.Occluded, 1d))
                .Where(pair => pair.Distance <= observer.MaximumDistance).ToArray();
            return Receipt(pairs);
        }
        private (EngineContextFake, SpatialFake, PerceptionFake, AppearanceFake) CreateEngine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, Inputs); PopulateContent(content, Castle);
            var spatial = SpatialFake.Create(Inputs.SpatialArtifact.Sha256, releases); spatial.KeepPosition = true;
            spatial.FloorHit = request => request.Direction.Y < 0f ? default(SpatialHit) with
            { Present = true, Point = new(request.Origin.X, 0, request.Origin.Z), Normal = Vector3.UnitY, Converged = true } : default;
            var perception = PerceptionFake.Create();
            var appearance = new AppearanceFake(releases);
            return (EngineContextFake.Create(content, spatial.Service, appearance, perception.Service), spatial, perception, appearance);
        }
        internal DaggerfallSession Restore(DaggerfallSiteProfiles? profiles = null) => DaggerfallSession.Restore(CreateEngine().Item1.Context,
            profiles is null ? Composition : Composition with { Profiles = profiles }, Session.CaptureSave());
        internal void Use() => Submit(new { action = "inventory-use", revision = Engine.PublishedNested("inventory", "revision"), item = $"unique:{Item.EntityId}" });
        internal void Update() => Session.Update(new ProductUpdate(OuterUpdate(++_step), []));
        private void Submit(object action) => Session.Update(new ProductUpdate(OuterUpdate(++_step), [Ui(JsonSerializer.Serialize(action))]));
        public void Dispose() => Session.Dispose();
    }
}
