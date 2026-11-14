using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallRestPanel"/> built without the session: Time's rest owner over the classic player, the rest's
/// list, the panel request and the held-input neutralize recorded as plain inputs.
/// </summary>
public sealed class DaggerfallRestPanelTests
{
    private static readonly DaggerfallSiteId SiteId = new(17, 4);
    private readonly List<DaggerfallRestRequest> _requests = [];
    private readonly List<string> _panels = [];
    private int _neutralized;

    [Fact]
    public void A_rest_action_reads_its_mode_and_hours_and_asks_the_rests_list()
    {
        DaggerfallRestPanel panel = Panel(DaggerfallRestInterruption.None);
        panel.Change(new DaggerfallPlayerUiAction("rest", Mode: "timed", Hours: 3));
        panel.Change(new DaggerfallPlayerUiAction("rest", Mode: "until-healed", Hours: 3));
        Assert.Equal([new DaggerfallRestRequest(DaggerfallRestMode.Timed, 3), new DaggerfallRestRequest(DaggerfallRestMode.UntilHealed, 0)], _requests);
        Assert.Empty(_panels);
        Assert.Equal(2, _neutralized);
        Assert.Throws<ArgumentException>(() => panel.Change(new DaggerfallPlayerUiAction("rest", Mode: "nap")));
    }

    [Fact]
    public void An_encounter_ends_the_rest_on_the_world_and_the_result_is_kept_for_the_HUD()
    {
        DaggerfallRestPanel panel = Panel(DaggerfallRestInterruption.Encounter);
        Assert.False(panel.View.HasResult);
        panel.Change(new DaggerfallPlayerUiAction("rest", Mode: "loiter", Hours: 1));
        Assert.Equal([DaggerfallPanel.Resume], _panels);
        Assert.True(panel.View.HasResult);
        Assert.Equal(DaggerfallRestInterruption.Encounter, panel.View.Interruption);
    }

    private DaggerfallRestPanel Panel(DaggerfallRestInterruption interruption)
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer("class00");
        ProgressionState progression = new();
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, career));
        DaggerfallSkillUseReactions skills = new(progression, stats, definitions, player);
        DaggerfallRewardReactions rewards = new(progression, stats, new EntityId(DaggerfallActorIdentity.PlayerEntityId), () => career,
            RandomMinimum.Create(), new Dictionary<long, DaggerfallActorDefinition>(), DaggerfallTuning.Defaults.Progression);
        DaggerfallLevelUpState levelUps = new(progression, skills, stats, definitions, () => career, RandomMinimum.Create(), rewards);
        DaggerfallWorldTime clock = new(new DaggerfallCalendar(405, 5, 1, 22, 0, 0), 0d, 12d);
        DaggerfallRest rest = new(clock, DaggerfallRestTuning.Default, skills, levelUps, seconds => clock.AdvanceInterval(seconds, []));
        DaggerfallCareerPassives passives = new(new DaggerfallCharacterState(definitions, stats, player), definitions,
            new Dictionary<long, DaggerfallActorDefinition>(), DaggerfallActorIdentity.PlayerEntityId);
        DaggerfallRestPanel panel = null!;
        panel = new(rest, stats, new Surroundings(), passives, () => DaggerfallWorldProfileKind.Exterior,
            (request, eligibility, _, _, _) =>
            {
                _requests.Add(request);
                DaggerfallRestResult result = new(true, request.Mode, 3600, 3600, 1, 0, 0, 0, interruption, "Rested.");
                panel.Publish(result);
                return result;
            },
            _panels.Add, () => _neutralized++);
        return panel;
    }

    private sealed class Surroundings : IDaggerfallRestSurroundings
    {
        public bool MustFeed => false;
        public bool EncounterPending => false;
        public bool EnemyNearby => false;
        public bool QuestPromptPending => false;
        public bool PlayerDefeated => false;
        public bool HasPosition => true;
        public DaggerfallWorldProfileKey Profile => new(SiteId, DaggerfallWorldProfileKind.Exterior, "exterior");
        public DaggerfallSiteRecord? Site => null;
        public bool InteriorPrivilege => false;
        public long? TavernRoomSeconds => null;
        public int ExteriorClimate => 228;
        public bool InSiteCell => false;
        public bool EncounterArrives(long minute) => false;
    }
}
