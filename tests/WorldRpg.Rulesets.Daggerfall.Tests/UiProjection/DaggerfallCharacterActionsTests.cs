using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallCharacterActions"/> built without the session: Character's creation, level-up and controls owners
/// over the classic player and the Engine's input and audio fakes, with whether the title is open a plain input.
/// </summary>
public sealed class DaggerfallCharacterActionsTests
{
    private readonly PresentationState _presentation = new("Ready", 5d);
    private readonly EngineContextFake _engine = EngineContextFake.Create(null!, null!, null!);
    private bool _titleOpen = true;
    private DaggerfallCharacterState _character = null!;
    private DaggerfallPlayerControls _controls = null!;

    [Fact]
    public void Creation_answers_only_at_the_title_and_says_what_it_did()
    {
        DaggerfallCharacterActions actions = Actions();
        actions.ChangeCharacter(new DaggerfallPlayerUiAction("character-begin"));
        Assert.Equal("Character choices opened.", _presentation.LastOutcome);
        actions.ChangeCharacter(new DaggerfallPlayerUiAction("character-class-answer"));
        Assert.StartsWith("Character choice was not accepted: ", _presentation.LastOutcome, StringComparison.Ordinal);
        actions.ChangeCharacter(new DaggerfallPlayerUiAction("character-cancel"));
        Assert.Equal("Character choices cancelled.", _presentation.LastOutcome);
        Assert.Null(_character.Pending);

        _titleOpen = false;
        actions.ChangeCharacter(new DaggerfallPlayerUiAction("character-begin"));
        Assert.Equal("Character creation is available from the title screen.", _presentation.LastOutcome);
        Assert.Null(_character.Pending);
    }

    [Fact]
    public void A_level_up_without_its_attribute_is_refused_in_words()
    {
        DaggerfallCharacterActions actions = Actions();
        actions.ChangeLevelUp(new DaggerfallPlayerUiAction("character-level-allocate"));
        Assert.StartsWith("Level-up choice was not accepted: ", _presentation.LastOutcome, StringComparison.Ordinal);
    }

    [Fact]
    public void A_controls_action_asks_the_controls_owner_for_its_edit()
    {
        DaggerfallCharacterActions actions = Actions();
        actions.ChangeControls(new DaggerfallPlayerUiAction("controls-volume", Item: "music", Scale: .35f));
        Assert.Equal(.35f, _controls.Settings.Volume("music"), 3);
        Assert.Throws<ArgumentException>(() => actions.ChangeControls(new DaggerfallPlayerUiAction("character-begin")));
    }

    private DaggerfallCharacterActions Actions()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer("class00");
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, career));
        _character = new DaggerfallCharacterState(definitions, stats, player);
        ProgressionState progression = new();
        DaggerfallSkillUseReactions skills = new(progression, stats, definitions, player);
        DaggerfallRewardReactions rewards = new(progression, stats, new EntityId(DaggerfallActorIdentity.PlayerEntityId), () => career,
            RandomMinimum.Create(), new Dictionary<long, DaggerfallActorDefinition>(), DaggerfallTuning.Defaults.Progression);
        DaggerfallLevelUpState levelUps = new(progression, skills, stats, definitions, () => career, RandomMinimum.Create(), rewards);
        DaggerfallTuning tuning = DaggerfallTuning.Defaults;
        DaggerfallControlSettings initial = new();
        _controls = new DaggerfallPlayerControls(_engine.Context.Input, _engine.Context.Audio,
            new PlayerInputSystem(tuning.PlayerControl, DaggerfallInput.Controls, DaggerfallInput.Bindings, tuning.ControllerInput),
            new DaggerfallLocomotionPolicy(tuning.Locomotion, initial), initial, tuning.PlayerControl.LookSensitivity,
            tuning.ControllerInput, _ => { }, (_, _) => { });
        return new DaggerfallCharacterActions(new DaggerfallCharacterCreation(_character, definitions, RandomMinimum.Create()), _character,
            levelUps, _controls, quickEntry: false, () => _titleOpen, _presentation);
    }
}
