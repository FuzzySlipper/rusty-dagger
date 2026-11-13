using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The rest owner on its own, with no session: where the player may rest, wait or camp, how a rest tick passes a
/// minute at a time through the time it is handed, and what interrupts it. Its surroundings are a fake that answers
/// only what it is asked.
/// </summary>
public sealed class DaggerfallRestTests
{
    private static readonly DaggerfallSiteId SiteId = new(17, 4);
    private static readonly DaggerfallWorldProfileKey Exterior = new(SiteId, DaggerfallWorldProfileKind.Exterior, "exterior");
    private static readonly DaggerfallWorldProfileKey Interior = new(SiteId, DaggerfallWorldProfileKind.Interior, "interior");

    [Fact]
    public void Refusals_come_in_the_donors_order_and_loitering_never_asks_where()
    {
        Fixture f = new();
        Surroundings world = f.World;
        world.MustFeed = world.EncounterPending = world.EnemyNearby = true;
        world.HasPosition = false;
        world.Profile = Exterior with { LogicalId = string.Empty };

        Assert.Equal("You must feed before you can rest.", f.Rest.Eligibility(DaggerfallRestMode.Timed, f.Stats, world).Message);
        world.MustFeed = false;
        Assert.StartsWith("Something approaches.", f.Rest.Eligibility(DaggerfallRestMode.Timed, f.Stats, world).Message);
        world.EncounterPending = false;
        Assert.Equal("Enemies are too close to rest.", f.Rest.Eligibility(DaggerfallRestMode.Timed, f.Stats, world).Message);
        world.EnemyNearby = false;
        Assert.Equal("You cannot rest before the player has a world position.", f.Rest.Eligibility(DaggerfallRestMode.Timed, f.Stats, world).Message);
        world.HasPosition = true;
        Assert.Equal("You cannot rest without an admitted world profile.", f.Rest.Eligibility(DaggerfallRestMode.Loiter, f.Stats, world).Message);
        world.Profile = Exterior;
        Assert.Equal(new DaggerfallRestEligibility(true), f.Rest.Eligibility(DaggerfallRestMode.Loiter, f.Stats, world));
        Assert.Equal("You cannot rest without an admitted location record.", f.Rest.Eligibility(DaggerfallRestMode.Timed, f.Stats, world).Message);
    }

    [Fact]
    public void A_dead_player_is_refused_as_defeated()
    {
        Fixture f = new();
        f.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).SetCurrent(0, clamp: true);

        DaggerfallRestEligibility eligibility = f.Rest.Eligibility(DaggerfallRestMode.Loiter, f.Stats, f.World);

        Assert.False(eligibility.Allowed);
        Assert.False(eligibility.IsAlive);
    }

    [Fact]
    public void An_interior_needs_a_privilege_and_never_asks_about_the_ground()
    {
        Fixture f = new();
        f.World.Profile = Interior;
        f.World.Site = Site(DaggerfallSiteKind.TownCity);

        Assert.Equal("You cannot rest in this interior without an admitted room or guild privilege.",
            f.Rest.Eligibility(DaggerfallRestMode.Timed, f.Stats, f.World).Message);
        f.World.InteriorPrivilege = true;
        Assert.Equal(new DaggerfallRestEligibility(true), f.Rest.Eligibility(DaggerfallRestMode.UntilHealed, f.Stats, f.World));
        Assert.Equal(0, f.World.GroundAsked);
    }

    [Fact]
    public void Camping_needs_land_and_a_place_outside_a_town_and_recovers_fatigue_at_a_camps_share()
    {
        Fixture f = new();
        f.World.Site = Site(DaggerfallSiteKind.TownCity);
        f.World.Climate = 0;
        Assert.Equal("You cannot rest on this terrain.", f.Rest.Eligibility(DaggerfallRestMode.Timed, f.Stats, f.World).Message);

        f.World.Climate = 228;
        f.World.InSiteCell = true;
        Assert.Equal("Camping in a town is not permitted.", f.Rest.Eligibility(DaggerfallRestMode.Timed, f.Stats, f.World).Message);

        f.World.Site = Site(DaggerfallSiteKind.DungeonKeep);
        Assert.Equal(new DaggerfallRestEligibility(true, FatigueDivisor: DaggerfallRestTuning.Default.InnFatigueMultiplier),
            f.Rest.Eligibility(DaggerfallRestMode.Timed, f.Stats, f.World));
    }

    [Fact]
    public void A_rest_tick_passes_minute_slices_and_an_encounter_at_a_minute_interrupts_it()
    {
        Fixture f = new(new DaggerfallCalendar(405, 5, 1, 22, 0, 30));
        f.World.Site = Site(DaggerfallSiteKind.DungeonKeep);
        f.World.Arrives = minute => minute == f.Clock.Calendar.MinuteNumber && f.Clock.Calendar.Minute == 3;

        DaggerfallRestTimeAdvance advance = f.Rest.PassInterval(DaggerfallRestPolicy.SecondsPerRestTick, DaggerfallRestMode.Timed,
            f.Stats, f.World);

        Assert.Equal(new long[] { 30, 60, 60 }, f.Passed);
        Assert.Equal(new DaggerfallRestTimeAdvance(600, 150, DaggerfallRestInterruption.Encounter), advance);
    }

    [Fact]
    public void A_quest_prompt_stops_a_rest_and_a_tavern_room_bounds_it()
    {
        Fixture f = new();
        f.World.Profile = Interior;
        f.World.Site = Site(DaggerfallSiteKind.TownCity);
        f.World.InteriorPrivilege = true;
        f.World.TavernRoomSeconds = 45;
        f.OnPass = seconds => f.World.QuestPromptPending = f.Passed.Sum() >= 60;

        DaggerfallRestTimeAdvance advance = f.Rest.PassInterval(600, DaggerfallRestMode.Timed, f.Stats, f.World);

        Assert.Equal(new long[] { 45, 15 }, f.Passed);
        Assert.Equal(new DaggerfallRestTimeAdvance(600, 60, DaggerfallRestInterruption.Stopped), advance);
        Assert.Equal(new DaggerfallRestTimeAdvance(600, 0, DaggerfallRestInterruption.Stopped),
            f.Rest.PassInterval(600, DaggerfallRestMode.Loiter, f.Stats, f.World));
    }

    [Fact]
    public void A_refused_request_reads_the_rulesets_sentence_and_a_hungry_vampire_is_refused_before_any_time_passes()
    {
        Fixture f = new();
        DaggerfallRestResult tooLong = f.Rest.Apply(new(DaggerfallRestMode.Timed, 100), new(true), f.Stats, f.World,
            _ => throw new InvalidOperationException("No time passes for a refused rest."), false, false, () => (false, false));
        Assert.False(tooLong.Accepted);
        Assert.Equal("Timed rest cannot exceed 99 hours.", tooLong.Message);

        f.World.MustFeed = true;
        DaggerfallRestResult hungry = f.Rest.Apply(new(DaggerfallRestMode.Timed, 1), new(true), f.Stats, f.World,
            _ => throw new InvalidOperationException("No time passes for a refused rest."), false, false, () => (false, false));
        Assert.Equal("You must feed before you can rest.", hungry.Message);
    }

    private static DaggerfallSiteRecord Site(DaggerfallSiteKind kind) =>
        new(SiteId, "Test", 0, 0, 0, 0, kind, true);

    private sealed class Fixture
    {
        internal Fixture(DaggerfallCalendar? start = null)
        {
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
            DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer("class00");
            ProgressionState progression = new();
            Stats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, career));
            DaggerfallSkillUseReactions skills = new(progression, Stats, definitions, player);
            DaggerfallRewardReactions rewards = new(progression, Stats, new EntityId(DaggerfallActorIdentity.PlayerEntityId), () => career,
                RandomMinimum.Create(), new Dictionary<long, DaggerfallActorDefinition>(), DaggerfallTuning.Defaults.Progression);
            DaggerfallLevelUpState levelUps = new(progression, skills, Stats, definitions, () => career, RandomMinimum.Create(), rewards);
            Clock = new DaggerfallWorldTime(start ?? new DaggerfallCalendar(405, 5, 1, 22, 0, 0), 0d, 12d);
            Rest = new DaggerfallRest(Clock, DaggerfallRestTuning.Default, skills, levelUps, seconds =>
            {
                Passed.Add(seconds);
                DaggerfallCalendarAdvance advance = Clock.AdvanceInterval(seconds, []);
                OnPass(seconds);
                return advance;
            });
        }

        internal StatsComponent Stats { get; }
        internal DaggerfallWorldTime Clock { get; }
        internal DaggerfallRest Rest { get; }
        internal Surroundings World { get; } = new();
        internal List<long> Passed { get; } = [];
        internal Action<long> OnPass { get; set; } = _ => { };
    }

    private sealed class Surroundings : IDaggerfallRestSurroundings
    {
        private int _climate = 228;
        private bool _inSiteCell;

        public bool MustFeed { get; set; }
        public bool EncounterPending { get; set; }
        public bool EnemyNearby { get; set; }
        public bool QuestPromptPending { get; set; }
        public bool PlayerDefeated { get; set; }
        public bool HasPosition { get; set; } = true;
        public DaggerfallWorldProfileKey Profile { get; set; } = Exterior;
        public DaggerfallSiteRecord? Site { get; set; }
        public bool InteriorPrivilege { get; set; }
        public long? TavernRoomSeconds { get; set; }
        internal int GroundAsked { get; private set; }
        internal Func<long, bool> Arrives { get; set; } = _ => false;

        public int ExteriorClimate { get { GroundAsked++; return _climate; } }
        internal int Climate { set => _climate = value; }
        public bool InSiteCell { get { GroundAsked++; return _inSiteCell; } set => _inSiteCell = value; }

        public bool EncounterArrives(long minute) => Arrives(minute);
    }
}
