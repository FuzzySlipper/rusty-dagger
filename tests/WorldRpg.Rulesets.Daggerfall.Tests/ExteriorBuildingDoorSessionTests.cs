using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Interaction;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Targeting;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Charing's published building doors follow the donor's building-entry rule (PlayerActivate.BuildingIsUnlocked):
/// a residence opens from six to six and is locked otherwise, a store keeps its hours, a guild hall its hours unless
/// the player's rank grants access at any hour, and Information mode names the building and its hours.
/// </summary>
public sealed class ExteriorBuildingDoorSessionTests
{
    private static readonly DaggerfallSiteBuildingId Residence = new(1, 1, 0);   // RESIAL05 House2, quality 8
    private static readonly DaggerfallSiteBuildingId GeneralStore = new(2, 1, 0); // GENRAL01, quality 14
    private static readonly DaggerfallSiteBuildingId MagesGuild = new(3, 4, 0);   // MAGEAA14, quality 7

    private static readonly SharedFixture<DaggerfallBlocksSnapshot> Blocks = new(() =>
        DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json"))));

    private static readonly SharedFixture<(DaggerfallSiteProfile Exterior, DaggerfallSiteProfile[] Interiors)> Profiles = new(() =>
    {
        string root = TestData.RepositoryRoot;
        ProductContent content = FullContent(root);
        return (ReadProfile(root, content, TestPayload.Definitions, "daggerfall.charing-exterior.json"),
            [ReadProfile(root, content, TestPayload.Definitions, "daggerfall.charing-interior-1-1-0.json"),
             ReadProfile(root, content, TestPayload.Definitions, "daggerfall.charing-interior-2-1-0.json"),
             ReadProfile(root, content, TestPayload.Definitions, "daggerfall.charing-interior-3-4-0.json")]);
    });

    /// <summary>At midnight the residence is locked: picking or bashing it is breaking in, and enters it.</summary>
    [Theory]
    [InlineData("steal")]
    [InlineData("bash")]
    public void Forcing_a_locked_residence_enters_its_source_interior_and_restores_the_same_door(string mode)
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallSkills.Lockpicking)).BaseValue = 100;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value)).BaseValue = 100;
        fixture.SetMode(mode);
        fixture.Interact(Residence);
        Assert.True(session.ActivationView.Applied, session.ActivationView.Message);
        Assert.Equal(fixture.InteriorOf(Residence), session.Sites.ActiveProfile);
        var incident = Assert.Single(session.State.Crime.Incidents);
        Assert.Equal(DaggerfallCrimeKind.BreakingAndEntering, incident.Crime);
        Assert.Equal(mode == "bash", incident.Reported); // Minimum random notices bashing, never lockpicking.
        Assert.True(session.Site.IsBuildingDiscovered(fixture.Exterior.Site!.Value, Residence));
        var map = session.ReadMapPresentation();
        Assert.NotNull(map);
        Assert.Equal("interior", map.Kind);
        Assert.Empty(map.Areas);
        var discovery = session.State.DungeonDiscoveries[session.Sites.ActiveProfile];
        var placement = discovery.Content.GeometryPlacements.First();
        discovery.ObservePlacement(placement.PlacementId);
        map = session.ReadMapPresentation()!;
        Assert.Equal(placement.BoundsMin.X, Assert.Single(map.Areas).MinX);
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.Equal(map.Areas, restored.ReadMapPresentation()!.Areas);
        Assert.True(restored.Site.IsBuildingDiscovered(fixture.Exterior.Site!.Value, Residence));
        Assert.Single(restored.State.Crime.Incidents);
        Assert.True(restored.TryTransitionTo(fixture.Exterior.ProfileKey));
        Assert.Equal(0, restored.Doors.Read(fixture.DoorOf(Residence)).LockValue);
        Assert.Equal(484, restored.Doors.All.Count());
    }

    /// <summary>
    /// A House2 residence is locked at night with the "Locked." of lockedExteriorDoor and its quality-over-two lock,
    /// and walked into without a crime from six in the morning. Information mode names it "Residence".
    /// </summary>
    [Fact]
    public void A_residence_is_locked_at_night_and_entered_freely_by_day()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        fixture.Interact(Residence);
        Assert.False(session.ActivationView.Applied);
        Assert.StartsWith("Locked. ", session.ActivationView.Message);
        Assert.Equal(fixture.Exterior.ProfileKey, session.Sites.ActiveProfile);
        Assert.Equal(DaggerfallBuildingEntryPolicy.LockValue(8), session.Doors.Read(fixture.DoorOf(Residence)).LockValue);

        fixture.SetMode("info");
        fixture.Interact(Residence);
        Assert.StartsWith("Residence ", session.ActivationView.Message);
        Assert.Equal("Residence", fixture.InspectLabel(Residence));

        fixture.AdvanceToHour(6);
        fixture.SetMode("grab");
        fixture.Interact(Residence);
        Assert.True(session.ActivationView.Applied, session.ActivationView.Message);
        Assert.Equal(fixture.Exterior.ProfileKey, session.Sites.ActiveProfile);
        Assert.Equal(256, session.DungeonTextProjection!.TextId);
        fixture.ContinueGreeting();
        Assert.Equal(fixture.InteriorOf(Residence), session.Sites.ActiveProfile);
        Assert.Empty(session.State.Crime.Incidents);
    }

    /// <summary>
    /// A store is closed outside its hours: Information mode names it and gives the hours as storeClosed does, and it
    /// is entered without a crime once it opens. A stale open door left from an earlier visit does not admit the player.
    /// </summary>
    [Fact]
    public void A_store_keeps_its_hours_and_names_itself_in_information_mode()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        string name = session.Site.RequireBuilding(session.Site.ActiveSite!.Id, GeneralStore).Name;
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.Equal(name, fixture.InspectLabel(GeneralStore));

        fixture.SetMode("info");
        fixture.Interact(GeneralStore);
        Assert.StartsWith($"{name}. Store is closed. Open from 6:00 to 23:00. ", session.ActivationView.Message);

        // An earlier visit leaves the door open in its saved state; the entry rule still holds it shut.
        session.Doors.Unlock(fixture.DoorOf(GeneralStore), DaggerfallDoorOperationSource.Player);
        session.Doors.Open(fixture.DoorOf(GeneralStore), DaggerfallDoorOperationSource.Player);
        fixture.SetMode("grab");
        fixture.Interact(GeneralStore);
        Assert.False(session.ActivationView.Applied);
        Assert.StartsWith("Locked. ", session.ActivationView.Message);
        Assert.Equal(DaggerfallBuildingEntryPolicy.LockValue(14), session.Doors.Read(fixture.DoorOf(GeneralStore)).LockValue);

        fixture.AdvanceToHour(9);
        fixture.SetMode("info");
        fixture.Interact(GeneralStore);
        Assert.Equal(name, session.ActivationView.Message);
        Assert.Equal(267, session.DungeonTextProjection!.TextId);
        Assert.False(string.IsNullOrWhiteSpace(session.DungeonTextProjection.Text));
        Assert.Equal(fixture.Exterior.ProfileKey, session.Sites.ActiveProfile);
        fixture.ContinueGreeting();
        Assert.Equal(fixture.InteriorOf(GeneralStore), session.Sites.ActiveProfile);
        Assert.Empty(session.State.Crime.Incidents);
    }

    /// <summary>
    /// A Mages Guild hall keeps its eleven-to-eleven hours (guildClosed) for strangers and junior members; from rank 6
    /// its members enter at any hour (MagesGuild.HallAccessAnytime).
    /// </summary>
    [Fact]
    public void A_guild_hall_admits_its_senior_members_at_any_hour()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        fixture.SetMode("info");
        fixture.Interact(MagesGuild);
        Assert.Contains(". Guild is closed. Open from 11:00 to 23:00.", session.ActivationView.Message);

        session.State.Social.JoinGuild(40, 0);
        fixture.SetMode("grab");
        fixture.Interact(MagesGuild);
        Assert.False(session.ActivationView.Applied);
        Assert.StartsWith("Locked. ", session.ActivationView.Message);

        for (int rank = 0; rank < 6; rank++) session.State.Social.PromoteGuild(40, 0);
        fixture.Interact(MagesGuild);
        Assert.True(session.ActivationView.Applied, session.ActivationView.Message);
        Assert.Equal(fixture.InteriorOf(MagesGuild), session.Sites.ActiveProfile);
        Assert.Empty(session.State.Crime.Incidents);
    }

    [Fact]
    public void Prepared_open_enters_only_after_the_player_level_reaches_the_building_lock()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        int lockValue = DaggerfallBuildingEntryPolicy.LockValue(8);
        fixture.CastOpen();
        fixture.Interact(Residence);
        Assert.False(session.ActivationView.Applied);
        Assert.Equal(fixture.Exterior.ProfileKey, session.Sites.ActiveProfile);
        Assert.Equal(lockValue, session.Doors.Read(fixture.DoorOf(Residence)).LockValue);
        Assert.Empty(session.State.Effects.Active);
        session.State.Progression.AdvanceTo(0, lockValue);
        fixture.CastOpen();
        fixture.Interact(Residence);
        Assert.True(session.ActivationView.Applied, session.ActivationView.Message);
        Assert.Equal(fixture.InteriorOf(Residence), session.Sites.ActiveProfile);
        Assert.Empty(session.State.Effects.Active);
    }

    [Fact]
    public void Quality_one_building_is_locked_at_zero_and_that_state_restores()
    {
        using Fixture f = new();
        var id = f.DoorOf(Residence);
        // Charing's fixture houses have higher qualities; admit the same policy's quality-one result.
        f.Session.Doors.ApplyBuildingEntry(id, DaggerfallBuildingEntryPolicy.LockValue(1), locked: true);
        var door = f.Session.Doors.Read(id);
        Assert.True(door.IsLocked); Assert.Equal(0, door.LockValue);
        Assert.Equal(DaggerfallDoorOperationResult.Locked, f.Session.Doors.Open(door.Id, DaggerfallDoorOperationSource.Player));
        using var restored = f.Restore(f.Session.CaptureSave());
        Assert.True(restored.Doors.Read(door.Id).IsLocked);
        Assert.Equal(0, restored.Doors.Read(door.Id).LockValue);
    }

    [Fact]
    public void Lock_spell_does_not_change_a_building_and_remains_ready_for_an_action_door()
    {
        using Fixture f = new(); f.AdvanceToHour(6); f.CastOpen(lockDoor: true); f.Interact(Residence);
        Assert.Equal(DaggerfallDungeonTextActionKind.BuildingGreeting, f.Session.DungeonTextProjection!.Kind);
        Assert.False(f.Session.Doors.Read(f.DoorOf(Residence)).IsLocked);
        Assert.Equal(DaggerfallDoorMagic.Lock, Assert.Single(f.Session.State.Effects.Active).Definition.DoorMagic);
        f.ContinueGreeting();
        Assert.Equal(f.InteriorOf(Residence), f.Session.Sites.ActiveProfile);
    }

    [Fact]
    public void Failed_pick_is_remembered_per_building_after_daytime_entry_exit_and_save()
    {
        using Fixture f = new(random: System.Reflection.DispatchProxy.Create<IRandomService, FailedPickRandom>());
        var session = f.Session; var site = session.Site.Active!.Value;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallSkills.Lockpicking)).BaseValue = 20;
        f.SetMode("steal"); f.Interact(Residence);
        Assert.False(session.ActivationView.Applied);
        Assert.Equal(20, session.Site.FailedBuildingPick(site, Residence));
        f.AdvanceToHour(6); f.SetMode("grab"); f.Interact(Residence); f.ContinueGreeting();
        Assert.Equal(f.InteriorOf(Residence), session.Sites.ActiveProfile);
        Assert.True(session.TryTransitionTo(f.Exterior.ProfileKey));
        f.AdvanceToHour(0);
        f.SetMode("steal"); f.Interact(Residence);
        Assert.Contains("already tried", session.ActivationView.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(20, session.Site.FailedBuildingPick(site, Residence));
        using var restored = f.Restore(session.CaptureSave());
        Assert.Equal(20, restored.Site.FailedBuildingPick(site, Residence));
        Assert.DoesNotContain(restored.Doors.Capture(), value => value.FailedLockpickingSkill is not null);
    }

    [Fact]
    public void Quest_residence_name_uses_persisted_dialogue_visibility_and_updates_live_labels()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: []);
        using Fixture f = new(definitions);
        var session = f.Session; var site = session.Site.ActiveSite!;
        var binding = DaggerfallQuestResourceBinding.PlaceBuilding(session.Site, site.MapId, DaggerfallQuestPlaceAllocator.BuildingKey(Residence));
        session.State.Quests.Start(new("residence-name", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", binding) { Text = new(Name: "The Moorcroft Residence"), DialogueVisible = false }], []));
        Assert.Equal("Residence", f.InspectLabel(Residence));
        Assert.True(session.State.Quests.TryGet("residence-name", out var quest));
        var resource = quest!.Resources.Single(value => value.Symbol == "location");
        session.State.Quests.SetResource(quest.InstanceId, resource with { DialogueVisible = true });
        Assert.Equal("The Moorcroft Residence", f.InspectLabel(Residence));
        session.Site.DiscoverBuilding(site.Id, Residence);
        Assert.Contains(session.ReadMapPresentation()!.Labels, label => label.Name == "The Moorcroft Residence");
        using var restored = f.Restore(session.CaptureSave());
        Assert.Equal("The Moorcroft Residence", restored.Site.RequireBuilding(site.Id, Residence).Name);
    }

    internal class FailedPickRandom : RandomMinimum
    {
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? arguments) =>
            method?.Name == nameof(IRandomService.DrawKeyed) && arguments![0] is KeyedRngRequest request && request.Scope == "daggerfall.lockpick.v1"
                ? new KeyedRngReceipt(request.Maximum) : base.Invoke(method, arguments);
    }

    private sealed class Fixture : IDisposable
    {
        internal DaggerfallSiteProfile Exterior { get; } = Profiles.Value.Exterior;
        internal DaggerfallSession Session { get; }
        private readonly DaggerfallSessionComposition _composition;
        private SpatialFake _spatial = null!;
        private PerceptionFake _perception = null!;
        private ulong _step;
        private readonly IRandomService _random;

        internal Fixture(DaggerfallDefinitions? definitions = null, IRandomService? random = null)
        {
            foreach (DaggerfallSiteBuildingId building in new[] { Residence, GeneralStore, MagesGuild })
            {
                DaggerfallRdbDoorDefinition door = Exterior.Doors.First(value => value.ExteriorBuilding == building);
                Assert.True(door.BoundsMin.X < door.BoundsMax.X && door.BoundsMin.Y < door.BoundsMax.Y && door.BoundsMin.Z < door.BoundsMax.Z);
            }
            _random = random ?? RandomMinimum.Create();
            _composition = new(definitions ?? TestPayload.Definitions, Exterior, DaggerfallTuning.Defaults)
            {
                Profiles = new DaggerfallSiteProfiles([Exterior, .. Profiles.Value.Interiors]),
                // The block catalog admits the classic building names Information mode and door labels show.
                Blocks = Blocks.Value,
            };
            Session = DaggerfallSession.StartNew(Engine().Context, _composition);
        }

        internal DaggerfallRdbDoorId DoorOf(DaggerfallSiteBuildingId building) =>
            Exterior.Doors.First(value => value.ExteriorBuilding == building).Id;

        internal DaggerfallWorldProfileKey InteriorOf(DaggerfallSiteBuildingId building) =>
            DaggerfallWorldProfileIds.Interior(Exterior.ProfileKey.Site, building);

        private EngineContextFake Engine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, Exterior);
            foreach (DaggerfallSiteProfile interior in Profiles.Value.Interiors) PopulateContent(content, interior);
            _spatial = SpatialFake.Create(Exterior.SpatialArtifact.Sha256, releases);
            _spatial.KeepPosition = true;
            _perception = PerceptionFake.Create();
            _perception.Responder = request => Receipt(request.Targets.Span.ToArray()
                .Select(target => new PerceptionPair(1, target.Entity, 1, 1, PerceptionPairKind.Visible, 1)).ToArray());
            return EngineContextFake.Create(content, _spatial.Service, new AppearanceFake(releases), _perception.Service, random: _random);
        }

        internal DaggerfallSession Restore(RulesetSavePayload save) => DaggerfallSession.Restore(Engine().Context, _composition, save);

        internal void SetMode(string mode) => Session.Update(new ProductUpdate(OuterUpdate(++_step), [Ui("{\"action\":\"activation-mode\",\"mode\":\"" + mode + "\"}")]));

        /// <summary>Advances the session's calendar to the next whole <paramref name="hour"/>.</summary>
        internal void AdvanceToHour(int hour)
        {
            DaggerfallCalendarSave now = DaggerfallSavePayload.Read(Session.CaptureSave()).Calendar;
            long seconds = ((((hour - now.Hour) % 24) + 24) % 24 * 3600L) - (now.Minute * 60L) - now.Second;
            if (seconds <= 0) seconds += 24 * 3600L;
            Session.AdvanceElapsedTime(seconds);
            Assert.Equal(hour, DaggerfallSavePayload.Read(Session.CaptureSave()).Calendar.Hour);
        }

        internal void ContinueGreeting()
        {
            var prompt = Session.DungeonTextProjection!;
            Session.Update(new ProductUpdate(OuterUpdate(++_step), [Ui(System.Text.Json.JsonSerializer.Serialize(new
                { action = "dungeon-text-close", item = prompt.ActionId, revision = prompt.Revision.ToString() }))]));
        }

        internal void CastOpen(bool lockDoor = false)
        {
            string spell = lockDoor ? "spell.019" : "spell.018";
            Session.State.Character.LearnSpell(spell);
            Track magicka = Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
            magicka.Maximum.BaseValue = 10000;
            magicka.SetCurrent(10000);
            Assert.Equal(DaggerfallCastOutcome.Ready, Session.Casting.Ready(1, spell).Outcome);
            Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(Session.ReleaseReadySpell(1, Vector3.UnitZ).Bundle!.Results).Outcome);
        }

        /// <summary>Stands in front of the building's door, facing it, with only that door seen.</summary>
        private DaggerfallDoorView Face(DaggerfallSiteBuildingId building)
        {
            DaggerfallDoorView door = Session.Doors.Read(DoorOf(building));
            Session.State.PlayerControl.MoveTo(door.Pose.Translation + Vector3.UnitZ);
            Session.State.PlayerControl.YawRadians = 0;
            Session.State.PlayerControl.PitchRadians = 0;
            _perception.Responder = request => Receipt(request.Targets.Span.ToArray()
                .Select(target => new PerceptionPair(1, target.Entity, 1, 1,
                    target.Entity == door.Entity.Value ? PerceptionPairKind.Visible : PerceptionPairKind.Occluded, 1)).ToArray());
            _spatial.FloorHit = request => request.Direction.Y < -.5 ? default : new SpatialHit
            {
                Present = true, Kind = SpatialHitKind.Entity, Entity = door.Entity.Value,
                Point = door.Pose.Translation, Distance = 1,
            };
            return door;
        }

        internal void Interact(DaggerfallSiteBuildingId building)
        {
            _ = Face(building);
            Session.Update(new ProductUpdate(OuterUpdate(++_step), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));
        }

        /// <summary>The label the interaction readout gives the building's door.</summary>
        internal string InspectLabel(DaggerfallSiteBuildingId building)
        {
            DaggerfallDoorView door = Face(building);
            WorldInteractionReadout readout = Session.CreateInteractionInspection().Inspect();
            return readout.Focus.Candidates.Single(candidate => candidate.Candidate.Target.Id == door.Entity.Value).Candidate.Label;
        }

        public void Dispose() => Session.Dispose();
    }
}
