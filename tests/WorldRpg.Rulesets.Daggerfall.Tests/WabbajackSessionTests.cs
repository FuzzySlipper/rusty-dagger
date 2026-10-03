using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;
using EquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using UniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class WabbajackSessionTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Real_strike_contact_transforms_once_keeps_prior_wounds_and_restores_without_a_hidden_actor(bool hit, bool dynamic)
    {
        using Fixture f = new(hit, dynamic);
        var old = f.Session.State.Actors.Get(f.Target);
        var oldEntity = old.Actor.Entity;
        var pose = new ActorPose(new WorldPoint(9, 3, -4), .6f);
        old.ApplyPose(pose);
        Health(old).Spend(3);
        int count = f.Session.State.Actors.All.Count();
        f.Strike();
        var replacement = f.Session.State.Actors.Get(f.Target);
        Assert.False(f.Session.State.Actors.Store.IsAlive(oldEntity));
        Assert.NotEqual(oldEntity, replacement.Actor.Entity);
        Assert.Equal(count, f.Session.State.Actors.All.Count());
        Assert.Equal(pose, replacement.Pose);
        Assert.Equal("lich", f.Session.DefinitionsByActor[f.Target].Id.Value);
        Assert.Equal(Health(replacement).Maximum.Value - 3, Health(replacement).Current);
        Assert.Contains("Wabbajack transformed", f.Session.Presentation.LastOutcome, StringComparison.Ordinal);
        int condition = f.Session.State.ItemInstances.RequireUnique(f.Source).CurrentCondition;
        Assert.Equal(hit, condition < 1500);
        using var restored = DaggerfallSession.Restore(f.Engine().Context, f.Composition, f.Session.CaptureSave());
        var resumed = restored.State.Actors.Get(f.Target);
        Assert.Equal(replacement.Actor.TypeId, resumed.Actor.TypeId);
        Assert.Equal(pose, resumed.Pose);
        Assert.Equal(DaggerfallWabbajack.DefinitionOf(replacement.Actor), DaggerfallWabbajack.DefinitionOf(resumed.Actor));
        Assert.Equal(Health(replacement).Current, Health(resumed).Current);
        Assert.Equal(condition, restored.State.ItemInstances.RequireUnique(f.Source).CurrentCondition);
        f.Session.ResolveExplicitMelee(new(1, f.Target, 1, 10000, .125));
        Assert.Equal(replacement.Actor.Entity, f.Session.State.Actors.Get(f.Target).Actor.Entity);
        Assert.Equal(hit ? Health(replacement).Maximum.Value - 16 : Health(replacement).Maximum.Value - 3, Health(replacement).Current);
    }

    public static IEnumerable<object[]> Variants => Enumerable.Range(0, 17).Select(index => new object[] { index });
    [Theory]
    [MemberData(nameof(Variants))]
    public void Every_donor_variant_has_a_real_definition_stats_and_appearance(int draw)
    {
        int[] donorMobiles = [0, 1, 2, 3, 4, 6, 10, 13, 15, 16, 17, 20, 36, 37, 38, 35, 32];
        using Fixture f = new(false, draw: draw);
        f.Strike();
        var actor = f.Session.State.Actors.Get(f.Target);
        Assert.Equal(donorMobiles[draw], f.Session.DefinitionsByActor[f.Target].MobileId);
        Assert.NotNull(DaggerfallWabbajack.DefinitionOf(actor.Actor));
        Assert.True(Health(actor).Current > 0);
        Assert.Equal(f.Session.DefinitionsByActor[f.Target].Id.Value, actor.Actor.TypeId.Value);
    }

    [Fact]
    public void Removed_source_between_release_and_impact_is_explicit_and_does_not_transform_or_replay()
    {
        using Fixture f = new(true);
        var original = f.Session.State.Actors.Get(f.Target).Actor.Entity;
        var facts = new FactBuffer<IProductFact>();
        Assert.True(f.Session.State.Kit.AttackExecution.Start(new(1, f.Target, 1, 1, .125, true), facts));
        f.Session.State.Equipment.Unequip(f.Item);
        f.Session.State.Inventory.Destroy(f.Item);
        f.Session.State.ItemInstances.RemoveUnique(f.Source);
        f.Session.State.Actors.Entities.Destroy(new(WorldRpg.Kit.World.DurableIdentityKind.Item, f.Source));
        f.Session.RemoveUniqueItemIdentity(f.Source);
        var notice = new AttackImpactNotice(1, f.Target, 1, 1, false);
        f.Session.State.Kit.AttackExecution.ApplyImpacts([notice], 1, facts);
        f.Session.State.Kit.AttackExecution.ApplyImpacts([notice], 1, facts);
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        Assert.Equal(DaggerfallWabbajackOutcome.SourceUnavailable, Assert.Single(delivered.OfType<ActorTransformedFact>()).Outcome);
        Assert.Single(delivered.OfType<AttackHitFact>());
        Assert.Equal(original, f.Session.State.Actors.Get(f.Target).Actor.Entity);
        Assert.Null(DaggerfallWabbajack.DefinitionOf(f.Session.State.Actors.Get(f.Target).Actor));
    }

    [Fact]
    public void Active_quest_actor_binding_protects_the_target_then_quest_end_releases_protection()
    {
        using Fixture f = new(false);
        var definitions = TestPayload.Definitions;
        var declaration = definitions.QuestSources.Resources.First(resource => resource.Kind == "foe"
            && definitions.QuestSources.Quests[resource.SourceFile].Disposition == DaggerfallQuestDisposition.Compiled
            && !definitions.QuestSources.Resources.Any(other => other.SourceFile == resource.SourceFile && other.Kind is "place" or "person")
            && !definitions.QuestSources.UnresolvedReferences.Any(link => link.SourceFile == resource.SourceFile)
            && !DaggerfallQuestClockCompiler.Compile(definitions.QuestSources.Resolve(resource.SourceFile)).Any(DaggerfallQuestClockCompiler.UsesTravelDuration));
        var quest = definitions.QuestSources.Quests[declaration.SourceFile];
        f.Session.State.Quests.Start(new("wabbajack-protected", declaration.SourceFile, quest.Name, DaggerfallQuestLifecycle.Active, null,
            [new(declaration.CanonicalId, DaggerfallQuestResourceBinding.Actors(f.Target))], []));
        var original = f.Session.State.Actors.Get(f.Target).Actor.Entity;
        f.Strike();
        Assert.Equal(original, f.Session.State.Actors.Get(f.Target).Actor.Entity);
        Assert.Contains("quest target", f.Session.Presentation.LastOutcome, StringComparison.Ordinal);
        Assert.Equal(1500, f.Session.State.ItemInstances.RequireUnique(f.Source).CurrentCondition);
        using var restored = DaggerfallSession.Restore(f.Engine().Context, f.Composition, f.Session.CaptureSave());
        Assert.True(restored.State.Quests.ProtectsActor(f.Target));
        f.Session.State.Quests.Complete("wabbajack-protected", "finished");
        f.Session.ResolveExplicitMelee(new(1, f.Target, 1, 10000, .125));
        Assert.NotNull(DaggerfallWabbajack.DefinitionOf(f.Session.State.Actors.Get(f.Target).Actor));
    }

    [Fact]
    public void Material_rejection_does_not_run_transformation_or_charge_the_source()
    {
        using Fixture f = new(true, allowed: false);
        var original = f.Session.State.Actors.Get(f.Target).Actor.Entity;
        f.Strike();
        Assert.Equal(original, f.Session.State.Actors.Get(f.Target).Actor.Entity);
        Assert.Null(DaggerfallWabbajack.DefinitionOf(f.Session.State.Actors.Get(f.Target).Actor));
        Assert.Equal(1500, f.Session.State.ItemInstances.RequireUnique(f.Source).CurrentCondition);
        Assert.Contains("material", f.Session.Presentation.LastOutcome, StringComparison.Ordinal);
    }

    [Fact]
    public void Unloaded_authored_and_dynamic_replacements_rebuild_the_same_meaning_and_pose()
    {
        using Fixture f = new(false);
        var dynamic = f.Session.SpawnActor("orc", new(new WorldPoint(7, 2, -5), .7f));
        f.Strike();
        f.Session.ResolveExplicitMelee(new(1, dynamic, 1, 10000, .125));
        var first = f.Session.State.Actors.Get(f.Target).Pose;
        var second = f.Session.State.Actors.Get(dynamic).Pose;
        var destination = DaggerfallSiteContent.Read(FullContent(TestData.RepositoryRoot),
            File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), TestPayload.Definitions);
        var profiles = new DaggerfallSiteProfiles([f.Inputs, destination]);
        f.Session.AdmitSiteProfiles(profiles);
        Assert.True(f.Session.TryTransitionTo(destination.ProfileKey));
        Assert.False(f.Session.State.Actors.TryGet(f.Target, out _));
        using var restored = DaggerfallSession.Restore(f.Engine(destination).Context, f.Composition with { Profiles = profiles }, f.Session.CaptureSave());
        Assert.True(restored.TryTransitionTo(f.Inputs.ProfileKey));
        Assert.Equal(first, restored.State.Actors.Get(f.Target).Pose);
        Assert.Equal(second, restored.State.Actors.Get(dynamic).Pose);
        Assert.Equal("lich", restored.State.Actors.Get(f.Target).Actor.TypeId.Value);
        Assert.Equal("lich", restored.State.Actors.Get(dynamic).Actor.TypeId.Value);
        Assert.NotNull(DaggerfallWabbajack.DefinitionOf(restored.State.Actors.Get(f.Target).Actor));
        Assert.NotNull(DaggerfallWabbajack.DefinitionOf(restored.State.Actors.Get(dynamic).Actor));
    }

    private static Track Health(ActorState actor) => actor.Stats.GetTrack(TrackId.Parse("health"));
    private sealed class Strike(bool hit, bool allowed) : ICombatContribution
    {
        public void Hit(TryHitEvent value) => value.Hit = hit;
        public void Damage(DamageEvent value) { value.Damage = 13; value.Allowed = allowed; }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly DaggerfallSiteProfile Inputs = ReadInputs(TestData.RepositoryRoot);
        internal readonly DaggerfallSessionComposition Composition;
        internal readonly DaggerfallSession Session;
        internal readonly long Target;
        internal readonly ulong Source;
        internal readonly UniqueInventoryItem Item;
        private readonly int _draw;
        internal Fixture(bool hit, bool dynamic = false, int draw = 16, bool allowed = true)
        {
            _draw = draw;
            var definitions = TestPayload.Definitions;
            var identity = GameCompositionResolver.Resolve(FullContent(TestData.RepositoryRoot), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
            Composition = new(definitions, Inputs, DaggerfallTuning.Defaults, identity);
            var engine = Engine();
            Session = DaggerfallSession.StartNew(engine.Context, Composition);
            Target = dynamic ? Session.SpawnActor("orc", new(new WorldPoint(2, 2, -2), 0))
                : Inputs.Project.Actors.Values.First(actor => definitions.RequireActor(actor.ActorId).MobileId == 7).EntityId;
            var created = new DaggerfallItemFactory(definitions, engine.Context.Random).Create(new("Magic", "wabbajack", DaggerfallItemOwner.Player, MagicItemKey: "magic-item.0006"));
            var id = Session.UniqueItemAllocator.AllocateReference(); Source = id.Value;
            Item = Session.State.Equipment.Materialize(id, created.Item);
            Session.State.ItemInstances.RegisterUnique(Source, created.Metadata);
            Assert.Equal(EquipmentMoveOutcome.Applied, Session.EquipmentMoves.MoveToSlot(Item, new EquipmentSlotId("right-hand")).Outcome);
            Session.State.Kit.Rules.RegisterAction(definitions.RequireActor(new("player")).ActionId!, new Strike(hit, allowed));
        }
        internal EngineContextFake Engine(DaggerfallSiteProfile? additional = null)
        {
            List<string> releases = [];
            ContentFake content = new(releases); PopulateContent(content, Inputs);
            var castle = DaggerfallSiteContent.Read(FullContent(TestData.RepositoryRoot),
                File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), TestPayload.Definitions);
            PopulateContent(content, castle);
            if (additional is not null) PopulateContent(content, additional);
            var spatial = SpatialFake.Create((additional ?? Inputs).SpatialArtifact.Sha256, releases); spatial.KeepPosition = true;
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: SelectionRandom.Create(_draw));
        }
        internal void Strike() => Session.ResolveExplicitMelee(new(1, Target, 1, 1, .125));
        public void Dispose() => Session.Dispose();
    }

    public class SelectionRandom : DispatchProxy
    {
        private int _draw;
        internal static IRandomService Create(int draw)
        {
            var service = DispatchProxy.Create<IRandomService, SelectionRandom>();
            ((SelectionRandom)(object)service)._draw = draw;
            return service;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            var request = (KeyedRngRequest)arguments![0]!;
            return new KeyedRngReceipt(request.Scope == "daggerfall.wabbajack.v1" ? Math.Min(_draw, request.Maximum) : request.Maximum);
        }
    }
}
