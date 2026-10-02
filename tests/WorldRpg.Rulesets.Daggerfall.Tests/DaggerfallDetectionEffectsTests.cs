using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDetectionEffectsTests
{
    private static long _nextCast;
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Every_detector_casts_quotes_keeps_first_settings_saves_and_expires(int subtype)
    {
        using Fixture f = new(); var s = f.Session;
        var setting = Setting(subtype, 10);
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(Cast(s, setting).Results).Outcome);
        Assert.Equal(DaggerfallCastOutcome.Refreshed, Assert.Single(Cast(s, Setting(subtype, 2)).Results).Outcome);
        var active = Assert.Single(s.State.Effects.Active);
        Assert.Equal(11u, active.Lifecycle.RemainingRounds);
        Assert.Equal(10, active.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)!.Settings.DurationBase);
        Assert.Equal(39, active.Definition.Spell!.Type);
        Assert.Equal(subtype, active.Definition.Spell.SubType);
        Assert.Equal(DaggerfallMagicAllowedTargets.CasterOnly, active.Definition.Spell.AllowedTargets);
        var row = TestPayload.Definitions.Magic.RequireEffectCost(setting);
        Assert.Equal(1, row.SettingsType); Assert.Equal("thaumaturgy", row.School);
        Assert.Equal(subtype == 2 ? 360 : 400, DaggerfallMagicCostPolicy.CalculateEffectCosts(TestPayload.Definitions.Magic,
            Setting(subtype, 10), new Dictionary<string, int> { ["thaumaturgy"] = 50 }).Gold);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(Assert.Single(s.ReadDetectors()).Source, Assert.Single(restored.ReadDetectors()).Source);
        Assert.Equal(11u, Assert.Single(restored.State.Effects.Active).Lifecycle.RemainingRounds);
        restored.State.Effects.AdvanceElapsedRounds(11); Assert.Empty(restored.ReadDetectors());
    }
    [Fact]
    public void Enemy_and_magic_use_live_classification_strict_engine_range_and_remove_stale_contacts()
    {
        using Fixture f = new(); var s = f.Session;
        s.State.PlayerControl.MoveTo(new Vector3(10, 0, 10));
        long near = s.SpawnActor("rat", new(new(12, 0, 10), 0));
        long boundary = s.SpawnActor("rat", new(new(24, 0, 10), 0));
        Cast(s, Setting(0, 20)); Cast(s, Setting(1, 20));
        Assert.DoesNotContain(s.ReadDetectors().Single(v => v.Kind == "magic").Contacts, v => v.Id == near.ToString());
        Conceal(s, near); Conceal(s, boundary);
        var views = s.ReadDetectors();
        var magic = Assert.Single(views.Single(v => v.Kind == "magic").Contacts, v => v.Id == near.ToString());
        Assert.Equal(2, magic.Distance); Assert.Equal(Math.PI / 2, magic.BearingRadians);
        Assert.DoesNotContain(views.SelectMany(v => v.Contacts), v => v.Id == boundary.ToString());
        Assert.Contains(views.Single(v => v.Kind == "enemy").Contacts, v => v.Id == near.ToString());
        Assert.All(f.Perception.Requests, request => Assert.Equal(14, request.Observers.Span[0].MaximumDistance));
        // Occluded is deliberately used by the Engine fake: classic detection ignores line of sight.
        s.State.Effects.CancelSource("test.conceal");
        Assert.DoesNotContain(s.ReadDetectors().Single(v => v.Kind == "magic").Contacts, v => v.Id == near.ToString());
        s.RetireActor(near);
        Assert.DoesNotContain(s.ReadDetectors().SelectMany(v => v.Contacts), v => v.Id == near.ToString());
        s.State.PlayerControl.MoveTo(new Vector3(40, 0, 40)); Assert.Empty(s.ReadDetectors().SelectMany(v => v.Contacts));
    }
    [Fact]
    public void Ground_treasure_uses_actual_inventory_items_and_clears_across_sites_then_restores()
    {
        using Fixture f = new(twoSites: true); var s = f.Session;
        s.State.PlayerControl.MoveTo(new Vector3(10, 0, 10));
        Cast(s, Setting(2, 30)); s.PublishInitial();
        var stack = s.State.Inventory.Read().Stacks.First();
        f.Submit(new { action = "inventory-drop", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"stack:{stack.Id.Value}", amount = 1 });
        var contact = Assert.Single(Assert.Single(s.ReadDetectors()).Contacts);
        Assert.Equal("container", contact.Kind);
        var item = Assert.Single(contact.Items); Assert.Equal(stack.Definition.Value, item.Definition); Assert.Equal(1UL, item.Quantity);
        Assert.NotEqual("1", contact.Id);
        s.PublishInitial(); string projection = JsonSerializer.Serialize(f.Engine.Published());
        Assert.Contains("\"detectors\"", projection); Assert.Contains(contact.Id, projection); Assert.Contains(item.Id, projection);
        using var restored = f.Restore(s.CaptureSave());
        var resumed = Assert.Single(Assert.Single(restored.ReadDetectors()).Contacts);
        Assert.Equal(contact.Id, resumed.Id); Assert.Equal(item.Id, Assert.Single(resumed.Items).Id);
        Assert.True(s.TryTransitionTo(f.Destination!.ProfileKey));
        Assert.Empty(Assert.Single(s.ReadDetectors()).Contacts);
        Assert.True(s.TryTransitionTo(f.Composition.StartSite.ProfileKey));
        s.State.PlayerControl.MoveTo(new Vector3(10, 0, 10));
        Assert.Equal(contact.Id, Assert.Single(Assert.Single(s.ReadDetectors()).Contacts).Id);
        // Emptying the canonical inventory removes treasure eligibility immediately.
        var save = DaggerfallSavePayload.Read(s.CaptureSave()); var ground = Assert.Single(save.GroundContainers);
        var owner = s.State.Containers.Entities.Resolve(new(WorldRpg.Kit.World.DurableIdentityKind.Container, checked((ulong)ground.Id)));
        var contents = s.State.Containers.Read(owner);
        s.State.Containers.Transfer(owner, s.State.Actors.Player.Actor.Entity, new(new WorldRpg.Kit.Inventory.InventoryItemId(stack.Definition.Value), 1, contents.Stacks.Single().Id));
        Assert.Empty(Assert.Single(s.ReadDetectors()).Contacts);
    }
    [Fact]
    public void Corpse_treasure_uses_the_durable_container_and_current_nonempty_inventory()
    {
        using Fixture f = new(random: RandomMinimum.Create()); var s = f.Session;
        long target = 2000;
        s.State.Actors.Get(target).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1);
        s.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60;
        s.ResolveExplicitMelee(new WorldRpg.Rulesets.Daggerfall.Modules.Combat.ExplicitMeleeRequest(1, target, 1, 1, .125));
        var corpse = s.Corpses[target];
        var inventory = s.State.Containers.Read(corpse.Owner);
        Assert.NotEmpty(inventory.Stacks);
        AimActivationAt(s, target); Cast(s, Setting(2, 20));
        var contact = Assert.Single(Assert.Single(s.ReadDetectors()).Contacts);
        Assert.Equal(corpse.ContainerIdentity.Value.ToString(), contact.Id);
        Assert.Equal(inventory.Stacks.Count + inventory.UniqueItems.Count, contact.Items.Count);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(contact.Id, Assert.Single(Assert.Single(restored.ReadDetectors()).Contacts).Id);
        foreach (var stack in inventory.Stacks)
            s.State.Containers.Transfer(corpse.Owner, s.State.Actors.Player.Actor.Entity,
                new(new WorldRpg.Kit.Inventory.InventoryItemId(stack.Definition.Value), stack.Quantity, stack.Id));
        foreach (var item in inventory.UniqueItems)
            s.State.Containers.Transfer(corpse.Owner, s.State.Actors.Player.Actor.Entity,
                new(new WorldRpg.Kit.Inventory.InventoryItemId(item.Definition.Value), 1, UniqueEntityId: item.Entity.Value));
        Assert.Empty(Assert.Single(s.ReadDetectors()).Contacts);
    }
    [Fact]
    public void Source_expiry_and_cancellation_keep_other_detector_sources_and_no_projection_ghosts()
    {
        using Fixture f = new(); var s = f.Session;
        Cast(s, Setting(0, 2)); Cast(s, Setting(1, 10)); Cast(s, Setting(2, 10));
        Assert.Equal(3, s.ReadDetectors().Count);
        s.State.Effects.AdvanceElapsedRounds(1);
        Assert.Equal(new[] { "enemy", "treasure" }, s.ReadDetectors().Select(v => v.Kind).Order());
        var enemy = s.State.Effects.Active.Single(effect => effect.Definition.Detection == DaggerfallDetection.Enemy);
        s.State.Effects.Cancel(enemy.Context.Instance); s.PublishInitial();
        Assert.Equal("treasure", Assert.Single(s.ReadDetectors()).Kind);
        var snapshot = (Dictionary<string, object?>)f.Engine.Published()!;
        Assert.Single((object?[])snapshot["detectors"]!);
        s.State.Effects.AdvanceElapsedRounds(9); s.PublishInitial();
        Assert.Empty((object?[])((Dictionary<string, object?>)f.Engine.Published()!)["detectors"]!);
    }
    [Fact]
    public void Invalid_detector_payload_and_nonmagic_cast_are_refused_and_tuning_is_validated()
    {
        using Fixture f = new(); var s = f.Session;
        var setting = Setting(0, 10); var spell = Spell(setting) with { Element = 0 };
        Assert.Equal(DaggerfallCastOutcome.UnsupportedEffect, Casting(s, spell).Ready(1, spell.Key).Outcome);
        Cast(s, setting); var save = DaggerfallSavePayload.Read(s.CaptureSave()); var effect = Assert.Single(save.ActiveEffects);
        var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)!;
        var invalid = effect with { State = JsonSerializer.SerializeToElement(state with { Amount = 1 }, DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState) };
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(save with { ActiveEffects = [invalid] })));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallDetectionTuning(double.NaN).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallDetectionTuning(0).Validate());
    }
    private static DaggerfallSpellEffectDefinition Setting(int subtype, int rounds) => new("detect", 39, subtype, rounds, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1);
    private static DaggerfallSpellDefinition Spell(DaggerfallSpellEffectDefinition setting) => new($"detect.test.{setting.SubType}", 1, false, "Detect", 4, 0, 0, 0, [setting]);
    private static DaggerfallCasting Casting(DaggerfallSession s, DaggerfallSpellDefinition spell)
    {
        var track = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); track.Maximum.BaseValue = 10000; track.SetCurrent(10000);
        return new(TestPayload.Definitions.Magic with { Spells = new Dictionary<string, DaggerfallSpellDefinition> { [spell.Key] = spell } }, s.State.Effects,
            id => id == 1 ? s.State.Actors.Player.Actor : s.State.Actors.TryGet(id, out var actor) ? actor.Actor : null,
            s.MagicProfile, _ => true, _ => { }, _ => { }, RandomMaximum.Create(), 1,
            nextSequence: Interlocked.Increment(ref _nextCast), playerKnowsSpell: _ => true, casterLevel: _ => 1);
    }
    private static DaggerfallLiveSpell Cast(DaggerfallSession s, DaggerfallSpellEffectDefinition setting)
    {
        var spell = Spell(setting); var casting = Casting(s, spell);
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
        var bundle = casting.Release(1, true).Bundle!; casting.Deliver(bundle, [1]); return bundle;
    }
    private static void Conceal(DaggerfallSession s, long target) => s.State.Effects.Start(new($"conceal.{target}", "invisibility-true", "test.conceal", target, target, "invisibility", "Magic", null, 1, 20,
        JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(new("conceal", 13, 1, 20, 0, 1, 0, 0, 1, 0, 0, 0, 0, 1), 1, 0, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)));
    private sealed class Fixture : IDisposable
    {
        internal DaggerfallSessionComposition Composition { get; }
        internal DaggerfallSiteProfile? Destination { get; }
        internal PerceptionFake Perception { get; } = PerceptionFake.Create();
        internal EngineContextFake Engine { get; }
        internal DaggerfallSession Session { get; }
        private ulong _step = 1;
        private readonly IRandomService _random;
        internal Fixture(bool twoSites = false, IRandomService? random = null)
        {
            _random = random ?? RandomMaximum.Create();
            var source = ReadInputs(TestData.RepositoryRoot);
            if (twoSites) Destination = DaggerfallSiteContent.Read(FullContent(TestData.RepositoryRoot), File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), TestPayload.Definitions);
            Composition = new(TestPayload.Definitions, source, DaggerfallTuning.Defaults) { Profiles = new DaggerfallSiteProfiles(Destination is null ? [source] : [source, Destination]) };
            Perception.Responder = request => Receipt(request.Targets.ToArray().Select(target => new PerceptionPair(request.Observers.Span[0].Entity,
                target.Entity, Vector3.Distance(request.Observers.Span[0].Origin, target.Center), 1, PerceptionPairKind.Occluded, 0)).ToArray());
            Engine = CreateEngine(); Session = DaggerfallSession.StartNew(Engine.Context, Composition);
        }
        private EngineContextFake CreateEngine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, Composition.StartSite); if (Destination is not null) PopulateContent(content, Destination);
            return EngineContextFake.Create(content, SpatialFake.Create(Composition.StartSite.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases), Perception.Service, random: _random);
        }
        internal void Submit(object action) => Session.Update(new ProductUpdate(OuterUpdate(++_step), [Ui(JsonSerializer.Serialize(action))]));
        internal DaggerfallSession Restore(RulesetSavePayload payload) => DaggerfallSession.Restore(CreateEngine().Context, Composition, payload);
        public void Dispose() => Session.Dispose();
    }
}
