using System.Text.Json;
using Rusty.Engine;
using System.Numerics;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestWorldTriggerTests
{
    [Theory]
    [InlineData("set")]
    [InlineData("do")]
    public void Bound_place_controls_task_and_saying_occurs_once_through_save_and_reentry(string verb)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: [$"pc at _location_ {verb} _here_ saying 100"],
            taskBlocks: [["_here_ task:", "journal note 101"]], messages: ["Arrived", "Here"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions);
        f.Update(); f.Update();
        Assert.True(Task(f.Session, "here").IsSet);
        Assert.Single(f.Session.State.Quests.Messages.Deliveries.Where(v => v.MessageId == 100));
        using var restored = f.Restore();
        restored.State.Quests.BindWorldRead(() => new(f.Castle, null, null));
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.False(Task(restored, "here").IsSet);
        restored.State.Quests.BindWorldRead(() => new(f.Inputs, null, definitions.Locations.Records.Single(v => v.Id == f.Inputs.Site).DungeonType));
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.True(Task(restored, "here").IsSet);
        Assert.Single(restored.State.Quests.Messages.Deliveries.Where(v => v.MessageId == 100));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Exterior_entry_exit_discriminators_and_one_shot_commands_survive_save(bool acceptedInside)
    {
        var baseDefinitions = TestPayload.Definitions;
        var type = baseDefinitions.QuestSources.Tables.Places.Rows.First(v => v.P1 == 2 && v.P2 >= 0);
        var site = baseDefinitions.Locations.Records.First(v => (int)v.Kind == type.P2);
        var other = baseDefinitions.Locations.Records.First(v => (int)v.Kind != type.P2);
        var definitions = QuestWorldAdmissionTests.Definitions(actions: [], taskBlocks:
            [["_entered_ task:", $"when pc enters {type.Name}", "journal note 100"], ["_exited_ task:", $"when pc exits {type.Name}", "journal note 101"]], messages: ["Entered", "Exited"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        DaggerfallSiteRecord? current = acceptedInside ? site : null;
        f.Session.State.Quests.BindWorldRead(() => new(f.Inputs, current, null));
        Start(f, definitions);
        f.Update(); Assert.Equal(acceptedInside, Task(f.Session, "entered").IsSet); Assert.False(Task(f.Session, "exited").IsSet);
        if (!acceptedInside)
        {
            current = other; f.Update(); Assert.False(Task(f.Session, "entered").IsSet);
            current = site; f.Update(); Assert.True(Task(f.Session, "entered").IsSet); Assert.False(Task(f.Session, "exited").IsSet);
        }
        using var restored = f.Restore();
        restored.State.Quests.BindWorldRead(() => new(f.Inputs, null, null));
        for (int i = 0; i < 3; i++) restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.True(Task(restored, "entered").IsSet); Assert.True(Task(restored, "exited").IsSet);
        Assert.All(restored.State.Quests.Capture().Instances.Single().Tasks.Where(v => v.Symbol is "entered" or "exited"), value => Assert.True(value.OperationCompleted[1]));
        Assert.Equal(2, DaggerfallSavePayload.Read(restored.CaptureSave()).Notebook.Notes.Length);
    }

    [Fact]
    public void Any_dungeon_reads_type_and_unsupported_detail_leaves_other_tasks_running()
    {
        var any = TestPayload.Definitions.QuestSources.Tables.Places.Rows.First(v => v.P1 == 1 && v.P2 == -1);
        var invalid = TestPayload.Definitions.QuestSources.Tables.Places.Rows.First(v => v.P1 == 2);
        var definitions = QuestWorldAdmissionTests.Definitions(actions: [$"pc at any {any.Name} set _here_", $"pc at any {invalid.Name} do _bad_", "journal note 100"],
            taskBlocks: [["_here_ task:"], ["_bad_ task:"]], messages: ["Unaffected"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); f.Update();
        Assert.True(Task(f.Session, "here").IsSet); Assert.False(Task(f.Session, "bad").IsSet);
        var instance = f.Session.State.Quests.Capture().Instances.Single();
        Assert.Equal(DaggerfallQuestLifecycle.Active, instance.Lifecycle);
        Assert.Contains("requires a building or dungeon", instance.Tasks[0].OperationState[1].UnavailableReason);
        Assert.False(instance.Tasks[0].OperationCompleted[1]);
        Assert.Single(DaggerfallSavePayload.Read(f.Session.CaptureSave()).Notebook.Notes);
    }

    [Theory]
    [InlineData(11, 66, 11, 66, true)]
    [InlineData(11, 66, 11, 21, false)]
    [InlineData(11, 66, 11, 0, true)]
    [InlineData(17, 0, -1, 1, true)]
    [InlineData(0, 0, -1, 1, false)]
    [InlineData(7, 0, -1, 2, true)]
    [InlineData(1, 0, -1, 2, false)]
    public void Any_building_preserves_house_shop_and_guild_discriminators(int actual, int faction, int type, int filter, bool expected) =>
        Assert.Equal(expected, DaggerfallQuestPlaceAllocator.BuildingMatches(actual, faction, type, filter));

    [Fact]
    public void Published_exterior_start_and_boundary_use_the_admitted_right_handed_profile_frame()
    {
        var definitions = TestPayload.Definitions;
        string root = TestData.RepositoryRoot;
        var profile = DaggerfallSiteContent.Read(FullContent(root), File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.charing-exterior.json")), definitions);
        List<string> releases = [];
        ContentFake content = new(releases); PopulateContent(content, profile);
        var spatial = SpatialFake.Create(profile.SpatialArtifact.Sha256, releases); spatial.KeepPosition = true;
        var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using var session = DaggerfallSession.StartNew(engine.Context, new(definitions, profile, DaggerfallTuning.Defaults));
        Assert.Equal(profile.Site, session.Sites.ReadQuestLocation().ExteriorLocation?.Id);
        var original = session.State.PlayerControl.Position!.Value;
        session.State.PlayerControl.MoveTo(original.ToVector() + new Vector3(2000, 0, 2000));
        Assert.Null(session.Sites.ReadQuestLocation().ExteriorLocation);
        session.State.PlayerControl.MoveTo(original.ToVector());
        Assert.Equal(profile.Site, session.Sites.ReadQuestLocation().ExteriorLocation?.Id);
    }

    [Theory]
    [InlineData(-102.4f, 0, true)]
    [InlineData(-103, 0, false)]
    [InlineData(307.2f, -409.6f, true)]
    [InlineData(308, -409.6f, false)]
    public void Actual_town_footprint_includes_source_one_block_clearance(float x, float z, bool expected)
    {
        var footprint = new DaggerfallSiteExterior(1, 1, 2, 3, 48, 40, false, 0, 0, 0, 0, 0);
        Assert.Equal(expected, DaggerfallSiteLifecycle.InsideLocationFootprint(footprint, x, z));
    }

    private static DaggerfallQuestTaskState Task(DaggerfallSession session, string symbol) => session.State.Quests.Capture().Instances.Single().Tasks.Single(v => v.Symbol == symbol);
    private static void Start(SanguineRoseSessionTests.Fixture f, DaggerfallDefinitions definitions)
    {
        var site = definitions.Locations.Records.Single(v => v.Id == f.Inputs.Site);
        f.Session.State.Quests.Start(new("location-test", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(f.Inputs.ProfileKind, site.MapId) })], []));
    }
}
