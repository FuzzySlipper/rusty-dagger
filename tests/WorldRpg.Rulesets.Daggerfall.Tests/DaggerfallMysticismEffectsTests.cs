using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallMysticismEffectsTests
{
    private static long _nextTestCast;
    [Fact]
    public void Silence_cast_chance_failure_has_no_condition_and_success_has_canonical_defense()
    {
        using Fixture f = new(); var s = f.Session;
        var failed = Cast(s, Setting(19, -1, 0));
        Assert.Equal(DaggerfallCastOutcome.ChanceFailed, Assert.Single(failed.Results).Outcome);
        Assert.False(s.State.Effects.MagicDefenseFor(1).BlocksCasting);
        var applied = Cast(s, Setting(19, -1, 100));
        Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(applied.Results).Outcome);
        Assert.True(s.State.Effects.MagicDefenseFor(1).BlocksCasting);
        s.State.Effects.Cancel(Assert.Single(s.State.Effects.Active).Context.Instance);
        Assert.False(s.State.Effects.MagicDefenseFor(1).BlocksCasting);
    }
    [Fact]
    public void Silence_blocks_ready_and_release_before_payment_restores_and_cures_or_expires()
    {
        using Fixture f = new(); var s = f.Session;
        var spell = new DaggerfallSpellDefinition("test.silence-block", 1, false, "Languages", 4, 0, 0, 0, [Setting(44, -1, 20)]);
        var track = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        track.Maximum.BaseValue = 10000; track.SetCurrent(10000);
        var casting = new DaggerfallCasting(TestPayload.Definitions.Magic with { Spells = new Dictionary<string, DaggerfallSpellDefinition> { [spell.Key] = spell } }, s.State.Effects,
            id => id == 1 ? s.State.Actors.Player.Actor : null, s.MagicProfile, _ => true, _ => { }, _ => { }, RandomMaximum.Create(), 1, playerKnowsSpell: _ => true);
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
        void Silence(string id) => s.State.Effects.Start(new(id, "silence", "spell.foreign", 2000, 1, "silence", "Magic", null, 1, 10,
            JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(Setting(19, -1, 100), 1, 0, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)));
        Silence("silence-one"); Silence("silence-two");
        Assert.Equal(19u, Assert.Single(s.State.Effects.Active).Lifecycle.RemainingRounds);
        double before = track.Current;
        Assert.Equal(DaggerfallCastOutcome.Silenced, casting.Release(1, true).Outcome);
        Assert.Equal(DaggerfallCastOutcome.Silenced, casting.Ready(1, spell.Key).Outcome);
        Assert.Equal(before, track.Current);
        using var restored = f.Restore(s.CaptureSave());
        Assert.True(restored.State.Effects.MagicDefenseFor(1).BlocksCasting);
        restored.State.Effects.AdvanceElapsedRounds(19);
        Assert.False(restored.State.Effects.MagicDefenseFor(1).BlocksCasting);
        Assert.True(s.State.Effects.Cure(Assert.Single(s.State.Effects.Active).Context.Instance));
        Assert.False(s.State.Effects.MagicDefenseFor(1).BlocksCasting);
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome);
    }

    [Fact]
    public void Paid_teleport_choice_and_anchor_restore_then_recall_exact_pose_once_without_elapsed_time()
    {
        using Fixture f = new(twoSites: true); var s = f.Session;
        var position = new WorldRpg.Kit.Controls.WorldPoint(17, 3, -11);
        s.State.PlayerControl.MoveTo(position.ToVector()); s.State.PlayerControl.YawRadians = .7f; s.State.PlayerControl.PitchRadians = -.2f;
        Cast(s, Setting(43, -1, 0)); var view = s.TeleportView!;
        Assert.False(view.AnchorSet); Assert.Equal(ProductMode.Modal, s.PendingModeRequest);
        var before = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).Current;
        using var pending = f.Restore(s.CaptureSave()); Assert.Equal(view.Revision, pending.TeleportView!.Revision);
        pending.ChooseTeleport("stale", "anchor"); Assert.NotNull(pending.TeleportView);
        pending.Update(new ProductUpdate(OuterUpdate(7), [Ui(JsonSerializer.Serialize(new { action = "teleport-select", revision = view.Revision, key = "anchor" }))]));
        Assert.Null(pending.TeleportView); Assert.NotNull(DaggerfallSavePayload.Read(pending.CaptureSave()).TeleportAnchor);
        Assert.Equal(before, pending.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).Current);
        Assert.True(pending.TryTransitionTo(f.Destination!.ProfileKey));
        using var away = f.Restore(pending.CaptureSave());
        Cast(away, Setting(43, -1, 0)); var calendar = DaggerfallSavePayload.Read(away.CaptureSave()).Calendar; var paid = away.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).Current;
        away.ChooseTeleport(away.TeleportView!.Revision, "recall");
        Assert.Equal(f.Composition.StartSite.ProfileKey, away.Sites.ActiveProfile);
        Assert.Equal(position, away.State.PlayerControl.Position); Assert.Equal(.7f, away.State.PlayerControl.YawRadians); Assert.Equal(-.2f, away.State.PlayerControl.PitchRadians);
        Assert.Equal(calendar, DaggerfallSavePayload.Read(away.CaptureSave()).Calendar); Assert.Equal(paid, away.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).Current);
        Assert.Null(DaggerfallSavePayload.Read(away.CaptureSave()).TeleportAnchor); Assert.Null(away.Sites.ReturnProfile);
        using var arrived = f.Restore(away.CaptureSave()); Assert.Equal(position, arrived.State.PlayerControl.Position);
        Cast(arrived, Setting(43, -1, 0)); arrived.ChooseTeleport(arrived.TeleportView!.Revision, "recall");
        Assert.Contains("must be set", arrived.Presentation.LastOutcome);
    }

    [Fact]
    public void Interior_anchor_restores_its_original_entrance_instead_of_the_recall_departure()
    {
        using Fixture f = new(twoSites: true); var s = f.Session;
        var original = s.State.PlayerControl.Position;
        Assert.True(s.TryTransitionTo(f.Destination!.ProfileKey));
        Cast(s, Setting(43, -1, 0)); s.ChooseTeleport(s.TeleportView!.Revision, "anchor");
        var anchored = s.State.PlayerControl.Position;
        Assert.True(s.TryTransitionTo(f.Composition.StartSite.ProfileKey));
        s.State.PlayerControl.MoveTo(new System.Numerics.Vector3(100, 1, 100));
        Cast(s, Setting(43, -1, 0)); s.ChooseTeleport(s.TeleportView!.Revision, "recall");
        Assert.Equal(anchored, s.State.PlayerControl.Position);
        using var restored = f.Restore(s.CaptureSave());
        Assert.True(restored.TryTransitionTo(f.Composition.StartSite.ProfileKey));
        Assert.Equal(original, restored.State.PlayerControl.Position);
    }

    [Fact]
    public void Teleport_cancel_retains_paid_cost_and_malformed_anchor_is_rejected()
    {
        using Fixture f = new(); var s = f.Session;
        Cast(s, Setting(43, -1, 0)); double paid = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).Current;
        s.ChooseTeleport(s.TeleportView!.Revision, "cancel"); Assert.Null(s.TeleportView);
        Assert.Equal(paid, s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).Current);
        Cast(s, Setting(43, -1, 0)); s.ChooseTeleport(s.TeleportView!.Revision, "anchor");
        var saved = DaggerfallSavePayload.Read(s.CaptureSave());
        var malformed = saved with { TeleportAnchor = saved.TeleportAnchor! with {
            Profile = saved.TeleportAnchor.Profile with { LogicalId = "not-admitted" } } };
        Assert.Throws<InvalidOperationException>(() => f.Restore(DaggerfallSavePayload.Encode(malformed)));
    }

    [Fact]
    public void Rejected_recall_keeps_the_departure_world_and_anchor_for_a_later_paid_attempt()
    {
        using Fixture f = new(twoSites: true); var s = f.Session;
        Cast(s, Setting(43, -1, 0)); s.ChooseTeleport(s.TeleportView!.Revision, "anchor");
        Assert.True(s.TryTransitionTo(f.Destination!.ProfileKey)); var departed = s.State.PlayerControl.Position;
        var relation = s.Sites.ReturnProfile;
        Cast(s, Setting(43, -1, 0)); f.Spatial.RejectContentReplacement = true;
        s.ChooseTeleport(s.TeleportView!.Revision, "recall");
        Assert.Contains("failed", s.Presentation.LastOutcome);
        Assert.Equal(f.Destination.ProfileKey, s.Sites.ActiveProfile); Assert.Equal(departed, s.State.PlayerControl.Position);
        Assert.Equal(relation, s.Sites.ReturnProfile);
        Assert.NotNull(DaggerfallSavePayload.Read(s.CaptureSave()).TeleportAnchor);
        f.Spatial.RejectContentReplacement = false;
        Cast(s, Setting(43, -1, 0)); s.ChooseTeleport(s.TeleportView!.Revision, "recall");
        Assert.Equal(f.Composition.StartSite.ProfileKey, s.Sites.ActiveProfile);
        Assert.Null(DaggerfallSavePayload.Read(s.CaptureSave()).TeleportAnchor);
    }
    [Fact]
    public void Language_bonus_keeps_first_settings_extends_duration_and_restores_then_expires()
    {
        using Fixture f = new(); var s = f.Session;
        Cast(s, Setting(44, -1, 15)); Assert.Equal(15, s.State.Effects.PerceptionFor(1).ComprehendLanguagesBonus);
        Cast(s, Setting(44, -1, 90)); Assert.Equal(15, s.State.Effects.PerceptionFor(1).ComprehendLanguagesBonus);
        var active = Assert.Single(s.State.Effects.Active); Assert.Equal(19u, active.Lifecycle.RemainingRounds);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(15, restored.State.Effects.PerceptionFor(1).ComprehendLanguagesBonus);
        restored.State.Effects.AdvanceElapsedRounds(19); Assert.Equal(0, restored.State.Effects.PerceptionFor(1).ComprehendLanguagesBonus);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void Creature_dispel_queries_engine_distance_classifies_and_removes_without_death_or_loot(int subtype)
    {
        using Fixture f = new(); var s = f.Session;
        long undead = Spawn(s, DaggerfallEnemyGroup.Undead), daedra = Spawn(s, DaggerfallEnemyGroup.Daedra), rat = s.SpawnActor("rat", new(new(1, 0, 1), 0));
        f.Perception.Receipt = Receipt(new PerceptionPair(1, (ulong)undead, 4, 1, PerceptionPairKind.Occluded, 0), new(1, (ulong)daedra, 5, 1, PerceptionPairKind.Visible, 1), new(1, (ulong)rat, 1, 1, PerceptionPairKind.Visible, 1));
        Cast(s, Setting(6, subtype, 100)); long removed = subtype == 1 ? undead : daedra;
        Assert.False(s.State.Actors.TryGet(removed, out _)); Assert.True(s.State.Actors.TryGet(subtype == 1 ? daedra : undead, out _)); Assert.True(s.State.Actors.TryGet(rat, out _));
        Assert.DoesNotContain(DaggerfallSavePayload.Read(s.CaptureSave()).Corpses, corpse => corpse.ActorId == removed);
        Assert.Empty(s.State.Effects.Active); Assert.Single(f.Perception.Requests);
        using var restored = f.Restore(s.CaptureSave()); Assert.False(restored.State.Actors.TryGet(removed, out _));
    }
    [Fact]
    public void Ineligible_out_of_range_and_failed_chance_are_no_match()
    {
        using Fixture f = new(); var s = f.Session; long id = Spawn(s, DaggerfallEnemyGroup.Undead);
        f.Perception.Receipt = Receipt(new PerceptionPair(1, (ulong)id, 14, 1, PerceptionPairKind.Visible, 1));
        Assert.Equal(DaggerfallCastOutcome.NoMatch, Assert.Single(Cast(s, Setting(6, 1, 100)).Results).Outcome);
        f.Perception.Receipt = Receipt(new PerceptionPair(1, (ulong)id, 1, 1, PerceptionPairKind.Visible, 1));
        Assert.Equal(DaggerfallCastOutcome.NoMatch, Assert.Single(Cast(s, Setting(6, 1, 0)).Results).Outcome);
        Assert.True(s.State.Actors.TryGet(id, out _));
    }
    [Fact]
    public void Paid_dispel_choice_restores_self_cast_bundle_is_unconditional_and_cancel_does_not_refund()
    {
        using Fixture f = new(); var s = f.Session;
        Cast(s, Setting(13, 1, 0)); Cast(s, Setting(23, 1, 0)); Cast(s, Setting(6, 0, 0));
        var view = s.DispelView!; Assert.Equal(2, view.Options.Count); Assert.Equal(ProductMode.Modal, s.PendingModeRequest);
        double paid = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).Current;
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(view.Revision, restored.DispelView!.Revision);
        restored.ChooseDispel(view.Revision, view.Options[0].Id); Assert.Single(restored.State.Effects.Active); Assert.Null(restored.DispelView);
        Cast(s, Setting(6, 0, 0)); s.ChooseDispel(s.DispelView!.Revision, null); Assert.Null(s.DispelView);
        Assert.True(s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")).Current < paid);
    }
    [Fact]
    public void Foreign_bundle_roll_failure_stale_choice_and_target_scoped_cleanup_preserve_effects()
    {
        using Fixture f = new(); var s = f.Session;
        Start(s, "foreign", 2000, 1, "shared"); Start(s, "other-target", 1, 2000, "shared");
        Cast(s, Setting(6, 0, 0)); var view = s.DispelView!;
        s.ChooseDispel("stale", view.Options[0].Id); Assert.NotNull(s.DispelView);
        s.ChooseDispel(view.Revision, view.Options[0].Id); Assert.Equal(2, s.State.Effects.Active.Count); Assert.Null(s.DispelView);
        Cast(s, Setting(6, 0, 100)); s.ChooseDispel(s.DispelView!.Revision, view.Options[0].Id);
        Assert.Equal("other-target", Assert.Single(s.State.Effects.Active).Context.Instance.Value);
    }
    [Fact]
    public void Authored_banishment_is_absent_in_current_save_and_never_respawns_on_site_return()
    {
        using Fixture f = new(twoSites: true); var s = f.Session;
        s.BanishActor(2000); Assert.False(s.State.Actors.TryGet(2000, out _)); var saved = DaggerfallSavePayload.Read(s.CaptureSave());
        Assert.Contains(2000, saved.BanishedActors); Assert.DoesNotContain(saved.Actors, actor => actor.EntityId == 2000);
        using var restored = f.Restore(s.CaptureSave()); Assert.False(restored.State.Actors.TryGet(2000, out _));
        Assert.True(s.TryTransitionTo(f.Destination!.ProfileKey)); Assert.True(s.TryTransitionTo(f.Composition.StartSite.ProfileKey));
        Assert.False(s.State.Actors.TryGet(2000, out _));
        using var returned = f.Restore(s.CaptureSave()); Assert.False(returned.State.Actors.TryGet(2000, out _));
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(saved with { BanishedActors = [2000, 2000] })));
    }
    [Fact]
    public void Selecting_a_saved_multi_effect_bundle_cancels_all_its_effects_and_preserves_other_bundles()
    {
        using Fixture f = new(); var s = f.Session;
        Cast(s, Setting(13, 1, 0), Setting(23, 1, 0)); Cast(s, Setting(24, 1, 0));
        var nested = s.State.Effects.Active.Where(effect => effect.Definition.Key != "shadow-true").ToArray();
        Assert.Equal(2, nested.Length); Assert.Equal(nested[0].BundleId, nested[1].BundleId);
        Cast(s, Setting(6, 0, 0)); Assert.Equal(2, s.DispelView!.Options.Count);
        using var restored = f.Restore(s.CaptureSave());
        restored.ChooseDispel(restored.DispelView!.Revision, nested[0].BundleId);
        Assert.Equal("shadow-true", Assert.Single(restored.State.Effects.Active).Definition.Key);
    }
    [Fact]
    public void Broken_held_item_cleans_its_bundle_and_stale_selection_cannot_cancel_another_source()
    {
        using Fixture f = new(); var s = f.Session;
        ulong item = s.State.Inventory.Read().UniqueItems.Select(value => s.State.Inventory.GetDurableItemId(value.Entity).Value).First();
        s.State.Effects.Start(new("held", "invisibility-true", "item", 1, 1, "invisibility", "Magic", item, 1, null,
            JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(Setting(13, 1, 0), 1, 0, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)) { BundleId = "held.bundle", BundleKind = DaggerfallEffectBundleKind.HeldMagicItem });
        Cast(s, Setting(24, 1, 0)); Cast(s, Setting(6, 0, 100)); var revision = s.DispelView!.Revision;
        s.State.ItemInstances.ReplaceUnique(item, s.State.ItemInstances.RequireUnique(item) with { CurrentCondition = 0 });
        s.ChooseDispel(revision, "held.bundle"); Assert.NotNull(s.DispelView);
        Assert.Equal("shadow-true", Assert.Single(s.State.Effects.Active).Definition.Key);
    }
    [Fact]
    public void Hidden_permanent_drain_is_not_selectable_but_leaves_with_a_visible_effect_in_its_bundle()
    {
        using Fixture f = new(); var s = f.Session;
        var setting = Setting(7, 0, 0) with { MagnitudeBaseLow = 7, MagnitudeBaseHigh = 7 };
        void Drain(string id, string bundle) => s.State.Effects.Start(new(id, "drain-strength", "spell.foreign", null, 1, "drain", "Magic", null, 1, null,
            DaggerfallAttributeDrainEffects.Encode(new(new(setting, 1, 7, 100, new(2000, null, DaggerfallCastSource.Spell)), 7))) { BundleId = bundle, BundleKind = DaggerfallEffectBundleKind.Spell });
        Drain("hidden", "hidden.bundle"); Cast(s, Setting(6, 0, 100)); Assert.Empty(s.DispelView!.Options);
        s.ChooseDispel(s.DispelView.Revision, "hidden.bundle"); Assert.Single(s.State.Effects.Active); s.ChooseDispel(s.DispelView!.Revision, null);
        // The same incumbent carries its original bundle; a visible companion makes that whole bundle eligible.
        Start(s, "visible", 2000, 1, "hidden.bundle"); Cast(s, Setting(6, 0, 100)); var option = Assert.Single(s.DispelView!.Options);
        s.ChooseDispel(s.DispelView.Revision, option.Id); Assert.Empty(s.State.Effects.Active);

    }
    [Theory]
    [InlineData(0)] [InlineData(3)]
    public void Nonspell_and_potion_bundles_are_not_dispel_choices(int kind)
    {
        using Fixture f = new(); var s = f.Session;
        Start(s, "not-spell", 1, 1, "excluded", (DaggerfallEffectBundleKind)kind);
        Cast(s, Setting(6, 0, 100)); Assert.Empty(s.DispelView!.Options);
        Assert.Single(s.State.Effects.Active);
    }
    private static long Spawn(DaggerfallSession s, DaggerfallEnemyGroup group) => s.SpawnActor(TestPayload.Definitions.Actors.Values.First(definition =>
        definition.Kind == DaggerfallActorKinds.Monster && DaggerfallFormulaPolicy.EnemyGroupFor(definition) == group).Id.Value, new(new(1, 0, 1), 0));
    private static DaggerfallSpellEffectDefinition Setting(int type, int subtype, int chance) => new("effect", type, subtype, 10, 0, 1, chance, 0, 1, 0, 0, 0, 0, 1);
    private static DaggerfallLiveSpell Cast(DaggerfallSession s, DaggerfallSpellEffectDefinition setting, params DaggerfallSpellEffectDefinition[] extra)
    {
        var spell = new DaggerfallSpellDefinition($"test.{setting.Type}.{setting.SubType}", 1, false, "Mysticism", 4, 0, 0, 0, [setting, .. extra]);
        var track = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); if (track.Maximum.BaseValue < 10000) { track.Maximum.BaseValue = 10000; track.SetCurrent(10000); }
        var casting = new DaggerfallCasting(TestPayload.Definitions.Magic with { Spells = new Dictionary<string, DaggerfallSpellDefinition> { [spell.Key] = spell } }, s.State.Effects,
            id => id == 1 ? s.State.Actors.Player.Actor : s.State.Actors.TryGet(id, out var actor) ? actor.Actor : null, s.MagicProfile, _ => true, _ => { }, _ => { }, RandomMaximum.Create(), 1,
            nextSequence: Interlocked.Increment(ref _nextTestCast), playerKnowsSpell: _ => true, casterLevel: _ => 1);
        Assert.Equal(DaggerfallCastOutcome.Ready, casting.Ready(1, spell.Key).Outcome); var bundle = casting.Release(1, true).Bundle!;
        casting.Deliver(bundle, [1]); return bundle;
    }
    private static void Start(DaggerfallSession s, string id, long caster, long target, string bundle, DaggerfallEffectBundleKind kind = DaggerfallEffectBundleKind.Spell) => s.State.Effects.Start(new(id, "invisibility-true", "spell.shared", caster, target, "invisibility", "Magic", null, 1, 10,
        JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(Setting(13, 1, 0), 1, 0, 100), DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState)) { BundleId = bundle, BundleKind = kind });
    private sealed class Fixture : IDisposable
    {
        internal DaggerfallSessionComposition Composition { get; }
        internal DaggerfallSiteProfile? Destination { get; }
        internal PerceptionFake Perception { get; } = PerceptionFake.Create();
        internal DaggerfallSession Session { get; }
        internal SpatialFake Spatial { get; private set; } = null!;
        internal Fixture(bool twoSites = false)
        {
            var source = ReadInputs(TestData.RepositoryRoot);
            if (twoSites) Destination = DaggerfallSiteContent.Read(FullContent(TestData.RepositoryRoot), File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), TestPayload.Definitions);
            Composition = new(TestPayload.Definitions, source, DaggerfallTuning.Defaults) { Profiles = new DaggerfallSiteProfiles(Destination is null ? [source] : [source, Destination]) };
            Session = DaggerfallSession.StartNew(Engine().Context, Composition);
        }
        private EngineContextFake Engine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, Composition.StartSite); if (Destination is not null) PopulateContent(content, Destination);
            Spatial = SpatialFake.Create(Composition.StartSite.SpatialArtifact.Sha256, releases);
            return EngineContextFake.Create(content, Spatial.Service, new AppearanceFake(releases), Perception.Service, random: RandomMaximum.Create());
        }
        internal DaggerfallSession Restore(RulesetSavePayload payload) => DaggerfallSession.Restore(Engine().Context, Composition, payload);
        public void Dispose() => Session.Dispose();
    }
}
