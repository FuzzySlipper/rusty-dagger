using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestNpcLifecycleTests
{
    [Theory]
    [InlineData("hide _person_", DaggerfallQuestTaskOperationKind.HideNpc)]
    [InlineData("hide npc _person_", DaggerfallQuestTaskOperationKind.HideNpc)]
    [InlineData("restore _person_", DaggerfallQuestTaskOperationKind.RestoreNpc)]
    [InlineData("restore npc _person_", DaggerfallQuestTaskOperationKind.RestoreNpc)]
    [InlineData("destroy _person_", DaggerfallQuestTaskOperationKind.DestroyNpc)]
    [InlineData("destroy npc _person_", DaggerfallQuestTaskOperationKind.DestroyNpc)]
    [InlineData("create npc _person_", DaggerfallQuestTaskOperationKind.CreateNpc)]
    internal void Source_aliases_preserve_their_lifecycle_meaning(string source, DaggerfallQuestTaskOperationKind expected)
    {
        var definition = new DaggerfallQuestSourceDefinition("aliases", "", "aliases.txt", DaggerfallQuestDisposition.Compiled, [],
            [new("headless", 1, [source], null)], []);
        Assert.Equal(expected, Assert.Single(Assert.Single(DaggerfallQuestTaskCompiler.Compile(definition).Tasks).Operations).Kind);
    }

    [Fact]
    public void Hide_restore_preserves_actual_identity_and_underlying_presence_across_encoded_restore()
    {
        var definitions = QuestNpcOverlayTests.Definitions(["hide npc _contact_"], taskBlocks:
            [["_restore_ task:", "restore npc _contact_"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long id = QuestNpcOverlayTests.Giver(f, definitions);
        var before = f.Session.State.Npcs.Require(id);
        QuestNpcOverlayTests.Start(f, "hidden", id); f.Update();
        Assert.False(f.Session.State.Npcs.IsGameplayActive(id));
        Assert.DoesNotContain(f.Session.Dialogue.NpcTargets(), value => value.Identity == ActorsState.Identity(id));
        Assert.Equal(before, f.Session.State.Npcs.Require(id));
        using var restored = f.Restore();
        Assert.False(restored.State.Npcs.IsGameplayActive(id));
        restored.State.Npcs.SetPresence(id, DaggerfallNpcPresence.Hidden);
        StartTask(restored, "restore");
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.False(restored.State.Quests.IsNpcUnavailable(id));
        Assert.False(restored.State.Npcs.IsGameplayActive(id));
        Assert.Equal(DaggerfallNpcPresence.Hidden, restored.State.Npcs.Require(id).Presence);
        restored.State.Npcs.SetPresence(id, DaggerfallNpcPresence.Active);
        Assert.True(restored.State.Npcs.IsGameplayActive(id));
        Assert.Equal(id, restored.State.Quests.Capture().Instances.Single().Resources.Single().Binding.ActorIds.Single());
    }

    [Fact]
    public void Destroyed_resource_cannot_be_restored_or_recreated_and_does_not_mutate_an_unrelated_person()
    {
        var definitions = QuestNpcOverlayTests.Definitions(["destroy _contact_"], taskBlocks:
            [["_restore_ task:", "restore npc _contact_", "create npc _contact_"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long id = QuestNpcOverlayTests.Giver(f, definitions);
        var original = f.Session.State.Npcs.Require(id);
        long unrelated = f.Session.State.Npcs.RegisterStable(DaggerfallNpcKind.Static, "unrelated", original.Site, original.Appearance, "unrelated", ["talk"]);
        var before = f.Session.State.Npcs.Require(unrelated);
        QuestNpcOverlayTests.Start(f, "destroyed", id); f.Update();
        using var restored = f.Restore();
        StartTask(restored, "restore");
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.True(restored.State.Quests.Capture().Instances.Single().Resources.Single().IsNpcDestroyed);
        Assert.False(restored.State.Npcs.IsGameplayActive(id));
        Assert.True(restored.State.Npcs.IsGameplayActive(unrelated));
        Assert.Equal(before.Appearance, restored.State.Npcs.Require(unrelated).Appearance);
        Assert.Empty(restored.State.Quests.Capture().Instances.Single().Placements);
        Assert.False(restored.State.Quests.ActorClicked(id));
    }

    [Fact]
    public void Quest_minted_person_destroy_retires_projection_and_a_new_quest_mints_a_new_identity()
    {
        var definitions = QuestNpcAdmissionTests.Definitions(taskBlocks:
            [["_destroy_ task:", "destroy npc _person_"], ["_restore_ task:", "restore npc _person_", "create npc _person_"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        f.Session.State.Quests.Start(new("first", "npc.txt", "npc", DaggerfallQuestLifecycle.Active, null, [], []));
        f.Update();
        long id = f.Session.State.Quests.Capture().Instances.Single().Resources.Single().Binding.ActorIds.Single();
        Assert.True(f.Session.State.Actors.Entities.TryResolve(ActorsState.Identity(id), out _));
        StartTask(f.Session, "destroy"); f.Update();
        Assert.Equal(DaggerfallNpcPresence.Removed, f.Session.State.Npcs.Require(id).Presence);
        Assert.False(f.Session.State.Actors.Entities.TryResolve(ActorsState.Identity(id), out _));
        using var restored = f.Restore();
        StartTask(restored, "restore");
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        restored.ReconcileNpcProjection();
        Assert.False(restored.State.Actors.Entities.TryResolve(ActorsState.Identity(id), out _));
        restored.State.Quests.Start(new("new-person", "npc.txt", "npc", DaggerfallQuestLifecycle.Active, null, [], []));
        restored.State.Quests.AdmitPlacements(f.Inputs, restored); restored.ReconcileNpcProjection();
        long replacement = restored.State.Quests.Capture().Instances.Single(value => value.InstanceId == "new-person").Resources.Single().Binding.ActorIds.Single();
        Assert.NotEqual(id, replacement);
        Assert.True(restored.State.Actors.Entities.TryResolve(ActorsState.Identity(replacement), out _));
    }

    [Fact]
    public void Individual_availability_uses_real_static_click_and_reservation_then_retains_accepted_click_across_save()
    {
        var named = TestPayload.Definitions.QuestSources.Tables.ActorItemTables.Factions.Rows.First(row => row.Active
            && TestPayload.Definitions.Factions.Factions.TryGetValue(row.P3, out var faction) && faction.Type == 0);
        var definitions = QuestNpcOverlayTests.Definitions([], taskBlocks: [["_available_ task:", $"when {named.Name} is available"], ["_hide_ task:", "hide npc _contact_"]]);
        string root = TestData.RepositoryRoot;
        var content = FullContent(root);
        var profile = StaticNpcAdmissionTests.StaticProviderSite(ReadProfile(root, content, definitions, "daggerfall.charing-interior-1-1-0.json"), 2, named.P3);
        List<string> releases = [];
        var contentService = new ContentFake(releases); PopulateContent(contentService, profile);
        var spatial = SpatialFake.Create(profile.SpatialArtifact.Sha256, releases); spatial.KeepPosition = true;
        var perception = PerceptionFake.Create();
        var appearance = new AppearanceFake(releases);
        var engine = EngineContextFake.Create(contentService, spatial.Service, appearance, perception.Service);
        var composition = new DaggerfallSessionComposition(definitions, profile, DaggerfallTuning.Defaults)
            { Blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.blocks.json"))) };
        using var session = DaggerfallSession.StartNew(engine.Context, composition);
        long target = session.State.Npcs.All.Single(value => value.Appearance.FactionId == named.P3).DurableId;
        long other = session.State.Npcs.All.Single(value => value.Appearance.FactionId == 60).DurableId;
        session.State.Quests.Start(new("listener", "overlay.txt", "overlay", DaggerfallQuestLifecycle.Active, null, [], []) { QuestorId = other });
        session.State.Quests.Start(new("reservation", "overlay.txt", "overlay", DaggerfallQuestLifecycle.Active, null, [], []) { QuestorId = target });
        perception.Responder = request => Receipt(request.Targets.ToArray().Select(value => new PerceptionPair(request.Observers.Span[0].Entity,
            value.Entity, 1d, 1d, value.Entity == (ulong)target ? PerceptionPairKind.Visible : PerceptionPairKind.Occluded, 1d)).ToArray());
        AimActivationAt(session, target);
        session.Update(new ProductUpdate(OuterUpdate(50), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));
        Assert.True(session.ActivationView.Applied, session.ActivationView.Message);
        Assert.False(Available(session));
        StartTask(session, "hide", "reservation");
        session.Update(new ProductUpdate(OuterUpdate(51), []));
        Assert.False(session.State.Npcs.IsGameplayActive(target));
        Assert.False(appearance.Snapshots.Last().Single(value => value.ObjectId == (ulong)target).Visible);
        session.State.Quests.Complete("reservation", "released");
        session.State.Quests.Advance(session.State.Variables, DaggerfallCalendar.Start);
        Assert.True(Available(session));
        using var restored = DaggerfallSession.Restore(engine.Context, composition, session.CaptureSave());
        restored.State.Quests.ActorClicked(target);
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.False(Available(restored)); // Same accepted person cannot retrigger by repeated clicking or loading.
        restored.State.Quests.ActorClicked(other);
        restored.State.Quests.ActorClicked(target);
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.True(Available(restored));
    }

    private static bool Available(DaggerfallSession session) => session.State.Quests.Capture().Instances.Single(value => value.InstanceId == "listener")
        .Tasks.Single(value => value.Symbol == "available").IsSet;

    private static void StartTask(DaggerfallSession session, string task, string? instanceId = null)
    {
        var saved = session.State.Quests.Capture();
        var instance = saved.Instances.Single(value => instanceId is null || value.InstanceId == instanceId);
        session.State.Quests.Restore(saved with { Instances = saved.Instances.Select(value => value.InstanceId == instance.InstanceId
            ? value with { Tasks = value.Tasks.Select(taskState => taskState.Symbol == task ? taskState with { IsSet = true } : taskState).ToArray() } : value).ToArray() });
    }
}
